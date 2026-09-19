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
        public AudioClip[] powerOnSounds=System.Array.Empty<AudioClip>(),powerOffSounds=System.Array.Empty<AudioClip>();
        [Min(0)] public float powerNoiseRadius=4;
        AudioSource speaker;
        void Awake(){speaker=gameObject.AddComponent<AudioSource>();speaker.playOnAwake=false;speaker.spatialBlend=1;speaker.maxDistance=12;AudioVolumeBus.RouteSfx(speaker);}
        public override void OnNetworkSpawn()=>Powered.OnValueChanged+=PowerChanged;
        public override void OnNetworkDespawn()=>Powered.OnValueChanged-=PowerChanged;
        void PowerChanged(bool old,bool on){if(IsServer){var clips=on?powerOnSounds:powerOffSounds;int index=clips!=null&&clips.Length>0?Random.Range(0,clips.Length):-1;PowerSoundRpc(on,index);var location=GetComponent<NetworkPickup>().Location.Value;var player=NetworkPlayer.Players.Find(p=>p.OwnerClientId==location.carrier);GameplayNoiseSystem.Emit(player?player.transform.position:transform.position,powerNoiseRadius,NoiseCategory.Door,player?player.gameObject:gameObject);}}
        [Rpc(SendTo.Everyone,InvokePermission=RpcInvokePermission.Server)]
        void PowerSoundRpc(bool on,int index){var clips=on?powerOnSounds:powerOffSounds;Play(index>=0&&clips!=null&&index<clips.Length?clips[index]:(on?powerOn:powerOff));}
        public void PrimaryUse(){if(IsServer && GetComponent<PickupItem>().Held)Powered.Value=!Powered.Value;}
        public void PlayTransmissionCue(bool start)=>Play(start?transmitStart:transmitEnd);
        public void PlayStatic(){if(radioStatic)Play(radioStatic);}
        void Play(AudioClip clip){if(clip && speaker)speaker.PlayOneShot(clip,.6f);}
    }
}
