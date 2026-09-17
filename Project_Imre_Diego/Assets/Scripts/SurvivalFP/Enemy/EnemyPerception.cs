using System;
using UnityEngine;
namespace SurvivalFP
{
    public sealed class EnemyPerception : MonoBehaviour
    {
        public Transform eyes;
        [Min(1)] public float sightDistance=18f;
        [Range(10,360)] public float fieldOfView=110f;
        [Min(0)] public float hearingMultiplier=1f;
        public LayerMask obstacles=~0;
        public NetworkPlayer FindVisible()
        {
            NetworkPlayer nearest=null; float best=sightDistance;
            foreach(var player in NetworkPlayer.Players)
            {
                if(!player || !player.Alive) continue;
                Vector3 offset=player.View.position-Eye;
                if(offset.magnitude>best || Vector3.Angle(transform.forward,offset)>fieldOfView*.5f || !HasLineOfSight(player)) continue;
                nearest=player; best=offset.magnitude;
            }
            return nearest;
        }
        public Vector3 Eye=>eyes?eyes.position:transform.position+Vector3.up*1.7f;
        public bool HasLineOfSight(NetworkPlayer player)
        {
            Vector3 delta=player.View.position-Eye;
            var hits=Physics.RaycastAll(Eye,delta.normalized,delta.magnitude,obstacles,QueryTriggerInteraction.Collide);
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in hits)
            {
                if(hit.transform.IsChildOf(transform)) continue;
                if(hit.collider.isTrigger && !hit.collider.GetComponentInParent<HidingCover>())continue;
                return hit.collider.GetComponentInParent<NetworkPlayer>()==player;
            }
            return true;
        }
    }
}
