using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SurvivalFP.Editor
{
    [InitializeOnLoad]
    public static class ControllerTestSceneBuilder
    {
        public const string ScenePath = "Assets/AI-Tools-DEV/Scenes/CharacterController_Test.unity";
        const string Root = "Assets/AI-Tools-DEV";
        const string Request = "Library/SurvivalFP.setup-request";
        static Transform course;
        static Material concrete, dark, mint, orange, white;

        static ControllerTestSceneBuilder() => EditorApplication.update += CheckRequest;
        static void CheckRequest()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string request = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            try { if (request == "polish") PolishCourse(); else CreateScene(); File.WriteAllText("Library/SurvivalFP.setup-result.txt", "SUCCESS " + ScenePath); }
            catch (Exception ex) { File.WriteAllText("Library/SurvivalFP.setup-result.txt", ex.ToString()); Debug.LogException(ex); }
        }

        [MenuItem("Tools/Survival FP/Open or Create Test Course")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your open scene before creating the course.");
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                if (UnityEngine.Object.FindAnyObjectByType<PlayerController>()) return;
            }
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Data");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            if (!File.Exists(ScenePath) && !AssetDatabase.CopyAsset("Assets/Game/Scenes/OutdoorsScene.unity", ScenePath)) throw new InvalidOperationException("Cannot copy the original scene.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (!camera) camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData)).GetComponent<Camera>();
            camera.name = "Player Camera";
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 250f;
            camera.fieldOfView = 75f;
            camera.usePhysicalProperties = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // Project-wide actions may be enabled even outside Play Mode. Edit an isolated copy.
            var input = InputActionAsset.FromJson(File.ReadAllText("Assets/Game/Input/InputSystem_Actions.inputactions"));
            var playerMap = input.FindActionMap("Player", true);
            AddAction(playerMap, "Flashlight", "<Keyboard>/f");
            AddAction(playerMap, "ReleaseCursor", "<Keyboard>/escape");
            AddAction(playerMap, "CaptureCursor", "<Mouse>/leftButton");
            var crouchAction = playerMap.FindAction("Crouch", true);
            if (!crouchAction.bindings.Any(b => b.path == "<Keyboard>/leftCtrl")) crouchAction.AddBinding("<Keyboard>/leftCtrl", groups: "Keyboard&Mouse");
            File.WriteAllText("Assets/Game/Input/InputSystem_Actions.inputactions", input.ToJson());
            UnityEngine.Object.DestroyImmediate(input);
            AssetDatabase.ImportAsset("Assets/Game/Input/InputSystem_Actions.inputactions", ImportAssetOptions.ForceSynchronousImport);
            input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Game/Input/InputSystem_Actions.inputactions");

            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0.1f, -14f);
            var capsule = player.AddComponent<CharacterController>();
            capsule.height = 2f;
            capsule.radius = 0.3f;
            capsule.center = Vector3.up;
            capsule.slopeLimit = 46f;
            capsule.stepOffset = 0.28f;
            capsule.skinWidth = 0.035f;
            capsule.minMoveDistance = 0f;
            var stamina = player.AddComponent<PlayerStamina>();
            var movement = player.AddComponent<PlayerMovement>();
            var controller = player.AddComponent<PlayerController>();
            camera.transform.SetParent(player.transform, false);
            camera.transform.localPosition = new Vector3(0f, 1.82f, 0f);
            camera.transform.localRotation = Quaternion.identity;
            var playerCamera = camera.gameObject.AddComponent<PlayerCamera>();
            var handAnchor = new GameObject("Item Hand Anchor").transform;
            handAnchor.SetParent(camera.transform, false);
            handAnchor.localPosition = new Vector3(0.42f, -0.34f, 0.62f);
            handAnchor.localRotation = Quaternion.identity;
            var inventory = player.GetComponent<PlayerInventory>();
            if (!inventory) inventory = player.AddComponent<PlayerInventory>();
            Assign(movement, "capsule", capsule, "stamina", stamina);
            Assign(controller, "inputActions", input, "movement", movement, "playerCamera", playerCamera);
            Assign(playerCamera, "movement", movement, "yawRoot", player.transform, "view", camera);
            Assign(inventory, "handAnchor", handAnchor, "playerCamera", playerCamera);
            PrefabUtility.SaveAsPrefabAsset(player, Root + "/Prefabs/SurvivalPlayer.prefab");

            ConfigureLighting();
            BuildCourse();
            BuildHUD(movement, stamina, controller);
            InventorySetup.Install();
            EditorSceneManager.SaveScene(scene);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, false));
            EditorBuildSettings.scenes = scenes.ToArray();
            Selection.activeGameObject = player;
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0f, 0f, 6f), Quaternion.Euler(40f, 0f, 0f), 42f);
            AssetDatabase.SaveAssets();
            Debug.Log("Survival FP: test course created, references assigned, prefab saved.");
        }

        static void AddAction(InputActionMap map, string name, string binding)
        {
            if (map.FindAction(name) == null) map.AddAction(name, InputActionType.Button, binding);
        }

        public static void Assign(UnityEngine.Object target, params object[] fields)
        {
            var serialized = new SerializedObject(target);
            for (int i = 0; i < fields.Length; i += 2)
                serialized.FindProperty((string)fields[i]).objectReferenceValue = (UnityEngine.Object)fields[i + 1];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureLighting()
        {
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional)
                {
                    light.transform.rotation = Quaternion.Euler(28f, -32f, 0f);
                    light.intensity = 1.2f;
                    light.color = new Color(1f, 0.94f, 0.86f);
                }
            var volumeObject = new GameObject("Course Exposure", typeof(Volume));
            var volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var exposure = profile.Add<ColorAdjustments>(true);
            exposure.postExposure.Override(0f);
            AssetDatabase.CreateAsset(profile, Root + "/Data/CourseExposure.asset");
            AssetDatabase.AddObjectToAsset(exposure, profile);
            volume.sharedProfile = profile;
        }

        static Material MakeMaterial(string name, Color color, float metallic = 0f)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.CreateAsset(mat, Root + "/Materials/" + name + ".mat");
            return mat;
        }

        static GameObject Box(string name, Vector3 position, Vector3 scale, Material material, Vector3 rotation = default, bool collision = true)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(course, false);
            obj.transform.position = position;
            obj.transform.localScale = scale;
            obj.transform.eulerAngles = rotation;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            obj.isStatic = true;
            return obj;
        }

        static void BuildCourse()
        {
            course = new GameObject("Test Course | Primitive Geometry").transform;
            concrete = MakeMaterial("Slate Concrete", new Color(0.25f, 0.3f, 0.33f));
            dark = MakeMaterial("Deep Graphite", new Color(0.045f, 0.066f, 0.08f), 0.2f);
            mint = MakeMaterial("Sea Glass", new Color(0.22f, 0.8f, 0.65f), 0.1f);
            orange = MakeMaterial("Safety Amber", new Color(1f, 0.48f, 0.14f));
            white = MakeMaterial("Chalk", new Color(0.8f, 0.87f, 0.87f));
            Box("Ground | 40 x 80 m", new Vector3(0f, -0.25f, 15f), new Vector3(40f, 0.5f, 80f), concrete);
            Box("Left perimeter", new Vector3(-20f, 0.5f, 15f), new Vector3(0.3f, 1f, 80f), dark);
            Box("Right perimeter", new Vector3(20f, 0.5f, 15f), new Vector3(0.3f, 1f, 80f), dark);
            Box("Far perimeter", new Vector3(0f, 0.5f, 55f), new Vector3(40f, 1f, 0.3f), dark);
            for (int z = -20; z < 55; z += 5)
            {
                Box("Floor grid", new Vector3(0f, 0.004f, z), new Vector3(39.7f, 0.008f, 0.025f), white, collision: false);
                Box("Sprint lane marker", new Vector3(0f, 0.011f, z), new Vector3(0.09f, 0.012f, 1.8f), mint, collision: false);
            }
            foreach (float x in new[] {-17f, -3f, 3f, 17f})
                Box("Lane edge", new Vector3(x, 0.008f, 15f), new Vector3(0.055f, 0.012f, 76f), mint, collision: false);

            // A clear central lane permits sustained sprint / exhaustion testing.
            Sign("FIELD STATION / 01", "LOCOMOTION LAB\nWALK  /  SPRINT  /  EXPLORE", new Vector3(0f, 3.8f, -5f), 5.6f);
            var entrancePost = GameObject.Find("FIELD STATION / 01 / post");
            entrancePost.transform.position += Vector3.left * 3.2f;
            Box("Entrance right post", new Vector3(3.2f, 1.9f, -4.9f), new Vector3(0.12f, 3.8f, 0.12f), dark);
            Sign("01 / CLEARANCE", "HOLD CTRL OR C\n1.25 m CLEARANCE", new Vector3(-10f, 2.6f, 2f));
            Box("Low tunnel roof | underside 1.25 m", new Vector3(-10f, 1.5f, 8f), new Vector3(4f, 0.5f, 8f), dark);
            Box("Tunnel left wall", new Vector3(-12f, 0.65f, 8f), new Vector3(0.25f, 1.3f, 8f), concrete);
            Box("Tunnel right wall", new Vector3(-8f, 0.65f, 8f), new Vector3(0.25f, 1.3f, 8f), concrete);
            for (int z = 4; z <= 12; z += 2)
                Box("Tunnel hazard stripe", new Vector3(-10f, 1.255f, z), new Vector3(4f, 0.02f, 0.14f), orange, collision: false);

            Sign("02 / TRAVERSE", "SPACE TO JUMP\n0.35 / 0.65 / 1.0 m", new Vector3(10f, 2.5f, 2f));
            for (int i = 0; i < 3; i++)
            {
                float h = new[] {0.35f, 0.65f, 1f}[i];
                Box("Jump block " + (i + 1), new Vector3(10f, h / 2f, 6f + i * 3f), new Vector3(3.5f, h, 1.6f), concrete);
                Box("Jump block trim", new Vector3(10f, h + 0.008f, 5.3f + i * 3f), new Vector3(3.5f, 0.014f, 0.13f), orange, collision: false);
            }

            Sign("03 / ELEVATION", "22 DEGREE RAMP\nSTEP OFF TO TEST LANDING", new Vector3(-10f, 2.5f, 17f));
            // Raised end joins a 3 m platform; the low end meets the floor without a step.
            Box("Walkable ramp | 22 degrees", new Vector3(-10f, 1.38f, 24f), new Vector3(4f, 0.3f, 8f), concrete, new Vector3(-22f, 0f, 0f));
            Box("Landing platform | 3 m", new Vector3(-10f, 1.5f, 30f), new Vector3(6f, 3f, 5f), dark);
            Box("Platform edge marking", new Vector3(-10f, 3.008f, 32.3f), new Vector3(6f, 0.016f, 0.15f), orange, collision: false);
            Box("Steep slope | 60 degrees", new Vector3(-16f, 1.2f, 24f), new Vector3(2f, 0.2f, 3f), orange, new Vector3(-60f, 0f, 0f));

            Sign("04 / NIGHT CHECK", "E PICKUP  /  LMB USE\nENTER THE SHELTER", new Vector3(10f, 2.5f, 19f));
            Box("Flashlight bay left", new Vector3(6.5f, 2f, 29f), new Vector3(0.4f, 4f, 12f), dark);
            Box("Flashlight bay right", new Vector3(13.5f, 2f, 29f), new Vector3(0.4f, 4f, 12f), dark);
            Box("Flashlight bay roof", new Vector3(10f, 4f, 29f), new Vector3(7.4f, 0.4f, 12f), dark);
            Box("Flashlight bay rear", new Vector3(10f, 2f, 35f), new Vector3(7.4f, 4f, 0.4f), concrete);
            for (int i = 0; i < 3; i++)
                Box("Beam target " + i, new Vector3(8f + i * 2f, 1.5f, 34.75f), new Vector3(0.75f, 1.5f, 0.05f), i % 2 == 0 ? white : orange);
        }

        static void Sign(string title, string subtitle, Vector3 position, float width = 4f)
        {
            Box(title + " / board", position, new Vector3(width, 1.25f, 0.15f), dark);
            Box(title + " / accent", position + new Vector3(-width / 2f + 0.06f, 0f, -0.09f), new Vector3(0.08f, 1.25f, 0.04f), mint, collision: false);
            Box(title + " / post", new Vector3(position.x, position.y / 2f, position.z + 0.1f), new Vector3(0.12f, position.y, 0.12f), dark);
            var canvas = new GameObject(title, typeof(Canvas));
            canvas.transform.SetParent(course, false);
            canvas.transform.position = position + Vector3.back * 0.09f;
            canvas.transform.localScale = Vector3.one * 0.005f;
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width * 200f, 250f);
            var label = Label(canvas.transform, title, new Vector2(0.08f, 0.54f), new Vector2(0.96f, 0.92f), 32, FontStyle.Bold);
            label.color = new Color(0.45f, 0.93f, 0.8f);
            Label(canvas.transform, subtitle, new Vector2(0.08f, 0.06f), new Vector2(0.96f, 0.55f), 25);
        }

        static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            var rect = obj.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Text Label(Transform parent, string text, Vector2 min, Vector2 max, int size, FontStyle style = FontStyle.Normal)
        {
            var label = Rect(parent, "Label - " + text.Split('\n')[0], min, max).gameObject.AddComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size;
            label.fontStyle = style;
            label.color = new Color(0.89f, 0.94f, 0.95f);
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }

        static void BuildHUD(PlayerMovement movement, PlayerStamina stamina, PlayerController controller)
        {
            var canvas = new GameObject("Field Station HUD", typeof(Canvas), typeof(CanvasScaler));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var top = Label(canvas.transform, "FIELD STATION     /     01", new Vector2(0.03f, 0.91f), new Vector2(0.5f, 0.97f), 22, FontStyle.Bold);
            top.color = new Color(0.4f, 0.92f, 0.78f);
            Label(canvas.transform, "CONTROLLER PROVING GROUND", new Vector2(0.03f, 0.875f), new Vector2(0.5f, 0.92f), 15);
            var panel = Rect(canvas.transform, "Telemetry Panel", new Vector2(0.025f, 0.035f), new Vector2(0.255f, 0.195f));
            panel.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.05f, 0.8f);
            var staminaText = Label(panel, "STAMINA", new Vector2(0.06f, 0.63f), new Vector2(0.94f, 0.94f), 18, FontStyle.Bold);
            var track = Rect(panel, "Stamina Track", new Vector2(0.06f, 0.52f), new Vector2(0.94f, 0.56f));
            track.gameObject.AddComponent<Image>().color = new Color(0.18f, 0.25f, 0.27f);
            var fill = Rect(track, "Stamina Fill", Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.color = new Color(0.32f, 0.86f, 0.72f);
            // Filled images need a sprite; use the built-in white UI sprite.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var status = Label(panel, "IDLE", new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.45f), 16);
            var controls = Label(canvas.transform, "WASD  move     SHIFT  sprint     CTRL / C  crouch\nSPACE  jump     E  pickup     Q  drop     F  flashlight     LMB  primary use", new Vector2(0.40f, 0.035f), new Vector2(0.97f, 0.12f), 18);
            controls.alignment = TextAnchor.MiddleRight;
            var cursorHint = Label(canvas.transform, "CLICK TO EXPLORE", new Vector2(0.68f, 0.91f), new Vector2(0.97f, 0.97f), 16);
            cursorHint.alignment = TextAnchor.MiddleRight;
            var crosshair = Rect(canvas.transform, "Reticle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            crosshair.sizeDelta = new Vector2(3f, 3f);
            crosshair.gameObject.AddComponent<Image>().color = new Color(0.9f, 1f, 0.97f, 0.75f);
            var hud = canvas.AddComponent<PlayerHUD>();
            Assign(hud, "movement", movement, "stamina", stamina, "controller", controller,
                "staminaFill", fill, "status", status, "staminaText", staminaText, "cursorHint", cursorHint);
            AddHUDContrast(canvas.transform);
        }

        static void AddHUDContrast(Transform canvas)
        {
            if (canvas.Find("Header Backdrop")) return;
            var header = Rect(canvas, "Header Backdrop", new Vector2(0.025f, 0.875f), new Vector2(0.27f, 0.975f));
            header.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.05f, 0.8f);
            header.SetAsFirstSibling();
            var hints = Rect(canvas, "Controls Backdrop", new Vector2(0.60f, 0.035f), new Vector2(0.975f, 0.125f));
            hints.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.05f, 0.8f);
            hints.SetAsFirstSibling();
            foreach (var text in canvas.GetComponentsInChildren<Text>())
            {
                var shadow = text.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
                shadow.effectDistance = new Vector2(1f, -1f);
            }
        }

        [MenuItem("Tools/Survival FP/Polish Course Layout")]
        public static void PolishCourse()
        {
            if (SceneManager.GetActiveScene().path != ScenePath || EditorApplication.isPlaying) return;
            foreach (string name in new[] { "FIELD STATION / 01", "FIELD STATION / 01 / board", "FIELD STATION / 01 / accent" })
            {
                var obj = UnityEngine.Object.FindObjectsByType<Transform>().FirstOrDefault(t => t.name == name && !t.GetComponent<Text>())?.gameObject;
                if (obj) { var p = obj.transform.position; p.y = 3.8f; obj.transform.position = p; }
            }
            var post = GameObject.Find("FIELD STATION / 01 / post");
            if (post)
            {
                post.transform.position = new Vector3(-3.2f, 1.9f, -4.9f);
                post.transform.localScale = new Vector3(0.12f, 3.8f, 0.12f);
                if (!GameObject.Find("Entrance right post"))
                {
                    var second = UnityEngine.Object.Instantiate(post, post.transform.parent);
                    second.name = "Entrance right post";
                    second.transform.position = new Vector3(3.2f, 1.9f, -4.9f);
                }
            }
            var signCanvas = UnityEngine.Object.FindObjectsByType<Canvas>().FirstOrDefault(c => c.name == "FIELD STATION / 01");
            if (signCanvas)
            {
                signCanvas.transform.position = new Vector3(0f, 3.8f, -5.09f);
                foreach (var label in signCanvas.GetComponentsInChildren<Text>())
                {
                    label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                    label.name = "Label - " + label.text.Split('\n')[0];
                }
            }
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional)
                {
                    light.intensity = 1.2f;
                    light.color = new Color(1f, 0.94f, 0.86f);
                }
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Data/CourseExposure.asset");
            if (profile && profile.TryGet<ColorAdjustments>(out var exposure))
            {
                exposure.postExposure.Override(0f);
                EditorUtility.SetDirty(exposure);
                AssetDatabase.SaveAssets();
            }
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHUD>();
            if (hud) AddHUDContrast(hud.transform);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
    }
}
