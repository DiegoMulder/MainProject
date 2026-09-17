using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SurvivalFP;

namespace SurvivalFP.Editor
{
    [InitializeOnLoad]
    public static class InventorySetup
    {
        const string Request = "Library/SurvivalFP.inventory-request";
        const string PickupPrefabPath = "Assets/AI-Tools-DEV/Prefabs/PickupItem.prefab";
        const string PickupablesFolder = "Assets/AI-Tools-DEV/Prefabs/Items";
        static InventorySetup() => EditorApplication.update += Check;

        static void Check()
        {
            if (!System.IO.File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            System.IO.File.Delete(Request);
            try { Install(); System.IO.File.WriteAllText("Library/SurvivalFP.inventory-result.txt", "SUCCESS"); }
            catch (System.Exception e) { System.IO.File.WriteAllText("Library/SurvivalFP.inventory-result.txt", e.ToString()); Debug.LogException(e); }
        }

        public static void Install()
        {
            var player = Object.FindAnyObjectByType<PlayerController>();
            if (!player)
            {
                EditorSceneManager.OpenScene(ControllerTestSceneBuilder.ScenePath);
                player = Object.FindAnyObjectByType<PlayerController>();
            }
            if (!player) throw new System.InvalidOperationException("CharacterController_Test has no PlayerController.");

            // The flashlight now belongs to its PickupItem. Remove the old camera-mounted copy.
            foreach (var old in player.GetComponentsInChildren<PlayerFlashlight>(true))
                if (!old.GetComponent<PickupItem>()) Object.DestroyImmediate(old.gameObject);

            var cam = player.GetComponentInChildren<PlayerCamera>().transform;
            var hand = cam.Find("Item Hand Anchor");
            if (!hand) hand = new GameObject("Item Hand Anchor").transform;
            hand.SetParent(cam, false);
            hand.localPosition = new Vector3(.42f, -.34f, .62f);
            hand.localRotation = Quaternion.identity;
            var inv = player.GetComponent<PlayerInventory>();
            if (!inv) inv = player.gameObject.AddComponent<PlayerInventory>();
            ControllerTestSceneBuilder.Assign(inv, "handAnchor", hand, "playerCamera", player.GetComponentInChildren<PlayerCamera>());
            var invSettings = new SerializedObject(inv);
            invSettings.FindProperty("pickupDistance").floatValue = 5f;
            invSettings.ApplyModifiedPropertiesWithoutUndo();

            MakePickup("Pickup Flashlight", PickupKind.Flashlight, new Vector3(1.5f, 1f, -10f), new Color(1f, .75f, .2f), PrimitiveType.Capsule, .18f);
            MakePickup("Pickup Cube", PickupKind.Cube, new Vector3(3f, .5f, -10f), new Color(.2f, .75f, 1f), PrimitiveType.Cube, .65f);
            MakePickup("Pickup Capsule", PickupKind.Capsule, new Vector3(4.5f, 1f, -10f), new Color(.9f, .25f, .7f), PrimitiveType.Capsule, .45f);
            EnsurePickupPrefab();
            EnsurePickupablePrefabs();

            var hud = Object.FindAnyObjectByType<PlayerHUD>();
            if (hud)
            {
                var canvas = hud.transform;
                var text = canvas.Find("Inventory Readout")?.GetComponent<UnityEngine.UI.Text>();
                if (!text)
                {
                    text = ControllerTestSceneBuilder.Label(canvas, "ITEMS [1] [2] [3]   E PICKUP   Q DROP   F FLASHLIGHT   SCROLL SWITCH", new Vector2(.40f, .14f), new Vector2(.98f, .21f), 17);
                    text.name = "Inventory Readout";
                    text.alignment = TextAnchor.MiddleRight;
                }
                ControllerTestSceneBuilder.Assign(hud, "inventory", inv, "inventoryText", text);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            PlayerPrefabSync.Sync();
            AssetDatabase.SaveAssets();
        }

        static void MakePickup(string name, PickupKind kind, Vector3 position, Color color, PrimitiveType primitive, float scale)
        {
            var o = GameObject.Find(name);
            if (!o)
            {
                o = GameObject.CreatePrimitive(primitive);
                o.name = name;
                o.transform.position = position;
                o.transform.localScale = Vector3.one * scale;
                o.GetComponent<Renderer>().sharedMaterial = MakeMat(name + " Material", color);
            }
            var p = o.GetComponent<PickupItem>();
            if (!p) p = o.AddComponent<PickupItem>();
            p.kind = kind;
            p.displayName = kind.ToString();
            var body = o.GetComponent<Rigidbody>();
            if (!body) body = o.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;

            if (kind != PickupKind.Flashlight) return;
            // Keep the beam on a child so the item can still be a normal mesh/collider
            // root and so the same setup works for runtime-created pickups.
            var beam = o.GetComponentInChildren<Light>(true);
            if (!beam)
            {
                var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Resources/Flashlight/FlashlightBeam.prefab");
                if (template)
                {
                    var beamObject = (GameObject)PrefabUtility.InstantiatePrefab(template, o.transform);
                    beamObject.name = "Flashlight Beam";
                    beam = beamObject.GetComponentInChildren<Light>(true);
                }
            }
            if (!beam)
            {
                var beamObject = new GameObject("Flashlight Beam");
                beamObject.transform.SetParent(o.transform, false);
                beam = beamObject.AddComponent<Light>();
            }
            if (!beam) throw new System.InvalidOperationException("Could not create the flashlight beam Light component.");
            beam.type = LightType.Spot;
            beam.range = 40f;
            beam.spotAngle = 62f;
            beam.innerSpotAngle = 32f;
            beam.color = new Color(1f, .94f, .82f);
            beam.shadows = LightShadows.Soft;
            beam.shadowBias = .02f;
            beam.shadowNormalBias = .1f;
            beam.enabled = false;
            var audio = o.GetComponent<AudioSource>();
            if (!audio) audio = o.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.volume = .35f;
            var flashlight = o.GetComponent<PlayerFlashlight>();
            if (!flashlight) flashlight = o.AddComponent<PlayerFlashlight>();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/switch_002.ogg");
            ControllerTestSceneBuilder.Assign(flashlight, "beam", beam, "toggleAudio", audio, "toggleSound", clip);
            var settings = new SerializedObject(flashlight);
            settings.FindProperty("onIntensity").floatValue = 12.6953125f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            ControllerTestSceneBuilder.Assign(p, "primaryUse", flashlight);
        }

        static Material MakeMat(string name, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>("Assets/AI-Tools-DEV/Materials/" + name + ".mat");
            if (existing) return existing;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, "Assets/AI-Tools-DEV/Materials/" + name + ".mat");
            return mat;
        }

        static void EnsurePickupPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPrefabPath);
            if (existing)
            {
                var root = PrefabUtility.LoadPrefabContents(PickupPrefabPath);
                var body = root.GetComponent<Rigidbody>();
                if (!body) body = root.AddComponent<Rigidbody>();
                body.isKinematic = false;
                body.useGravity = true;
                PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }
            var o = GameObject.CreatePrimitive(PrimitiveType.Cube);
            o.name = "PickupItem Template";
            o.transform.localScale = Vector3.one * .5f;
            o.AddComponent<Rigidbody>();
            var p = o.AddComponent<PickupItem>();
            p.kind = PickupKind.Generic;
            p.displayName = "New Item";
            PrefabUtility.SaveAsPrefabAsset(o, PickupPrefabPath);
            Object.DestroyImmediate(o);
            AssetDatabase.ImportAsset(PickupPrefabPath);
        }

