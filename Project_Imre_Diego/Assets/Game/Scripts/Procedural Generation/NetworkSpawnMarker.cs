using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    // Put inside a randomized structure group to add a networked prop (for example a closet).
    // When the group is enabled, the server spawns the prefab here; clients receive it through Netcode.
    public sealed class NetworkSpawnMarker : MonoBehaviour
    {
        [Tooltip("A registered network prefab. Its pivot is placed at this marker.")]
        public NetworkObject prefab;
        void OnDrawGizmos()
        {
            Gizmos.color=new Color(1,.55f,.1f,.8f);Gizmos.matrix=transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up,new Vector3(1.2f,2,.7f));Gizmos.DrawRay(Vector3.up,Vector3.forward);
        }
    }
}
