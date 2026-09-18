using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    [RequireComponent(typeof(PickupItem),typeof(NetworkPickup))]
    public sealed class WalkieTalkieUse : NetworkBehaviour,IPrimaryUse
    {
        public NetworkVariable<bool> Powered=new(false);
        [Min(0)] public float transmitNoiseRadius=18,receivedNoiseRadius=8;
        [Min(.2f)] public float noiseInterval=.65f;
        public bool receiverNoise=true;
        public AudioClip powerOn,powerOff,transmitStart,transmitEnd,radioStatic;
        AudioSource speaker;
        void Awake(){speaker=gameObject.AddComponent<AudioSource>();speaker.playOnAwake=false;speaker.spatialBlend=1;speaker.maxDistance=12;AudioVolumeBus.RouteSfx(speaker);}
        public override void OnNetworkSpawn()=>Powered.OnValueChanged+=PowerChanged;
        public override void OnNetworkDespawn()=>Powered.OnValueChanged-=PowerChanged;
        void PowerChanged(bool old,bool on)=>Play(on?powerOn:powerOff);
        public void PrimaryUse(){if(IsServer && GetComponent<PickupItem>().Held)Powered.Value=!Powered.Value;}
        public void PlayTransmissionCue(bool start)=>Play(start?transmitStart:transmitEnd);
        public void PlayStatic(){if(radioStatic)Play(radioStatic);}
        void Play(AudioClip clip){if(clip && speaker)speaker.PlayOneShot(clip,.6f);}
    }
}
