using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivalFP.EditorTools
{
    // Bakes each Mansion room prefab's lantern lighting into lightmaps that travel with the prefab.
    //
    // Every room is baked alone in an empty scene, once per mood (Lit, Dim, Dark), with the Mansion atmosphere's
    // ambient. The shell and furniture receive lightmaps. Furniture is baked without casting shadows, because the
    // randomizer may switch it off and must not leave a ghost shadow behind. Lightmaps are non-directional, so they
    // stay correct under the generator's 90-degree rotations. The result is stored on RoomBakedLighting and
    // restored when the room spawns.
    //
    // Rerun after splitting furniture (the splitter replaces the meshes and clears the old bake).
    public static class RoomLightBaker
    {
        const string Output = "Assets/Game/Art/Mansion/Current/Lightmaps";
        static readonly LightingMood[] Moods = { LightingMood.Lit, LightingMood.Dim, LightingMood.Dark };
        const float GutteringBake = .5f;

        [MenuItem("Survival FP/Lighting/Bake Mansion Room Lighting (All Rooms)")]
        static void BakeAll() => Debug.Log("Room lighting bake:\n" + Bake(RoomFurnitureSplitter.MansionRoomPaths().ToList()));

        [MenuItem("Survival FP/Lighting/Bake Mansion Room Lighting (Selected Room Prefabs)")]
        static void BakeSelected() => Debug.Log("Room lighting bake:\n" + Bake(Selection.objects.Select(AssetDatabase.GetAssetPath)
            .Where(p => p.EndsWith(".prefab") && AssetDatabase.LoadAssetAtPath<RoomModule>(p)).ToList()));

        public static string Bake(List<string> paths)
        {
            if (EditorApplication.isPlaying) return "Leave Play Mode before baking.";
            EditorSceneManager.SaveOpenScenes();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            KeepLightmapShaderVariants();
            var report = new List<string>();
            try { foreach (var path in paths) report.Add(BakeRoom(path)); }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                // Removed on disk: deleting the bake scenes' lighting data through the AssetDatabase asks for confirmation.
                FileUtil.DeleteFileOrDirectory(Output + "/_Bake"); FileUtil.DeleteFileOrDirectory(Output + "/_Bake.meta");
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            }
            return string.Join("\n", report);
        }

        static string BakeRoom(string path)
        {
            double started = EditorApplication.timeSinceStartup;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            string name = prefab.name;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var room = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var module = room.GetComponent<RoomModule>();

            // Lightmapped: the room shell and the split furniture (their meshes carry no usable lightmap UVs
            // from the FBX, so they are generated here).
            var environment = room.GetComponentsInChildren<MeshRenderer>(true).Where(IsEnvironment).ToList();
            foreach (var mesh in environment.Select(r => r.GetComponent<MeshFilter>().sharedMesh).Distinct())
            {
                UnwrapParam.SetDefaults(out var unwrap); unwrap.packMargin = 4f / 1024f;
                Unwrapping.GenerateSecondaryUVSet(mesh, unwrap);
                EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();
            var furniture = new HashSet<Renderer>((module.randomizedStructures ?? new RandomizedStructure[0])
                .Where(s => s.target).SelectMany(s => s.target.GetComponentsInChildren<Renderer>(true)));
            foreach (var r in room.GetComponentsInChildren<Renderer>(true))
            {
                bool lightmapped = r is MeshRenderer m && environment.Contains(m);
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, lightmapped ? StaticEditorFlags.ContributeGI : 0);
                if (!lightmapped) continue;
                ((MeshRenderer)r).receiveGI = ReceiveGI.Lightmaps;
                if (furniture.Contains(r)) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var t in room.GetComponentsInChildren<Transform>(true)) if (!t.gameObject.activeSelf && environment.Any(e => e.transform.IsChildOf(t))) t.gameObject.SetActive(true);

            var lanterns = room.GetComponentsInChildren<LanternLight>(true);
            foreach (var lantern in lanterns)
            {
                var lamp = lantern.GetComponentInChildren<Light>(true);
                lamp.type = LightType.Point; lamp.lightmapBakeType = LightmapBakeType.Baked;
                lamp.color = lantern.FlameColour; lamp.range = lantern.Range;
                lamp.shadows = LightShadows.Soft; lamp.shapeRadius = .04f; lamp.bounceIntensity = 1f;
            }

            var atmosphere = AssetDatabase.FindAssets("t:MapAtmosphere").Select(g => AssetDatabase.LoadAssetAtPath<MapAtmosphere>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(a => a.name.Contains("Mansion"));
            if (atmosphere) atmosphere.Apply();
            Lightmapping.lightingSettings = new LightingSettings
            {
                bakedGI = true, realtimeGI = false, lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
                lightmapResolution = 10, lightmapMaxSize = 1024, lightmapPadding = 4, directionalityMode = LightmapsMode.NonDirectional,
                maxBounces = 2, directSampleCount = 32, indirectSampleCount = 256, environmentSampleCount = 64,
                lightmapCompression = LightmapCompression.NormalQuality, ao = false, mixedBakeMode = MixedLightingMode.IndirectOnly,
            };
            // Light probes for dynamic objects: a grid through the room's occupancy volumes, skipping solid spots.
            Physics.SyncTransforms();
            var probePositions = ProbeGrid(module).Where(p => !Physics.CheckSphere(p, .18f, ~0, QueryTriggerInteraction.Ignore)).ToArray();
            var probeGroup = new GameObject("Bake Probes").AddComponent<LightProbeGroup>();
            probeGroup.probePositions = probePositions;
            Directory.CreateDirectory(Output + "/_Bake"); Directory.CreateDirectory(Output + "/" + name);
            EditorSceneManager.SaveScene(scene, Output + "/_Bake/" + name + ".unity");

            var variants = new List<(LightingMood mood, string[] maps, (string path, int index, Vector4 so)[] entries, LanternLight.State[] states, Color[] cube)>();
            // A room with an authored mood only ever uses that one; Auto rooms can roll any of them.
            foreach (var mood in module.lighting == LightingMood.Auto ? Moods : new[] { module.lighting })
            {
                var states = lanterns.Select((l, i) => l.AlwaysBurning ? LanternLight.State.Lit : LanternLight.StateFor(mood, Roll(name, i))).ToArray();
                for (int i = 0; i < lanterns.Length; i++)
                {
                    var lamp = lanterns[i].GetComponentInChildren<Light>(true);
                    lamp.enabled = states[i] != LanternLight.State.Out;
                    lamp.intensity = lanterns[i].Intensity * (states[i] == LanternLight.State.Guttering ? GutteringBake : 1f);
                }
                Lightmapping.Clear();
                if (!Lightmapping.Bake()) { EditorSceneManager.SaveScene(scene); return name + ": bake failed (" + mood + ")"; }
                var maps = new string[LightmapSettings.lightmaps.Length];
                for (int i = 0; i < maps.Length; i++)
                {
                    maps[i] = Output + "/" + name + "/" + name + " " + mood + " " + i + ".exr";
                    AssetDatabase.DeleteAsset(maps[i]);
                    AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(LightmapSettings.lightmaps[i].lightmapColor), maps[i]);
                }
                var entries = environment.Where(r => r.lightmapIndex >= 0)
                    .Select(r => (AnimationUtility.CalculateTransformPath(r.transform, room.transform), r.lightmapIndex, r.lightmapScaleOffset)).ToArray();
                // Probes again with the lanterns as Mixed (indirect only): dynamic objects already get the lanterns'
                // direct light in real time, so their probes must hold only the bounce, or they are lit twice.
                foreach (var lantern in lanterns) lantern.GetComponentInChildren<Light>(true).lightmapBakeType = LightmapBakeType.Mixed;
                bool probesBaked = Lightmapping.Bake();
                var cube = probesBaked ? ProbeCubes(probePositions) : new Color[probePositions.Length * 6];
                foreach (var lantern in lanterns) lantern.GetComponentInChildren<Light>(true).lightmapBakeType = LightmapBakeType.Baked;
                variants.Add((mood, maps, entries, states, cube));
            }
            // Saved so that leaving the throwaway bake scene never asks to save it.
            EditorSceneManager.SaveScene(scene);
            // Drop lightmaps left from an earlier bake that produced more pages.
            foreach (var old in AssetDatabase.FindAssets("t:Texture2D", new[] { Output + "/" + name }).Select(AssetDatabase.GUIDToAssetPath))
                if (!variants.Any(v => v.maps.Contains(old))) AssetDatabase.DeleteAsset(old);
            AssetDatabase.Refresh();

            var contents = PrefabUtility.LoadPrefabContents(path);
            long bytes = 0;
            try
            {
                var bake = contents.GetComponent<RoomBakedLighting>();
                if (!bake) bake = contents.AddComponent<RoomBakedLighting>();
                bake.lanterns = contents.GetComponentsInChildren<LanternLight>(true);
                bake.variants = variants.Select(v => new RoomBakedLighting.Variant
                {
                    mood = v.mood,
                    lightmaps = v.maps.Select(AssetDatabase.LoadAssetAtPath<Texture2D>).ToArray(),
                    renderers = v.entries.Select(e => new RoomBakedLighting.BakedRenderer { renderer = contents.transform.Find(e.path)?.GetComponent<Renderer>(), lightmap = e.index, scaleOffset = e.so })
                        .Where(e => e.renderer).ToArray(),
                    lanternStates = v.states,
                    probes = probePositions,
                    probeCube = v.cube,
                }).ToArray();
                foreach (var map in bake.variants.SelectMany(v => v.lightmaps)) if (map) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(map);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return name + ": " + variants.Count + " moods, " + variants.Sum(v => v.maps.Length) + " lightmaps (" + (bytes / 1048576f).ToString("F1") + " MB), "
                + environment.Count + " lightmapped renderers, " + probePositions.Length + " probes, " + lanterns.Length + " lanterns, "
                + string.Join(" / ", variants.Select(v => v.mood + " " + v.states.Count(s => s != LanternLight.State.Out) + " burning"))
                + ", " + (EditorApplication.timeSinceStartup - started).ToString("F0") + " s";
        }

        // Every 2.5 m across each occupancy volume, from chest height up through tall rooms (galleries, stairs).
        static IEnumerable<Vector3> ProbeGrid(RoomModule module)
        {
            const float step = 2.5f, inset = .5f;
            foreach (var volume in module.OccupancyVolumes)
            {
                var min = volume.min + new Vector3(inset, 0, inset); var max = volume.max - new Vector3(inset, 0, inset);
                int nx = Mathf.Max(1, Mathf.RoundToInt((max.x - min.x) / step)), nz = Mathf.Max(1, Mathf.RoundToInt((max.z - min.z) / step));
                for (float y = volume.min.y + .9f; y < volume.max.y - .4f; y += 1.3f)
                    for (int ix = 0; ix <= nx; ix++)
                        for (int iz = 0; iz <= nz; iz++)
                            yield return new Vector3(Mathf.Lerp(min.x, max.x, ix / (float)nx), y, Mathf.Lerp(min.z, max.z, iz / (float)nz));
            }
        }

        // The light arriving at each probe from the six axis directions, read from the scene's baked probes.
        static Color[] ProbeCubes(Vector3[] positions)
        {
            var baked = LightmapSettings.lightProbes;
            var result = new Color[positions.Length * 6];
            if (!baked) return result;
            var bakedPositions = baked.positions; var coefficients = baked.bakedProbes;
            var directions = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var colours = new Color[6];
            for (int i = 0; i < positions.Length; i++)
            {
                int match = 0; float best = float.MaxValue;
                for (int j = 0; j < bakedPositions.Length; j++) { float d = (bakedPositions[j] - positions[i]).sqrMagnitude; if (d < best) { best = d; match = j; } }
                coefficients[match].Evaluate(directions, colours);
                for (int f = 0; f < 6; f++) result[i * 6 + f] = colours[f];
            }
            return result;
        }

        static bool IsEnvironment(MeshRenderer r)
        {
            var filter = r.GetComponent<MeshFilter>();
            return filter && filter.sharedMesh && !r.GetComponentInParent<LanternLight>(true)
                && AssetDatabase.GetAssetPath(filter.sharedMesh).Contains("/Split/");
        }

        // Fixed per lantern in the prefab, so every copy of a room in the same mood burns the same way.
        static float Roll(string room, int index)
        {
            float v = Mathf.Sin((index + 1) * 12.9898f + room.Length * 78.233f) * 43758.5453f;
            v -= Mathf.Floor(v); v = v * 13.7f; return v - Mathf.Floor(v);
        }

        // No build scene contains a bake (rooms bring theirs at runtime), so automatic stripping would remove the
        // lightmap shader variants from builds. Keep them explicitly.
        static void KeepLightmapShaderVariants()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset"));
            settings.FindProperty("m_LightmapStripping").intValue = 1;
            settings.FindProperty("m_LightmapKeepPlain").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
