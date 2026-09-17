using System.Collections.Generic;
using UnityEngine;
namespace SurvivalFP
{
    // Local-space usable hiding volume. Structural colliders still decide ordinary sight.
    public sealed class HidingCover : MonoBehaviour
    {
        public Vector3 center=new(0,.65f,0),size=new(2,1.2f,1.4f);
        static readonly List<HidingCover> covers=new();
        void OnEnable()=>covers.Add(this);
        void OnDisable()=>covers.Remove(this);
        public bool Contains(NetworkPlayer player)=>player && player.Alive && player.Motor.IsCrouching && new Bounds(center,size).Contains(transform.InverseTransformPoint(player.transform.position+Vector3.up*.5f));
        public static HidingCover For(NetworkPlayer player)=>covers.Find(c=>c && c.Contains(player));
        void OnDrawGizmosSelected(){Gizmos.matrix=transform.localToWorldMatrix;Gizmos.color=Color.cyan;Gizmos.DrawWireCube(center,size);}
    }
}
