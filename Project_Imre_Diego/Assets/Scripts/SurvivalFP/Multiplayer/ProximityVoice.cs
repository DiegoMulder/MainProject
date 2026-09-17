using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;
namespace SurvivalFP
{
    // Vivox owns microphone capture, encoding, transport and spatial mixing.
    public sealed class ProximityVoice : MonoBehaviour
    {
        [Min(2)] public int maximumDistance=24;
        [Min(1)] public int conversationalDistance=3;
        [Range(.1f,4)] public float falloff=1;
        [Range(0,1)] public float speechThreshold=.01f;
        public bool Muted {get;set;}
        public string Status {get;private set;}="Voice connects when the match starts.";
        public int InputVolume {get;private set;}=0;
        public int OutputVolume {get;private set;}=0;
        bool initialized,busy,joined,failed,closing;string channel;float nextUpdate;
        async void Update()
        {
            if(closing)return;
            var player=NetworkPlayer.Local;
            bool worldVoice=player && player.IsSpawned && (player.Alive||player.Life.Value==PlayerLife.Downed);
            if(!worldVoice)
            {
                if(initialized)VivoxService.Instance.MuteInputDevice();
                if(joined && !busy){VivoxService.Instance.MuteInputDevice();await LeaveChannel();}
                return;
            }
            string desired=GameSession.Instance?GameSession.Instance.VoiceChannel:null;
            if(string.IsNullOrEmpty(desired)){Status="Voice requires a Unity Services lobby.";return;}
            if(!joined && !busy && !failed){await Join(desired);return;}
            if(!joined||Time.unscaledTime<nextUpdate)return;
            nextUpdate=Time.unscaledTime+.1f;
            try
            {
                if(Muted)VivoxService.Instance.MuteInputDevice();else VivoxService.Instance.UnmuteInputDevice();
                VivoxService.Instance.Set3DPosition(player.View.position,player.View.position,player.View.forward,Vector3.up,channel);
                if(!Muted && VivoxService.Instance.ActiveChannels.TryGetValue(channel,out var participants))
                {
                    var self=participants.FirstOrDefault(p=>p.IsSelf);
                    if(self!=null && self.SpeechDetected && self.AudioEnergy>=speechThreshold)player.ReportSpeech();
                }
            }
            catch(Exception ex){Status="Voice: "+ex.Message;failed=true;await LeaveChannel();}
        }
        async Task Join(string name)
        {
            busy=true;
            try
            {
                if(!initialized){await VivoxService.Instance.InitializeAsync();initialized=true;}
                if(closing)return;
                if(!VivoxService.Instance.IsLoggedIn)await VivoxService.Instance.LoginAsync(new LoginOptions{DisplayName=GameSession.Instance.PlayerName,ParticipantUpdateFrequency=ParticipantPropertyUpdateFrequency.FivePerSecond});
                if(closing)return;
                channel=name;
                var properties=new Channel3DProperties(maximumDistance,Mathf.Min(conversationalDistance,maximumDistance-1),falloff,AudioFadeModel.LinearByDistance);
                await VivoxService.Instance.JoinPositionalChannelAsync(channel,ChatCapability.AudioOnly,properties);
                joined=true;SetVolumes(InputVolume,OutputVolume);Status="Proximity voice connected";
                var player=NetworkPlayer.Local;
                if(closing || !player || (player.Life.Value!=PlayerLife.Alive && player.Life.Value!=PlayerLife.Downed))VivoxService.Instance.MuteInputDevice();
            }
            catch(Exception ex){failed=true;Status=ex is NullReferenceException?"Voice unavailable: enable Vivox and fetch this project's configuration in Project Settings > Services > Vivox.":"Voice unavailable: "+ex.Message;}
            finally{busy=false;}
        }
        public void Retry(){failed=false;}
        public void SetVolumes(int input,int output)
        {
            InputVolume=Mathf.Clamp(input,-50,50);OutputVolume=Mathf.Clamp(output,-50,50);
            if(!initialized)return;
            VivoxService.Instance.SetInputDeviceVolume(InputVolume);VivoxService.Instance.SetOutputDeviceVolume(OutputVolume);
        }
        async Task LeaveChannel()
        {
            busy=true;
            try{if(joined)await VivoxService.Instance.LeaveChannelAsync(channel);}
            catch(Exception ex){Status="Voice disconnected: "+ex.Message;}
            finally{joined=false;busy=false;}
        }
        public async Task Leave()
        {
            closing=true;
            if(initialized)VivoxService.Instance.MuteInputDevice();
            while(busy)await Task.Yield();
            await LeaveChannel();
            if(initialized && VivoxService.Instance.IsLoggedIn)
                try{await VivoxService.Instance.LogoutAsync();}catch(Exception ex){Debug.LogWarning("Voice logout: "+ex.Message);}
        }
    }
}
