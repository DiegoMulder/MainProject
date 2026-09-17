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
    public sealed class MansionFeedbackAcceptance : MonoBehaviour
    {
        public string DirectoryPath="Logs/MansionFeedback";
        readonly List<string> results=new();int command=200;int voiceEvents;bool done;
        void OnEnable()=>EditorApplication.update+=Pump;
        void OnDisable(){EditorApplication.update-=Pump;GameplayNoiseSystem.Emitted-=Noise;}
        void Pump(){if(EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();}
        void Check(bool pass,string text){results.Add((pass?"PASS: ":"FAIL: ")+text);File.WriteAllText(DirectoryPath+"/acceptance.txt",string.Join("\n",results)+"\n"+(done?"COMPLETE":"RUNNING"));}
        void Noise(GameplayNoise noise){if(noise.Category==NoiseCategory.Voice)voiceEvents++;}
        void Send(string action,ulong target=0)
        {
            var request=new MansionDevelopmentProbe.Command{id=++command,action=action,target=target.ToString()};
            for(int i=0;;i++){try{File.WriteAllText(DirectoryPath+"/client.command.json",JsonUtility.ToJson(request));break;}catch(IOException)when(i<10){System.Threading.Thread.Sleep(10);}}
        }
        MansionDevelopmentProbe.Snapshot Client(){try{return JsonUtility.FromJson<MansionDevelopmentProbe.Snapshot>(File.ReadAllText(DirectoryPath+"/client.snapshot.json"));}catch{return new MansionDevelopmentProbe.Snapshot();}}
        IEnumerator Start()
        {
            Directory.CreateDirectory(DirectoryPath);float deadline=Time.realtimeSinceStartup+50;
            while((!LobbyRoster.Instance || LobbyRoster.Instance.Members.Count<2)&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!LobbyRoster.Instance || LobbyRoster.Instance.Members.Count<2){done=true;Check(false,"Two lobby members connected");yield break;}
            yield return new WaitForSecondsRealtime(.8f);
            Check(!RoundManager.Instance && NetworkPlayer.Players.Count==0,"Lobby waits without generating geometry or spawning players");
            Check(Client().names?.Length==2 && Client().names.Contains("Client Tester"),"Both synchronized player names appear on client");
            Send("start");yield return new WaitForSecondsRealtime(.5f);Check(!RoundManager.Instance,"Client cannot start the match");
            GameSession.Instance.StartMatch();deadline=Time.realtimeSinceStartup+30;
            while((!RoundManager.Instance || RoundManager.Instance.Phase.Value==RoundPhase.Generating)&&Time.realtimeSinceStartup<deadline)yield return null;
            var round=RoundManager.Instance;
            if(!round || round.Phase.Value!=RoundPhase.Playing){done=true;Check(false,"Generated connected multi-floor round: "+(round?round.Failure.Value.ToString():"missing round"));yield break;}
            yield return new WaitForSecondsRealtime(1);
            var host=NetworkPlayer.Local;var client=NetworkPlayer.Players.First(p=>!p.IsOwner);var enemy=FindAnyObjectByType<EnemyController>();var agent=enemy.GetComponent<NavMeshAgent>();enemy.enabled=false;agent.isStopped=true;
            Check(NetworkPlayer.Players.Count==2 && Client().players==2,"Host start spawns both players into the same round");
            var layout=new List<string>();foreach(var r in round.Layout)layout.Add($"{r.module}:{r.quarter}:{r.position.x:F2},{r.position.y:F2},{r.position.z:F2}");
            Check(Client().layout==string.Join(";",layout),"Clients receive identical three-dimensional room placements");
            Check(Client().rooms==round.World.Rooms.Count && Client().props==round.Props.Count,"Client constructs every replicated room and prop before gameplay");
            Check(round.World.Rooms.Select(r=>r.transform.position.y).Distinct().Count()>=2,"Multiple connected floors generated");
            var kits=FindObjectsByType<MedkitItem>();Check(kits.Length==round.settings.medkitCount,"Configured medkits spawn on reachable furniture anchors");
            Check(round.World.ItemAnchors.Count>=round.settings.objectiveCount+round.settings.medkitCount,"Validated anchors cover every required item");
            var stair=round.World.Rooms.First(r=>r.staircase);agent.Warp(stair.NavigationPosition);agent.speed=5;agent.isStopped=false;agent.SetDestination(stair.transform.TransformPoint(new Vector3(0,4,6)));
            yield return new WaitForSecondsRealtime(5);
            Check(agent.isOnNavMesh && enemy.transform.position.y>stair.transform.position.y+3.8f,"Enemy agent physically navigates up the generated staircase");agent.isStopped=true;
            // Real perception/catch, followed by independent hiding fixtures.
            host.Motor.Teleport(new Vector3(-3,.03f,-3));client.Motor.Teleport(new Vector3(0,.03f,1));
            agent.Warp(new Vector3(0,.03f,.3f));enemy.transform.rotation=Quaternion.identity;enemy.chaseSpeed=0;enemy.enabled=true;
            yield return new WaitForSecondsRealtime(.6f);
            Check(client.Life.Value==PlayerLife.Downed,"Monster catch enters Downed rather than spectator state");
            enemy.enabled=false;agent.isStopped=true;client.Revive();
            var cover=FindObjectsByType<HidingCover>().OrderByDescending(c=>c.transform.position.sqrMagnitude).First();
            void Hide(){client.Motor.Teleport(cover.transform.position+Vector3.up*.03f);for(int tick=0;tick<60;tick++)client.Motor.Tick(new PlayerCommand{Crouch=true},1f/60);client.View.localPosition=Vector3.up*(client.Motor.Height-.18f);Physics.SyncTransforms();}
            void PlaceEnemy(float distance){agent.Warp(cover.transform.position+cover.transform.forward*distance);enemy.transform.rotation=Quaternion.LookRotation(-cover.transform.forward);Physics.SyncTransforms();}
            Hide();PlaceEnemy(2.2f);
            Check(HidingCover.For(client)==cover && !enemy.GetComponent<EnemyPerception>().HasLineOfSight(client) && enemy.GetComponent<EnemyPerception>().FindVisible()!=client,"Quiet crouched player is concealed by table cloth and not acquired");
            client.Motor.Teleport(cover.transform.position+cover.transform.forward*1.4f+Vector3.up*.03f);for(int tick=0;tick<60;tick++)client.Motor.Tick(default,1f/60);
            enemy.killDistance=.1f;enemy.enabled=true;yield return new WaitForSecondsRealtime(.4f);
            Check(enemy.Target==client,"Monster sees the player before entry into hiding cover");
            Hide();PlaceEnemy(1.2f);enemy.killDistance=1.1f;yield return new WaitForSecondsRealtime(.4f);
            Check(client.Life.Value==PlayerLife.Downed,"Remembered hiding location permits a catch through furniture occlusion");
            enemy.enabled=false;agent.isStopped=true;client.Revive();Hide();PlaceEnemy(2.2f);enemy.State.Value=EnemyState.Idle;
            GameplayNoiseSystem.Emit(client.transform.position,16,NoiseCategory.Voice,client.gameObject);
            Check(enemy.State.Value==EnemyState.Investigate,"Voice from hidden player causes normal investigation");
            PlaceEnemy(1.2f);enemy.enabled=true;yield return new WaitForSecondsRealtime(.4f);
            Check(client.Life.Value==PlayerLife.Downed,"Monster can catch a hidden player located through hearing");
            enemy.enabled=false;agent.isStopped=true;client.Revive();
            var objective=FindObjectsByType<NetworkPickup>().First(i=>i.GetComponent<ObjectiveItem>());Check(objective.Claim(client),"Objective assigned through standard inventory before downing");
            var medkit=kits[0].GetComponent<NetworkPickup>();Check(medkit.Claim(host),"Medkit occupies a normal inventory slot");
            client.Motor.Teleport(new Vector3(0,.03f,1));host.Motor.Teleport(new Vector3(0,.03f,-1));yield return new WaitForSecondsRealtime(.5f);
            client.Down();yield return new WaitForSecondsRealtime(.5f);
            Check(client.Life.Value==PlayerLife.Downed && Client().life=="Downed" && Client().bleedOut>295,"Downed state and default 300-second deadline synchronize");
            Check(objective.Location.Value.Held && Client().spectating=="","Downed player retains objectives and does not spectate");
            var before=client.transform.position;Send("drive");yield return new WaitForSecondsRealtime(.4f);Send("drop");yield return new WaitForSecondsRealtime(.3f);Send("stop");
            Check(Vector3.Distance(before,client.transform.position)<.1f && objective.Location.Value.Held,"Downed movement and inventory requests are rejected");
            host.View.rotation=Quaternion.LookRotation(client.transform.position+Vector3.up*.45f-host.View.position);Physics.SyncTransforms();host.Interaction.Tick(host.View,true);yield return new WaitForSecondsRealtime(.5f);
            Check(client.Alive && Client().life=="Alive" && !medkit,"Shared interaction revives and consumes exactly one medkit");
            Check(objective.Location.Value.Held,"Revived player retains carried objectives");
            int remaining=FindObjectsByType<MedkitItem>().Length;client.GetComponent<DownedInteractable>().Interact(host.Interaction);
            Check(FindObjectsByType<MedkitItem>().Length==remaining,"Duplicate revive does not consume another medkit");
            // Mirror the direction: a real client shared-ray RPC revives the host.
            var second=FindObjectsByType<MedkitItem>().First().GetComponent<NetworkPickup>();Check(second.Claim(client),"Client can carry a medkit alongside another item");
            host.Down();yield return new WaitForSecondsRealtime(.5f);Send("interact",host.NetworkObjectId);yield return new WaitForSecondsRealtime(.7f);
            Check(host.Alive && !second,"Client-origin revive RPC restores host and consumes its own medkit");
            GameplayNoiseSystem.Emitted+=Noise;
            Send("speech");yield return new WaitForSecondsRealtime(.3f);Check(voiceEvents>0,"Living client voice activity reaches the shared server noise bus");
            client.Down();yield return new WaitForSecondsRealtime(.7f);int previous=voiceEvents;Send("speech");yield return new WaitForSecondsRealtime(.4f);Check(voiceEvents>previous,"Downed speech still creates gameplay noise");
            client.BleedOutAt.Value=client.NetworkManager.ServerTime.Time+.25;
            yield return new WaitForSecondsRealtime(.8f);
            Check(client.Life.Value==PlayerLife.Dead && Client().life=="Dead" && !string.IsNullOrEmpty(Client().spectating),"Server deadline causes full death and living-player spectating");
            Check(!objective.Location.Value.Held,"Full death recovers carried objectives");previous=voiceEvents;Send("speech");yield return new WaitForSecondsRealtime(.8f);
            Check(voiceEvents==previous,"Dead spectator cannot emit voice gameplay noise");GameplayNoiseSystem.Emitted-=Noise;
            Check(!client.GetComponent<DownedInteractable>().CanInteract(host.Interaction),"Fully dead players cannot be revived");
            host.Down();yield return new WaitForSecondsRealtime(.4f);
            Check(round.Phase.Value==RoundPhase.Lost && host.Life.Value==PlayerLife.Dead,"No living reviver ends the round without waiting for bleed-out");
            string error=Client().error;
            Check(string.IsNullOrEmpty(error) || error.StartsWith("[Vivox]: System.ArgumentException: 'server'"),"Client has no gameplay runtime errors (missing Vivox configuration tracked separately)");
            done=true;Check(true,"Feedback network acceptance completed");
        }
    }
}
#endif
