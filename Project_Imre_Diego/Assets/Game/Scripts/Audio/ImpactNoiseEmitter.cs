using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
namespace SurvivalFP
{
    public sealed class ImpactNoiseEmitter : MonoBehaviour
    {
        public AudioClip[] impactSounds=System.Array.Empty<AudioClip>();
        [SerializeField,HideInInspector,FormerlySerializedAs("impactClip")] AudioClip legacyClip;
        [Min(0)] public float minimumSpeed=1.4f;
        [Min(.1f)] public float referenceSpeed=8f;
        [Range(0,1)] public float minimumVolume=.1f, maximumVolume=.65f;
        [Min(0)] public float minimumRadius=2.8f, maximumRadius=16f;
        [Min(0)] public float cooldown=.5f;
        public Vector2 pitchRange=new Vector2(.94f,1.06f);
        AudioSource speaker; float nextNoise;
        void Awake()=>MigrateClip();
        void OnValidate()=>MigrateClip();
        void MigrateClip()
        {
            if(!legacyClip)return;
            if(impactSounds==null || impactSounds.Length==0)impactSounds=new[]{legacyClip};
            legacyClip=null;
        }
        public int ChooseClip()
        {
            int count=0,chosen=-1;
            if(impactSounds!=null)for(int i=0;i<impactSounds.Length;i++)
                if(impactSounds[i] && Random.Range(0,++count)==0)chosen=i;
            return chosen;
        }
        public float Strength(float speed)=>Mathf.InverseLerp(minimumSpeed,Mathf.Max(minimumSpeed+.01f,referenceSpeed),speed);
        public void PlayImpact(float strength,int clipIndex,float pitch)
        {
            if(impactSounds==null || clipIndex<0 || clipIndex>=impactSounds.Length || !impactSounds[clipIndex])return;
            if(!speaker)
            {
                speaker=gameObject.AddComponent<AudioSource>();speaker.playOnAwake=false;
                speaker.spatialBlend=1;speaker.minDistance=1;
            }
            speaker.maxDistance=Mathf.Max(1,maximumRadius);
            speaker.pitch=Mathf.Clamp(pitch,.1f,3f);
            speaker.PlayOneShot(impactSounds[clipIndex],Mathf.Lerp(minimumVolume,maximumVolume,Mathf.Clamp01(strength)));
        }
        void OnCollisionEnter(Collision collision)
        {
            var network=GetComponent<NetworkObject>();
            if(network && network.IsSpawned && !network.NetworkManager.IsServer)return;
            var item=GetComponent<PickupItem>();float speed=collision.relativeVelocity.magnitude;
            if((item && item.Held) || speed<minimumSpeed || Time.time<nextNoise)return;
            nextNoise=Time.time+cooldown;
            float strength=Strength(speed),radius=Mathf.Lerp(minimumRadius,Mathf.Max(minimumRadius,maximumRadius),strength);
            var point=collision.contactCount>0?collision.GetContact(0).point:transform.position;
            GameplayNoiseSystem.Emit(point,radius,NoiseCategory.Impact,gameObject);
            int clip=ChooseClip();float pitch=Random.Range(Mathf.Min(pitchRange.x,pitchRange.y),Mathf.Max(pitchRange.x,pitchRange.y));
            var pickup=GetComponent<NetworkPickup>();
            if(pickup && pickup.IsSpawned)pickup.PlayImpactSound(strength,clip,pitch);
            else PlayImpact(strength,clip,pitch);
        }
    }
}
