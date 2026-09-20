#if UNITY_EDITOR || DEBUG
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    // Opt-in local integration harness. Never instantiated in a non-development player.
    [DefaultExecutionOrder(600)]
    public sealed class MansionDevelopmentProbe : MonoBehaviour
    {
        [Serializable] public class Command { public int id; public string action; public string target; public int slot; }
        [Serializable] public class AnimationSnapshot
        {public ulong owner;public float blend,crouch,down,talkingWeight,killCameraError=-1;public bool jump,talking,isDown,isCrouching,bodyHidden,grabbed;public string itemParent,clips;public bool animatorReady,bodyVisible,itemVisible;public float vignetteOpacity,vignetteIntensity,bleedProgress;public int state;public Vector3 cameraPos,itemPosition,itemScale,rigPosition;public Quaternion itemRotation,armRotation;}
        [Serializable] public class Snapshot
        {
            public string phase,layout,life,prompt,error,lobbyCode,status,spectating;
            public string[] names;public int map;public string[] roomNames,enemyStates;public AnimationSnapshot[] animations;
            public bool matchStarted;
            public float bleedOut;
            public int seed,rooms,props,players,slot,command,activeCameras,enemies; public string configHash;
            public float time,timeScale,stamina,height,vertical,immediateDistance;public int corrections;public string scene;public bool grounded;
            public bool paused,light,hidden,radioOn,radioTransmitting,localBodyHidden;
            public int difficulty,requiredObjectives; public float fov; public string voiceStatus; public string[] bodyVisibility;
            public int movementFrames,zeroMovementFrames,zeroPresentationFrames;public float maximumFrameStep,maximumCorrection,measuredSeconds,maximumPresentationStep;
            public float heartbeat; public int internalWidth,internalHeight; public string[] closets;
            public Vector3 position;
            public string[] inventory,items,doors;
        }
        string directory,mode,cloudCode,lastError=""; int handled; float nextSnapshot; bool drive,cloud,controlled;float immediateDistance;bool requestJump; int driveMode;int movementFrames,zeroMovementFrames;float maximumFrameStep,maximumCorrection,measuredSeconds;Vector3 lastMeasuredPosition;bool measured;
        int zeroPresentationFrames;float maximumPresentationStep,turnRate;Vector3 lastPresentationPosition;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if(!Debug.isDebugBuild && !Application.isEditor) return;
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-mansion-probe");
            if(index<0 || index+2>=args.Length) return;
            var go=new GameObject("Development integration probe");DontDestroyOnLoad(go);var p=go.AddComponent<MansionDevelopmentProbe>();p.mode=args[index+1];p.directory=args[index+2];
            p.cloud=p.mode.StartsWith("cloud");if(p.cloud)p.mode=p.mode.Substring(5);if(index+3<args.Length)p.cloudCode=args[index+3];
        }
        IEnumerator Start()
        {
            Application.runInBackground=true;
            Application.logMessageReceived+=Log;
            Directory.CreateDirectory(directory);
            yield return null;
            GameSession.Instance.SetPlayerName(mode=="host"?"Host Tester":"Client Tester");
            if(cloud){if(mode=="host")GameSession.Instance.HostLobby();else GameSession.Instance.JoinLobby(cloudCode);}
            else if(mode=="host") GameSession.Instance.Host();else GameSession.Instance.Join("127.0.0.1");
        }
        void Log(string message,string trace,LogType type) { if(type==LogType.Exception || type==LogType.Error) lastError=message+"\n"+trace; }
        void OnDestroy()=>Application.logMessageReceived-=Log;
        float CameraError(NetworkPlayer p)
        {
            if(!p.IsOwner||!p.IsGrabbed||!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(p.GrabbedBy.Value,out var enemy))return -1;
            var sequence=enemy.GetComponent<EnemyKillSequence>();
            return sequence&&sequence.killCameraPoint?Vector3.Distance(p.View.position,sequence.killCameraPoint.position):-1;
        }
        void Update()
        {
            if(string.IsNullOrEmpty(directory)) return;
            var player=NetworkPlayer.Local;
            if(controlled&&player&&turnRate!=0)player.CameraMotion.Look(new Vector2(turnRate*Time.deltaTime/player.CameraMotion.Sensitivity,0),Time.deltaTime);
            if(controlled && player){player.Controller.enabled=false;player.SubmitMovement(new PlayerCommand {Move=drive?Vector2.up:Vector2.zero,Sprint=drive && driveMode==0,Crouch=drive && driveMode==2,JumpPressed=requestJump},player.View);requestJump=false;}
            string commandPath=Path.Combine(directory,mode+".command.json");
            if(File.Exists(commandPath))
            {
                Command command=null;try {command=JsonUtility.FromJson<Command>(File.ReadAllText(commandPath));}catch(IOException){}catch(ArgumentException){}
                if(command!=null && command.id>handled)
                {
                    handled=command.id;
                    try
                    {
                        switch(command.action)
                        {
                            case "down":if(player.IsServer)player.Down();break;
                            case "revive":if(player.IsServer)player.Revive();break;
                            case "measure":movementFrames=zeroMovementFrames=zeroPresentationFrames=0;maximumFrameStep=maximumCorrection=measuredSeconds=maximumPresentationStep=0;measured=false;Application.targetFrameRate=command.slot;QualitySettings.vSyncCount=0;break;
                            case "turn":turnRate=command.slot;break;
                            case "smoothing":typeof(PlayerCamera).GetField("smoothMouse",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(player.CameraMotion,command.slot!=0);break;
                            case "look":player.CameraMotion.Look(new Vector2(float.Parse(command.target,System.Globalization.CultureInfo.InvariantCulture),0),Time.deltaTime);break;
                            case "voicepause":GameSession.Instance.GetComponent<ProximityVoice>().enabled=false;break;
                            case "map":LobbyRoster.Instance.SelectMap(command.slot);break;
                            case "talk":player.GetComponent<PlayerAnimationDriver>().ReportTalking(command.slot!=0);break;
                            case "start":GameSession.Instance.StartMatch();break;
                            case "difficulty":LobbyRoster.Instance.SelectDifficulty(command.slot);break;
                            case "radio":player.GetComponent<PlayerRadio>().Report(command.slot==2);break;
                            case "speech":player.ReportSpeech();break;
                            case "interact":
                                if(NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(ulong.Parse(command.target),out var target))
                                { Vector3 point=target.transform.position+(target.GetComponent<ClosetHideout>()?Vector3.up*1.5f:target.GetComponent<DoorInteractable>()?Vector3.up*1.3f:target.GetComponent<DownedInteractable>()?Vector3.up*.45f:Vector3.zero);player.View.rotation=Quaternion.LookRotation(point-player.View.position);player.Interaction.Tick(player.View,true); }
                                break;
                            case "select":player.Inventory.Select(command.slot);break;
                            case "light":player.InventoryAction(3);break;
                            case "use":player.Inventory.UsePrimary();break;
                            case "drop":player.InventoryAction(1);break;
                            case "pause":drive=false;player.SetPaused(true);player.SubmitMovement(default,player.View);break;
                            case "resume":player.SetPaused(false);break;
                            case "drive":player.Controller.enabled=false;controlled=true;drive=true;driveMode=command.slot;break;
                            case "screenshot":ScreenCapture.CaptureScreenshot(Path.Combine(directory,"client-view.png"));break;
                            case "resize":Screen.SetResolution(command.slot, int.Parse(command.target), FullScreenMode.Windowed);break;
                            case "jump":controlled=true;requestJump=true;break;
                            case "predict":
                                controlled=true;player.Controller.enabled=false;var before=player.transform.position;
                                player.GetComponent<PlayerPrediction>().Predict(new PlayerCommand{Move=Vector2.up},player.transform.eulerAngles.y,0,1f/60);
                                immediateDistance=Vector3.Distance(before,player.transform.position);break;
                            case "lobby":GameSession.Instance.BackToLobby();break;
                            case "stop":controlled=true;drive=false;player.SubmitMovement(default,player.View);break;
                            case "disconnect":NetworkManager.Singleton.Shutdown();break;
                            case "menu":GameSession.Instance.ReturnToMenu();break;
                        }
                    }
                    catch(Exception ex){lastError=ex.ToString();}
                }
            }
        }
        void LateUpdate()
        {
            if(string.IsNullOrEmpty(directory))return;
            var player=NetworkPlayer.Local;
            if(player&&controlled&&drive)
            {
                var prediction=player.GetComponent<PlayerPrediction>();var presentation=player.transform.position+prediction.RenderOffset;
                float delta=Vector3.ProjectOnPlane(player.transform.position-lastMeasuredPosition,Vector3.up).magnitude;
                float renderDelta=Vector3.ProjectOnPlane(presentation-lastPresentationPosition,Vector3.up).magnitude;
                if(measured&&player.Motor.ActualSpeed>1){movementFrames++;if(delta<.0001f)zeroMovementFrames++;if(renderDelta<.0001f)zeroPresentationFrames++;maximumFrameStep=Mathf.Max(maximumFrameStep,delta);maximumPresentationStep=Mathf.Max(maximumPresentationStep,renderDelta);measuredSeconds+=Time.unscaledDeltaTime;}
                maximumCorrection=Mathf.Max(maximumCorrection,prediction.LastCorrection);lastMeasuredPosition=player.transform.position;lastPresentationPosition=presentation;measured=true;
            }
            if(Time.unscaledTime<nextSnapshot) return;nextSnapshot=Time.unscaledTime+.2f;
            var round=RoundManager.Instance;
            var snapshot=new Snapshot {scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,immediateDistance=immediateDistance,phase=round?round.Phase.Value.ToString():"Menu",time=Time.time,timeScale=Time.timeScale,command=handled,error=lastError,players=NetworkPlayer.Players.Count,activeCameras=Camera.allCamerasCount};
            snapshot.movementFrames=movementFrames;snapshot.zeroMovementFrames=zeroMovementFrames;snapshot.maximumFrameStep=maximumFrameStep;snapshot.maximumCorrection=maximumCorrection;snapshot.measuredSeconds=measuredSeconds;
            snapshot.zeroPresentationFrames=zeroPresentationFrames;snapshot.maximumPresentationStep=maximumPresentationStep;
            snapshot.enemies=EnemyController.Enemies.Count;snapshot.configHash=NetworkManager.Singleton?NetworkManager.Singleton.NetworkConfig.GetConfig(false).ToString():"";
            var lobby=LobbyRoster.Instance;var names=new System.Collections.Generic.List<string>();if(lobby)foreach(var member in lobby.Members)names.Add(member.name.ToString());snapshot.names=names.ToArray();
            snapshot.map=lobby?lobby.Map.Value:-1;snapshot.roomNames=round?round.World.Rooms.Select(r=>r.name).ToArray():Array.Empty<string>();snapshot.enemyStates=EnemyController.Enemies.Select(e=>e.State.Value.ToString()).ToArray();
            snapshot.animations=NetworkPlayer.Players.Where(p=>p&&p.IsSpawned&&p.GetComponent<PlayerAnimationDriver>()).Select(p=>{var a=p.GetComponent<PlayerAnimationDriver>().animator;return new AnimationSnapshot{owner=p.OwnerClientId,blend=a.GetFloat("Blend"),crouch=a.GetFloat("Crouch"),down=a.GetFloat("Down"),talkingWeight=a.GetLayerWeight(1),jump=a.GetBool("Jump"),talking=a.GetBool("IsTalking"),isDown=a.GetBool("IsDown"),isCrouching=a.GetBool("IsCrouching"),grabbed=p.IsGrabbed,itemParent=p.Inventory.Current&&p.Inventory.Current.transform.parent?p.Inventory.Current.transform.parent.name:"",state=a.GetCurrentAnimatorStateInfo(0).fullPathHash,cameraPos=p.View.position,bodyVisible=p.visualBody.GetComponentsInChildren<Renderer>().Any(r=>r.enabled&&!r.forceRenderingOff&&r.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),itemVisible=p.Inventory.Current&&p.Inventory.Current.GetComponentsInChildren<Renderer>().Any(r=>r.enabled&&!r.forceRenderingOff),vignetteOpacity=p.GetComponent<DownedVignette>().Opacity,vignetteIntensity=p.GetComponent<DownedVignette>().Intensity,bleedProgress=p.BleedOutProgress,animatorReady=a.enabled&&a.runtimeAnimatorController&&a.avatar&&a.avatar.isValid,clips=string.Join(",",a.GetCurrentAnimatorClipInfo(0).Select(c=>c.clip.name+":"+c.weight)),rigPosition=a.transform.Find("Rig").localPosition,armRotation=a.GetComponentsInChildren<Transform>().First(t=>t.name=="Upper_Arm_L").localRotation,itemPosition=p.Inventory.Current?p.Inventory.Current.transform.localPosition:Vector3.zero,itemRotation=p.Inventory.Current?p.Inventory.Current.transform.localRotation:Quaternion.identity,itemScale=p.Inventory.Current?p.Inventory.Current.transform.localScale:Vector3.zero,killCameraError=CameraError(p),bodyHidden=p.visualBody.GetComponentsInChildren<Renderer>().Where(r=>!r.GetComponentInParent<PickupItem>()).All(r=>r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)};}).ToArray();
            snapshot.difficulty=lobby?lobby.Difficulty.Value:-1;snapshot.requiredObjectives=round?round.RequiredObjectives.Value:0;
            snapshot.voiceStatus=GameSession.Instance?GameSession.Instance.GetComponent<ProximityVoice>().Status:"";
            snapshot.matchStarted=lobby && lobby.Started.Value;snapshot.lobbyCode=GameSession.Instance?GameSession.Instance.JoinCode:"";snapshot.status=GameSession.Instance?GameSession.Instance.Status:"";
            if(round)
            {
                snapshot.seed=round.Seed.Value;snapshot.rooms=round.World.Rooms.Count;snapshot.props=round.World.PropInstanceCount;
                var placements=new System.Collections.Generic.List<string>();
                foreach(var r in round.Layout) placements.Add($"{r.module}:{r.quarter}:{r.position.x:F2},{r.position.y:F2},{r.position.z:F2}");
                snapshot.layout=string.Join(";",placements);
            }
            if(player)
            {
                snapshot.height=player.Motor.Height;snapshot.vertical=player.Motor.VerticalSpeed;snapshot.grounded=player.Motor.IsGrounded;snapshot.corrections=player.GetComponent<PlayerPrediction>().Corrections;
                snapshot.hidden=player.IsHidden;snapshot.heartbeat=player.GetComponent<HeartbeatFeedback>()?player.GetComponent<HeartbeatFeedback>().Intensity:0;
                var psx=player.View.GetComponent<PsxCameraPresentation>();if(psx){snapshot.internalWidth=psx.InternalSize.x;snapshot.internalHeight=psx.InternalSize.y;}
                var radio=player.GetComponent<PlayerRadio>();snapshot.radioOn=radio && radio.ActiveRadio;snapshot.radioTransmitting=radio && radio.Transmitting.Value;
                snapshot.localBodyHidden=player.visualBody && player.visualBody.GetComponentsInChildren<Renderer>().All(r=>r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly);snapshot.fov=player.View.GetComponent<Camera>().fieldOfView;
                snapshot.bodyVisibility=NetworkPlayer.Players.Where(p=>p && p.visualBody).Select(p=>p.OwnerClientId+":"+p.visualBody.GetComponentInChildren<Renderer>().shadowCastingMode).ToArray();
                snapshot.life=player.Life.Value.ToString();snapshot.position=player.transform.position;snapshot.paused=player.Paused;snapshot.slot=player.Inventory.CurrentSlot;snapshot.stamina=player.Stamina.Value;
                snapshot.bleedOut=player.BleedOutRemaining;var spectator=player.GetComponent<SpectatorController>();snapshot.spectating=spectator.Target?spectator.Target.DisplayName:"";
                snapshot.prompt=player.Interaction.CurrentPrompt;
                snapshot.inventory=player.Inventory.Slots.Select(i=>i?i.GetComponent<NetworkObject>().NetworkObjectId.ToString():"empty").ToArray();
                var light=player.GetComponent<PlayerFlashlightShortcut>().Flashlight;snapshot.light=light&&light.IsOn;
            }
            snapshot.closets=ClosetHideout.All.Where(c=>c && c.IsSpawned).Select(c=>$"{c.NetworkObjectId}:{c.Occupant.Value}").ToArray();
            snapshot.items=FindObjectsByType<NetworkPickup>().Where(i=>i.IsSpawned).Select(i=>$"{i.NetworkObjectId}:{i.Location.Value.carrier}:{i.Location.Value.slot}").ToArray();
            snapshot.doors=FindObjectsByType<DoorInteractable>().Where(d=>d.IsSpawned).Select(d=>$"{d.NetworkObjectId}:{d.Open.Value}:{d.SwingAngle.Value}"+(d is ExitDoor e?$":{e.Deposited.Value}/{e.Required.Value}":"")).ToArray();
            // A concurrent test reader may briefly hold the report; publish on the next tick.
            try{File.WriteAllText(Path.Combine(directory,mode+".snapshot.json"),JsonUtility.ToJson(snapshot,true));}catch(IOException){}
        }
    }
}

#endif
