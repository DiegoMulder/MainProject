using System;
using System.Collections.Generic;
using UnityEngine;
namespace SurvivalFP
{
    // Pure setup-time geometry planning. Explicit placements, including structure choices,
    // are synchronized so clients never run random selection or physics-dependent retries.
    public sealed class MansionLayoutPlanner
    {
        public readonly List<RoomPlacement> Rooms = new();
        public readonly List<ConnectionPlacement> Connections = new();
        public readonly List<StructurePlacement> Structures = new();
        public int ExitRoom, ExitConnector;
        public void Generate(MansionSettings settings, int seed)
        {
            Rooms.Clear(); Connections.Clear(); Structures.Clear(); placedVolumes.Clear();
            var compatible=new List<int>(); var candidateVolumes=new List<Bounds>();
            var problem=settings?settings.Validate():"No map content assigned.";
            if (problem != null) throw new InvalidOperationException("Map configuration: " + problem);
            var rng = new System.Random(seed);
            int stairPool=Array.FindIndex(settings.rooms,r=>r.prefab && r.prefab.staircase);
            if(settings.floorCount>1 && stairPool<0)throw new InvalidOperationException("Multiple floors require a staircase module in the room pool.");
            int stairModule=settings.PoolModule(stairPool);
            // The starting room is placed exactly once, first, at the origin: the root of the room graph.
            // It counts toward Room Count, so a count of 20 means the starting room plus 19 others.
            Rooms.Add(new RoomPlacement { module = MansionSettings.StartModule });
            placedVolumes.Add(new List<Bounds>()); WorldVolumes(Rooms[0],settings.startingRoom,placedVolumes[0]);
            var open = new List<(int room, int connector)>();
            AddOpen(0, -1);
            for (int next = 1; next < settings.roomCount; next++)
            {
                bool placed = false;
                float highestRoom=0;foreach(var room in Rooms)highestRoom=Mathf.Max(highestRoom,room.position.y);
                bool extendFloors=highestRoom<(settings.floorCount-1)*settings.floorHeight-.1f;
                for (int attempt = 0; attempt < settings.attemptsPerRoom && open.Count > 0; attempt++)
                {
                    int openIndex = rng.Next(open.Count);
                    if(extendFloors)
                    {
                        float highestSocket=float.NegativeInfinity;
                        for(int oi=0;oi<open.Count;oi++)
                        {
                            var candidate=open[oi];var placedRoom=Rooms[candidate.room];
                            float y=placedRoom.position.y+settings.Module(placedRoom.module).connectors[candidate.connector].transform.localPosition.y;
                            if(y>highestSocket+.01f){highestSocket=y;openIndex=oi;}
                        }
                    }
                    var socket = open[openIndex]; var parent = Rooms[socket.room];
                    var pc = settings.Module(parent.module).connectors[socket.connector];
                    // Normal pool only; the starting room is never selected again.
                    bool aboveEverything=parent.position.y+pc.transform.localPosition.y>highestRoom+.1f;
                    int module = extendFloors && !aboveEverything ? stairModule
                        : settings.PoolModule(WeightedRoomIndex(settings, rng, extendFloors));
                    var prefab = settings.Module(module);
                    // Only compatible connectors are candidates; each is aligned from its actual transform.
                    compatible.Clear();
                    for(int c=0;c<prefab.connectors.Length;c++)if(prefab.connectors[c] && pc.Compatible(prefab.connectors[c]))compatible.Add(c);
                    if(compatible.Count==0)continue;
                    int ci=compatible[rng.Next(compatible.Count)];var child=prefab.connectors[ci];
                    if(!TryAlign(parent,pc,module,child,out var placement))continue;
                    WorldVolumes(placement,prefab,candidateVolumes);
                    if(!WithinFloors(candidateVolumes,settings) || OverlapsPlaced(candidateVolumes))continue;
                    // Register only after acceptance so rejected candidates never reach the graph.
                    Rooms.Add(placement); placedVolumes.Add(new List<Bounds>(candidateVolumes)); open.RemoveAt(openIndex);
                    Connections.Add(new ConnectionPlacement { a = socket.room, ac = socket.connector, b = Rooms.Count - 1, bc = ci });
                    AddOpen(Rooms.Count - 1, ci); placed = true; break;
                }
                if (!placed) break;
            }
            if (Rooms.Count < 4) throw new InvalidOperationException("Generation could not place four connected rooms. Check connectors and occupancy volumes.");
            var unreachable=UnreachableRooms();
            if(unreachable.Count>0)throw new InvalidOperationException("Room graph disconnected: rooms "+string.Join(",",unreachable)+" have no connector path to the starting room.");
            if(!Rooms.Exists(r=>r.position.y>=(settings.floorCount-1)*settings.floorHeight-.1f))throw new InvalidOperationException("Placement attempts exhausted before connecting every requested floor.");
            var exits = open.FindAll(c => (settings.allowExitInStartingRoom || c.room != 0) && settings.Module(Rooms[c.room].module).connectors[c.connector].exitEligible);
            if (exits.Count == 0) throw new InvalidOperationException("No eligible exit connector.");
            // Reject full swept-door/approach volumes, not just a connector point.
            Bounds reserved=default;bool foundExit=false;
            while(exits.Count>0)
            {
                int index=rng.Next(exits.Count);var exit=exits[index];exits.RemoveAt(index);
                var placement=Rooms[exit.room];var socket=settings.Module(placement.module).connectors[exit.connector];
                var position=placement.position+placement.Rotation*socket.transform.localPosition;
                var rotation=placement.Rotation*socket.transform.localRotation;
                var clearance=ExitPlacement.WorldBounds(ExitPlacement.LocalClearance(settings.exit),position,rotation);
                bool blocked=false;
                for(int r=0;r<Rooms.Count && !blocked;r++)if(r!=exit.room)
                    foreach(var volume in placedVolumes[r]){var bounds=volume;bounds.Expand(.3f);if(bounds.Intersects(clearance)){blocked=true;break;}}
                if(blocked)continue;
                ExitRoom=exit.room;ExitConnector=exit.connector;reserved=clearance;foundExit=true;break;
            }
            if(!foundExit)throw new InvalidOperationException("No exit has enough door-swing and approach clearance. Adjust room spacing or eligible connectors.");
            var doorClearances=new List<Bounds>();
            var localDoorClearance=ExitPlacement.LocalClearance(settings.door,true);
            foreach(var connection in Connections)
            {
                var room=Rooms[connection.a];var socket=settings.Module(room.module).connectors[connection.ac];
                if(socket.allowDoor)doorClearances.Add(ExitPlacement.WorldBounds(localDoorClearance,room.position+room.Rotation*socket.transform.localPosition,room.Rotation*socket.transform.localRotation));
            }
            // One authoritative roll per structure. Every entry is recorded so clients apply
            // exactly the server's result; a structure in a door swing or the exit approach is forced off.
            var footprints=new Dictionary<(RoomModule,int),Bounds>();
            for (int r = 0; r < Rooms.Count; r++)
            {
                var prefab = settings.Module(Rooms[r].module);
                if (prefab.randomizedStructures == null) continue;
                for (int s = 0; s < prefab.randomizedStructures.Length; s++)
                {
                    if (!prefab.TryGetStructure(s, out var target, out _)) continue;
                    bool enabled = rng.NextDouble() * 100 < Mathf.Clamp(prefab.randomizedStructures[s].spawnChance, 0, 100);
                    if (enabled)
                    {
                        if (!footprints.TryGetValue((prefab, s), out var local)) footprints[(prefab, s)] = local = prefab.StructureLocalBounds(target);
                        var world = ExitPlacement.WorldBounds(local, Rooms[r].position, Rooms[r].Rotation);
                        if (world.Intersects(reserved) || doorClearances.Exists(clearance => clearance.Intersects(world))) enabled = false;
                    }
                    Structures.Add(new StructurePlacement { room = r, entry = s, enabled = enabled });
                }
            }
            void AddOpen(int room, int exclude)
            {
                var connectors = settings.Module(Rooms[room].module).connectors;
                for (int c = 0; c < connectors.Length; c++) if (c != exclude && connectors[c]) open.Add((room,c));
            }
        }
        // Weighted pick from the normal pool; skips the unique starting room and, when asked, staircases.
        static int WeightedRoomIndex(MansionSettings settings, System.Random rng, bool excludeStairs)
        {
            var rooms = settings.rooms;
            bool Eligible(int i) => settings.Selectable(i) && !(excludeStairs && rooms[i].prefab.staircase);
            int total = 0; for (int i = 0; i < rooms.Length; i++) if (Eligible(i)) total += Math.Max(1,rooms[i].weight);
            int roll = rng.Next(total);
            for (int i = 0; i < rooms.Length; i++) { if (!Eligible(i)) continue; roll -= Math.Max(1,rooms[i].weight); if (roll < 0) return i; }
            throw new InvalidOperationException("The normal room pool has no selectable rooms.");
        }
        readonly List<List<Bounds>> placedVolumes = new();
        // Doorway seams may touch; this per-side tolerance keeps flush neighbours valid.
        const float SeamTolerance = .04f;
        public IReadOnlyList<Bounds> VolumesOf(int room) => placedVolumes[room];
        // Aligns the candidate so its connector coincides with the parent connector, facing it.
        static bool TryAlign(RoomPlacement parent, RoomConnector parentConnector, int module, RoomConnector child, out RoomPlacement placement)
        {
            Vector3 socketPosition = parent.position + parent.Rotation * parentConnector.transform.localPosition;
            Vector3 socketForward = parent.Rotation * parentConnector.transform.localRotation * Vector3.forward;
            float angle = Quaternion.LookRotation(-socketForward).eulerAngles.y - child.transform.localEulerAngles.y;
            placement = new RoomPlacement { module = module, quarter = ((Mathf.RoundToInt(angle / 90) % 4) + 4) % 4 };
            placement.position = socketPosition - placement.Rotation * child.transform.localPosition;
            // Placements replicate as quarter turns; reject connectors whose yaw cannot align exactly.
            Vector3 childPosition = placement.position + placement.Rotation * child.transform.localPosition;
            Vector3 childForward = placement.Rotation * child.transform.localRotation * Vector3.forward;
            return (childPosition - socketPosition).sqrMagnitude < .0001f && Vector3.Dot(childForward, socketForward) < -.999f;
        }
        public static void WorldVolumes(RoomPlacement room, RoomModule prefab, List<Bounds> output)
        {
            output.Clear();
            foreach (var local in prefab.OccupancyVolumes)
            {
                var world = ExitPlacement.WorldBounds(local, room.position, room.Rotation);
                world.Expand(-2 * SeamTolerance);
                output.Add(world);
            }
        }
        static bool WithinFloors(List<Bounds> volumes, MansionSettings settings)
        {
            foreach (var volume in volumes)
                if (volume.min.y < -.05f || volume.max.y > settings.floorCount * settings.floorHeight) return false;
            return true;
        }
        bool OverlapsPlaced(List<Bounds> volumes)
        {
            foreach (var placed in placedVolumes)
                foreach (var existing in placed)
                    foreach (var volume in volumes)
                        if (existing.Intersects(volume)) return true;
            return false;
        }
        // Logical reachability comes only from accepted connector pairings, never distance or bounds.
        public List<int> UnreachableRooms()
        {
            var links = new List<int>[Rooms.Count];
            for (int i = 0; i < links.Length; i++) links[i] = new List<int>();
            foreach (var c in Connections) { links[c.a].Add(c.b); links[c.b].Add(c.a); }
            var seen = new bool[Rooms.Count]; var queue = new Queue<int>();
            if (Rooms.Count > 0) { seen[0] = true; queue.Enqueue(0); }
            while (queue.Count > 0) foreach (int next in links[queue.Dequeue()]) if (!seen[next]) { seen[next] = true; queue.Enqueue(next); }
            var missing = new List<int>();
            for (int i = 0; i < seen.Length; i++) if (!seen[i]) missing.Add(i);
            return missing;
        }
    }
}
