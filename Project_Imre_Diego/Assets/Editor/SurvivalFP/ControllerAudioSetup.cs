using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivalFP.Editor
{
    [InitializeOnLoad]
    public static class ControllerAudioSetup
    {
        const string Folder = "Assets/SurvivalFP/Audio/";
        const string Request = "Library/SurvivalFP.audio-request";
        static ControllerAudioSetup() => EditorApplication.update += Update;
        static void Update()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try { Install(); File.WriteAllText("Library/SurvivalFP.audio-result.txt", "SUCCESS"); }
            catch (Exception ex) { File.WriteAllText("Library/SurvivalFP.audio-result.txt", ex.ToString()); Debug.LogException(ex); }
        }
        [MenuItem("Tools/Survival FP/Install Player Audio")]
        public static void Install()
        {
            foreach (string path in Directory.GetFiles(Folder, "*.ogg"))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.forceToMono = true;
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
            foreach (var motor in UnityEngine.Object.FindObjectsByType<PlayerMovement>())
            {
                Configure(motor.gameObject);
                EditorSceneManager.MarkSceneDirty(motor.gameObject.scene);
            }
            // Preserve all current scene edits while saving the newly assigned references.
            EditorSceneManager.SaveOpenScenes();
            const string prefabPath = "Assets/SurvivalFP/Prefabs/SurvivalPlayer.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try { Configure(prefab); PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
        }
        static void Configure(GameObject player)
        {
            var component = player.GetComponent<PlayerAudio>() ?? player.AddComponent<PlayerAudio>();
            var feet = Source(player.transform, "Footstep Audio");
            var body = Source(player.transform, "Body Audio");
            ControllerTestSceneBuilder.Assign(component, "movement", player.GetComponent<PlayerMovement>(), "footsteps", feet,
                "body", body, "jumpClip", Clip("footstep_carpet_000"), "landingClip", Clip("impactSoft_heavy_000"));
            var serialized = new SerializedObject(component);
            var clips = serialized.FindProperty("footstepClips");
            clips.arraySize = 5;
            for (int i = 0; i < 5; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = Clip("footstep_concrete_00" + i);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // Audio belongs to the flashlight pickup. Do not bind the legacy,
            // disabled camera-mounted copy that may still exist in old scenes.
            var torch = UnityEngine.Object.FindObjectsByType<PlayerFlashlight>(FindObjectsInactive.Include)
                .FirstOrDefault(f => f.GetComponent<PickupItem>() && f.GetComponent<PickupItem>().kind == PickupKind.Flashlight);
            if (torch) ControllerTestSceneBuilder.Assign(torch, "toggleSound", Clip("switch_002"));
        }
        static AudioClip Clip(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + name + ".ogg");
            if (!clip) throw new InvalidOperationException("Missing sound: " + name);
            return clip;
        }
        static AudioSource Source(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (!child) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            var source = child.gameObject.GetComponent<AudioSource>();
            if (!source) source = child.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.volume = 1f;
            return source;
        }
    }
}
