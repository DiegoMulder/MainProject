using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP
{
    public sealed class MansionWorld : MonoBehaviour
    {
        public readonly List<RoomModule> Rooms=new();
        public readonly List<ItemSpawnPoint> ItemAnchors=new();
        public readonly List<DoorInteractable> Doors=new();
        public bool Built {get; private set;}
        public int NetworkPropCount {get;private set;}
        // Players start at the starting room's Player Start, which follows the room's generated rotation.
        public Transform PlayerStart=>Rooms.Count>0?Rooms[0].playerStart:null;
        public Vector3 SpawnPosition=>PlayerStart?PlayerStart.position+Vector3.up*.1f:Rooms.Count>0?Rooms[0].transform.position+Vector3.up*.1f:Vector3.up;
        public Quaternion SpawnRotation=>PlayerStart?Quaternion.Euler(0,PlayerStart.eulerAngles.y,0):Quaternion.identity;
        // The first player stands on the start; others line up beside and behind it in its own frame.
        public Vector3 SpawnPoint(int index)=>SpawnPosition+SpawnRotation*new Vector3(((index%3+1)%3-1)*1.2f,0,-(index/3)*1.2f);
        GameObject geometry;
        public void Build(RoundManager round,bool navigation)
        {
            if(Built) return;
            var settings=round.settings;
            geometry=new GameObject("Generated Mansion"); geometry.transform.SetParent(transform,false);
            foreach(var placement in round.Layout)
            {
                var room=Instantiate(settings.Module(placement.module),placement.position,placement.Rotation,geometry.transform);
                room.name=$"Room {Rooms.Count:00} - {room.name}"; Rooms.Add(room);
            }
            // Apply the server's structure result. Disabling a group removes its colliders, renderers,
            // item anchors and network markers before anchors are collected and navigation is baked.
            foreach(var choice in round.Structures)
            {
                if(choice.room<0 || choice.room>=Rooms.Count)continue;
                if(Rooms[choice.room].TryGetStructure(choice.entry,out var target,out var problem))target.SetActive(choice.enabled);
                else Debug.LogWarning($"{Rooms[choice.room].name}: {problem}. Structure ignored.",Rooms[choice.room]);
            }
            var connected=new HashSet<(int,int)>();
            foreach(var connection in round.Connections) { connected.Add((connection.a,connection.ac)); connected.Add((connection.b,connection.bc)); }
            connected.Add((round.ExitRoom.Value,round.ExitConnector.Value));
            for(int r=0;r<Rooms.Count;r++)
                for(int c=0;c<Rooms[r].connectors.Length;c++)
                    if(Rooms[r].connectors[c] && !connected.Contains((r,c)))
                    {
                        var socket=Rooms[r].connectors[c]; var cap=GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cap.name="Closed connector"; cap.transform.SetParent(geometry.transform);
                        cap.transform.SetPositionAndRotation(socket.transform.position+Vector3.up*1.5f,socket.transform.rotation);
                        // Width is the clear opening; the cap overlaps the jambs so no seam shows at the edges.
                        cap.transform.localScale=new Vector3(socket.width+.4f,3,.2f); cap.GetComponent<Renderer>().sharedMaterial=settings.wallMaterial;
                    }
            for(int r=0;r<Rooms.Count;r++)
            {
                // Only active markers exist here; disabled structures never spawn their networked props.
                foreach(var marker in Rooms[r].GetComponentsInChildren<NetworkSpawnMarker>())
                {
                    if(!marker.prefab){Debug.LogWarning($"{Rooms[r].name}: Network Spawn Marker '{marker.name}' has no prefab.",marker);continue;}
                    NetworkPropCount++;
                    if(!round.IsServer)continue;
                    var instance=Instantiate(marker.prefab,marker.transform.position,marker.transform.rotation,geometry.transform);
                    instance.AutoObjectParentSync=false;instance.Spawn();
                    foreach(var itemAnchor in instance.GetComponentsInChildren<ItemSpawnPoint>()){itemAnchor.RoomIndex=r;ItemAnchors.Add(itemAnchor);}
                }
                foreach(var anchor in Rooms[r].GetComponentsInChildren<ItemSpawnPoint>()){anchor.RoomIndex=r;ItemAnchors.Add(anchor);}
            }
            Physics.SyncTransforms();
            if(navigation)
            {
                var surface=geometry.AddComponent<NavMeshSurface>();
                surface.collectObjects=CollectObjects.Children; surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
                surface.overrideTileSize=true; surface.tileSize=128; surface.overrideVoxelSize=true; surface.voxelSize=.1f;
                surface.BuildNavMesh();
                // Logical connectivity was already proven by the planner's room graph.
                // Failures here are physical: geometry or colliders block a connected room.
                if(!NavMesh.SamplePosition(SpawnPosition,out var start,2,NavMesh.AllAreas)) throw new System.InvalidOperationException("NavMesh path failure: the starting room has no walkable NavMesh.");
                var path=new NavMeshPath();
                foreach(var room in Rooms)
                    if(!NavMesh.SamplePosition(room.NavigationPosition,out var end,1.2f,NavMesh.AllAreas) || !NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                        throw new System.InvalidOperationException("NavMesh path failure: "+room.name+" is in the room graph but cannot be walked to. Check its colliders and doorway openings.");
                foreach(var room in Rooms)foreach(var connector in room.connectors)
                {
                    if(!connector)continue;
                    Vector3 approach=connector.transform.position-connector.transform.forward;
                    if(!NavMesh.SamplePosition(approach,out var end,1.2f,NavMesh.AllAreas)||!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)
                        throw new System.InvalidOperationException("NavMesh path failure: connector "+room.name+" / "+connector.name+" cannot be walked to.");
                }
                foreach(var closet in ClosetHideout.All)
                    if(!NavMesh.SamplePosition(closet.InvestigationPosition,out var approach,.4f,NavMesh.AllAreas) ||
                       !NavMesh.CalculatePath(start.position,approach.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                        throw new System.InvalidOperationException("NavMesh path failure: closet approach "+closet.name+" cannot be walked to.");
                ItemAnchors.RemoveAll(anchor=>!NavMesh.SamplePosition(anchor.Approach,out var end,1.2f,NavMesh.AllAreas) || !NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete);
            }
            Built=true;
        }
        public RoomConnector Connector(int room,int connector)=>Rooms[room].connectors[connector];
        public Vector3 RecoveryPosition(NetworkPickup item,Vector3 death)
        {
            // Return objectives to validated surfaces; original anchors are normally vacant.
            foreach(var anchor in ItemAnchors.OrderBy(a=>(a.transform.position-item.SafeAnchor).sqrMagnitude))
            {
                Vector3 point=anchor.transform.position;
                if(!Physics.OverlapSphere(point,.2f,~0,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(item.transform))) return point;
            }
            return item.SafeAnchor+Vector3.up*.4f;
        }
    }
}