        static void EnsurePickupablePrefabs()
        {
            if (!AssetDatabase.IsValidFolder(PickupablesFolder))
                AssetDatabase.CreateFolder("Assets/AI-Tools-DEV/Prefabs", "Items");
            CreatePickupablePrefab("Flashlight", PickupKind.Flashlight, PrimitiveType.Capsule, .32f, new Color(1f, .75f, .2f));
            CreatePickupablePrefab("Cube", PickupKind.Cube, PrimitiveType.Cube, .65f, new Color(.2f, .75f, 1f));
            CreatePickupablePrefab("Capsule", PickupKind.Capsule, PrimitiveType.Capsule, .45f, new Color(.9f, .25f, .7f));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void CreatePickupablePrefab(string name, PickupKind kind, PrimitiveType primitive, float scale, Color color)
        {
            var path = PickupablesFolder + "/" + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path))
            {
                var existing = PrefabUtility.LoadPrefabContents(path);
                var existingBody = existing.GetComponent<Rigidbody>();
                if (!existingBody) existingBody = existing.AddComponent<Rigidbody>();
                existingBody.isKinematic = false;
                existingBody.useGravity = true;
                PrefabUtility.SaveAsPrefabAsset(existing, path);
                PrefabUtility.UnloadPrefabContents(existing);
                return;
            }

            var root = GameObject.CreatePrimitive(primitive);
            root.name = name;
            root.transform.localScale = Vector3.one * scale;
            var renderer = root.GetComponent<Renderer>();
            if (renderer) renderer.sharedMaterial = MakeMat(name + " Pickup Material", color);
            var pickup = root.AddComponent<PickupItem>();
            pickup.kind = kind;
            pickup.displayName = name;
            var body = root.GetComponent<Rigidbody>();
            if (!body) body = root.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (kind == PickupKind.Flashlight) ConfigureFlashlightPrefab(root, pickup);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(path);
        }

        static void ConfigureFlashlightPrefab(GameObject root, PickupItem pickup)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Resources/Flashlight/FlashlightBeam.prefab");
            Light beam = null;
            if (template)
            {
                var beamObject = (GameObject)PrefabUtility.InstantiatePrefab(template, root.transform);
                beamObject.name = "Flashlight Beam";
                beam = beamObject.GetComponentInChildren<Light>(true);
            }
            if (!beam)
            {
                var beamObject = new GameObject("Flashlight Beam");
                beamObject.transform.SetParent(root.transform, false);
                beam = beamObject.AddComponent<Light>();
            }
            if (!beam) throw new System.InvalidOperationException("Could not create the flashlight beam Light component.");
            beam.type = LightType.Spot;
            beam.range = 40f;
            beam.spotAngle = 62f;
            beam.innerSpotAngle = 32f;
            beam.color = new Color(1f, .94f, .82f);
            beam.shadows = LightShadows.Soft;
            beam.enabled = false;
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.volume = .35f;
            var flashlight = root.AddComponent<PlayerFlashlight>();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/switch_002.ogg");
            ControllerTestSceneBuilder.Assign(flashlight, "beam", beam, "toggleAudio", audio, "toggleSound", clip);
            var settings = new SerializedObject(flashlight);
            settings.FindProperty("onIntensity").floatValue = 12.6953125f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            ControllerTestSceneBuilder.Assign(pickup, "primaryUse", flashlight);
        }
    }
}
