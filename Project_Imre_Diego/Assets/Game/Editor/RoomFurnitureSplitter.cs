using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace SurvivalFP.EditorTools
{
    // Splits the furniture baked into a combined room FBX into separate objects, each registered
    // as a Randomized Structure. Rerunnable: it always starts from RoomVisualSource.sourceMesh.
    //
    // Classification (per connected mesh island):
    //   furniture = islands using a "Dressing" material that, grouped by touching bounds, stand on the floor.
    //   shell     = everything else: walls, floor, panelling, lanterns, wall-hung decor, rugs, door frames, stair rails.
    public static class RoomFurnitureSplitter
    {
        const string OutputRoot = "Assets/Game/Art/Mansion/Current/Split";
        static readonly string[] FurnitureMaterials = { "Dressing" };
        static readonly string[] PlantMaterials = { "PlantAtlas" };
        // The retired prop spawner left its primitive props under these anchor objects.
        static readonly string[] LegacyAnchors = { "Furniture surface anchor", "Optional prop anchor" };
        static readonly string[] LegacyProps = { "Table", "Shelf", "Plant", "Cabinet" };
        static readonly Dictionary<string, float> DefaultChance = new() { { "Bookcase", 70 }, { "Desk", 75 }, { "Table", 75 }, { "Sideboard", 70 }, { "Chair", 60 }, { "Cabinet", 70 }, { "Furniture", 65 } };

        [MenuItem("Survival FP/Rooms/Split Baked Furniture (All Mansion Rooms)")]
        static void SplitAll()
        {
            var report = new List<string>();
            foreach (var path in MansionRoomPaths()) report.Add(SplitPrefab(path));
            Debug.Log("Furniture split:\n" + string.Join("\n", report));
        }
        [MenuItem("Survival FP/Rooms/Split Baked Furniture (Selected Room Prefabs)")]
        static void SplitSelected()
        {
            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (path.EndsWith(".prefab") && AssetDatabase.LoadAssetAtPath<RoomModule>(path)) Debug.Log(SplitPrefab(path));
            }
        }
        public static IEnumerable<string> MansionRoomPaths() =>
            AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs/Maps/Mansion/Rooms", "Assets/Game/Prefabs/Maps/Mansion/Hallways", "Assets/Game/Prefabs/Maps/Mansion/Stairs" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => AssetDatabase.LoadAssetAtPath<RoomModule>(p));

        public static string SplitPrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { var report = Split(root); report += PlaceNetworkProps(root); PrefabUtility.SaveAsPrefabAsset(root, path); return report; }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Survival FP/Rooms/Place Hiding Closets (All Mansion Rooms)")]
        static void PlaceAll()
        {
            var report = new List<string>();
            foreach (var path in MansionRoomPaths())
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try { report.Add(root.name + PlaceNetworkProps(root)); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("Closet placement:\n" + string.Join("\n", report));
        }

        // Places every networked prop group (hiding closets) flush and square against a wall, where it
        // touches nothing even with every furniture piece enabled, keeps its entry/exit/AI approach clear,
        // stays away from doorways and never covers another piece's item spot. Tested with real physics
        // on a temporary copy of the room.
        public static string PlaceNetworkProps(GameObject contents)
        {
            var room = contents.GetComponent<RoomModule>();
            var notes = new List<string>();
            var entries = room.randomizedStructures ?? Array.Empty<RandomizedStructure>();
            for (int index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (!entry.target) continue;
                var marker = entry.target.GetComponentInChildren<NetworkSpawnMarker>(true);
                if (!marker || !marker.prefab) continue;
                // A closet that replaces one modelled into the room stays exactly where the model had it.
                if (marker.keepAuthoredPose) { notes.Add($"{entry.target.name} kept at its modelled spot"); continue; }
                var probe = new GameObject("__PlacementProbe") { hideFlags = HideFlags.HideAndDontSave };
                probe.transform.position = new Vector3(0, -5000, 0);
                try
                {
                    var copy = UnityEngine.Object.Instantiate(contents, probe.transform, false);
                    copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity;
                    var copyRoom = copy.GetComponent<RoomModule>();
                    // Every other piece on (worst case); network groups off so they don't test against themselves.
                    foreach (var e in copyRoom.randomizedStructures) if (e.target) e.target.SetActive(!e.target.GetComponentInChildren<NetworkSpawnMarker>(true));
                    var closet = UnityEngine.Object.Instantiate(marker.prefab.gameObject, probe.transform);
                    var hide = closet.GetComponent<ClosetHideout>();
                    var boxes = closet.GetComponentsInChildren<BoxCollider>();
                    var pieceBounds = copyRoom.randomizedStructures.Where(e => e.target && e.target.activeSelf)
                        .SelectMany(e => e.target.GetComponentsInChildren<Collider>()).Select(c => c.bounds).ToList();
                    var itemApproaches = copy.GetComponentsInChildren<ItemSpawnPoint>().Select(a => a.Approach).ToList();
                    bool OnlyCloset(Collider c) => c.transform.IsChildOf(closet.transform);
                    var occ = copyRoom.OccupancyVolumes.Aggregate(copyRoom.OccupancyVolumes[0], (a, b) => { a.Encapsulate(b); return a; });
                    float bestScore = float.NegativeInfinity; Vector3 bestPos = default; float bestYaw = 0;
                    var rejected = new Dictionary<string, int>();
                    void Reject(string why) { rejected.TryGetValue(why, out int n); rejected[why] = n + 1; }
                    foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                        for (float x = occ.min.x + .3f; x <= occ.max.x - .3f; x += .1f)
                            for (float z = occ.min.z + .3f; z <= occ.max.z - .3f; z += .1f)
                            {
                                var rot = Quaternion.Euler(0, yaw, 0);
                                closet.transform.SetPositionAndRotation(copy.transform.TransformPoint(new Vector3(x, 0, z)), rot); Physics.SyncTransforms();
                                // Wall behind: sample the whole back; snap to the most protruding surface.
                                var back = -closet.transform.forward;
                                if (!Physics.RaycastAll(closet.transform.TransformPoint(new Vector3(0, 1.2f, -.30f)), back, .45f).Any(r => !OnlyCloset(r.collider))) continue; // cheap early out
                                // Stand on the real floor surface (floorboards sit a little above the room origin).
                                var floor = Physics.RaycastAll(closet.transform.position + Vector3.up, Vector3.down, 1.5f).Where(r => !OnlyCloset(r.collider)).OrderBy(r => r.distance).FirstOrDefault();
                                if (!floor.collider) { Reject("no floor"); continue; }
                                var standing = closet.transform.position; standing.y = floor.point.y + .005f; closet.transform.position = standing; Physics.SyncTransforms();
                                // Slide back until the whole back face is 1 cm from the most protruding surface
                                // (panelling, rails, skirting): a box cast finds contacts rays would miss.
                                // Slab covers the closet's full back face: back panel at -0.305, width ±0.62, 3 cm above the floor.
                                var slab = closet.transform.TransformPoint(new Vector3(0, 1.08f, -.30f));
                                var contact = Physics.BoxCastAll(slab, new Vector3(.625f, 1.03f, .005f), back, closet.transform.rotation, .45f)
                                    .Where(r => !OnlyCloset(r.collider) && r.distance > 0).OrderBy(r => r.distance).FirstOrDefault();
                                if (!contact.collider) { Reject("no wall contact"); continue; }
                                closet.transform.position += back * Mathf.Max(0, contact.distance - .01f); Physics.SyncTransforms();
                                // Sweeps against the room mesh land slightly short, so resolve any wall contact
                                // by pushing the closet straight out along its facing until it just clears.
                                for (int pass = 0; pass < 6; pass++)
                                {
                                    float push = 0;
                                    foreach (var b in boxes)
                                    {
                                        var m = b.transform.localToWorldMatrix; var half = Vector3.Scale(b.size * .5f, b.transform.lossyScale);
                                        foreach (var c in Physics.OverlapBox(m.MultiplyPoint3x4(b.center), half, b.transform.rotation).Where(c => !OnlyCloset(c)))
                                            if (Physics.ComputePenetration(b, b.transform.position, b.transform.rotation, c, c.transform.position, c.transform.rotation, out var dir, out var depth)
                                                && Vector3.Dot(dir, closet.transform.forward) > .7f && depth < .06f)
                                                push = Mathf.Max(push, depth / Mathf.Max(.7f, Vector3.Dot(dir, closet.transform.forward)));
                                    }
                                    if (push <= 0) break;
                                    closet.transform.position += closet.transform.forward * (push + .005f); Physics.SyncTransforms();
                                }
                                // Straight along a continuous flat wall: most of the back must be within 10 cm of it.
                                int samples = 0, near = 0;
                                for (float u = -.55f; u <= .551f; u += .275f)
                                    for (float h = .15f; h <= 2.0f; h += .3f)
                                    {
                                        samples++;
                                        if (Physics.RaycastAll(closet.transform.TransformPoint(new Vector3(u, h, -.30f)), back, .12f).Any(r => !OnlyCloset(r.collider))) near++;
                                    }
                                if (near < samples * .8f) { Reject("wall not continuous"); continue; }
                                var snapped = closet.transform.position;
                                bool blocked = false;
                                foreach (var b in boxes)
                                {
                                    var m = b.transform.localToWorldMatrix; var half = Vector3.Scale(b.size * .5f, b.transform.lossyScale) * .98f;
                                    if (Physics.OverlapBox(m.MultiplyPoint3x4(b.center), half, b.transform.rotation).Any(c => !OnlyCloset(c))) { blocked = true; break; }
                                }
                                if (blocked) { Reject("touches geometry or furniture"); continue; }
                                foreach (var p in new[] { hide.exitPosition.position, hide.entryPoint.position, hide.InvestigationPosition })
                                    if (Physics.OverlapCapsule(p + Vector3.up * .4f, p + Vector3.up * 1.6f, .35f).Any(c => !OnlyCloset(c)) || !Physics.Raycast(p + Vector3.up * .3f, Vector3.down, .5f)) { blocked = true; break; }
                                if (blocked) { Reject("entry/exit blocked"); continue; }
                                var local = copy.transform.InverseTransformPoint(snapped);
                                if (copyRoom.connectors.Any(c => c && Vector3.Distance(c.transform.localPosition, local) < 2.2f)) { Reject("near doorway"); continue; }
                                var footprint = new Bounds(closet.transform.TransformPoint(new Vector3(0, 1.05f, .5f)), Vector3.zero);
                                foreach (var b in boxes) footprint.Encapsulate(b.bounds);
                                footprint.Encapsulate(hide.exitPosition.position); footprint.Expand(new Vector3(.4f, 0, .4f));
                                if (itemApproaches.Any(a => footprint.Contains(new Vector3(a.x, footprint.center.y, a.z)))) { Reject("covers item spot"); continue; }
                                // Keep at least 25 cm of floor between the closet body and every furniture piece,
                                // then prefer the most open spot.
                                var body = boxes.Select(bx => bx.bounds).Aggregate((a, b2) => { a.Encapsulate(b2); return a; });
                                float Gap(Bounds pb)
                                {
                                    float dx = Mathf.Max(0, Mathf.Max(pb.min.x - body.max.x, body.min.x - pb.max.x));
                                    float dz = Mathf.Max(0, Mathf.Max(pb.min.z - body.max.z, body.min.z - pb.max.z));
                                    return Mathf.Sqrt(dx * dx + dz * dz);
                                }
                                float clearance = pieceBounds.Count == 0 ? 1.5f : pieceBounds.Min(Gap);
                                if (clearance < .25f) { Reject("too close to furniture"); continue; }
                                float score = Mathf.Min(clearance, 1.5f) - Vector3.Distance(local, entry.target.transform.localPosition) * .01f;
                                if (score > bestScore) { bestScore = score; bestPos = local; bestYaw = yaw; }
                            }
                    if (float.IsNegativeInfinity(bestScore)) { notes.Add($"{entry.target.name}: NO VALID WALL SPOT (chance set to 0; rejected: {string.Join(", ", rejected.Select(r => r.Key + " " + r.Value))})"); entries[index].spawnChance = 0; continue; }
                    entry.target.transform.localPosition = bestPos; entry.target.transform.localRotation = Quaternion.Euler(0, bestYaw, 0);
                    if (entries[index].spawnChance <= 0) entries[index].spawnChance = 45; // re-enable after an earlier failed placement
                    marker.transform.localPosition = Vector3.zero; marker.transform.localRotation = Quaternion.identity;
                    notes.Add($"{entry.target.name} at {bestPos:F2} facing {bestYaw}Â°");
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }
            return notes.Count == 0 ? "" : "; closets: " + string.Join(", ", notes);
        }

        struct Tri { public int a, b, c, sub; }
        sealed class Piece { public List<Tri> tris = new(); public Bounds bounds; public string name; }

        public static string Split(GameObject root)
        {
            var room = root.GetComponent<RoomModule>();
            var visuals = root.transform.Find("_Visuals");
            var filter = visuals ? visuals.GetComponentInChildren<MeshFilter>(true) : null;
            if (!room || !filter) return $"{root.name}: skipped (no RoomModule or _Visuals mesh)";
            // The pieces and their meshes are about to be replaced, so any baked lighting no longer matches them.
            var bake = root.GetComponent<RoomBakedLighting>();
            if (bake && bake.HasBake) { bake.variants = System.Array.Empty<RoomBakedLighting.Variant>(); Debug.LogWarning($"{root.name}: furniture re-split, baked lighting cleared. Rebake with Survival FP > Lighting > Bake Mansion Room Lighting.", root); }
            var source = filter.GetComponent<RoomVisualSource>();
            if (!source) { source = filter.gameObject.AddComponent<RoomVisualSource>(); source.sourceMesh = filter.sharedMesh; }
            var mesh = source.sourceMesh;
            if (!mesh || !mesh.isReadable) return $"{root.name}: source mesh missing or not Read/Write enabled";
            var renderer = filter.GetComponent<MeshRenderer>();
            // Classify by the FBX's own materials (one per submesh), never by what a previous split assigned.
            var fbxRenderer = PrefabUtility.GetCorrespondingObjectFromSource(renderer) as MeshRenderer;
            var materials = fbxRenderer && fbxRenderer.sharedMaterials.Length == mesh.subMeshCount ? fbxRenderer.sharedMaterials : renderer.sharedMaterials;
            Material[] Styled(Material[] used) => source.materialScheme ? used.Select(source.materialScheme.Resolve).ToArray() : used;
            var toRoom = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;

            // Previous chances survive a rerun; previously generated pieces are replaced.
            var keptChance = new Dictionary<string, float>();
            foreach (var entry in room.randomizedStructures ?? Array.Empty<RandomizedStructure>())
                if (entry.target && source.generatedPieces.Contains(entry.target)) keptChance[entry.target.name] = entry.spawnChance;
            foreach (var old in source.generatedPieces) if (old) UnityEngine.Object.DestroyImmediate(old);
            source.generatedPieces.Clear();
            int legacy = 0;
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                if (LegacyAnchors.Contains(child.name) || IsLegacyProp(child)) { UnityEngine.Object.DestroyImmediate(child.gameObject); legacy++; }

            // Islands: triangles connected through welded vertex positions. Furniture-material and
            // other triangles are connected separately, so a bookcase welded to a wall still separates.
            var v = mesh.vertices; var furnitureSub = new bool[mesh.subMeshCount];
            for (int s = 0; s < materials.Length && s < furnitureSub.Length; s++) furnitureSub[s] = materials[s] && FurnitureMaterials.Any(m => materials[s].name.Contains(m));
            var ids = new Dictionary<(Vector3Int, bool), int>();
            int Weld(int i, bool furniture) { var k = (Vector3Int.RoundToInt(v[i] * 1000f), furniture); if (!ids.TryGetValue(k, out int id)) ids[k] = id = ids.Count; return id; }
            var tris = new List<Tri>();
            for (int s = 0; s < mesh.subMeshCount; s++) { var t = mesh.GetTriangles(s); for (int i = 0; i < t.Length; i += 3) tris.Add(new Tri { a = t[i], b = t[i + 1], c = t[i + 2], sub = s }); }
            var node = tris.Select(t => (Weld(t.a, furnitureSub[t.sub]), Weld(t.b, furnitureSub[t.sub]), Weld(t.c, furnitureSub[t.sub]))).ToArray();
            var parent = Enumerable.Range(0, ids.Count).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var n in node) { int r = Find(n.Item1); parent[Find(n.Item2)] = r; parent[Find(n.Item3)] = r; }
            var islands = new Dictionary<int, Piece>();
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti]; int r = Find(node[ti].Item1);
                if (!islands.TryGetValue(r, out var island)) islands[r] = island = new Piece { bounds = new Bounds(toRoom.MultiplyPoint3x4(v[t.a]), Vector3.zero) };
                island.tris.Add(t);
                island.bounds.Encapsulate(toRoom.MultiplyPoint3x4(v[t.a])); island.bounds.Encapsulate(toRoom.MultiplyPoint3x4(v[t.b])); island.bounds.Encapsulate(toRoom.MultiplyPoint3x4(v[t.c]));
            }
            // Wall lanterns baked into the model are replaced by the Wall Lantern prefab (current model,
            // flame light, collider), placed exactly where the baked one hung. Their triangles leave the shell.
            var lanterns = FindWallLanterns(tris, islands.Values, materials, v, toRoom);
            var lanternTris = new HashSet<Tri>(lanterns.SelectMany(l => l.tris));
            var lanternIslands = new HashSet<Piece>(islands.Values.Where(i => i.tris.Any(lanternTris.Contains)));
            // Closets modelled into the room look real but are only shell geometry: nobody can hide in them.
            // They leave the shell and a real networked closet stands in exactly the same place.
            var bakedClosets = FindBakedClosets(tris, islands.Values, room, v, toRoom, lanternTris);
            foreach (var bc in bakedClosets) lanternTris.UnionWith(bc.tris);
            foreach (var island in islands.Values) if (island.tris.Any(t => bakedClosets.Any(bc => bc.tris.Contains(t)))) lanternIslands.Add(island);

            // Candidates: furniture-material islands, excluding flat floor decor (rugs) and long wall trims
            // (skirting, crown moulding) that would otherwise glue neighbouring pieces together.
            bool Trim(Bounds b) => b.size.y < .3f && Mathf.Min(b.size.x, b.size.z) < .12f && Mathf.Max(b.size.x, b.size.z) > 1.5f;
            var candidates = islands.Values.Where(i => furnitureSub[i.tris[0].sub] && i.bounds.size.y > .02f && !Trim(i.bounds) && !lanternIslands.Contains(i)).ToList();

            // Group touching candidates into pieces; only floor-standing groups become furniture.
            var group = Enumerable.Range(0, candidates.Count).ToArray();
            int G(int x) { while (group[x] != x) { group[x] = group[group[x]]; x = group[x]; } return x; }
            for (int i = 0; i < candidates.Count; i++)
                for (int j = i + 1; j < candidates.Count; j++)
                { var b = candidates[i].bounds; b.Expand(.06f); if (b.Intersects(candidates[j].bounds)) group[G(j)] = G(i); }
            var pieces = new List<Piece>();
            foreach (var g in Enumerable.Range(0, candidates.Count).GroupBy(G))
            {
                var piece = new Piece { bounds = candidates[g.First()].bounds };
                foreach (var i in g) { piece.tris.AddRange(candidates[i].tris); piece.bounds.Encapsulate(candidates[i].bounds); }
                var b = piece.bounds;
                bool floorStanding = b.min.y < .15f && b.size.y > .3f;
                bool plausible = b.size.y <= 2.8f && Mathf.Max(b.size.x, b.size.z) <= 3.6f;
                bool nearDoor = room.connectors.Any(c => c && Vector2.Distance(new Vector2(c.transform.localPosition.x, c.transform.localPosition.z),
                    new Vector2(Mathf.Clamp(c.transform.localPosition.x, b.min.x, b.max.x), Mathf.Clamp(c.transform.localPosition.z, b.min.z, b.max.z))) < 1.0f
                    && Mathf.Abs(c.transform.localPosition.y - b.min.y) < 1f);
                if (floorStanding && plausible && !nearDoor) pieces.Add(piece);
            }
            // Loose items (papers, books, candlesticks) resting on a piece travel with it, whatever
            // their material, so nothing floats when the piece is disabled. Wall-mounted parts don't rest.
            var assigned = new HashSet<Piece>(candidates.Where(c => pieces.Any(p => p.tris.Contains(c.tris[0]))));
            foreach (var island in islands.Values)
            {
                if (assigned.Contains(island) || lanternIslands.Contains(island) || island.bounds.min.y < .1f || island.bounds.size.y > .8f) continue;
                var islandMaterial = island.tris[0].sub < materials.Length ? materials[island.tris[0].sub] : null;
                if (islandMaterial && PlantMaterials.Any(m => islandMaterial.name.Contains(m))) continue; // plants stay in the room
                foreach (var piece in pieces)
                    if (RestsOn(island, piece, v, toRoom)) { piece.tris.AddRange(island.tris); piece.bounds.Encapsulate(island.bounds); break; }
            }
            var furnitureTris = new HashSet<Tri>(pieces.SelectMany(p => p.tris));

            // Shell keeps the original mesh space so the existing visual transform and collider stay valid.
            string folder = $"{OutputRoot}/{root.name}";
            Directory.CreateDirectory(folder);
            var shellTris = tris.Where(t => !furnitureTris.Contains(t) && !lanternTris.Contains(t)).ToList();
            var shellMesh = BuildMesh(mesh, shellTris, Matrix4x4.identity, Vector3.zero, materials, out var shellMaterials, $"{root.name} Shell");
            int uvFixes = RepairCollapsedUVs(shellMesh, toRoom, shellMaterials);
            int sillFixes = RepairDoorwaySills(shellMesh, toRoom, shellMaterials);
            filter.sharedMesh = SaveMesh(shellMesh, $"{folder}/{root.name} Shell.asset");
            renderer.sharedMaterials = Styled(shellMaterials);
            int lanternCount = PlaceWallLanterns(root, source, lanterns);
            // Baked plants stay visible in the shell but are left out of its collider: a leaf mesh collider
            // would snag players and carve the NavMesh. Each plant gets a small capsule at its pot instead.
            var plantSub = new bool[mesh.subMeshCount];
            for (int s = 0; s < materials.Length && s < plantSub.Length; s++) plantSub[s] = materials[s] && PlantMaterials.Any(m => materials[s].name.Contains(m));
            var shellCollider = filter.GetComponent<MeshCollider>(); if (!shellCollider) shellCollider = filter.gameObject.AddComponent<MeshCollider>();
            int plants = 0;
            if (plantSub.Any(p => p))
            {
                var collisionMesh = BuildMesh(mesh, shellTris.Where(t => !plantSub[t.sub]), Matrix4x4.identity, Vector3.zero, materials, out _, $"{root.name} Shell Collision");
                shellCollider.sharedMesh = SaveMesh(collisionMesh, $"{folder}/{root.name} Shell Collision.asset");
                var plantIslands = islands.Values.Where(i => plantSub[i.tris[0].sub]).ToList();
                var pg = Enumerable.Range(0, plantIslands.Count).ToArray();
                int P(int x) { while (pg[x] != x) { pg[x] = pg[pg[x]]; x = pg[x]; } return x; }
                for (int i = 0; i < plantIslands.Count; i++)
                    for (int j = i + 1; j < plantIslands.Count; j++)
                    { var b = plantIslands[i].bounds; b.Expand(.1f); if (b.Intersects(plantIslands[j].bounds)) pg[P(j)] = P(i); }
                var holder = new GameObject("Plant Collision"); holder.transform.SetParent(root.transform, false);
                source.generatedPieces.Add(holder);
                foreach (var g in Enumerable.Range(0, plantIslands.Count).GroupBy(P))
                {
                    var points = g.SelectMany(i => plantIslands[i].tris).SelectMany(t => new[] { t.a, t.b, t.c }).Select(i => toRoom.MultiplyPoint3x4(v[i])).ToList();
                    var all = new Bounds(points[0], Vector3.zero); foreach (var p in points) all.Encapsulate(p);
                    if (all.size.y < .3f) continue;
                    AddPlantCapsule(holder.transform, points, all);
                    plants++;
                }
            }
            else shellCollider.sharedMesh = filter.sharedMesh;

            var names = new Dictionary<string, int>(); var entries = (room.randomizedStructures ?? Array.Empty<RandomizedStructure>()).Where(e => e.target).ToList();
            int anchors = 0; var created = new List<string>();
            foreach (var piece in pieces.OrderBy(p => p.bounds.center.x).ThenBy(p => p.bounds.center.z))
            {
                var pivot = new Vector3(piece.bounds.center.x, piece.bounds.min.y, piece.bounds.center.z);
                var pieceMesh = BuildMesh(mesh, piece.tris, toRoom, pivot, materials, out var pieceMaterials, "piece");
                // Piece vertices are room space minus the pivot; project in room space so grain lines up with the shell.
                uvFixes += RepairCollapsedUVs(pieceMesh, Matrix4x4.Translate(pivot), pieceMaterials);
                string kind = Classify(piece.bounds, pieceMesh);
                names[kind] = names.TryGetValue(kind, out int n) ? n + 1 : 1;
                piece.name = names[kind] == 1 ? kind : $"{kind} {names[kind]}";
                pieceMesh.name = $"{root.name} {piece.name}";
                var saved = SaveMesh(pieceMesh, $"{folder}/{root.name} {piece.name}.asset");
                var setup = new GameObject(piece.name + " Setup"); setup.transform.SetParent(root.transform, false); setup.transform.localPosition = pivot;
                var model = new GameObject(piece.name); model.transform.SetParent(setup.transform, false);
                model.AddComponent<MeshFilter>().sharedMesh = saved;
                model.AddComponent<MeshRenderer>().sharedMaterials = Styled(pieceMaterials);
                model.AddComponent<MeshCollider>().sharedMesh = saved;
                model.isStatic = setup.isStatic = false; // toggled at runtime by the structure system
                anchors += AddItemAnchors(setup.transform, saved, piece.bounds, pivot, room);
                source.generatedPieces.Add(setup);
                float chance = keptChance.TryGetValue(setup.name, out var kept) ? kept : DefaultChance.TryGetValue(kind, out var def) ? def : 65;
                entries.Add(new RandomizedStructure { target = setup, spawnChance = chance });
                created.Add($"{setup.name} {chance}%");
            }
            // Real closets where the model had them: always present (the model always showed them), fixed pose.
            var closetPrefab = AssetDatabase.LoadAssetAtPath<Unity.Netcode.NetworkObject>(ClosetPrefab);
            int closetIndex = 0;
            foreach (var bc in bakedClosets)
            {
                if (!closetPrefab) break;
                string setupName = $"Modelled Closet {++closetIndex} Setup";
                var setup = new GameObject(setupName); setup.transform.SetParent(root.transform, false);
                setup.transform.localPosition = bc.position; setup.transform.localRotation = Quaternion.Euler(0, bc.yaw, 0);
                var spawn = new GameObject("Closet Spawn"); spawn.transform.SetParent(setup.transform, false);
                var marker = spawn.AddComponent<NetworkSpawnMarker>(); marker.prefab = closetPrefab; marker.keepAuthoredPose = true;
                source.generatedPieces.Add(setup);
                float chance = keptChance.TryGetValue(setupName, out var kept) ? kept : 100f;
                entries.Add(new RandomizedStructure { target = setup, spawnChance = chance });
                created.Add($"{setupName} {chance}%");
            }
            room.randomizedStructures = entries.ToArray();
            EditorUtility.SetDirty(room);
            return $"{root.name}: {pieces.Count} pieces [{string.Join(", ", created)}], {anchors} item anchors, {plants} plant capsules, {lanternCount} wall lanterns, {bakedClosets.Count} modelled closets made real, {uvFixes} stretched faces re-mapped, {sillFixes} doorway sill faces floored, removed {legacy} legacy prop groups, shell {filter.sharedMesh.triangles.Length / 3} tris";
        }

        const string ClosetPrefab = "Assets/Game/Prefabs/Maps/Mansion/Props/Closet.prefab";
        sealed class BakedCloset { public HashSet<Tri> tris = new(); public Vector3 position; public float yaw; }

        // A modelled closet is an island the size of the Closet prefab's model (about 1.24 x 2.1 x 0.67 m), standing
        // near the floor. Everything wholly inside its footprint (feet, crown, handles) goes with it. It faces the
        // room: its back is to the wall.
        static List<BakedCloset> FindBakedClosets(List<Tri> tris, IEnumerable<Piece> islandSet, RoomModule room, Vector3[] v, Matrix4x4 toRoom, HashSet<Tri> taken)
        {
            var found = new List<BakedCloset>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClosetPrefab);
            var model = prefab ? prefab.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.enabled) : null;
            if (!model) return found;
            var size = model.bounds.size; float width = Mathf.Max(size.x, size.z), depth = Mathf.Min(size.x, size.z);
            var occupancy = room.OccupancyVolumes.Aggregate(room.OccupancyVolumes[0], (a, b) => { a.Encapsulate(b); return a; });
            // Connected by position alone: a modelled closet mixes materials, so the per-material islands split it up.
            var ids = new Dictionary<Vector3Int, int>();
            // Half-millimetre welding: at 1 mm a closet can fuse with the furniture standing against it.
            int Id(int i) { var k = Vector3Int.RoundToInt(v[i] * 2000f); if (!ids.TryGetValue(k, out int id)) ids[k] = id = ids.Count; return id; }
            var nodes = tris.Select(t => (Id(t.a), Id(t.b), Id(t.c))).ToArray();
            var parent = Enumerable.Range(0, ids.Count).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var n in nodes) { int r = Find(n.Item1); parent[Find(n.Item2)] = r; parent[Find(n.Item3)] = r; }
            var groups = new Dictionary<int, Bounds>();
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti]; int r = Find(nodes[ti].Item1);
                var p = toRoom.MultiplyPoint3x4(v[t.a]);
                if (!groups.TryGetValue(r, out var gb)) gb = new Bounds(p, Vector3.zero);
                gb.Encapsulate(p); gb.Encapsulate(toRoom.MultiplyPoint3x4(v[t.b])); gb.Encapsulate(toRoom.MultiplyPoint3x4(v[t.c]));
                groups[r] = gb;
            }
            foreach (var b in groups.Values)
            {
                float w = Mathf.Max(b.size.x, b.size.z), d = Mathf.Min(b.size.x, b.size.z);
                if (Mathf.Abs(w - width) > .12f || Mathf.Abs(d - depth) > .15f || b.size.y < size.y * .8f || b.size.y > size.y * 1.1f) continue;
                if (found.Any(f => Vector3.Distance(new Vector3(f.position.x, 0, f.position.z), new Vector3(b.center.x, 0, b.center.z)) < .5f)) continue;
                // Footprint down to the floor it stands on, so the feet under the body come along.
                var foot = new Bounds(b.center, b.size); foot.min = new Vector3(b.min.x - .03f, b.min.y - (size.y - b.size.y) - .05f, b.min.z - .03f); foot.max = new Vector3(b.max.x + .03f, b.max.y + .05f, b.max.z + .03f);
                var closet = new BakedCloset();
                float floorY = float.MaxValue;
                foreach (var t in tris)
                {
                    if (taken.Contains(t)) continue;
                    Vector3 a = toRoom.MultiplyPoint3x4(v[t.a]), bb = toRoom.MultiplyPoint3x4(v[t.b]), c = toRoom.MultiplyPoint3x4(v[t.c]);
                    if (!foot.Contains(a) || !foot.Contains(bb) || !foot.Contains(c)) continue;
                    closet.tris.Add(t); floorY = Mathf.Min(floorY, Mathf.Min(a.y, Mathf.Min(bb.y, c.y)));
                }
                // Facing: along the thin axis, towards the middle of the room.
                bool thinX = b.size.x < b.size.z;
                float toCentre = thinX ? occupancy.center.x - b.center.x : occupancy.center.z - b.center.z;
                closet.yaw = thinX ? (toCentre >= 0 ? 90f : 270f) : (toCentre >= 0 ? 0f : 180f);
                // The prefab's model sits slightly forward of its pivot; place the pivot so the models coincide.
                var offset = Quaternion.Euler(0, closet.yaw, 0) * new Vector3(model.bounds.center.x - prefab.transform.position.x, 0, model.bounds.center.z - prefab.transform.position.z);
                closet.position = new Vector3(b.center.x, floorY, b.center.z) - offset;
                found.Add(closet);
            }
            return found;
        }

        sealed class Lantern { public HashSet<Tri> tris = new(); public Vector3 mount; public Vector3 outward; }
        const string WallLanternPrefab = "Assets/Game/Prefabs/Maps/Mansion/Props/Wall Lantern.prefab";

        // A baked wall lantern is a small cluster of lantern glass (12-45 cm, so the chandelier's tiny
        // droplets never qualify) plus the brass around it and any small part wholly inside its region.
        // Works per triangle: the model welds glass, cage and bracket together, sometimes into the wall.
        // The side where the parts reach furthest past the glass is the bracket, i.e. the wall.
        static List<Lantern> FindWallLanterns(List<Tri> tris, IEnumerable<Piece> islandSet, Material[] materials, Vector3[] v, Matrix4x4 toRoom)
        {
            string Mat(int sub) => sub < materials.Length && materials[sub] ? materials[sub].name : "";
            var islandOf = new Dictionary<Tri, Piece>();
            foreach (var island in islandSet) foreach (var t in island.tris) islandOf[t] = island;
            Bounds TriBounds(Tri t) { var b = new Bounds(toRoom.MultiplyPoint3x4(v[t.a]), Vector3.zero); b.Encapsulate(toRoom.MultiplyPoint3x4(v[t.b])); b.Encapsulate(toRoom.MultiplyPoint3x4(v[t.c])); return b; }
            var glass = tris.Where(t => Mat(t.sub).Contains("LanternGlass")).Select(t => (tri: t, bounds: TriBounds(t))).ToList();
            // Cluster glass triangles on a 5 cm-expanded overlap (grid-bucketed to stay fast).
            var cluster = Enumerable.Range(0, glass.Count).ToArray();
            int C(int x) { while (cluster[x] != x) { cluster[x] = cluster[cluster[x]]; x = cluster[x]; } return x; }
            var buckets = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < glass.Count; i++)
            {
                var key = Vector3Int.FloorToInt(glass[i].bounds.center / .5f);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                list.Add(i);
            }
            for (int i = 0; i < glass.Count; i++)
            {
                // 12 cm joins a lantern's separate panes and top glass; chandelier droplets are 40+ cm apart.
                var b = glass[i].bounds; b.Expand(.12f); var key = Vector3Int.FloorToInt(glass[i].bounds.center / .5f);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    if (buckets.TryGetValue(key + new Vector3Int(dx, dy, dz), out var near))
                        foreach (int j in near) if (j > i && b.Intersects(glass[j].bounds)) cluster[C(j)] = C(i);
            }
            // Then merge clusters whose bounds overlap: a lantern's inner chimney glass sits inside its panes.
            var clusters = new List<Bounds>();
            foreach (var g in Enumerable.Range(0, glass.Count).GroupBy(C)) { var gb = glass[g.First()].bounds; foreach (var i in g) gb.Encapsulate(glass[i].bounds); clusters.Add(gb); }
            for (bool merged = true; merged;)
            {
                merged = false;
                for (int i = 0; i < clusters.Count && !merged; i++)
                    for (int j = i + 1; j < clusters.Count; j++)
                    {
                        var a = clusters[i]; a.Expand(.04f);
                        if (!a.Intersects(clusters[j])) continue;
                        var m = clusters[i]; m.Encapsulate(clusters[j]); clusters[i] = m; clusters.RemoveAt(j); merged = true; break;
                    }
            }
            var result = new List<Lantern>();
            foreach (var glassBounds in clusters)
            {
                float size = Mathf.Max(glassBounds.size.x, glassBounds.size.y, glassBounds.size.z);
                if (size < .12f || size > .45f || glassBounds.center.y < 1.2f) continue;
                var region = glassBounds; region.Expand(new Vector3(.6f, .7f, .6f));
                // The lantern's own footprint: its bracket and back plate may be welded into the wall panelling
                // island, so small faces here are taken whatever island they belong to (else they stay in the
                // shell, right beside the new flame, and cast a hard shadow wedge across the wall).
                var footprint = glassBounds; footprint.Expand(new Vector3(.3f, .5f, .3f));
                var lantern = new Lantern();
                var whole = glassBounds;
                foreach (var t in tris)
                {
                    string m = Mat(t.sub);
                    if (m.Contains("Wallpaper") || m.Contains("Floor")) continue;
                    var b = TriBounds(t);
                    if (!region.Contains(b.min) || !region.Contains(b.max)) continue;
                    bool lanternMaterial = m.Contains("LanternGlass") || m.Contains("Brass");
                    // Other materials (a wooden back plate) only when their whole island is lantern-sized,
                    // so panelling or door trim passing near a lantern is never taken.
                    bool smallPartAtLantern = Mathf.Max(b.size.x, b.size.y, b.size.z) < .4f && footprint.Contains(b.min) && footprint.Contains(b.max);
                    if (!lanternMaterial && !smallPartAtLantern && (!islandOf.TryGetValue(t, out var island) || Mathf.Max(island.bounds.size.x, island.bounds.size.y, island.bounds.size.z) > .8f)) continue;
                    lantern.tris.Add(t); whole.Encapsulate(b);
                }
                float px = whole.max.x - glassBounds.max.x, nx = glassBounds.min.x - whole.min.x, pz = whole.max.z - glassBounds.max.z, nz = glassBounds.min.z - whole.min.z;
                float best = Mathf.Max(px, nx, pz, nz);
                Vector3 wall = best == px ? Vector3.right : best == nx ? Vector3.left : best == pz ? Vector3.forward : Vector3.back;
                // The new lantern's pivot is on its back plate; its glass sits 3.8 cm below the pivot.
                float plane = best == px ? whole.max.x : best == nx ? whole.min.x : best == pz ? whole.max.z : whole.min.z;
                var mount = new Vector3(glassBounds.center.x, glassBounds.center.y + .038f, glassBounds.center.z);
                if (wall.x != 0) mount.x = plane; else mount.z = plane;
                // Only lanterns actually hung on a wall: a bracket on one side, and wallpaper or panelling
                // right behind it. Rules out the chandelier and anything free-standing.
                if (best < .05f) continue;
                var behind = new Bounds(mount + wall * .04f, new Vector3(.12f, .3f, .12f));
                if (!tris.Any(t => { string m = Mat(t.sub); return (m.Contains("Wallpaper") || m.Contains("Wood")) && !lantern.tris.Contains(t) && TriBounds(t).Intersects(behind); })) continue;
                lantern.mount = mount; lantern.outward = -wall;
                result.Add(lantern);
            }
            return result;
        }

        static int PlaceWallLanterns(GameObject root, RoomVisualSource source, List<Lantern> lanterns)
        {
            // Lights that used to hang in front of the baked lanterns are superseded by each lantern's own flame.
            var oldLights = root.transform.Find("LanternLights");
            if (oldLights)
            {
                foreach (Transform light in oldLights.Cast<Transform>().ToArray())
                    if (light.name.StartsWith("LanternLight")) UnityEngine.Object.DestroyImmediate(light.gameObject);
                if (oldLights.childCount == 0) UnityEngine.Object.DestroyImmediate(oldLights.gameObject);
            }
            if (lanterns.Count == 0) return 0;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallLanternPrefab);
            if (!prefab) throw new InvalidOperationException("Missing " + WallLanternPrefab);
            var holder = new GameObject("Wall Lanterns"); holder.transform.SetParent(root.transform, false);
            source.generatedPieces.Add(holder);
            int n = 0;
            foreach (var lantern in lanterns.OrderBy(l => l.mount.y).ThenBy(l => l.mount.x).ThenBy(l => l.mount.z))
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
                instance.name = $"Wall Lantern {++n}";
                instance.transform.localPosition = lantern.mount;
                instance.transform.localRotation = Quaternion.LookRotation(lantern.outward, Vector3.up);
            }
            return n;
        }

        // Upper-floor doorways in the room models (the staircase landing, the Grand Hall's gallery doors) were cut
        // through the wall with the wall's own wallpaper left on the bottom of the opening: a strip of wallpaper
        // lying on the floor right at the threshold. A face is such a sill when it faces straight up, carries a
        // wallpaper material, and lies exactly on a walkable floor level next to that level's floor. It moves to
        // the floor material and takes its UVs from the adjoining floor face's own mapping, so the boards run
        // straight through the doorway with no seam. Tops of walls (above the ceiling, no floor there) never
        // qualify. Geometry is untouched.
        static int RepairDoorwaySills(Mesh mesh, Matrix4x4 toRoom, Material[] materials)
        {
            int floorSub = -1;
            for (int s = 0; s < materials.Length && s < mesh.subMeshCount; s++) if (materials[s] && materials[s].name.Contains("Floor")) { floorSub = s; break; }
            if (floorSub < 0) return 0;
            var verts = new List<Vector3>(); mesh.GetVertices(verts);
            var normals = new List<Vector3>(); mesh.GetNormals(normals);
            var colors = new List<Color>(); mesh.GetColors(colors);
            var uvs = new List<Vector2>[4]; for (int c = 0; c < 4; c++) { uvs[c] = new List<Vector2>(); mesh.GetUVs(c, uvs[c]); }
            if (uvs[0].Count != verts.Count) return 0;
            Vector3 P(int i) => toRoom.MultiplyPoint3x4(verts[i]);
            bool Up(Vector3 a, Vector3 b, Vector3 c, out float area) { var n = Vector3.Cross(b - a, c - a); area = n.magnitude * .5f; return area > 1e-5f && n.y / (area * 2f) > .95f; }
            var subTris = new List<List<int>>(); for (int s = 0; s < mesh.subMeshCount; s++) subTris.Add(mesh.GetTriangles(s).ToList());
            // The floor's up-facing faces, kept for their UV mapping.
            var floor = new List<(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)>();
            var ft = subTris[floorSub];
            for (int i = 0; i < ft.Count; i += 3)
            {
                Vector3 a = P(ft[i]), b = P(ft[i + 1]), c = P(ft[i + 2]);
                if (Up(a, b, c, out float area) && area > .01f) floor.Add((a, b, c, uvs[0][ft[i]], uvs[0][ft[i + 1]], uvs[0][ft[i + 2]]));
            }
            if (floor.Count == 0) return 0;
            float EdgeDistance(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f)); return (a + ab * t - p).magnitude; }
            int moved = 0;
            for (int s = 0; s < subTris.Count; s++)
            {
                if (s == floorSub || s >= materials.Length || !materials[s] || !materials[s].name.Contains("Wallpaper")) continue;
                var t = subTris[s];
                for (int i = t.Count - 3; i >= 0; i -= 3)
                {
                    Vector3 a = P(t[i]), b = P(t[i + 1]), c = P(t[i + 2]);
                    if (!Up(a, b, c, out _)) continue;
                    float y = (a.y + b.y + c.y) / 3f;
                    // The floor face on this level that shares an edge with the sill (touching within 2 cm).
                    int best = -1; float bestGap = .02f;
                    for (int f = 0; f < floor.Count; f++)
                    {
                        var fl = floor[f]; if (Mathf.Abs((fl.a.y + fl.b.y + fl.c.y) / 3f - y) > .01f) continue;
                        Vector2 fa = new(fl.a.x, fl.a.z), fb = new(fl.b.x, fl.b.z), fc = new(fl.c.x, fl.c.z);
                        foreach (var p in new[] { a, b, c })
                        {
                            var q = new Vector2(p.x, p.z);
                            float gap = Mathf.Min(EdgeDistance(q, fa, fb), Mathf.Min(EdgeDistance(q, fb, fc), EdgeDistance(q, fc, fa)));
                            if (gap < bestGap) { bestGap = gap; best = f; }
                        }
                    }
                    if (best < 0) continue;
                    // The floor face's affine mapping (x, z) -> UV, extended over the sill.
                    var src = floor[best];
                    Vector2 e1 = new(src.b.x - src.a.x, src.b.z - src.a.z), e2 = new(src.c.x - src.a.x, src.c.z - src.a.z);
                    float det = e1.x * e2.y - e2.x * e1.y; if (Mathf.Abs(det) < 1e-8f) continue;
                    Vector2 d1 = src.ub - src.ua, d2 = src.uc - src.ua;
                    Vector2 Map(Vector3 p)
                    {
                        float px = p.x - src.a.x, pz = p.z - src.a.z;
                        float u = (px * e2.y - e2.x * pz) / det, w = (e1.x * pz - px * e1.y) / det; // barycentric along e1, e2
                        return src.ua + d1 * u + d2 * w;
                    }
                    var corner = new[] { t[i], t[i + 1], t[i + 2] };
                    var fresh = new int[3];
                    for (int k = 0; k < 3; k++)
                    {
                        int old = corner[k], n0 = verts.Count;
                        verts.Add(verts[old]);
                        if (normals.Count == n0) normals.Add(normals[old]);
                        if (colors.Count == n0) colors.Add(colors[old]);
                        for (int ch = 1; ch < 4; ch++) if (uvs[ch].Count == n0) uvs[ch].Add(uvs[ch][old]);
                        uvs[0].Add(Map(P(old)));
                        fresh[k] = n0;
                    }
                    t.RemoveRange(i, 3);
                    subTris[floorSub].AddRange(fresh);
                    moved++;
                }
            }
            if (moved == 0) return 0;
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            if (normals.Count == verts.Count) mesh.SetNormals(normals);
            if (colors.Count == verts.Count) mesh.SetColors(colors);
            for (int c = 0; c < 4; c++) if (uvs[c].Count == verts.Count) mesh.SetUVs(c, uvs[c]);
            for (int s = 0; s < subTris.Count; s++) mesh.SetTriangles(subTris[s], s, false);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return moved;
        }

        // Some faces in the room models (notably the underside of every doorway head) have all three UVs
        // collapsed to one point, which smears the texture into a streak; the wooden door liners are strip-
        // mapped across their depth, stretching the grain sideways. Those faces alone are re-projected along
        // their dominant axis at the material's own texel density, matching the model's box mapping, with wood
        // grain (the texture's V axis) running along the piece. Atlased materials are left untouched.
        static int RepairCollapsedUVs(Mesh mesh, Matrix4x4 toRoom, Material[] materials)
        {
            var verts = new List<Vector3>(); mesh.GetVertices(verts);
            var normals = new List<Vector3>(); mesh.GetNormals(normals);
            var tangents = new List<Vector4>(); mesh.GetTangents(tangents);
            var colors = new List<Color>(); mesh.GetColors(colors);
            var uvs = new List<Vector2>[4]; for (int c = 0; c < 4; c++) { uvs[c] = new List<Vector2>(); mesh.GetUVs(c, uvs[c]); }
            if (uvs[0].Count != verts.Count) return 0;
            int repaired = 0;
            var subTris = new List<int[]>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s); subTris.Add(t);
                string name = s < materials.Length && materials[s] ? materials[s].name : "";
                Vector3 P(int i) => toRoom.MultiplyPoint3x4(verts[i]);
                // Gives one triangle corner its own copy of the vertex with a new UV; every other channel is copied.
                void Emit(int triIndex, int corner, Vector2 uv)
                {
                    int old = t[triIndex + corner], n0 = verts.Count;
                    if (normals.Count == n0) normals.Add(normals[old]);
                    if (tangents.Count == n0) tangents.Add(tangents[old]);
                    if (colors.Count == n0) colors.Add(colors[old]);
                    for (int c = 1; c < 4; c++) if (uvs[c].Count == n0) uvs[c].Add(uvs[c][old]);
                    uvs[0].Add(uv);
                    verts.Add(verts[old]);
                    t[triIndex + corner] = n0;
                }
                if (name.Contains("Dressing")) { repaired += RepairUnmappedFurniture(t, P, i => uvs[0][i], Emit); continue; }
                if (!(name.Contains("Wallpaper") || name.Contains("Wood") || name.Contains("Floor"))) continue;
                float Density(int i, out float area, out Vector3 normal)
                {
                    Vector3 a = P(t[i]), b = P(t[i + 1]), c = P(t[i + 2]);
                    normal = Vector3.Cross(b - a, c - a); area = normal.magnitude * .5f; normal = area > 0 ? normal.normalized : Vector3.up;
                    Vector2 ua = uvs[0][t[i]], ub = uvs[0][t[i + 1]], uc = uvs[0][t[i + 2]];
                    float uvArea = Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y)) * .5f;
                    return area > 1e-6f ? Mathf.Sqrt(uvArea / area) : 0;
                }
                var densities = new List<float>(); var offsets = new List<float>();
                for (int i = 0; i < t.Length; i += 3) { float d = Density(i, out float area, out _); if (area > .0005f) densities.Add(d); }
                if (densities.Count == 0) continue;
                densities.Sort(); float median = densities[densities.Count / 2];
                if (median <= 0) continue;
                // Vertical offset of the wall mapping, so repaired wall faces continue their neighbours.
                for (int i = 0; i < t.Length; i += 3)
                    if (Density(i, out float area, out var n) > median * .7f && area > .01f && Mathf.Abs(n.y) < .3f)
                        offsets.Add(uvs[0][t[i]].y - P(t[i]).y * median);
                offsets.Sort(); float vOffset = offsets.Count > 0 ? offsets[offsets.Count / 2] : 0;
                bool wood = name.Contains("Wood"), wallpaper = name.Contains("Wallpaper");
                // Smallest and largest texture scale across the face (singular values of its UV mapping), UV per metre.
                (float lo, float hi) Scale(int i)
                {
                    Vector3 a = P(t[i]), b = P(t[i + 1]), c = P(t[i + 2]);
                    var e1 = (b - a).normalized; var nrm = Vector3.Cross(b - a, c - a).normalized; var e2 = Vector3.Cross(nrm, e1);
                    float x1 = Vector3.Dot(b - a, e1), x2 = Vector3.Dot(c - a, e1), y2 = Vector3.Dot(c - a, e2);
                    if (Mathf.Abs(x1 * y2) < 1e-8f) return (median, median);
                    Vector2 du1 = uvs[0][t[i + 1]] - uvs[0][t[i]], du2 = uvs[0][t[i + 2]] - uvs[0][t[i]];
                    // J maps (x, y) in the face plane to UV: columns J*(1,0) and J*(0,1).
                    var j0 = du1 / x1; var j1 = (du2 - j0 * x2) / y2;
                    float p00 = j0.sqrMagnitude, p11 = j1.sqrMagnitude, p01 = Vector2.Dot(j0, j1);
                    float tr = p00 + p11, det = p00 * p11 - p01 * p01, disc = Mathf.Sqrt(Mathf.Max(0, tr * tr * .25f - det));
                    return (Mathf.Sqrt(Mathf.Max(0, tr * .5f - disc)), Mathf.Sqrt(tr * .5f + disc));
                }
                for (int i = 0; i < t.Length; i += 3)
                {
                    bool collapsed = Density(i, out float area, out var n) < median * .35f;
                    var (lo, hi) = collapsed ? (0f, 0f) : Scale(i);
                    // Wood trims (skirting, dado rails, crown moulding, door casings) are strip-mapped in the
                    // models: the grain barely changes along their length and smears into streaks. Any wood face
                    // whose scale is off by more than ~40% in either direction is re-mapped. Wallpaper: only the
                    // 45 degree corner chamfers, which the box mapping squashes by 30%.
                    bool offScale = !collapsed && (wood ? lo < median * .6f || hi > median * 1.6f : wallpaper && lo < median * .82f);
                    if ((!collapsed && !offScale) || area < .0005f) continue;
                    // In-plane axes of the face; wood grain (V) follows the face's longer extent.
                    Vector3 lo3 = Vector3.Min(Vector3.Min(P(t[i]), P(t[i + 1])), P(t[i + 2])), hi3 = Vector3.Max(Vector3.Max(P(t[i]), P(t[i + 1])), P(t[i + 2])), size = hi3 - lo3;
                    int normalAxis = Mathf.Abs(n.y) >= Mathf.Abs(n.x) && Mathf.Abs(n.y) >= Mathf.Abs(n.z) ? 1 : Mathf.Abs(n.x) >= Mathf.Abs(n.z) ? 0 : 2;
                    for (int k = 0; k < 3; k++)
                    {
                        int old = t[i + k]; var p = P(old);
                        Vector2 uv;
                        if (wood)
                        {
                            int a0 = normalAxis == 0 ? 1 : 0, a1 = normalAxis == 2 ? 1 : 2;
                            int along = size[a0] >= size[a1] ? a0 : a1, across = along == a0 ? a1 : a0;
                            uv = new Vector2(-p[across], p[along]) * median;
                        }
                        else if (normalAxis != 1 && Mathf.Abs(Mathf.Abs(n.x) - Mathf.Abs(n.z)) < .5f)
                        {
                            // Diagonal wall face: measure along the face itself, so it keeps the wall's true scale.
                            var along = Vector3.Cross(Vector3.up, n).normalized;
                            uv = new Vector2(-Vector3.Dot(p, along) * median, p.y * median + vOffset);
                        }
                        else uv = normalAxis == 1 ? new Vector2(-p.x, -p.z) * median
                            : normalAxis == 0 ? new Vector2(-p.z * median, p.y * median + vOffset) : new Vector2(-p.x * median, p.y * median + vOffset);
                        Emit(i, k, uv);
                    }
                    repaired++;
                }
            }
            if (repaired == 0) return 0;
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            if (normals.Count == verts.Count) mesh.SetNormals(normals);
            if (colors.Count == verts.Count) mesh.SetColors(colors);
            for (int c = 0; c < 4; c++) if (uvs[c].Count == verts.Count) mesh.SetUVs(c, uvs[c]);
            for (int s = 0; s < subTris.Count; s++) mesh.SetTriangles(subTris[s], s, false);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return repaired;
        }

        // The furniture atlas is a 4 x 4 grid (book covers, page edges, brass, rug, cloth, prints, plaster and
        // dark wood planks). Some furniture parts were exported without UVs: every vertex at (0, 0), so they
        // show one flat texel of the plaster tile and read as untextured (the sideboard carcass, desk tops,
        // a box, drawer pulls). Each such part is mapped onto the tile it is made of, grain along its length.
        static readonly Rect DarkWoodTile = new(.5f, 0f, .25f, .25f), BrassTile = new(.25f, .5f, .25f, .25f);
        static int RepairUnmappedFurniture(int[] t, Func<int, Vector3> P, Func<int, Vector2> UV, Action<int, int, Vector2> emit)
        {
            var faces = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector2 a = UV(t[i]), b = UV(t[i + 1]), c = UV(t[i + 2]);
                if (Mathf.Max((b - a).magnitude, (c - a).magnitude, (c - b).magnitude) < 1e-4f) faces.Add(i);
            }
            if (faces.Count == 0) return 0;
            // Parts: unmapped faces connected through shared corner positions.
            var ids = new Dictionary<Vector3Int, int>(); var parent = new List<int>();
            int Id(Vector3 p) { var k = Vector3Int.RoundToInt(p * 1000f); if (!ids.TryGetValue(k, out int id)) { id = ids.Count; ids[k] = id; parent.Add(id); } return id; }
            int Root(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var i in faces) { int a = Id(P(t[i])), b = Id(P(t[i + 1])), c = Id(P(t[i + 2])); parent[Root(b)] = Root(a); parent[Root(c)] = Root(a); }
            var parts = new Dictionary<int, Bounds>();
            foreach (var i in faces)
            {
                int r = Root(Id(P(t[i])));
                for (int k = 0; k < 3; k++) { var p = P(t[i + k]); if (parts.TryGetValue(r, out var b)) { b.Encapsulate(p); parts[r] = b; } else parts[r] = new Bounds(p, Vector3.zero); }
            }
            const float margin = .012f;
            foreach (var i in faces)
            {
                var bounds = parts[Root(Id(P(t[i])))]; var size = bounds.size;
                int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                float extent = size[longest];
                var tile = extent < .25f ? BrassTile : DarkWoodTile;
                float scale = (tile.width - 2 * margin) / Mathf.Max(extent, .2f);
                var n = Vector3.Cross(P(t[i + 1]) - P(t[i]), P(t[i + 2]) - P(t[i]));
                int normalAxis = Mathf.Abs(n.y) >= Mathf.Abs(n.x) && Mathf.Abs(n.y) >= Mathf.Abs(n.z) ? 1 : Mathf.Abs(n.x) >= Mathf.Abs(n.z) ? 0 : 2;
                int along, across;
                if (normalAxis == longest) { int a0 = (normalAxis + 1) % 3, a1 = (normalAxis + 2) % 3; along = size[a0] >= size[a1] ? a0 : a1; across = along == a0 ? a1 : a0; }
                else { along = longest; across = 3 - normalAxis - longest; }
                for (int k = 0; k < 3; k++)
                {
                    var p = P(t[i + k]) - bounds.min;
                    emit(i, k, tile.min + new Vector2(margin + p[across] * scale, margin + p[along] * scale));
                }
            }
            return faces.Count;
        }

        // A capsule around the pot, as tall as the plant: blocks walking through it without the
        // snagging and NavMesh holes a full leaf collider would cause. Points are in room space.
        public static CapsuleCollider AddPlantCapsule(Transform parent, List<Vector3> points, Bounds all)
        {
            float potTop = all.min.y + all.size.y * .35f;
            var pot = new Bounds(new Vector3(all.center.x, all.min.y, all.center.z), Vector3.zero);
            foreach (var p in points) if (p.y <= potTop) pot.Encapsulate(p);
            float radius = Mathf.Clamp(Mathf.Max(pot.size.x, pot.size.z) * .5f, .12f, .35f);
            float height = Mathf.Max(Mathf.Min(all.size.y, 1.2f), radius * 2);
            var go = new GameObject("Plant Collider"); go.transform.SetParent(parent, false);
            var toParent = parent.worldToLocalMatrix * parent.root.localToWorldMatrix;
            go.transform.localPosition = toParent.MultiplyPoint3x4(new Vector3(pot.center.x, all.min.y, pot.center.z));
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 1; capsule.radius = radius; capsule.height = height; capsule.center = new Vector3(0, height * .5f, 0);
            return capsule;
        }

        // True when the island's underside sits on an upward-facing face of the piece.
        static bool RestsOn(Piece island, Piece piece, Vector3[] v, Matrix4x4 toRoom)
        {
            var b = island.bounds; var outer = piece.bounds; outer.Expand(.05f);
            if (b.center.x < outer.min.x || b.center.x > outer.max.x || b.center.z < outer.min.z || b.center.z > outer.max.z) return false;
            var centre = new Vector3(b.center.x, 0, b.center.z);
            foreach (var t in piece.tris)
            {
                Vector3 p0 = toRoom.MultiplyPoint3x4(v[t.a]), p1 = toRoom.MultiplyPoint3x4(v[t.b]), p2 = toRoom.MultiplyPoint3x4(v[t.c]);
                var n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude < 1e-8f || Mathf.Abs(n.normalized.y) < .95f) continue;
                float y = (p0.y + p1.y + p2.y) / 3f;
                if (Mathf.Abs(b.min.y - y) < .04f && PointInTriangleXZ(centre, p0, p1, p2)) return true;
            }
            return false;
        }
        static bool IsLegacyProp(Transform t)
        {
            // Today's authored decoration reuses some old names (Props/Plant.prefab); it is never legacy.
            if (t.name == RoomPlantPlacer.AuthoredName || t.GetComponentInChildren<LanternLight>(true)) return false;
            foreach (var instance in t.GetComponentsInChildren<Transform>(true))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject);
                var path = source ? AssetDatabase.GetAssetPath(source) : "";
                if (path.Contains("/Mansion/Props/") && LegacyProps.Any(p => path.EndsWith("/" + p + ".prefab"))) return true;
            }
            return false;
        }
        // Names come from the overall size plus the height of the largest upward surface
        // (items on a desk make its bounds taller than its working surface).
        static string Classify(Bounds b, Mesh mesh)
        {
            float h = b.size.y, longSide = Mathf.Max(b.size.x, b.size.z);
            var v = mesh.vertices; var t = mesh.triangles; var area = new Dictionary<int, float>();
            for (int i = 0; i < t.Length; i += 3)
            {
                var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (n.normalized.y < .95f) continue;
                int key = Mathf.RoundToInt((v[t[i]].y + v[t[i + 1]].y + v[t[i + 2]].y) / 3f * 20);
                area[key] = (area.TryGetValue(key, out var a) ? a : 0) + n.magnitude * .5f;
            }
            float top = area.Count > 0 ? area.OrderByDescending(p => p.Value).First().Key / 20f : h;
            if (h > 1.8f) return "Bookcase";
            if (longSide < .8f && h > .8f) return "Chair";
            if (longSide >= 1.2f && top < .95f && top > .5f) return h > 1.3f ? "Desk" : "Table";
            if (h >= .9f && h <= 1.6f && longSide >= 1.2f) return "Sideboard";
            if (h > .5f) return "Cabinet";
            return "Furniture";
        }

        // Item surfaces: upward faces of the piece between 0.4 m and 1.9 m with headroom and room for a pickup.
        static int AddItemAnchors(Transform setup, Mesh mesh, Bounds roomBounds, Vector3 pivot, RoomModule room)
        {
            var v = mesh.vertices; var t = mesh.triangles;
            var levels = new Dictionary<int, List<(Vector3 a, Vector3 b, Vector3 c, float area)>>();
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var n = Vector3.Cross(b - a, c - a); float area = n.magnitude * .5f;
                if (area < .002f || n.normalized.y < .95f) continue;
                float y = (a.y + b.y + c.y) / 3f; if (y < .4f || y > 1.9f) continue;
                int key = Mathf.RoundToInt(y * 50);
                if (!levels.TryGetValue(key, out var list)) levels[key] = list = new();
                list.Add((a, b, c, area));
            }
            var chosen = new List<Vector3>(); var axes = new List<Vector3>();
            // A spot needs a fully supported 46 x 30 cm footprint on the piece's own main surface, aligned
            // with the surface's long side, plus 45 cm of headroom: pickups never lean on backs or overhang.
            foreach (var level in levels.Values.Where(l => l.Sum(x => x.area) >= .12f).OrderByDescending(l => l.Sum(x => x.area)))
            {
                var extent = new Bounds(level[0].a, Vector3.zero); foreach (var f in level) { extent.Encapsulate(f.a); extent.Encapsulate(f.b); extent.Encapsulate(f.c); }
                var along = extent.size.x >= extent.size.z ? Vector3.right : Vector3.forward; var across = Vector3.Cross(Vector3.up, along);
                bool Supported(Vector3 p)
                {
                    foreach (float u in new[] { -.23f, 0, .23f }) foreach (float w in new[] { -.15f, 0, .15f })
                        if (!level.Any(f => PointInTriangleXZ(p + along * u + across * w, f.a, f.b, f.c))) return false;
                    return true;
                }
                // Anything rising into the 45 cm above the footprint (books, boxes, the shelf above) blocks it.
                bool Covered(Vector3 p)
                {
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                        float low = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), high = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                        if (high <= p.y + .02f || low > p.y + .45f) continue;
                        foreach (float u in new[] { -.23f, 0, .23f }) foreach (float w in new[] { -.15f, 0, .15f })
                            if (PointInTriangleXZ(p + along * u + across * w, a, b, c)) return true;
                    }
                    return false;
                }
                var weighted = level.Aggregate(Vector3.zero, (s, f) => s + (f.a + f.b + f.c) / 3f * f.area) / level.Sum(f => f.area);
                // Candidates: the level's weighted centre first, then face centres from largest to smallest.
                var tries = new[] { weighted }.Concat(level.OrderByDescending(f => f.area).Select(f => (f.a + f.b + f.c) / 3f));
                foreach (var candidate in tries)
                {
                    var point = candidate; point.y = level[0].a.y;
                    if (!Supported(point) || Covered(point) || chosen.Any(p => (p - point).sqrMagnitude < .6f * .6f)) continue;
                    chosen.Add(point); axes.Add(along); break;
                }
                if (chosen.Count >= 2) break;
            }
            var roomCentre = room.OccupancyVolumes[0].center; roomCentre.y = 0;
            for (int i = 0; i < chosen.Count; i++)
            {
                var anchor = new GameObject($"Item Surface {i + 1:00}", typeof(ItemSpawnPoint)).transform;
                // Pickups spawn centred on the spot, so it floats 20 cm up and the item settles onto the surface.
                anchor.SetParent(setup, false); anchor.localPosition = chosen[i] + Vector3.up * .2f;
                var worldish = pivot + chosen[i];
                var toCentre = Vector3.ProjectOnPlane(roomCentre - worldish, Vector3.up);
                // Item forward (its long side) runs along the surface's long side.
                anchor.localRotation = Quaternion.LookRotation(axes[i]);
                // Approach: floor just beyond the piece footprint, toward the room centre.
                float reach = Mathf.Max(roomBounds.extents.x, roomBounds.extents.z) + .6f;
                var approachWorld = new Vector3(pivot.x, 0, pivot.z) + (toCentre.sqrMagnitude > .01f ? toCentre.normalized : Vector3.forward) * reach;
                approachWorld.y = pivot.y + .05f;
                anchor.GetComponent<ItemSpawnPoint>().approachOffset = anchor.InverseTransformPoint(setup.parent.TransformPoint(approachWorld));
            }
            return chosen.Count;
        }
        static bool PointInTriangleXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = (p.x - b.x) * (a.z - b.z) - (a.x - b.x) * (p.z - b.z), d2 = (p.x - c.x) * (b.z - c.z) - (b.x - c.x) * (p.z - c.z), d3 = (p.x - a.x) * (c.z - a.z) - (c.x - a.x) * (p.z - a.z);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0; return !(neg && pos);
        }

        // Copies the listed triangles (and only the vertices/materials they use) into a new mesh,
        // optionally transformed into room space relative to a pivot.
        static Mesh BuildMesh(Mesh source, IEnumerable<Tri> triangles, Matrix4x4 transform, Vector3 pivot, Material[] materials, out Material[] usedMaterials, string name)
        {
            var sv = source.vertices; var sn = source.normals; var st = source.tangents; var sc = source.colors;
            var uv = new List<Vector2>[4]; for (int c = 0; c < 4; c++) { uv[c] = new List<Vector2>(); source.GetUVs(c, uv[c]); }
            var normalMatrix = transform.inverse.transpose; bool flip = transform.determinant < 0;
            var map = new Dictionary<int, int>(); var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tans = new List<Vector4>(); var cols = new List<Color>();
            var newUv = new List<Vector2>[4]; for (int c = 0; c < 4; c++) newUv[c] = new List<Vector2>();
            var bySub = new SortedDictionary<int, List<int>>();
            int Map(int i)
            {
                if (map.TryGetValue(i, out int m)) return m;
                m = verts.Count; map[i] = m;
                verts.Add(transform.MultiplyPoint3x4(sv[i]) - pivot);
                if (sn.Length == sv.Length) norms.Add(normalMatrix.MultiplyVector(sn[i]).normalized);
                if (st.Length == sv.Length) { var d = transform.MultiplyVector(new Vector3(st[i].x, st[i].y, st[i].z)).normalized; tans.Add(new Vector4(d.x, d.y, d.z, flip ? -st[i].w : st[i].w)); }
                if (sc.Length == sv.Length) cols.Add(sc[i]);
                for (int c = 0; c < 4; c++) if (uv[c].Count == sv.Length) newUv[c].Add(uv[c][i]);
                return m;
            }
            foreach (var t in triangles)
            {
                if (!bySub.TryGetValue(t.sub, out var list)) bySub[t.sub] = list = new List<int>();
                int a = Map(t.a), b = Map(t.b), c = Map(t.c);
                if (flip) { list.Add(a); list.Add(c); list.Add(b); } else { list.Add(a); list.Add(b); list.Add(c); }
            }
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            if (norms.Count == verts.Count) mesh.SetNormals(norms);
            if (tans.Count == verts.Count) mesh.SetTangents(tans);
            if (cols.Count == verts.Count) mesh.SetColors(cols);
            for (int c = 0; c < 4; c++) if (newUv[c].Count == verts.Count) mesh.SetUVs(c, newUv[c]);
            mesh.subMeshCount = bySub.Count; int index = 0; var used = new List<Material>();
            foreach (var pair in bySub) { mesh.SetTriangles(pair.Value, index++, false); used.Add(pair.Key < materials.Length ? materials[pair.Key] : null); }
            mesh.RecalculateBounds(); usedMaterials = used.ToArray();
            return mesh;
        }
        // Updates an existing asset in place so references and GUIDs survive reruns.
        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                // Rewrite through the Mesh API, not CopySerialized: CopySerialized replaces the stored data but
                // renderers (and the GPU Resident Drawer) keep drawing the previous geometry and UVs.
                existing.Clear();
                existing.indexFormat = mesh.indexFormat;
                existing.SetVertices(mesh.vertices);
                if (mesh.normals.Length > 0) existing.SetNormals(mesh.normals);
                if (mesh.tangents.Length > 0) existing.SetTangents(mesh.tangents);
                if (mesh.colors.Length > 0) existing.SetColors(mesh.colors);
                var channel = new List<Vector2>();
                for (int c = 0; c < 4; c++) { mesh.GetUVs(c, channel); if (channel.Count > 0) existing.SetUVs(c, channel); }
                existing.subMeshCount = mesh.subMeshCount;
                for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s, false);
                existing.RecalculateBounds();
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing); return existing;
            }
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }
    }
}
