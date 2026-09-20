using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;
namespace SurvivalFP
{
    // One microphone/VAD source feeds proximity and optional radio transmission.
    public sealed class ProximityVoice : MonoBehaviour
    {
        [Min(2)] public int maximumDistance=24;
        [Min(1)] public int conversationalDistance=3;
        [Range(.1f,4)] public float falloff=1;
        [Range(0,1)] public float speechThreshold=.01f;
        [Header("Radio receiver filter")]
        public float radioHighPass=350,radioLowPass=3000;
        [Range(0,1)] public float radioDistortion=.18f;
        public bool Muted=>LocalSettings.Muted;
        public string Status {get;private set;}="Voice connects when the match starts.";
        public bool Connected=>joined;
        public int InputVolume=>0;
        public int OutputVolume=>Mathf.RoundToInt(20*Mathf.Log10(Mathf.Max(.0032f,LocalSettings.Master*LocalSettings.Voice)));
        bool initialized,busy,joined,failed,closing,radioTransmission;
        string channel,radioChannel;float nextUpdate,lastSpeech=float.NegativeInfinity;
        [Min(0)] public float speechReleaseDelay=.2f;
        void OnEnable()=>LocalSettings.Changed+=ApplyVolumes;
        void OnDisable()=>LocalSettings.Changed-=ApplyVolumes;
        void OnDestroy(){if(initialized)VivoxService.Instance.ParticipantAddedToChannel-=ParticipantAdded;}
        void ParticipantAdded(VivoxParticipant participant)
        {
            // New radio participants are inaudible until their server state is known.
            if(!participant.IsSelf && participant.ChannelName==radioChannel)
            {
                var tap=participant.CreateVivoxParticipantTap("Radio voice receiver",true);
                if(tap){tap.transform.SetParent(transform);var audio=tap.GetComponent<AudioSource>();audio.spatialBlend=0;audio.volume=LocalSettings.Voice;audio.mute=true;
                    tap.AddComponent<AudioHighPassFilter>().cutoffFrequency=radioHighPass;
                    tap.AddComponent<AudioLowPassFilter>().cutoffFrequency=radioLowPass;
                    tap.AddComponent<AudioDistortionFilter>().distortionLevel=radioDistortion;}
            }
        }
        void ApplyVolumes()
        {
            if(!initialized)return;
            var voice=VivoxService.Instance;
            voice.SetOutputDeviceVolume(Mathf.Clamp(OutputVolume,-50,0));
            if(LocalSettings.Master*LocalSettings.Voice<=0)voice.MuteOutputDevice();else voice.UnmuteOutputDevice();
            if(Muted)voice.MuteInputDevice();
        }
        async void Update()
        {
            if(closing||busy)return;
            var player=NetworkPlayer.Local;
            bool worldVoice=player && player.IsSpawned && (player.Alive||player.Life.Value==PlayerLife.Downed);
            if(!worldVoice)
            {
                if(initialized)VivoxService.Instance.MuteInputDevice();
                if(joined)await LeaveChannel();
                return;
            }
            string desired=GameSession.Instance?GameSession.Instance.VoiceChannel:null;
            if(string.IsNullOrEmpty(desired)){Status="Voice requires a Unity Services lobby.";return;}
            if(!joined && !failed){await Join(desired);return;}
            if(!joined||Time.unscaledTime<nextUpdate)return;
            nextUpdate=Time.unscaledTime+.1f;
            try
            {
                var voice=VivoxService.Instance;var radio=player.GetComponent<PlayerRadio>();
                radio?.RegisterVoice(voice.SignedInPlayerId);
                bool transmit=radio && radio.WantsTransmit;
                // Route the live mic to both channels while powered on; VAD gates radio playback/noise.
                // Keeping the route open avoids cutting the beginning of each spoken phrase.
                if(transmit!=radioTransmission)
                {
                    busy=true;
                    try{await voice.SetChannelTransmissionModeAsync(transmit?TransmissionMode.All:TransmissionMode.Single,transmit?null:channel);radioTransmission=transmit;}
                    finally{busy=false;}
                    if(closing)return;
                }
                if(Muted)voice.MuteInputDevice();else voice.UnmuteInputDevice();
                voice.Set3DPosition(player.View.position,player.View.position,player.View.forward,Vector3.up,channel);
                bool speaking=false;
                if(voice.ActiveChannels.TryGetValue(channel,out var localParticipants))
                {
                    var self=localParticipants.FirstOrDefault(p=>p.IsSelf);
                    speaking=!Muted && self!=null && self.SpeechDetected && self.AudioEnergy>=speechThreshold;
                }
                if(speaking)lastSpeech=Time.unscaledTime;
                speaking=!Muted&&!player.Paused&&!player.IsGrabbed&&(speaking||Time.unscaledTime-lastSpeech<speechReleaseDelay);
                radio?.Report(speaking);
                player.GetComponent<PlayerAnimationDriver>()?.ReportTalking(speaking);
                if(speaking && !transmit)player.ReportSpeech();
                foreach(var pair in voice.ActiveChannels)
                {
                    if(pair.Key!=channel && pair.Key!=radioChannel)continue;
                    foreach(var participant in pair.Value)
                    {
                        if(participant.IsSelf)continue;
                        var sender=NetworkPlayer.Players.FirstOrDefault(p=>p && p.GetComponent<PlayerRadio>() &&
                            p.GetComponent<PlayerRadio>().VoiceId.Value.ToString()==participant.PlayerId);
                        bool hearRadio=PlayerRadio.ReceiveRadio(player,sender);
                        bool alive=sender && (sender.Alive||sender.Life.Value==PlayerLife.Downed);
                        if(pair.Key==radioChannel){var output=participant.ParticipantTapAudioSource;if(output){output.mute=!hearRadio;output.volume=LocalSettings.Voice;}continue;}
                        bool mute=!alive||hearRadio;
                        if(mute && !participant.IsMuted)participant.MutePlayerLocally();
                        else if(!mute && participant.IsMuted)participant.UnmutePlayerLocally();
                    }
                }
                Status=transmit?(speaking?"Radio transmitting":"Radio on - open mic"):"Proximity voice - radio off";
            }
            catch(Exception ex){Status="Voice: "+ex.Message;failed=true;await LeaveChannel();}
        }
        async Task Join(string name)
        {
            busy=true;
            try
            {
                var voice=VivoxService.Instance;
                if(!initialized){await voice.InitializeAsync();initialized=true;voice.ParticipantAddedToChannel+=ParticipantAdded;}
                if(closing)return;
                if(!voice.IsLoggedIn)await voice.LoginAsync(new LoginOptions{DisplayName=GameSession.Instance.PlayerName,ParticipantUpdateFrequency=ParticipantPropertyUpdateFrequency.FivePerSecond});
                if(closing)return;
                voice.MuteInputDevice();
                channel=name;radioChannel=name+"_radio";
                var properties=new Channel3DProperties(maximumDistance,Mathf.Min(conversationalDistance,maximumDistance-1),falloff,AudioFadeModel.LinearByDistance);
                await voice.JoinPositionalChannelAsync(channel,ChatCapability.AudioOnly,properties);
                await voice.SetChannelTransmissionModeAsync(TransmissionMode.Single,channel);
                await voice.JoinGroupChannelAsync(radioChannel,ChatCapability.AudioOnly,new ChannelOptions{MakeActiveChannelUponJoining=false});
                radioTransmission=false;joined=true;ApplyVolumes();Status="Proximity + radio connected";
            }
            catch(Exception ex)
            {
                failed=true;Status="Voice unavailable: "+ex.Message;
                // Also clean up a successful positional join if radio joining failed.
                await LeaveJoinedChannels();
            }
            finally{busy=false;}
        }
        public void Retry(){failed=false;}
        async Task LeaveJoinedChannels()
        {
            if(!initialized)return;
            foreach(var name in new[]{radioChannel,channel})
                if(!string.IsNullOrEmpty(name) && VivoxService.Instance.ActiveChannels.ContainsKey(name))
                    try{await VivoxService.Instance.LeaveChannelAsync(name);}catch(Exception ex){Status="Voice disconnected: "+ex.Message;}
            joined=false;radioTransmission=false;
        }
        async Task LeaveChannel()
        {busy=true;try{await LeaveJoinedChannels();}finally{busy=false;}}
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
