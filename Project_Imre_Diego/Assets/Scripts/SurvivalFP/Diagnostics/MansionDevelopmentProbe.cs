using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    // Opt-in local integration harness. Never instantiated in a non-development player.
    public sealed class MansionDevelopmentProbe : MonoBehaviour
    {
        [Serializable] public class Command { public int id; public string action; public string target; public int slot; }
        [Serializable] public class Snapshot
        {
            public string phase,layout,life,prompt,error,lobbyCode,status,spectating;
            public string[] names;
            public bool matchStarted;
            public float bleedOut;
            public int seed,rooms,props,players,slot,command,activeCameras;
            public float time,timeScale,stamina;
            public bool paused,light,hidden;
            public float heartbeat; public int internalWidth,internalHeight; public string[] closets;
            public Vector3 position;
            public string[] inventory,items,doors;
        }
        string directory,mode,cloudCode,lastError=""; int handled; float nextSnapshot; bool drive,cloud; int driveMode;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if(!Debug.isDebugBuild && !Application.isEditor) return;
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-mansion-probe");
            if(index<0 || index+2>=args.Length) return;
            var go=new GameObject("Development integration probe");var p=go.AddComponent<MansionDevelopmentProbe>();p.mode=args[index+1];p.directory=args[index+2];
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
        void Update()
        {
            if(string.IsNullOrEmpty(directory)) return;
            var player=NetworkPlayer.Local;
            if(drive && player) player.SubmitMovement(new PlayerCommand {Move=Vector2.up,Sprint=driveMode==0,Crouch=driveMode==2},player.View);
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
                            case "start":GameSession.Instance.StartMatch();break;
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
                            case "drive":player.Controller.enabled=false;drive=true;driveMode=command.slot;break;
                            case "screenshot":ScreenCapture.CaptureScreenshot(Path.Combine(directory,"client-view.png"));break;
                            case "resize":Screen.SetResolution(command.slot, int.Parse(command.target), FullScreenMode.Windowed);break;
                            case "stop":drive=false;player.SubmitMovement(default,player.View);break;
                            case "disconnect":NetworkManager.Singleton.Shutdown();break;
                            case "menu":GameSession.Instance.ReturnToMenu();break;
                        }
                    }
                    catch(Exception ex){lastError=ex.ToString();}
                }
            }
            if(Time.unscaledTime<nextSnapshot) return;nextSnapshot=Time.unscaledTime+.2f;
            var round=RoundManager.Instance;
            var snapshot=new Snapshot {phase=round?round.Phase.Value.ToString():"Menu",time=Time.time,timeScale=Time.timeScale,command=handled,error=lastError,players=NetworkPlayer.Players.Count,activeCameras=Camera.allCamerasCount};
            var lobby=LobbyRoster.Instance;var names=new System.Collections.Generic.List<string>();if(lobby)foreach(var member in lobby.Members)names.Add(member.name.ToString());snapshot.names=names.ToArray();
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
                snapshot.hidden=player.IsHidden;snapshot.heartbeat=player.GetComponent<HeartbeatFeedback>()?player.GetComponent<HeartbeatFeedback>().Intensity:0;
                var psx=player.View.GetComponent<PsxCameraPresentation>();if(psx){snapshot.internalWidth=psx.InternalSize.x;snapshot.internalHeight=psx.InternalSize.y;}
                snapshot.life=player.Life.Value.ToString();snapshot.position=player.transform.position;snapshot.paused=player.Paused;snapshot.slot=player.Inventory.CurrentSlot;snapshot.stamina=player.Stamina.Value;
                snapshot.bleedOut=player.BleedOutRemaining;var spectator=player.GetComponent<SpectatorController>();snapshot.spectating=spectator.Target?spectator.Target.DisplayName:"";
                snapshot.prompt=player.Interaction.CurrentPrompt;
                snapshot.inventory=player.Inventory.Slots.Select(i=>i?i.GetComponent<NetworkObject>().NetworkObjectId.ToString():"empty").ToArray();
                var light=player.GetComponent<PlayerFlashlightShortcut>().Flashlight;snapshot.light=light&&light.IsOn;
            }
            snapshot.closets=ClosetHideout.All.Where(c=>c && c.IsSpawned).Select(c=>$"{c.NetworkObjectId}:{c.Occupant.Value}").ToArray();
            snapshot.items=FindObjectsByType<NetworkPickup>().Where(i=>i.IsSpawned).Select(i=>$"{i.NetworkObjectId}:{i.Location.Value.carrier}:{i.Location.Value.slot}").ToArray();
            snapshot.doors=FindObjectsByType<DoorInteractable>().Where(d=>d.IsSpawned).Select(d=>$"{d.NetworkObjectId}:{d.Open.Value}"+(d is ExitDoor e?$":{e.Deposited.Value}/{e.Required.Value}":"")).ToArray();
            // A concurrent test reader may briefly hold the report; publish on the next tick.
            try{File.WriteAllText(Path.Combine(directory,mode+".snapshot.json"),JsonUtility.ToJson(snapshot,true));}catch(IOException){}
        }
    }
}
