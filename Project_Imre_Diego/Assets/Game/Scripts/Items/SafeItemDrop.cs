using UnityEngine;
namespace SurvivalFP
{
    public static class SafeItemDrop
    {
        static readonly Collider[] overlaps=new Collider[64];
        // Preserve camera aim first. Corrections only raise or move the item out of
        // the player's body; never search behind the player or through a wall.
        public static bool TryFind(PickupItem item,Transform player,Transform view,out Vector3 point,out Quaternion rotation)
        {
            point=default;rotation=view.rotation;
            var settings=player.GetComponent<PlayerInventory>();
            float distance=settings?Mathf.Max(.1f,settings.dropForwardDistance):.9f;
            float padding=settings?Mathf.Max(0,settings.dropCheckPadding):.015f;
            float clearance=settings?Mathf.Max(0,settings.dropMinimumClearance):.04f;
            float correction=settings?Mathf.Max(0,settings.dropVerticalCorrectionLimit):.45f;
            float radius=item.DropRadius+padding;
            var controller=player.GetComponent<CharacterController>();
            float outward=(controller?controller.radius:.35f)+radius+clearance;
            Vector3 horizontal=Vector3.ProjectOnPlane(view.forward,Vector3.up).normalized;
            if(horizontal.sqrMagnitude<.1f)horizontal=player.forward;
            Vector3 origin=view.position;
            Physics.SyncTransforms();
            if(Blocked(origin,radius,item,player,true))return false;
            // Shorten the ideal throw in front of an obstacle, before trying corrections.
            foreach(var hit in Physics.SphereCastAll(origin,radius,view.forward,distance,~0,QueryTriggerInteraction.Ignore))
                if(!Ignore(hit.collider,item,player))distance=Mathf.Min(distance,Mathf.Max(0,hit.distance-clearance));
            Vector3 desired=origin+view.forward*distance;
            for(int step=0;step<=12;step++)
            {
                Vector3 candidate=desired+Vector3.up*(correction*step/12f);
                if(ClearPath(origin,candidate,radius,item,player))
                {point=candidate-item.DropCenterOffset(rotation);return true;}
                // Looking down while crouched can put the ideal point inside the
                // capsule. Move just beyond it, preserving aim and bounded height.
                float projection=Vector3.Dot(candidate-player.position,horizontal);
                candidate+=horizontal*Mathf.Max(0,outward-projection);
                if(ClearPath(origin,candidate,radius,item,player))
                {point=candidate-item.DropCenterOffset(rotation);return true;}
            }
            return false; // Keep the item when no safe point exists.
        }
        static bool ClearPath(Vector3 origin,Vector3 target,float radius,PickupItem item,Transform player)
        {
            if(Blocked(target,radius,item,player,false))return false;
            Vector3 delta=target-origin;
            if(delta.sqrMagnitude<.0001f)return false;
            foreach(var hit in Physics.SphereCastAll(origin,radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(!Ignore(hit.collider,item,player))return false;
            return true;
        }
        static bool Blocked(Vector3 point,float radius,PickupItem item,Transform player,bool ignorePlayer)
        {
            int count=Physics.OverlapSphereNonAlloc(point,radius,overlaps,~0,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return true;
            for(int i=0;i<count;i++)
                if(!overlaps[i].transform.IsChildOf(item.transform)&&(!ignorePlayer||!overlaps[i].transform.IsChildOf(player)))return true;
            return false;
        }
        static bool Ignore(Collider c,PickupItem item,Transform player)=>c.transform.IsChildOf(item.transform)||c.transform.IsChildOf(player);
    }
}
