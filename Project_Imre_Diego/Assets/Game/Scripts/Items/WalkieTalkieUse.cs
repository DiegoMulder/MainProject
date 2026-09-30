using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    // Power, transmit and receive sounds of a walkie talkie. The voice itself goes through Vivox (ProximityVoice),
    // band-limited like a real radio; these are the physical radio's own sounds, placed on the radio in the world,
    // so anyone standing near a receiving radio hears its squelch and hiss as well as the voice.
    [RequireComponent(typeof(PickupItem),typeof(NetworkPickup))]
    public sealed class WalkieTalkieUse : NetworkBehaviour,IPrimaryUse
    {
        public NetworkVariable<bool> Powered=new(false);
        [Min(0)] public float transmitNoiseRadius=18,receivedNoiseRadius=8;
        [Min(.2f)] public float noiseInterval=.65f;
        public bool receiverNoise=true;
        [Header("WALKIE-TALKIE AUDIO (empty = Sound Library)")]
        public SoundEvent powerOnSound;
        public SoundEvent powerOffSound, transmitStartSound, transmitEndSound, receiveStartSound, receiveEndSound, receiveStaticSound;
        [Header("Legacy fallback clips")]
        public AudioClip powerOn;
        public AudioClip powerOff,transmitStart,transmitEnd,radioStatic;
        public AudioClip[] powerOnSounds=System.Array.Empty<AudioClip>(),powerOffSounds=System.Array.Empty<AudioClip>();
        public Renderer walkieRenderer;
        public Material walkie_Off, walkie_On;
        [Min(0)] public float powerNoiseRadius=4;
        AudioSource speaker, hiss; int receiving;
        void Awake(){speaker=gameObject.AddComponent<AudioSource>();speaker.playOnAwake=false;speaker.spatialBlend=1;speaker.maxDistance=12;AudioVolumeBus.RouteSfx(speaker);}
        public override void OnNetworkSpawn()=>Powered.OnValueChanged+=PowerChanged;
        public override void OnNetworkDespawn(){Powered.OnValueChanged-=PowerChanged;StopReceiving();}
        SoundEvent Power(bool on)=>on?SoundLibrary.Pick(powerOnSound,l=>l.walkieOn):SoundLibrary.Pick(powerOffSound,l=>l.walkieOff);
        void PowerChanged(bool old,bool on)
        {
            UpdateWalkieMaterial();
            if(!on)StopReceiving();
            if(!IsServer)return;
            var clips=on?powerOnSounds:powerOffSounds;int index=clips!=null&&clips.Length>0?Random.Range(0,clips.Length):-1;
            PowerSoundRpc(on,index);
            var location=GetComponent<NetworkPickup>().Location.Value;
            var player=NetworkPlayer.Players.Find(p=>p.OwnerClientId==location.carrier);
            var where=player?player.transform.position:transform.position;var who=player?player.gameObject:gameObject;
            var heard=Power(on);
            if(heard && heard.alertsEnemy)heard.EmitHearing(where,who);
            else GameplayNoiseSystem.Emit(where,powerNoiseRadius,NoiseCategory.Door,who);
        }
        [Rpc(SendTo.Everyone,InvokePermission=RpcInvokePermission.Server)]
        void PowerSoundRpc(bool on,int index)
        {
            var sound=Power(on);
            if(sound && sound.HasClips){sound.Play(transform.position,transform);return;}
            var clips=on?powerOnSounds:powerOffSounds;Play(index>=0&&clips!=null&&index<clips.Length?clips[index]:(on?powerOn:powerOff));
        }
        public void PrimaryUse(){if(IsServer && GetComponent<PickupItem>().CanUse)Powered.Value=!Powered.Value;}
        // The talker's own radio: a click and squelch as the key goes down and comes up.
        public void PlayTransmissionCue(bool start)
        {
            var sound=start?SoundLibrary.Pick(transmitStartSound,l=>l.transmitStart):SoundLibrary.Pick(transmitEndSound,l=>l.transmitEnd);
            if(sound && sound.HasClips)sound.Play(transform.position,transform);else Play(start?transmitStart:transmitEnd);
        }
        // A receiving radio: squelch in, a low hiss while anyone talks, squelch out.
        public void SetReceiving(bool active)
        {
            receiving=Mathf.Max(0,receiving+(active?1:-1));
            var cue=active?SoundLibrary.Pick(receiveStartSound,l=>l.receiveStart):SoundLibrary.Pick(receiveEndSound,l=>l.receiveEnd);
            if(cue && (active?receiving==1:receiving==0))cue.Play(transform.position,transform);
            if(receiving>0 && !hiss){var bed=SoundLibrary.Pick(receiveStaticSound,l=>l.receiveStatic);if(bed)hiss=SoundPlayer.PlayLoop(bed,transform.position,transform);else PlayStatic();}
            if(receiving==0)StopHiss();
        }
        void StopReceiving(){receiving=0;StopHiss();}
        void StopHiss(){if(hiss){SoundPlayer.Stop(hiss);hiss=null;}}
        public void PlayStatic(){if(radioStatic)Play(radioStatic);}
        void Play(AudioClip clip){if(clip && speaker)speaker.PlayOneShot(clip,.6f);}
        private void UpdateWalkieMaterial(){if (!walkieRenderer) return; walkieRenderer.sharedMaterial = Powered.Value ? walkie_On : walkie_Off;}
    }
}
