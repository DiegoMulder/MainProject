using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using UnityEngine;
namespace SurvivalFP
{
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Instance {get;private set;}
        public RoundManager roundPrefab;
        public LobbyRoster lobbyPrefab;
        public string menuScene="MainMenu";
        public ushort port=7777;
        [Range(2,12)] public int maxPlayers=4;
        public string Status {get;private set;}="";
        public bool Starting {get;private set;}
        public string PlayerName {get;private set;}
        public string JoinCode=>cloudSession?.Code??"Local test session";
        public string VoiceChannel=>cloudSession==null?null:"mansion_"+cloudSession.Id.Replace("-","");
        public static string MenuNotice="";
        NetworkManager manager; ISession cloudSession; bool leaving,startingMatch,cloudConnecting;
        void Awake()
        {
            Instance=this;manager=GetComponent<NetworkManager>();PlayerName=LobbyRoster.Sanitize(PlayerPrefs.GetString("SurvivalFP.Name","Survivor"));
            Status=MenuNotice;MenuNotice="";
            manager.OnServerStarted+=ServerStarted;manager.OnClientConnectedCallback+=Connected;manager.OnClientDisconnectCallback+=Disconnected;
            manager.NetworkConfig.ConnectionApproval=true;manager.ConnectionApprovalCallback=Approve;
            AudioListener.volume=PlayerPrefs.GetFloat("SurvivalFP.Volume",.8f);
        }
        public void SetPlayerName(string value)
        {
            PlayerName=LobbyRoster.Sanitize(value);
            try{PlayerPrefs.SetString("SurvivalFP.Name",PlayerName);PlayerPrefs.Save();}
            catch(PlayerPrefsException){Debug.LogWarning("Player name will be kept for this session; local preferences are read-only.");}
        }
        async Task InitializeServices()
        {
            if(UnityServices.State!=ServicesInitializationState.Initialized)
            {
                var options=new InitializationOptions();
                options.SetProfile(Application.isEditor?"editor":"player"+System.Diagnostics.Process.GetCurrentProcess().Id);
                await UnityServices.InitializeAsync(options);
            }
            if(!AuthenticationService.Instance.IsSignedIn)await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        public async void HostLobby()=>await OpenLobby(null);
        public async void JoinLobby(string code)=>await OpenLobby(code);
        async Task OpenLobby(string code)
        {
            if(Starting||manager.IsListening||leaving)return;
            if(code!=null && (code.Trim().Length<4 || code.Trim().Length>12)){Status="Enter a valid lobby code.";return;}
            Starting=true;cloudConnecting=true;Status=code==null?"Creating private lobby…":"Joining lobby…";
            try
            {
                await InitializeServices();
                if(leaving||!this)return;
                cloudSession=code==null
                    ?await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions{Name=PlayerName+"'s Mansion",MaxPlayers=maxPlayers,IsPrivate=true}.WithRelayNetwork())
                    :await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
                cloudSession.RemovedFromSession+=ServiceEnded;cloudSession.Deleted+=ServiceEnded;
                if(leaving){await cloudSession.LeaveAsync();cloudSession=null;return;}
                Status="";
            }
            catch(Exception ex){Status="Lobby unavailable: "+ex.Message;if(manager.IsListening)manager.Shutdown();}
            finally{Starting=false;cloudConnecting=false;}
        }
        // Preserved for direct-IP development tests. Neither path starts a match automatically.
        public void Host(){if(Starting||manager.IsListening)return;Starting=true;manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",port,"0.0.0.0");if(!manager.StartHost()){Starting=false;Status="Could not start local host.";}}
        public void Join(string address){if(Starting||manager.IsListening)return;if(!System.Net.IPAddress.TryParse(address,out _)){Status="Invalid IP address.";return;}Starting=true;manager.GetComponent<UnityTransport>().SetConnectionData(address,port);if(!manager.StartClient()){Starting=false;Status="Could not connect.";}}
        void Approve(NetworkManager.ConnectionApprovalRequest request,NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved=!(LobbyRoster.Instance && LobbyRoster.Instance.Started.Value) && manager.ConnectedClientsIds.Count<maxPlayers;
            response.CreatePlayerObject=false;response.Pending=false;response.Reason=response.Approved?"":"The lobby is full or the match has started.";
        }
        void ServerStarted(){Instantiate(lobbyPrefab).GetComponent<NetworkObject>().Spawn();}
        void Connected(ulong id){if(id==manager.LocalClientId && !cloudConnecting){Starting=false;Status="";}}
        public async void StartMatch()
        {
            var roster=LobbyRoster.Instance;
            if(startingMatch||!manager.IsServer||!roster||roster.Started.Value||Starting)return;
            startingMatch=true;
            try
            {
                if(cloudSession!=null){var host=cloudSession.AsHost();host.IsLocked=true;await host.SavePropertiesAsync();}
                if(leaving||!manager.IsServer)return;
                roster.Started.Value=true;Instantiate(roundPrefab).GetComponent<NetworkObject>().Spawn();
            }
            catch(Exception ex){Status="Could not start match: "+ex.Message;}
            finally{startingMatch=false;}
        }
        void ServiceEnded(){if(!leaving){MenuNotice="The lobby has ended.";ReturnToMenu();}}
        void Disconnected(ulong id)
        {
            if(leaving)return;
            if(id==manager.LocalClientId || !manager.IsServer){MenuNotice="Connection ended. The host may have left or the lobby is unavailable.";ReturnToMenu();}
            else if(RoundManager.Instance)RoundManager.Instance.EvaluateRound();
        }
        public async void ReturnToMenu()
        {
            if(leaving)return;leaving=true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            var voice=GetComponent<ProximityVoice>();if(voice)await voice.Leave();
            if(cloudSession!=null)
            {
                cloudSession.RemovedFromSession-=ServiceEnded;cloudSession.Deleted-=ServiceEnded;
                try{if(cloudSession.IsHost)await cloudSession.AsHost().DeleteAsync();else await cloudSession.LeaveAsync();}catch(Exception ex){Debug.LogWarning("Lobby cleanup: "+ex.Message);}cloudSession=null;
            }
            if(!this)return;manager.Shutdown();
            while(manager && (manager.IsListening||manager.ShutdownInProgress))await Task.Yield();
            if(this)SessionExitLoader.LoadAfterShutdown(menuScene,gameObject);
        }
        void OnDestroy()
        {
            if(manager){manager.OnServerStarted-=ServerStarted;manager.OnClientConnectedCallback-=Connected;manager.OnClientDisconnectCallback-=Disconnected;}
            if(Instance==this)Instance=null;
        }
    }
}
