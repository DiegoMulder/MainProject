using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    public sealed class ImpactNoiseEmitter : MonoBehaviour
    {
        [Min(.1f)] public float minimumSpeed=1.4f, cooldown=.5f, radiusPerSpeed=2f, maximumRadius=16f;
        float nextNoise;
        void OnCollisionEnter(Collision collision)
        {
            var network=GetComponent<NetworkObject>();
            if (network && network.IsSpawned && !network.IsOwnedByServer) return;
            if (network && network.IsSpawned && !NetworkManager.Singleton.IsServer) return;
            var item=GetComponent<PickupItem>();
            float speed=collision.relativeVelocity.magnitude;
            if ((item && item.Held) || speed<minimumSpeed || Time.time<nextNoise) return;
            nextNoise=Time.time+cooldown;
            GameplayNoiseSystem.Emit(transform.position,Mathf.Min(maximumRadius,speed*radiusPerSpeed),NoiseCategory.Impact,gameObject);
        }
    }
}
