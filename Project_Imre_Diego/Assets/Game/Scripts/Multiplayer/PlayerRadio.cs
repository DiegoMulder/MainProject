using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    [RequireComponent(typeof(NetworkPlayer))]
    public sealed class PlayerRadio : NetworkBehaviour
    {
        public const float NearbyVoiceDistance=6;
        public NetworkVariable<bool> Transmitting=new(false);
        public NetworkVariable<FixedString128Bytes> VoiceId=new(default);
        NetworkPlayer player;float nextReport,nextNoise,lastReport;bool reportedSpeech;
        public WalkieTalkieUse ActiveRadio=>player && player.Inventory?player.Inventory.Slots.Where(i=>i).Select(i=>i.GetComponent<WalkieTalkieUse>()).FirstOrDefault(r=>r && r.Powered.Value):null;
        public bool Eligible=>IsSpawned && player && (player.Alive||player.Life.Value==PlayerLife.Downed) && RoundManager.Instance && RoundManager.Instance.Phase.Value==RoundPhase.Playing && ActiveRadio;
        public bool WantsTransmit=>Eligible && !player.Paused && !LocalSettings.Muted && !player.IsGrabbed;
        void Awake()=>player=GetComponent<NetworkPlayer>();
        public override void OnNetworkSpawn()=>Transmitting.OnValueChanged+=Changed;
        public override void OnNetworkDespawn()=>Transmitting.OnValueChanged-=Changed;
        void Changed(bool old,bool current){var radio=ActiveRadio;if(radio)radio.PlayTransmissionCue(current);}
        void Update(){if(IsServer && Transmitting.Value && (!Eligible || Time.unscaledTime-lastReport>.5f))Transmitting.Value=false;}
        public void RegisterVoice(string id){if(IsOwner && !string.IsNullOrEmpty(id) && id.Length<=120 && VoiceId.Value.ToString()!=id)RegisterVoiceRpc(new FixedString128Bytes(id));}
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner)]
        void RegisterVoiceRpc(FixedString128Bytes id){if(id.Length>0 && !NetworkPlayer.Players.Any(p=>p!=player && p.GetComponent<PlayerRadio>() && p.GetComponent<PlayerRadio>().VoiceId.Value.Equals(id)))VoiceId.Value=id;}
        public void Report(bool speaking)
        {
            if(!IsOwner)return;
            speaking &= WantsTransmit;
            // Only active speech refreshes its lease. Silence sends one stop event.
            if(!speaking&&!reportedSpeech)return;
            if(speaking&&Time.unscaledTime<nextReport)return;
            nextReport=Time.unscaledTime+.1f;reportedSpeech=speaking;ReportRpc(speaking);
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner)]
        void ReportRpc(bool speaking)
        {
            lastReport=Time.unscaledTime;
            Transmitting.Value=speaking && Eligible && !player.IsGrabbed;
            if(!Transmitting.Value || !speaking || Time.unscaledTime<nextNoise)return;
            var radio=ActiveRadio;nextNoise=Time.unscaledTime+Mathf.Max(.2f,radio.noiseInterval);
            GameplayNoiseSystem.Emit(player.transform.position,radio.transmitNoiseRadius,NoiseCategory.RadioVoice,player.gameObject);
            foreach(var other in NetworkPlayer.Players)
            {
                if(!other || other==player)continue;
                var receiver=other.GetComponent<PlayerRadio>();
                if(!receiver || !receiver.Eligible || Vector3.Distance(other.transform.position,player.transform.position)<=NearbyVoiceDistance)continue;
                var device=receiver.ActiveRadio;
                if(device.receiverNoise)GameplayNoiseSystem.Emit(other.transform.position,device.receivedNoiseRadius,NoiseCategory.RadioReceiver,other.gameObject);
            }
        }
        public static bool ReceiveRadio(NetworkPlayer listener,NetworkPlayer sender)
        {
            if(!listener||!sender||listener==sender)return false;
            var receive=listener.GetComponent<PlayerRadio>();var send=sender.GetComponent<PlayerRadio>();
            return receive && send && receive.Eligible && send.Eligible && send.Transmitting.Value &&
                Vector3.Distance(listener.transform.position,sender.transform.position)>NearbyVoiceDistance;
        }
    }
}
