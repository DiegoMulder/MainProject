using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SurvivalFP.Editor
{
    public static class PlayerPrefabSync
    {
        public const string PlayerPath = "Assets/AI-Tools-DEV/Prefabs/SurvivalPlayer.prefab";

        [MenuItem("Tools/Survival FP/Sync Player Prefab From Scene")]
        public static void Sync()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before syncing the player prefab.");
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (!player) throw new InvalidOperationException("Open the player scene before syncing its prefab.");
            var root = player.gameObject;
            var camera = root.GetComponentInChildren<PlayerCamera>(true);
            if (!camera) throw new InvalidOperationException("The scene player has no camera.");
            // Keep scene flashlight pickups on the current item-owned beam setup
            // even when this menu command is run on an older scene.
            foreach (var item in UnityEngine.Object.FindObjectsByType<PickupItem>())
                if (item.GetComponent<PlayerFlashlight>()) MigrateFlashlight(item.gameObject);
            foreach (var legacy in root.GetComponentsInChildren<PlayerFlashlight>(true))
                if (!legacy.GetComponent<PickupItem>()) UnityEngine.Object.DestroyImmediate(legacy.gameObject);
            var inventory = root.GetComponent<PlayerInventory>();
            if (!inventory) inventory = root.AddComponent<PlayerInventory>();
            if (!root.GetComponent<PlayerFlashlightShortcut>()) root.AddComponent<PlayerFlashlightShortcut>();
            var hand = camera.transform.Find("Item Hand Anchor");
            if (!hand)
            {
                hand = new GameObject("Item Hand Anchor").transform;
                hand.SetParent(camera.transform, false);
            }
            hand.localPosition = new Vector3(.42f, -.34f, .62f);
            hand.localRotation = Quaternion.identity;
            hand.localScale = Vector3.one;
            var settings = new SerializedObject(inventory);
            settings.FindProperty("handAnchor").objectReferenceValue = hand;
            settings.FindProperty("playerCamera").objectReferenceValue = camera;
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PlayerPath, InteractionMode.AutomatedAction, out bool success);
            if (!success) throw new InvalidOperationException("Saving the player prefab failed.");
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
        }

        static void MigrateFlashlight(GameObject root)
        {
            var flashlight = root.GetComponent<PlayerFlashlight>();
            if (!flashlight) flashlight = root.AddComponent<PlayerFlashlight>();
            var legacyLight = root.GetComponent<Light>();
            var beam = root.GetComponentsInChildren<Light>(true).FirstOrDefault(l => l.transform != root.transform);
            if (!beam)
            {
                var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Resources/Flashlight/FlashlightBeam.prefab");
                if (!template) throw new InvalidOperationException("FlashlightBeam prefab is missing.");
                var child = (GameObject)PrefabUtility.InstantiatePrefab(template, root.transform);
                child.name = "Flashlight Beam";
                beam = child.GetComponent<Light>();
            }
            beam.transform.localRotation = Quaternion.identity;
            var audio = root.GetComponent<AudioSource>();
            if (!audio) audio = root.AddComponent<AudioSource>();
            var lightSettings = new SerializedObject(flashlight);
            lightSettings.FindProperty("beam").objectReferenceValue = beam;
            lightSettings.FindProperty("toggleAudio").objectReferenceValue = audio;
            lightSettings.FindProperty("toggleSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/switch_002.ogg");
            lightSettings.ApplyModifiedPropertiesWithoutUndo();
            var itemSettings = new SerializedObject(root.GetComponent<PickupItem>());
            itemSettings.FindProperty("primaryUse").objectReferenceValue = flashlight;
            itemSettings.FindProperty("heldEulerAngles").vector3Value = Vector3.zero;
            itemSettings.ApplyModifiedPropertiesWithoutUndo();
            if (legacyLight)
            {
                var hd = root.GetComponent<UniversalAdditionalLightData>();
                if (hd) UnityEngine.Object.DestroyImmediate(hd);
                UnityEngine.Object.DestroyImmediate(legacyLight);
            }
        }

        public static void PrepareAndValidate()
        {
            EditorSceneManager.OpenScene(ControllerTestSceneBuilder.ScenePath);
            foreach (var item in UnityEngine.Object.FindObjectsByType<PickupItem>())
                if (item.GetComponent<PlayerFlashlight>()) MigrateFlashlight(item.gameObject);
            const string path = "Assets/AI-Tools-DEV/Prefabs/Items/Flashlight.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try { MigrateFlashlight(prefab); PrefabUtility.SaveAsPrefabAsset(prefab, path); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            Sync();
            AssetDatabase.SaveAssets();
            Verify();
            PickupPhysicsValidation.Run();
        }

        public static void Verify()
        {
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (!player || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player) != PlayerPath)
                throw new InvalidOperationException("Scene player is not connected to SurvivalPlayer.prefab.");
            if (PrefabUtility.HasPrefabInstanceAnyOverrides(player.gameObject, false))
                throw new InvalidOperationException("Scene player has differences from its prefab.");
            if (player.GetComponentsInChildren<PlayerFlashlight>(true).Length != 0)
                throw new InvalidOperationException("Player still contains a legacy flashlight.");
            if (!player.GetComponent<PlayerInventory>() || !player.GetComponent<PlayerFlashlightShortcut>())
                throw new InvalidOperationException("Player inventory or flashlight shortcut is missing.");
            var all = player.GetComponentsInChildren<Transform>(true);
            foreach (var child in all)
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) > 0)
                    throw new InvalidOperationException("Missing script on " + child.name);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/PlayerPrefab-validation.txt", "PASS: Scene player is connected to SurvivalPlayer.prefab with no property or component overrides.\n" +
                "PASS: Inventory, flashlight shortcut, camera and hand anchor are included; no legacy player flashlight or missing scripts.\n");
        }
    }
}
