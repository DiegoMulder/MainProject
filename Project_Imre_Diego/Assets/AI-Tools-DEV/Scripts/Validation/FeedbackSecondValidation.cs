#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.Netcode;
namespace SurvivalFP.Editor
{
    public sealed class FeedbackSecondValidation:MonoBehaviour
    {
        public string directory="Logs/MansionFeedbackRound";
        readonly List<string> results=new();int command;bool done;
        void OnEnable(){DontDestroyOnLoad(gameObject);EditorApplication.update+=Pump;}
        void OnDisable()=>EditorApplication.update-=Pump;
        void Pump(){if(EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();}
        void Check(bool ok,string label){results.Add((ok?"PASS: ":"FAIL: ")+label);File.WriteAllText(directory+"/second-validation.txt",string.Join("\n",results)+"\n"+(done?"COMPLETE":"RUNNING"));}
        MansionDevelopmentProbe.Snapshot Client(){try{return JsonUtility.FromJson<MansionDevelopmentProbe.Snapshot>(File.ReadAllText(directory+"/client.snapshot.json"));}catch{return new MansionDevelopmentProbe.Snapshot();}}
        void Send(string action,int slot=0,string target="")
        {
            var json=JsonUtility.ToJson(new MansionDevelopmentProbe.Command{id=++command,action=action,slot=slot,target=target});
            for(int attempt=0;;attempt++)try{File.WriteAllText(directory+"/client.command.json",json);break;}catch(IOException)when(attempt<15){System.Threading.Thread.Sleep(5);}
        }
        IEnumerator Until(Func<bool> condition,float timeout=30)
        {float end=Time.realtimeSinceStartup+timeout;while(!condition() && Time.realtimeSinceStartup<end)yield return null;}
        IEnumerator Start()
        {
            command=Mathf.Max(2000,Client().command);var session=GameSession.Instance;
            var old=RoundManager.Instance;int seed=old.Seed.Value;ulong roundId=old.NetworkObjectId;
            var names=Enumerable.Range(0,LobbyRoster.Instance.Members.Count).Select(i=>LobbyRoster.Instance.Members[i].name.ToString()).ToArray();string code=session.JoinCode;
            old.Phase.Value=RoundPhase.Lost;session.BackToLobby();
            yield return Until(()=>SceneManager.GetActiveScene().name==session.menuScene && !session.Transitioning);
            yield return new WaitForSecondsRealtime(.5f);
            Check(!RoundManager.Instance && NetworkPlayer.Players.Count==0 && EnemyController.Enemies.Count==0 && ClosetHideout.All.Count==0,"Back to Lobby removes the previous round, players, enemies and closets");
            Check(LobbyRoster.Instance && !LobbyRoster.Instance.Started.Value && session.JoinCode==code && Enumerable.Range(0,LobbyRoster.Instance.Members.Count).Select(i=>LobbyRoster.Instance.Members[i].name.ToString()).SequenceEqual(names),"Lobby preserves party, names and session code");
            Check(Client().scene=="MainMenu" && Client().players==0,"Connected client follows the host back to MainMenu lobby");
            session.StartMatch();yield return Until(()=>RoundManager.Instance && RoundManager.Instance.Phase.Value!=RoundPhase.Generating);
            yield return new WaitForSecondsRealtime(1);
            var round=RoundManager.Instance;
            if(!round || round.Phase.Value!=RoundPhase.Playing){done=true;Check(false,"Second round generated");yield break;}
            var host=NetworkPlayer.Local;var client=NetworkPlayer.Players.First(p=>!p.IsOwner);
            host.Controller.enabled=false;host.SetPaused(false);
            var enemy=EnemyController.Enemies.First();enemy.enabled=false;enemy.GetComponent<NavMeshAgent>().isStopped=true;
            Check(round.NetworkObjectId!=roundId && round.Seed.Value!=seed && Client().scene=="Game","Second round uses Game scene and a fresh procedural seed");
            Check(NetworkPlayer.Players.All(p=>p.Alive && !p.IsHidden && p.Inventory.Slots.Count(i=>i)==1 && p.Inventory.Slots.Any(i=>i && i.kind==PickupKind.Flashlight)) && round.Exit.Deposited.Value==0 && EnemyController.Enemies.Count==1,"Second round resets life, hiding, inventory, default flashlights, exit and enemy");
            Check(FindObjectsByType<NetworkManager>(FindObjectsSortMode.None).Length==1 && FindObjectsByType<ProximityVoice>(FindObjectsSortMode.None).Length==1,"Scene changes keep one networking and voice manager");
            host.Motor.Teleport(round.World.SpawnPosition+Vector3.right*3);
            client.Motor.Teleport(round.World.SpawnPosition+Vector3.back*2);
            Send("resume");yield return new WaitForSecondsRealtime(.4f);Send("predict");yield return new WaitForSecondsRealtime(.4f);
            Check(Client().immediateDistance>.001f,"Owning client moves within the local input call, before a server response");
            int corrections=Client().corrections;var start=client.transform.position;
            Send("drive",1);yield return new WaitForSecondsRealtime(1.2f);Send("stop");yield return new WaitForSecondsRealtime(.5f);
            Check(Vector3.Distance(start,client.transform.position)>2 && Vector3.Distance(Client().position,client.transform.position)<.3f,"Client walking is responsive and converges with server movement");
            Check(Client().corrections-corrections<12,"Ordinary client walking avoids constant correction");
            Send("jump");yield return new WaitForSecondsRealtime(.25f);
            Check(!Client().grounded && client.Motor.VerticalSpeed>0,"Client jump predicts locally and is accepted by server");
            yield return new WaitForSecondsRealtime(1);Send("drive",2);yield return new WaitForSecondsRealtime(.5f);
            Check(Client().height<1.15f && client.Motor.Height<1.15f,"Client crouch uses the same immediate local and authoritative capsule");
            Send("stop");yield return new WaitForSecondsRealtime(.5f);
            var revive=client.GetComponent<DownedInteractable>();
            Check(revive.Prompt(host.Interaction)=="" && !revive.CanInteract(host.Interaction),"Alive player exposes neither revive prompt nor revive action");
            client.Down();yield return new WaitForSecondsRealtime(.4f);
            Check(client.Life.Value==PlayerLife.Downed && Client().life=="Downed" && client.BleedOutRemaining>295,"Downed state replicates with the 300-second timer");
            Check(client.Motor.Height<.7f && Vector3.Dot(client.visualBody.up,Vector3.up)<.1f,"Downed body is prone with a low capsule and upright network root");
            var perception=enemy.GetComponent<EnemyPerception>();
            Check(!perception.CanSee(client) && !perception.HasLineOfSight(client),"Downed player is rejected by vision and line-of-sight entry points");
            Check(revive.Prompt(host.Interaction)=="Needs Medkit to Revive" && !revive.CanInteract(host.Interaction),"Downed teammate without medkit gives requirement feedback only");
            start=client.transform.position;Send("drive",0);yield return new WaitForSecondsRealtime(.2f);Send("jump");Send("drop");
            yield return new WaitForSecondsRealtime(1.8f);Send("stop");yield return new WaitForSecondsRealtime(.4f);
            float crawl=Vector3.Distance(start,client.transform.position);
            Check(crawl>.4f && crawl<2 && client.Motor.Height<.7f && client.Motor.IsGrounded,"Downed client crawls slowly on the ground; sprint and jump do not stand it up");
            Check(client.Inventory.Slots.Count(i=>i)==1,"Downed inventory/drop request is rejected by server");
            var kit=Instantiate(round.settings.medkit,host.transform.position+Vector3.up,Quaternion.identity);kit.GetComponent<NetworkObject>().Spawn();kit.Claim(host);
            Check(revive.CanInteract(host.Interaction) && revive.Prompt(host.Interaction).Contains("Revive"),"Carrying a medkit enables the eligible revive action");
            revive.Interact(host.Interaction);yield return new WaitForSecondsRealtime(.7f);
            Check(client.Alive && Client().life=="Alive" && client.Motor.Height>1.9f && client.visualBody.up.y>.99f && revive.Prompt(host.Interaction)=="","Revival restores movement, capsule, visual pose and removes revive prompt");
            Check(!kit && host.Inventory.Slots.Count(i=>i)==1,"Successful revival consumes exactly one medkit");
            // Generic impact clip selection including null/empty entries.
            var impact=client.Inventory.Slots.First(i=>i).GetComponent<ImpactNoiseEmitter>();var original=impact.impactSounds;
            var a=AudioClip.Create("Impact test A",800,1,8000,false);var b=AudioClip.Create("Impact test B",800,1,8000,false);
            impact.impactSounds=new[]{a,null,b};var picks=new HashSet<int>();for(int i=0;i<100;i++)picks.Add(impact.ChooseClip());
            Check(picks.SetEquals(new[]{0,2}),"Impact array randomly selects all valid clips and ignores empty slots");
            impact.impactSounds=Array.Empty<AudioClip>();impact.PlayImpact(.5f,-1,1);
            Check(impact.ChooseClip()==-1 && impact.Strength(impact.referenceSpeed)>impact.Strength(impact.minimumSpeed),"Empty audio arrays are safe; harder impacts have greater strength");
            impact.impactSounds=original;Destroy(a);Destroy(b);
            Check(Camera.allCameras.Count(c=>!c.targetTexture)==1,"Game maintains a real screen-rendering camera");
            Check(string.IsNullOrEmpty(Client().error),"Client lifecycle and movement produced no errors: "+Client().error);
            Send("menu");yield return Until(()=>NetworkPlayer.Players.Count==1,10);yield return new WaitForSecondsRealtime(.7f);
            Check(Client().scene=="MainMenu" && NetworkManager.Singleton.IsServer && NetworkPlayer.Players.Count==1,"Individual client Main Menu leaves cleanly while host remains connected");
            session.ReturnToMenu();yield return Until(()=>SceneManager.GetActiveScene().name=="MainMenu" && !NetworkManager.Singleton.IsListening,15);
            Check(!RoundManager.Instance && !LobbyRoster.Instance && FindObjectsByType<NetworkManager>(FindObjectsSortMode.None).Length==1,"Host Main Menu shutdown removes round/lobby and creates one fresh session");
            done=true;Check(true,"Second feedback validation complete");
        }
    }
}
#endif
