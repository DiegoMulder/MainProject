using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    public sealed class ImpactNoiseEmitter : MonoBehaviour
    {
        [Min(.1f)] public float minimumSpeed=1.4f, cooldown=.5f, radiusPerSpeed=2f, maximumRadius=16f;
        public AudioClip impactClip;
        [Range(0,1)] public float maximumVolume=.65f;
        AudioSource speaker;
        float nextNoise;
        public void PlayImpact(float strength)
        {
            if (!impactClip) return;
            if (!speaker)
            {
                speaker=gameObject.AddComponent<AudioSource>(); speaker.playOnAwake=false;
                speaker.spatialBlend=1; speaker.minDistance=1; speaker.maxDistance=maximumRadius;
            }
            speaker.PlayOneShot(impactClip,Mathf.Clamp01(strength)*maximumVolume);
        }
        void OnCollisionEnter(Collision collision)
        {
            var network=GetComponent<NetworkObject>();
            if (network && network.IsSpawned && !network.IsOwnedByServer) return;
            if (network && network.IsSpawned && !NetworkManager.Singleton.IsServer) return;
            var item=GetComponent<PickupItem>();
            float speed=collision.relativeVelocity.magnitude;
            if ((item && item.Held) || speed<minimumSpeed || Time.time<nextNoise) return;
            nextNoise=Time.time+cooldown;
            float radius=Mathf.Min(maximumRadius,speed*radiusPerSpeed);
            Vector3 point=collision.contactCount>0?collision.GetContact(0).point:transform.position;
            GameplayNoiseSystem.Emit(point,radius,NoiseCategory.Impact,gameObject);
            var pickup=GetComponent<NetworkPickup>();
            if(pickup && pickup.IsSpawned)pickup.PlayImpactSound(radius/Mathf.Max(.1f,maximumRadius));
            else PlayImpact(radius/Mathf.Max(.1f,maximumRadius));
        }
    }
}
