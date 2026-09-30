using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
namespace SurvivalFP
{
    // A dropped or thrown item hitting something: the server reports the noise to the enemies (louder and further for
    // harder hits) and tells every client which clip to play. Which sound: this item's Impact Event if set, otherwise
    // the Sound Library's heavy or light drop set (chosen by Rigidbody mass), otherwise the legacy clip array below.
    public sealed class ImpactNoiseEmitter : MonoBehaviour
    {
        [Tooltip("Optional: this item's own impact sound. Empty = Sound Library (ITEM AUDIO, heavy or light by mass).")]
        public SoundEvent impactEvent;
        [Tooltip("Legacy clips, used only when neither the item nor the Sound Library has impact sounds.")]
        public AudioClip[] impactSounds=System.Array.Empty<AudioClip>();
        [SerializeField,HideInInspector,FormerlySerializedAs("impactClip")] AudioClip legacyClip;
        [Min(0)] public float minimumSpeed=1.4f;
        [Min(.1f)] public float referenceSpeed=8f;
        [Range(0,1)] public float minimumVolume=.1f, maximumVolume=.65f;
        [Header("Enemy hearing")]
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
        public bool Heavy
        {
            get { var body=GetComponent<Rigidbody>();var library=SoundLibrary.Instance;return body && library && body.mass>=library.heavyItemMass; }
        }
        // The Sound Event this item uses, or null for the legacy array.
        public SoundEvent Event
        {
            get
            {
                if(impactEvent && impactEvent.HasClips)return impactEvent;
                var library=SoundLibrary.Instance;if(!library)return null;
                var chosen=Heavy?library.itemDropHeavy:library.itemDropLight;
                return chosen && chosen.HasClips?chosen:null;
            }
        }
        public int ChooseClip()
        {
            var sound=Event;
            if(sound)return sound.PickIndex();
            int count=0,chosen=-1;
            if(impactSounds!=null)for(int i=0;i<impactSounds.Length;i++)
                if(impactSounds[i] && Random.Range(0,++count)==0)chosen=i;
            return chosen;
        }
        public float Strength(float speed)=>Mathf.InverseLerp(minimumSpeed,Mathf.Max(minimumSpeed+.01f,referenceSpeed),speed);
        public void PlayImpact(float strength,int clipIndex,float pitch)
        {
            float volume=Mathf.Lerp(minimumVolume,maximumVolume,Mathf.Clamp01(strength));
            var sound=Event;
            if(sound)
            {
                // Volume and pitch relative to the event: a hard hit plays near full, a tap much quieter.
                sound.PlayIndex(clipIndex,transform.position,null,volume/Mathf.Max(.01f,maximumVolume),pitch);
                return;
            }
            if(impactSounds==null || clipIndex<0 || clipIndex>=impactSounds.Length || !impactSounds[clipIndex])return;
            if(!speaker)
            {
                speaker=gameObject.AddComponent<AudioSource>();speaker.playOnAwake=false;
                speaker.spatialBlend=1;speaker.minDistance=1;AudioVolumeBus.RouteSfx(speaker);
            }
            speaker.maxDistance=Mathf.Max(1,maximumRadius);
            speaker.pitch=Mathf.Clamp(pitch,.1f,3f);
            speaker.PlayOneShot(impactSounds[clipIndex],volume);
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
