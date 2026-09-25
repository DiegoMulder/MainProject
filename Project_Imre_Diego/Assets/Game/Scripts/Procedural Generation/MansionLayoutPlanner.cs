using System;
using System.Collections.Generic;
using UnityEngine;
namespace SurvivalFP
{
    // Pure setup-time geometry planning. Explicit placements, including prop choices,
    // are synchronized so clients never run random selection or physics-dependent retries.
    public sealed class MansionLayoutPlanner
    {
        public readonly List<RoomPlacement> Rooms = new();
        public readonly List<ConnectionPlacement> Connections = new();
        public readonly List<PropPlacement> Props = new();
        public readonly List<StructurePlacement> Structures = new();
        public int ExitRoom, ExitConnector;
        public void Generate(MansionSettings settings, int seed)
        {
            Rooms.Clear(); Connections.Clear(); Props.Clear(); Structures.Clear();
            if (!settings || settings.rooms == null || settings.rooms.Length == 0) throw new InvalidOperationException("No room modules configured.");
            var rng = new System.Random(seed);
            int stairModule=Array.FindIndex(settings.rooms,r=>r.prefab && r.prefab.staircase);
            if(settings.floorCount>1 && stairModule<0)throw new InvalidOperationException("Multiple floors require a staircase module in the room catalog.");
            Rooms.Add(new RoomPlacement { module = 0 });
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
                            float y=placedRoom.position.y+settings.rooms[placedRoom.module].prefab.connectors[candidate.connector].transform.localPosition.y;
                            if(y>highestSocket+.01f){highestSocket=y;openIndex=oi;}
                        }
                    }
                    var socket = open[openIndex]; var parent = Rooms[socket.room];
                    var pc = settings.rooms[parent.module].prefab.connectors[socket.connector];
                    int module = WeightedRoomIndex(settings.rooms, rng);
                    if(extendFloors)module=parent.position.y+pc.transform.localPosition.y>highestRoom+.1f?0:stairModule;
                    var prefab = settings.rooms[module].prefab;
                    int ci = rng.Next(prefab.connectors.Length); var child = prefab.connectors[ci];
                    if (!pc.Compatible(child)) continue;
                    Vector3 forward = parent.Rotation * pc.transform.localRotation * Vector3.forward;
                    float angle = Quaternion.LookRotation(-forward).eulerAngles.y - child.transform.localEulerAngles.y;
                    int quarter = ((Mathf.RoundToInt(angle / 90) % 4) + 4) % 4;
                    var placement = new RoomPlacement { module = module, quarter = quarter };
                    placement.position = parent.position + parent.Rotation * pc.transform.localPosition - placement.Rotation * child.transform.localPosition;
                    var bounds = BoundsOf(placement, prefab.size);
                    if(bounds.min.y<-.05f || bounds.max.y>settings.floorCount*settings.floorHeight)continue;
                    bool overlaps = Rooms.Exists(r => bounds.Intersects(BoundsOf(r, settings.rooms[r.module].prefab.size)));
                    if (overlaps) continue;
                    Rooms.Add(placement); open.RemoveAt(openIndex);
                    Connections.Add(new ConnectionPlacement { a = socket.room, ac = socket.connector, b = Rooms.Count - 1, bc = ci });
                    AddOpen(Rooms.Count - 1, ci); placed = true; break;
                }
                if (!placed) break;
            }
            if (Rooms.Count < 4) throw new InvalidOperationException("Generation could not place four connected rooms. Check connectors and bounds.");
            if(!Rooms.Exists(r=>r.position.y>=(settings.floorCount-1)*settings.floorHeight-.1f))throw new InvalidOperationException("Placement attempts exhausted before connecting every requested floor.");
            var exits = open.FindAll(c => (settings.allowExitInStartingRoom || c.room != 0) && settings.rooms[Rooms[c.room].module].prefab.connectors[c.connector].exitEligible);
            if (exits.Count == 0) throw new InvalidOperationException("No eligible exit connector.");
            // Reject full swept-door/approach volumes, not just a connector point.
            Bounds reserved=default;bool foundExit=false;
            while(exits.Count>0)
            {
                int index=rng.Next(exits.Count);var exit=exits[index];exits.RemoveAt(index);
                var placement=Rooms[exit.room];var socket=settings.rooms[placement.module].prefab.connectors[exit.connector];
                var position=placement.position+placement.Rotation*socket.transform.localPosition;
                var rotation=placement.Rotation*socket.transform.localRotation;
                var clearance=ExitPlacement.WorldBounds(ExitPlacement.LocalClearance(settings.exit),position,rotation);
                bool blocked=false;
                for(int r=0;r<Rooms.Count;r++)if(r!=exit.room)
                {var bounds=BoundsOf(Rooms[r],settings.rooms[Rooms[r].module].prefab.size);bounds.Expand(.3f);if(bounds.Intersects(clearance)){blocked=true;break;}}
                if(blocked)continue;
                ExitRoom=exit.room;ExitConnector=exit.connector;reserved=clearance;foundExit=true;break;
            }
            if(!foundExit)throw new InvalidOperationException("No exit has enough door-swing and approach clearance. Adjust room spacing or eligible connectors.");
            var doorClearances=new List<Bounds>();
            var localDoorClearance=ExitPlacement.LocalClearance(settings.door,true);
            foreach(var connection in Connections)
            {
                var room=Rooms[connection.a];var socket=settings.rooms[room.module].prefab.connectors[connection.ac];
                if(socket.allowDoor)doorClearances.Add(ExitPlacement.WorldBounds(localDoorClearance,room.position+room.Rotation*socket.transform.localPosition,room.Rotation*socket.transform.localRotation));
            }
            for (int r = 0; r < Rooms.Count; r++)
            {
                var structures = settings.rooms[Rooms[r].module].prefab.randomizedStructures;
                if (structures != null)
                    for (int s = 0; s < structures.Length; s++)
                    {
                        var entry = structures[s];
                        if (!entry.target) continue;
                        Structures.Add(new StructurePlacement { room = r, entry = s,
                            enabled = rng.NextDouble() * 100 < Mathf.Clamp(entry.spawnChance, 0, 100) });
                    }
                var anchors = settings.rooms[Rooms[r].module].prefab.propAnchors;
                for (int a = 0; a < anchors.Length; a++)
                {
                    var anchor = anchors[a]; if (rng.NextDouble() > anchor.chance || anchor.variants.Length == 0) continue;
                    int total = 0; foreach (var v in anchor.variants) total += Math.Max(1,v.weight);
                    int roll = rng.Next(total), selected = 0;
                    for (int v = 0; v < anchor.variants.Length; v++) { roll -= Math.Max(1,anchor.variants[v].weight); if (roll < 0) { selected = v; break; } }
                    int turn=anchor.randomHalfTurn?rng.Next(2):0;
                    var placement=Rooms[r];var propPosition=placement.position+placement.Rotation*anchor.transform.localPosition;
                    var propRotation=placement.Rotation*anchor.transform.localRotation*Quaternion.Euler(0,turn*180,0);
                    var propBounds=ExitPlacement.PrefabBounds(anchor.variants[selected].prefab,propPosition,propRotation);
                    if(propBounds.Intersects(reserved)||doorClearances.Exists(clearance=>clearance.Intersects(propBounds)))continue;
                    Props.Add(new PropPlacement { room = r, anchor = a, variant = selected, halfTurn = turn });
                }
            }
            void AddOpen(int room, int exclude)
            {
                var connectors = settings.rooms[Rooms[room].module].prefab.connectors;
                for (int c = 0; c < connectors.Length; c++) if (c != exclude) open.Add((room,c));
            }
        }
        static int WeightedRoomIndex(WeightedRoom[] rooms, System.Random rng)
        {
            int total = 0; foreach (var r in rooms) total += Math.Max(1,r.weight);
            int roll = rng.Next(total);
            for (int i = 0; i < rooms.Length; i++) { roll -= Math.Max(1,rooms[i].weight); if (roll < 0) return i; }
            return 0;
        }
        public static Bounds BoundsOf(RoomPlacement room, Vector3 size)
        {
            if (room.quarter % 2 != 0) size = new Vector3(size.z,size.y,size.x);
            return new Bounds(room.position + Vector3.up * size.y * .5f, size - new Vector3(.08f,.08f,.08f));
        }
    }
}
