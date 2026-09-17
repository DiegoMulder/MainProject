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
        public int PropInstanceCount {get;private set;}
        public Vector3 SpawnPosition=>Rooms.Count>0?Rooms[0].transform.position+new Vector3(0,.1f,-1):Vector3.up;
        GameObject geometry;
        public void Build(RoundManager round,bool navigation)
        {
            if(Built) return;
            var settings=round.settings;
            geometry=new GameObject("Generated Mansion"); geometry.transform.SetParent(transform,false);
            foreach(var placement in round.Layout)
            {
                var room=Instantiate(settings.rooms[placement.module].prefab,placement.position,placement.Rotation,geometry.transform);
                room.name=$"Room {Rooms.Count:00} - {room.name}"; Rooms.Add(room);
            }
            var connected=new HashSet<(int,int)>();
            foreach(var connection in round.Connections) { connected.Add((connection.a,connection.ac)); connected.Add((connection.b,connection.bc)); }
            connected.Add((round.ExitRoom.Value,round.ExitConnector.Value));
            for(int r=0;r<Rooms.Count;r++)
                for(int c=0;c<Rooms[r].connectors.Length;c++)
                    if(!connected.Contains((r,c)))
                    {
                        var socket=Rooms[r].connectors[c]; var cap=GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cap.name="Closed connector"; cap.transform.SetParent(geometry.transform);
                        cap.transform.SetPositionAndRotation(socket.transform.position+Vector3.up*1.5f,socket.transform.rotation);
                        cap.transform.localScale=new Vector3(socket.width,3,.2f); cap.GetComponent<Renderer>().sharedMaterial=settings.wallMaterial;
                    }
            foreach(var prop in round.Props)
            {
                var anchor=Rooms[prop.room].propAnchors[prop.anchor];
                var prefab=anchor.variants[prop.variant].prefab;
                PropInstanceCount++;
                // Static props reconstruct on peers; interactive props spawn once on the server.
                if(prefab.GetComponent<Unity.Netcode.NetworkObject>() && !round.IsServer)continue;
                var instance=Instantiate(prefab,anchor.transform.position,anchor.transform.rotation*Quaternion.Euler(0,prop.halfTurn*180,0),geometry.transform);
                var network=instance.GetComponent<Unity.Netcode.NetworkObject>();
                if(network){network.AutoObjectParentSync=false;network.Spawn();}
                foreach(var itemAnchor in instance.GetComponentsInChildren<ItemSpawnPoint>()) { itemAnchor.RoomIndex=prop.room; ItemAnchors.Add(itemAnchor); }
            }
            foreach(var room in Rooms)
                foreach(var anchor in room.GetComponentsInChildren<ItemSpawnPoint>()) { anchor.RoomIndex=Rooms.IndexOf(room); ItemAnchors.Add(anchor); }
            Physics.SyncTransforms();
            if(navigation)
            {
                var surface=geometry.AddComponent<NavMeshSurface>();
                surface.collectObjects=CollectObjects.Children; surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
                surface.overrideTileSize=true; surface.tileSize=128; surface.overrideVoxelSize=true; surface.voxelSize=.1f;
                surface.BuildNavMesh();
                if(!NavMesh.SamplePosition(SpawnPosition,out var start,2,NavMesh.AllAreas)) throw new System.InvalidOperationException("Generated mansion has no usable starting NavMesh.");
                var path=new NavMeshPath();
                foreach(var room in Rooms)
                    if(!NavMesh.SamplePosition(room.NavigationPosition,out var end,1.2f,NavMesh.AllAreas) || !NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                        throw new System.InvalidOperationException("Disconnected navigation at "+room.name);
                foreach(var room in Rooms)foreach(var connector in room.connectors)
                {
                    Vector3 approach=connector.transform.position-connector.transform.forward;
                    if(!NavMesh.SamplePosition(approach,out var end,1.2f,NavMesh.AllAreas)||!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)
                        throw new System.InvalidOperationException("Unreachable connector: "+room.name+" / "+connector.name);
                }
                foreach(var closet in ClosetHideout.All)
                    if(!NavMesh.SamplePosition(closet.InvestigationPosition,out var approach,.4f,NavMesh.AllAreas) ||
                       !NavMesh.CalculatePath(start.position,approach.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                        throw new System.InvalidOperationException("Unreachable closet approach: "+closet.name);
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
