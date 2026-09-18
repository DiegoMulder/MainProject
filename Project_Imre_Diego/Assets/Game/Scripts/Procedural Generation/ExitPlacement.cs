using System.Linq;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP
{
    public static class ExitPlacement
    {
        public static Bounds LocalClearance(DoorInteractable door,bool bothSides=false)
        {
            var bounds=new Bounds(new Vector3(0,1,-.85f),new Vector3(1.1f,2,1.7f));
            if(!door)return bounds;
            foreach(var collider in door.GetComponentsInChildren<BoxCollider>(true))
                for(int i=bothSides?-12:0;i<=12;i++)
                {
                    var matrix=DoorMatrix(door,collider.transform,door.openAngle*i/12f);
                    Encapsulate(ref bounds,collider.center,collider.size,matrix);
                }
            bounds.Expand(.12f);return bounds;
        }
        static Matrix4x4 DoorMatrix(DoorInteractable door,Transform part,float angle)
        {
            if(door.hinge && part.IsChildOf(door.hinge))
                return door.transform.worldToLocalMatrix*door.hinge.parent.localToWorldMatrix*
                    Matrix4x4.TRS(door.hinge.localPosition,Quaternion.Euler(0,angle,0),door.hinge.localScale)*door.hinge.worldToLocalMatrix*part.localToWorldMatrix;
            return door.transform.worldToLocalMatrix*part.localToWorldMatrix;
        }
        static void Encapsulate(ref Bounds bounds,Vector3 center,Vector3 size,Matrix4x4 matrix)
        {for(int i=0;i<8;i++)bounds.Encapsulate(matrix.MultiplyPoint3x4(center+Vector3.Scale(size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));}
        public static Bounds WorldBounds(Bounds local,Vector3 position,Quaternion rotation)
        {var result=new Bounds(position+rotation*local.center,Vector3.zero);Encapsulate(ref result,local.center,local.size,Matrix4x4.TRS(position,rotation,Vector3.one));return result;}
        public static Bounds PrefabBounds(GameObject prefab,Vector3 position,Quaternion rotation)
        {
            var local=new Bounds(Vector3.zero,Vector3.zero);
            foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                Encapsulate(ref local,renderer.localBounds.center,renderer.localBounds.size,prefab.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix);
            return WorldBounds(local,position,rotation);
        }
        public static bool ValidatePhysical(ExitDoor prefab,Transform socket,Vector3 start,out string reason)
        {
            Physics.SyncTransforms();
            foreach(var collider in prefab.GetComponentsInChildren<BoxCollider>(true))
                for(int i=0;i<=24;i++)
                {
                    var matrix=Matrix4x4.TRS(socket.position,socket.rotation,Vector3.one)*DoorMatrix(prefab,collider.transform,prefab.openAngle*i/24f);
                    Vector3 half=Vector3.Scale(collider.size*.5f,matrix.lossyScale);
                    var center=matrix.MultiplyPoint3x4(collider.center);
                    foreach(var hit in Physics.OverlapBox(center,Vector3.Max(Vector3.one*.001f,half-Vector3.one*.003f),matrix.rotation,~0,QueryTriggerInteraction.Ignore))
                        if(hit.bounds.max.y>socket.position.y+.035f && !hit.GetComponentInParent<ExitDoor>())
                        {reason="Door swing obstructed by "+hit.name;return false;}
                }
            Vector3 approach=socket.position-socket.forward*1.2f;
            if(Physics.CheckCapsule(approach+Vector3.up*.35f,approach+Vector3.up*1.65f,.3f,~0,QueryTriggerInteraction.Ignore))
            {reason="Exit approach is obstructed";return false;}
            var path=new NavMeshPath();
            if(!NavMesh.SamplePosition(start,out var from,1.2f,NavMesh.AllAreas)||!NavMesh.SamplePosition(approach,out var to,.4f,NavMesh.AllAreas)||
                !NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)
            {reason="Exit approach is unreachable";return false;}
            reason="";return true;
        }
    }
}
