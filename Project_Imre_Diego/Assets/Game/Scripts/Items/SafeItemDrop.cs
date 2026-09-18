using UnityEngine;
namespace SurvivalFP
{
    public static class SafeItemDrop
    {
        static readonly Collider[] overlaps=new Collider[64];
        static readonly float[] angles={0,35,-35,70,-70,110,-110,180};
        // Fixed candidate budget. If every point is blocked, retain the item and let
        // the player step away instead of materializing it through solid geometry.
        public static bool TryFind(PickupItem item,Transform player,Transform view,out Vector3 point,out Quaternion rotation)
        {
            point=default;rotation=Quaternion.Euler(0,view.eulerAngles.y,0);
            Vector3 forward=Vector3.ProjectOnPlane(view.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.1f)forward=player.forward;
            float radius=item.DropRadius;
            var controller=player.GetComponent<CharacterController>();
            float bodyRadius=controller?controller.radius:.35f;
            float height=controller?controller.height:2;
            float minimumDistance=bodyRadius+radius+.12f;
            Physics.SyncTransforms();
            foreach(float angle in angles)
            {
                Vector3 direction=Quaternion.Euler(0,angle,0)*forward;
                for(int level=0;level<3;level++)
                {
                    Vector3 origin=player.position+Vector3.up*Mathf.Max(radius+.12f,Mathf.Min(height-.12f,view.position.y-player.position.y)+level*.25f);
                    Vector3 candidate=origin+direction*(minimumDistance+.25f);
                    // The whole path must be free: never put an item beyond a wall.
                    bool blocked=false;
                    int originCount=Physics.OverlapSphereNonAlloc(origin,radius,overlaps,~0,QueryTriggerInteraction.Ignore);
                    if(originCount==overlaps.Length)continue;
                    for(int i=0;i<originCount;i++)if(!Ignore(overlaps[i],item,player)){blocked=true;break;}
                    if(blocked)continue;
                    foreach(var hit in Physics.SphereCastAll(origin,radius,direction,minimumDistance+.25f,~0,QueryTriggerInteraction.Ignore))
                        if(!Ignore(hit.collider,item,player)){blocked=true;break;}
                    if(blocked)continue;
                    int count=Physics.OverlapSphereNonAlloc(candidate,radius,overlaps,~0,QueryTriggerInteraction.Ignore);
                    if(count==overlaps.Length)continue;
                    for(int i=0;i<count;i++)if(!overlaps[i].transform.IsChildOf(item.transform)){blocked=true;break;}
                    if(blocked)continue;
                    point=candidate-item.DropCenterOffset(rotation);return true;
                }
            }
            return false;
        }
        static bool Ignore(Collider c,PickupItem item,Transform player)=>c.transform.IsChildOf(item.transform)||c.transform.IsChildOf(player);
    }
}
