using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
namespace SurvivalFP
{
    public sealed class GameUI : MonoBehaviour
    {
        public Camera menuCamera;
        string address="",playerName;
        bool paused, options;
        int optionsTab;
        Vector2 optionsScroll;
        GUIStyle title, text, button, panel;
        ExitDoor exit;
        void Start() { playerName=PlayerPrefs.GetString("SurvivalFP.Name","Survivor"); }
        void Update()
        {
            var player=NetworkPlayer.Local;
            if(menuCamera) { menuCamera.enabled=!player; var listener=menuCamera.GetComponent<AudioListener>(); if(listener) listener.enabled=!player; }
            if(player && Keyboard.current!=null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if(options) options=false;
                else paused=!paused;
                player.SetPaused(paused);
            }
            var round=RoundManager.Instance;
            if(round && (round.Phase.Value==RoundPhase.Lost || round.Phase.Value==RoundPhase.Won || round.Phase.Value==RoundPhase.Failed))
            { Cursor.lockState=CursorLockMode.None; Cursor.visible=true; if(player) player.SetPaused(true); }
            if(!exit && round) exit=FindAnyObjectByType<ExitDoor>();
        }
        void Styles()
        {
            if(title!=null) return;
            title=new GUIStyle(GUI.skin.label) {fontSize=30,fontStyle=FontStyle.Bold,wordWrap=true};
            text=new GUIStyle(GUI.skin.label) {fontSize=17,wordWrap=true};
            button=new GUIStyle(GUI.skin.button) {fontSize=18,fixedHeight=44};
            panel=new GUIStyle(GUI.skin.box) {padding=new RectOffset(28,28,24,24)};
            title.normal.textColor=new Color(.85f,.78f,.57f);text.normal.textColor=new Color(.82f,.84f,.78f);
        }
        void OnGUI()
        {
            Styles();
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);
            float width=Screen.width/scale,height=Screen.height/scale;
            var session=GameSession.Instance; if(!session) return;
            var player=NetworkPlayer.Local; var round=RoundManager.Instance;
            if(!round && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name==session.gameScene)
            {GUI.Label(new Rect(30,30,700,60),"Loading the mansionï¿½",title);return;}
            if(!round)
            {
                GUILayout.BeginArea(new Rect((width-560)/2,(height-620)/2,560,620),panel);
                GUILayout.Label("THE MANSION",title); GUILayout.Label("Find the seals. Unlock the exit. Stay quiet.",text); GUILayout.Space(18);
                var roster=LobbyRoster.Instance;
                if(options) DrawOptions(player);
                else if(roster)
                {
                    GUILayout.Label("LOBBY  /  "+session.JoinCode,title);
                    GUILayout.Label("Players",text);
                    foreach(var member in roster.Members)GUILayout.Label(member.name.ToString(),text);
                    GUILayout.Space(8);
                    GUI.enabled=NetworkManager.Singleton.IsServer && !roster.Started.Value && !session.Starting;
                    int mapCount=roster.maps==null?0:roster.maps.Length;
                    if(mapCount>0){GUILayout.BeginHorizontal();GUILayout.Label("Map: "+roster.MapName,text);if(GUILayout.Button("Change map",GUILayout.Width(130)))roster.SelectMap((roster.Map.Value+1)%mapCount);GUILayout.EndHorizontal();}
                    GUILayout.Label("DIFFICULTY",text);GUILayout.BeginHorizontal();
                    int count=roster.difficultyConfig?roster.difficultyConfig.profiles.Length:4;
                    if(GUILayout.Button("<",GUILayout.Width(48),GUILayout.Height(34)))roster.SelectDifficulty((roster.Difficulty.Value+count-1)%count);
                    GUILayout.Label(roster.DifficultyName,text);
                    if(GUILayout.Button(">",GUILayout.Width(48),GUILayout.Height(34)))roster.SelectDifficulty((roster.Difficulty.Value+1)%count);
                    GUI.enabled=true;GUILayout.EndHorizontal();
                    if(NetworkManager.Singleton.IsServer)
                    {GUI.enabled=!session.Starting && !roster.Started.Value;if(GUILayout.Button("Start Game",button))session.StartMatch();GUI.enabled=true;}
                    else GUILayout.Label("Waiting for the host to start…",text);
                    if(GUILayout.Button("Options",button))ShowOptions();
                    if(GUILayout.Button("Leave Lobby",button))session.ReturnToMenu();
                    GUILayout.Label(session.Status,text);
                }
                
                else
                {
                    GUI.enabled=!session.Starting && !(NetworkManager.Singleton && NetworkManager.Singleton.IsListening);
                    GUILayout.Label("Player name",text);playerName=GUILayout.TextField(playerName??"",20,GUILayout.Height(32));
                    if(GUILayout.Button("Host Game",button)){session.SetPlayerName(playerName);session.HostLobby();}
                    GUILayout.Label("Lobby code",text); address=GUILayout.TextField(address,12,GUILayout.Height(32));
                    if(GUILayout.Button("Join Lobby",button)){session.SetPlayerName(playerName);session.JoinLobby(address);}
                    GUI.enabled=true;
                    if(GUILayout.Button("Options",button)) ShowOptions();
                    if(!string.IsNullOrEmpty(session.Status)) { GUILayout.Label(session.Status,text); if(GUILayout.Button("Reset connection",button)) session.ReturnToMenu(); }
                    GUILayout.Label("Private lobby • Unity Relay",text);
                }
                GUILayout.EndArea(); return;
            }
            if(round.Phase.Value!=RoundPhase.Playing)
            {
                GUILayout.BeginArea(new Rect((width-500)/2,(height-320)/2,500,320),panel);
                GUILayout.Label(round.Phase.Value switch {RoundPhase.Generating=>"Building the mansion…",RoundPhase.Won=>"YOU ESCAPED",RoundPhase.Lost=>"NO ONE SURVIVED",_=>"Round could not start"},title);
                if(round.Phase.Value==RoundPhase.Failed) GUILayout.Label(round.Failure.Value.ToString(),text);
                if(round.Phase.Value!=RoundPhase.Generating)
                {
                    if(NetworkManager.Singleton.IsServer)
                    {GUI.enabled=!session.Transitioning;if(GUILayout.Button("Back to Lobby",button))session.BackToLobby();GUI.enabled=true;}
                    else GUILayout.Label("The host can return the party to the lobby.",text);
                }
                if(round.Phase.Value!=RoundPhase.Generating && GUILayout.Button("Back to Main Menu",button)) session.ReturnToMenu();
                GUILayout.EndArea(); return;
            }
            if(!player) { GUI.Label(new Rect(30,30,500,40),"Joining the round…",text); return; }
            GUI.Label(new Rect(28,20,700,40),$"THE MANSION  /  Seed {round.Seed.Value}"+(exit?$"  /  Exit {exit.Deposited.Value}/{exit.Required.Value}":""),text);
            if(player.Life.Value==PlayerLife.Downed)
            {
                GUI.Label(new Rect(width/2-260,height/2-60,520,140),$"DOWNED — {Mathf.CeilToInt(player.BleedOutRemaining)} seconds\nA teammate needs a medkit to revive you.",text);
            }
            else if(!player.Alive)
            {
                var target=player.GetComponent<SpectatorController>().Target;
                GUI.Label(new Rect(28,64,900,60),player.Life.Value==PlayerLife.Escaped?"Escaped — watching the remaining survivors":"Spectating "+(target?target.DisplayName:"— no survivors")+"  •  Tab switches target",text);
            }
            else
            {
                GUI.Label(new Rect(width/2-8,height/2-12,24,30),"+",text);
                GUI.Label(new Rect(width/2-220,height/2+40,440,60),player.Interaction.CurrentPrompt,text);
                GUI.Label(new Rect(28,height-95,320,30),$"Stamina {player.Stamina.Value:0} / {player.GetComponent<PlayerStamina>().Maximum:0}",text);
                string slots=""; for(int i=0;i<3;i++)
                {
                    var item=player.Inventory.Slots[i];var radio=item?item.GetComponent<WalkieTalkieUse>():null;
                    slots+=(player.Inventory.CurrentSlot==i?"► ":"")+$"[{i+1}] "+(item?item.displayName:"Empty")+(radio?(radio.Powered.Value?" [ON]":" [OFF]"):"")+"   ";
                }
                GUI.Label(new Rect(28,height-57,width-56,34),slots,text);
                GUI.Label(new Rect(width-390,height-102,365,36),"E interact  •  Q drop  •  F light  •  V radio",text);
            }
            if(!paused) return;
            float panelHeight=options?580:380;
            GUILayout.BeginArea(new Rect((width-500)/2,(height-panelHeight)/2,500,panelHeight),panel);
            GUILayout.Label(options?"OPTIONS":"PAUSED LOCALLY",title);
            if(options) DrawOptions(player);
            else
            {
                GUILayout.Label("The round continues while this menu is open.",text);
                if(GUILayout.Button("Resume",button)) {paused=false; player.SetPaused(false);}
                if(GUILayout.Button("Options",button)) ShowOptions();
                if(GUILayout.Button("Back to Main Menu",button)) session.ReturnToMenu();
            }
            GUILayout.EndArea();
        }
        float Slider(string label,float value,float min,float max,string format)
        {
            GUILayout.Label(label+"  "+value.ToString(format),text);
            return GUILayout.HorizontalSlider(value,min,max,GUILayout.Height(24));
        }
        public void ShowOptions(int category=0){options=true;optionsTab=Mathf.Clamp(category,0,2);optionsScroll=Vector2.zero;}
        void DrawOptions(NetworkPlayer player)
        {
            optionsTab=GUILayout.Toolbar(optionsTab,new[]{"AUDIO","CONTROLS","VIDEO"},GUILayout.Height(34));
            GUILayout.Space(10);
            optionsScroll=GUILayout.BeginScrollView(optionsScroll,GUILayout.Height(290));
            float master=LocalSettings.Master,sfx=LocalSettings.Sfx,voice=LocalSettings.Voice;
            float sensitivity=LocalSettings.Sensitivity,fov=LocalSettings.Fov,strength=LocalSettings.SmoothingStrength;
            bool smoothing=LocalSettings.Smoothing,psx=LocalSettings.Psx,muted=LocalSettings.Muted;
            GUI.changed=false;
            if(optionsTab==0)
            {
                master=Slider("Master",master,0,1,"P0");
                sfx=Slider("Sound effects",sfx,0,1,"P0");
                voice=Slider("Voice / radio",voice,0,1,"P0");
                muted=GUILayout.Toggle(muted,"Mute microphone",GUILayout.Height(28));
                GUILayout.Label("Voice volume affects what you hear. Radios can still alert the monster.",text);
            }
            else if(optionsTab==1)
            {
                sensitivity=Slider("Mouse sensitivity",sensitivity*10,.1f,5,"0.00")/10;
                smoothing=GUILayout.Toggle(smoothing,"Smooth camera look",GUILayout.Height(28));
                GUI.enabled=smoothing;
                strength=Slider("Smoothing strength",strength,0,1,"P0");GUI.enabled=true;
                GUILayout.Label("Zero strength gives raw look. Movement direction always responds immediately.\nRadio: hold V to transmit with a powered-on walkie talkie.",text);
            }
            else
            {
                fov=Slider("Field of view",fov,LocalSettings.MinFov,LocalSettings.MaxFov,"0");
                psx=GUILayout.Toggle(psx,"PSX visual presentation",GUILayout.Height(28));
                GUILayout.Label("These choices affect only your screen. Sprinting adds a small temporary FOV increase.",text);
            }
            bool changed=GUI.changed;
            GUILayout.EndScrollView();
            if(changed)LocalSettings.Set(master,sfx,voice,sensitivity,fov,smoothing,strength,psx,muted);
            var voiceSystem=GameSession.Instance.GetComponent<ProximityVoice>();
            if(optionsTab==0 && voiceSystem)
            {
                GUILayout.Label(voiceSystem.Status,text);
                if(GUILayout.Button("Retry voice",GUILayout.Height(28)))voiceSystem.Retry();
            }
            if(GUILayout.Button("Back",button)){LocalSettings.Save();options=false;}
        }
    }
}
