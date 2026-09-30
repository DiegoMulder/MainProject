using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace SurvivalFP.EditorTools
{
    // Authors decorative plants into Mansion room prefabs. Plants are ordinary static children of the
    // room (not a spawning system and not randomized), so every client builds them identically.
    // Each spot is tested with real physics on a temporary copy of the room with every furniture
    // piece and the hiding closet present: a corner or wall spot, clear of doorways, stairs, item
    // spots and closet approaches, with the leaves not clipping walls or furniture.
    public static class RoomPlantPlacer
    {
        public const string PlantPrefabPath = "Assets/Game/Prefabs/Maps/Mansion/Props/Plant.prefab";
        const string PlantModelPath = "Assets/Game/Art/Mansion/Current/Plant.fbx";
        public const string AuthoredName = "Plant (Authored)";
        // Plants per room prefab. Rooms whose model already contains baked plants (T Junction,
        // Grand Hall) and staircases get none.
        static readonly Dictionary<string, int> PlantsPerRoom = new() { { "Small Room", 1 }, { "Standard Room", 2 }, { "Large Room", 2 }, { "Straight Hall", 1 }, { "Corner Hall", 1 } };
        const float PotRadius = .3f, LeafHalf = .44f, DoorwayClearance = 2.2f, MinSpacing = 3f;

        [MenuItem("Survival FP/Rooms/Create Plant Prefab")]
        public static GameObject CreatePlantPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlantModelPath);
            var root = new GameObject("Plant");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                visual.name = "Plant Visual";
                // Lightweight collision: a capsule around the pot, as tall as the plant. No leaf mesh collider.
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = 1; capsule.radius = PotRadius; capsule.height = .94f; capsule.center = new Vector3(0, .47f, 0);
                return PrefabUtility.SaveAsPrefabAsset(root, PlantPrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [MenuItem("Survival FP/Rooms/Place Authored Plants (All Mansion Rooms)")]
        static void PlaceAll()
        {
            var report = new List<string>();
            foreach (var path in RoomFurnitureSplitter.MansionRoomPaths()) report.Add(PlacePrefab(path));
            Debug.Log("Plant placement:\n" + string.Join("\n", report));
        }

        public static string PlacePrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { var report = Place(root); PrefabUtility.SaveAsPrefabAsset(root, path); return report; }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static string Place(GameObject contents)
        {
            var room = contents.GetComponent<RoomModule>();
            foreach (Transform child in contents.transform.Cast<Transform>().ToArray())
                if (child.name == AuthoredName) UnityEngine.Object.DestroyImmediate(child.gameObject);
            if (!room || room.staircase || !PlantsPerRoom.TryGetValue(contents.name, out int wanted)) return $"{contents.name}: no plants";
            var plantPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlantPrefabPath);
            if (!plantPrefab) plantPrefab = CreatePlantPrefab();

            var probe = new GameObject("__PlantProbe") { hideFlags = HideFlags.HideAndDontSave };
            probe.transform.position = new Vector3(0, -6000, 0);
            var chosen = new List<Vector3>();
            try
            {
                var copy = UnityEngine.Object.Instantiate(contents, probe.transform, false);
                copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity;
                var copyRoom = copy.GetComponent<RoomModule>();
                foreach (var e in copyRoom.randomizedStructures) if (e.target) e.target.SetActive(true);
                // Closets exist only at runtime, so stand one in at every marker.
                var avoid = new List<Vector3>();
                foreach (var marker in copy.GetComponentsInChildren<NetworkSpawnMarker>())
                {
                    if (!marker.prefab) continue;
                    var closet = UnityEngine.Object.Instantiate(marker.prefab.gameObject, marker.transform.position, marker.transform.rotation, probe.transform);
                    var hide = closet.GetComponent<ClosetHideout>();
                    if (hide) avoid.AddRange(new[] { hide.exitPosition.position, hide.entryPoint.position, hide.InvestigationPosition });
                }
                avoid.AddRange(copy.GetComponentsInChildren<ItemSpawnPoint>().Select(a => a.Approach));
                Physics.SyncTransforms();
                var origin = copy.transform.position;
                var occ = copyRoom.OccupancyVolumes.Aggregate(copyRoom.OccupancyVolumes[0], (a, b) => { a.Encapsulate(b); return a; });
                var spots = new List<(Vector3 local, float score, float yaw)>();
                for (float x = occ.min.x + .35f; x <= occ.max.x - .35f; x += .1f)
                    for (float z = occ.min.z + .35f; z <= occ.max.z - .35f; z += .1f)
                    {
                        var top = origin + new Vector3(x, 1.8f, z);
                        if (!Physics.Raycast(top, Vector3.down, out var floor, 2.2f)) continue;
                        float floorY = floor.point.y - origin.y;
                        if (floorY < -.05f || floorY > .1f) continue; // on the room floor, never on stairs, rugs or furniture
                        var foot = floor.point + Vector3.up * .01f;
                        var local = foot - origin;
                        if (copyRoom.connectors.Any(c => c && Vector2.Distance(new Vector2(c.transform.localPosition.x, c.transform.localPosition.z), new Vector2(local.x, local.z)) < DoorwayClearance)) continue;
                        if (avoid.Any(a => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(foot.x, foot.z)) < 1f)) continue;
                        // Pot and leaves must be clear: the plant never clips walls or furniture.
                        if (Physics.CheckCapsule(foot + Vector3.up * (PotRadius + .03f), foot + Vector3.up * .9f, PotRadius)) continue;
                        if (Physics.CheckBox(foot + Vector3.up * .66f, new Vector3(LeafHalf, .28f, LeafHalf))) continue;
                        // Along a wall or beside furniture, preferably in a corner.
                        int sides = 0; Vector3 open = Vector3.zero;
                        foreach (var dir in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                        {
                            if (Physics.Raycast(foot + Vector3.up * .5f, dir, LeafHalf + .25f) && Physics.Raycast(foot + Vector3.up * 1.3f, dir, LeafHalf + .4f)) sides++;
                            else open += dir;
                        }
                        if (sides == 0 || sides > 2) continue; // not free-standing in the walkway, not boxed into a niche
                        float score = sides == 2 ? 2 : 1;
                        // Players walk around it: a clear 0.7 m ring on the open side.
                        if (open != Vector3.zero && Physics.CheckCapsule(foot + open.normalized * .9f + Vector3.up * .4f, foot + open.normalized * .9f + Vector3.up * 1.6f, .3f)) continue;
                        float yaw = Mathf.Round(Quaternion.LookRotation(open == Vector3.zero ? Vector3.forward : open).eulerAngles.y / 90) * 90;
                        spots.Add((local, score, yaw));
                    }
                foreach (var spot in spots.OrderByDescending(s => s.score).ThenBy(s => s.local.sqrMagnitude))
                {
                    if (chosen.Count >= wanted) break;
                    if (chosen.Any(c => Vector3.Distance(c, spot.local) < MinSpacing)) continue;
                    // Place for real in the probe so later plants see earlier ones.
                    var test = (GameObject)PrefabUtility.InstantiatePrefab(plantPrefab, copy.transform);
                    test.transform.localPosition = spot.local; test.transform.localRotation = Quaternion.Euler(0, spot.yaw, 0);
                    Physics.SyncTransforms();
                    chosen.Add(spot.local);
                    var plant = (GameObject)PrefabUtility.InstantiatePrefab(plantPrefab, contents.transform);
                    plant.name = AuthoredName;
                    plant.transform.localPosition = spot.local; plant.transform.localRotation = Quaternion.Euler(0, spot.yaw, 0);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            return $"{contents.name}: {chosen.Count}/{wanted} plants at {string.Join(" ", chosen.Select(c => c.ToString("F2")))}";
        }
    }
}
