#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP.Editor
{
    public sealed class MansionFeedbackRoundValidation : MonoBehaviour
    {
        public string directory="Logs/MansionFeedbackRound";
        readonly List<string> results=new(); readonly List<GameplayNoise> noises=new();int command=1000;bool done;
        void OnEnable(){DontDestroyOnLoad(gameObject);EditorApplication.update+=Pump;GameplayNoiseSystem.Emitted+=Noise;}
        void OnDisable(){EditorApplication.update-=Pump;GameplayNoiseSystem.Emitted-=Noise;}
        void Pump(){if(EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();}
        void Noise(GameplayNoise noise)=>noises.Add(noise);
        void Check(bool pass,string name){results.Add((pass?"PASS: ":"FAIL: ")+name);File.WriteAllText(directory+"/validation.txt",string.Join("\n",results)+"\n"+(done?"COMPLETE":"RUNNING"));}
        void Send(string action,ulong target=0,int slot=0)
        {
            string json=JsonUtility.ToJson(new MansionDevelopmentProbe.Command{id=++command,action=action,target=target.ToString(),slot=slot});
            for(int attempt=0;;attempt++)try{File.WriteAllText(directory+"/client.command.json",json);break;}catch(IOException)when(attempt<15){System.Threading.Thread.Sleep(5);}
        }
        MansionDevelopmentProbe.Snapshot Client(){try{return JsonUtility.FromJson<MansionDevelopmentProbe.Snapshot>(File.ReadAllText(directory+"/client.snapshot.json"));}catch{return new MansionDevelopmentProbe.Snapshot();}}
        IEnumerator Start()
        {
            Directory.CreateDirectory(directory);command=Mathf.Max(command,Client().command);float deadline=Time.realtimeSinceStartup+60;
            while((!LobbyRoster.Instance || LobbyRoster.Instance.Members.Count<2) && Time.realtimeSinceStartup<deadline)yield return null;
            if(!LobbyRoster.Instance || LobbyRoster.Instance.Members.Count<2){done=true;Check(false,"Two peers connected");yield break;}
            GameSession.Instance.StartMatch();deadline=Time.realtimeSinceStartup+35;
            while((!RoundManager.Instance || RoundManager.Instance.Phase.Value==RoundPhase.Generating) && Time.realtimeSinceStartup<deadline)yield return null;
            var round=RoundManager.Instance;
            if(!round || round.Phase.Value!=RoundPhase.Playing){done=true;Check(false,"Generation: "+(round?round.Failure.Value.ToString():"missing"));yield break;}
            yield return new WaitForSecondsRealtime(1);
            var host=NetworkPlayer.Local;var client=NetworkPlayer.Players.First(p=>!p.IsOwner);
            host.Controller.enabled=false;
            var enemy=FindAnyObjectByType<EnemyController>();var agent=enemy.GetComponent<NavMeshAgent>();enemy.enabled=false;agent.isStopped=true;
            Check(ClosetHideout.All.Count>0,"Closets selected through procedural prop manifest");
            Check(Client().closets?.Length==ClosetHideout.All.Count && Client().props==round.Props.Count,"Client receives exactly the server closets and full prop manifest");
            var closet=ClosetHideout.All.First(c=>c.HasClearExit(client));
            Vector3 home=round.World.Rooms.Last().NavigationPosition;
            host.Motor.Teleport(home);client.Motor.Teleport(closet.entryPoint.position);yield return new WaitForSecondsRealtime(.5f);
            void EnemyAt(Vector3 point,bool facePlayer=false){if(NavMesh.SamplePosition(point,out var nav,2,NavMesh.AllAreas))agent.Warp(nav.position);enemy.transform.rotation=Quaternion.LookRotation(facePlayer?client.transform.position-enemy.transform.position:closet.transform.forward);Physics.SyncTransforms();}
            EnemyAt(closet.InvestigationPosition+closet.transform.forward*2);enemy.State.Value=EnemyState.Idle;
            Send("interact",closet.NetworkObjectId);yield return new WaitForSecondsRealtime(.7f);
            Check(client.IsHidden && Client().hidden && closet.Occupant.Value==client.OwnerClientId,"Client shared interaction enters synchronized occupied closet");
            host.Motor.Teleport(closet.entryPoint.position+closet.transform.right*2);Physics.SyncTransforms();
            Check(Vector3.Distance(host.transform.position,closet.entryPoint.position)<host.Interaction.distance && !closet.TryEnter(host),"Second in-range player cannot claim an occupied closet");
            host.Motor.Teleport(home);
            Check(!enemy.GetComponent<EnemyPerception>().HasLineOfSight(client),"Closed closet geometry blocks ordinary enemy sight");
            var position=client.transform.position;Send("drive");yield return new WaitForSecondsRealtime(.5f);Send("drop");yield return new WaitForSecondsRealtime(.3f);Send("light");yield return new WaitForSecondsRealtime(.3f);
            Check(Vector3.Distance(position,client.transform.position)<.05f && client.Inventory.Slots.Count(i=>i)==1,"Hidden movement and inventory requests are blocked");Send("stop");
            host.Motor.Teleport(closet.exitPosition.position);
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.position=closet.alternateExits[0].position+Vector3.up;blocker.transform.localScale=new Vector3(.8f,2,.8f);Physics.SyncTransforms();
            Send("interact",closet.NetworkObjectId);yield return new WaitForSecondsRealtime(.5f);
            Check(client.IsHidden && !closet.HasClearExit(client),"Blocked primary and alternate exits keep player safely hidden");
            Destroy(blocker);host.Motor.Teleport(home);yield return null;Physics.SyncTransforms();Send("interact",closet.NetworkObjectId);yield return new WaitForSecondsRealtime(.6f);
            Check(!client.IsHidden && !Client().hidden && closet.Occupant.Value==ClosetHideout.Empty,"Exit restores controls and clears synchronized occupancy");
            client.Motor.Teleport(closet.entryPoint.position);yield return new WaitForSecondsRealtime(.4f);EnemyAt(closet.InvestigationPosition+closet.transform.forward*2,true);enemy.State.Value=EnemyState.Idle;
            Check(enemy.GetComponent<EnemyPerception>().CanSee(client),"Enemy sees player before entering closet");
            closet.TryEnter(client);Check(enemy.State.Value==EnemyState.Investigate,"Visible entry feeds existing investigate state");
            EnemyAt(closet.InvestigationPosition);enemy.enabled=true;yield return new WaitForSecondsRealtime(1.5f);enemy.enabled=false;agent.isStopped=true;
            Check(client.Life.Value==PlayerLife.Downed && !client.IsHidden && closet.Occupant.Value==ClosetHideout.Empty,"Known closet catch releases occupant into normal Downed flow; known="+enemy.KnownHidingSpot+" state="+enemy.State.Value+" exitClear="+closet.HasClearExit(client)+" distance="+Vector3.Distance(enemy.transform.position,closet.InvestigationPosition));
            Check(Client().internalHeight>0,"Downed camera retains low-resolution target");
            client.Revive();client.Motor.Teleport(closet.entryPoint.position);yield return new WaitForSecondsRealtime(.5f);
            EnemyAt(closet.InvestigationPosition+closet.transform.forward*2);closet.TryEnter(client);enemy.State.Value=EnemyState.Idle;noises.Clear();
            Send("speech");yield return new WaitForSecondsRealtime(.5f);
            Check(noises.Any(n=>n.Category==NoiseCategory.Voice && Mathf.Abs(n.Radius-client.voiceNoiseRadius*closet.noiseMultiplier)<.01f),"Client speech reaches shared bus with closet noise multiplier");
            Check(enemy.State.Value==EnemyState.Investigate,"Heard closet speech uses existing investigate state");
            EnemyAt(closet.InvestigationPosition);enemy.enabled=true;yield return new WaitForSecondsRealtime(1.5f);enemy.enabled=false;agent.isStopped=true;
            Check(client.Life.Value==PlayerLife.Downed,"Hearing evidence can discover/down a hidden player");client.Revive();
            client.Motor.Teleport(closet.entryPoint.position);yield return new WaitForSecondsRealtime(.5f);EnemyAt(client.transform.position+closet.transform.forward*1.8f);enemy.State.Value=EnemyState.Idle;noises.Clear();
            Send("light");yield return new WaitForSecondsRealtime(.5f);
            Check(noises.Count(n=>n.Category==NoiseCategory.Flashlight)==1 && enemy.State.Value==EnemyState.Investigate,"Client flashlight toggle reaches server hearing inside configured radius");
            EnemyAt(client.transform.position+closet.transform.forward*8);enemy.State.Value=EnemyState.Idle;noises.Clear();Send("light");yield return new WaitForSecondsRealtime(.5f);
            Check(noises.Count(n=>n.Category==NoiseCategory.Flashlight)==1 && enemy.State.Value==EnemyState.Idle,"Distant flashlight click does not alert monster");
            EnemyAt(client.transform.position+closet.transform.forward*2);enemy.State.Value=EnemyState.Idle;noises.Clear();
            var dropped=client.Inventory.Current.GetComponent<NetworkPickup>();Send("drop");yield return new WaitForSecondsRealtime(2);
            int impacts=noises.Count(n=>n.Category==NoiseCategory.Impact && n.Source==dropped.gameObject);
            Check(impacts>0 && impacts<=4 && !dropped.Location.Value.Held,"Real dropped-item collisions emit bounded server impact noises");
            Check(dropped.GetComponent<ImpactNoiseEmitter>().impactSounds.Length>0 && enemy.State.Value==EnemyState.Investigate,"Impact clip assigned and nearby enemy investigates collision");
            foreach(int gait in new[]{2,1,0})
            {
                host.Motor.Teleport(round.World.SpawnPosition+Vector3.back*2);host.transform.rotation=Quaternion.identity;noises.Clear();
                float finish=Time.unscaledTime+(gait==2?1.5f:gait==1?1.1f:.8f);
                while(Time.unscaledTime<finish){host.SubmitMovement(new PlayerCommand{Move=Vector2.up,Crouch=gait==2,Sprint=gait==0},host.View);yield return null;}
                host.SubmitMovement(default,host.View);
                float expected=gait==2?2:gait==1?7:14;
                Check(noises.Any(n=>n.Category==NoiseCategory.Footstep && Mathf.Abs(n.Radius-expected)<.01f),"Actual "+(gait==2?"crouching":gait==1?"walking":"sprinting")+" feeds shared hearing bus");
            }
            var door=round.World.Doors.First();noises.Clear();door.SetOpen(!door.Open.Value,host.gameObject);
            Check(noises.Any(n=>n.Category==NoiseCategory.Door),"Door transitions feed the same noise bus");
            yield return new WaitForSecondsRealtime(.5f);
            // Heartbeats use independent local distances and never generate gameplay events.
            host.Motor.Teleport(round.World.SpawnPosition);client.Motor.Teleport(closet.entryPoint.position);EnemyAt(client.transform.position+closet.transform.forward*2);
            var hb=host.GetComponent<HeartbeatFeedback>();Check(hb.ProximityIntensity(enemy.transform.position)>.99f && hb.ProximityIntensity(enemy.transform.position+Vector3.up*100)==0,"Heartbeat distance curve spans intense to silent");
            noises.Clear();yield return new WaitForSecondsRealtime(1);Check(noises.Count==0,"Heartbeat audio emits no gameplay noise");
            Check(Client().heartbeat>.1f,"Nearby client has its own heartbeat intensity");
            Send("resize",540,960);yield return new WaitForSecondsRealtime(1);Send("screenshot");yield return new WaitForSecondsRealtime(.5f);
            Check(Client().internalHeight==360 && Mathf.Abs(Client().internalWidth-640)<=1,"PSX target follows 960x540 window aspect ratio");
            Send("resize",720,1280);yield return new WaitForSecondsRealtime(1);
            Check(Client().internalHeight==360 && Mathf.Abs(Client().internalWidth-640)<=1,"PSX target survives window resizing");
            client.Down();client.BleedOutAt.Value=client.NetworkManager.ServerTime.Time+.1;yield return new WaitForSecondsRealtime(.8f);
            Check(Client().life=="Dead" && Client().heartbeat==0 && Client().internalHeight>0,"Spectator disables old-body heartbeat and retains PSX camera");
            Check(string.IsNullOrEmpty(Client().error),"Client has no runtime errors: "+Client().error);

            done=true;Check(true,"Feedback round validation finished");
        }
    }
}
#endif

