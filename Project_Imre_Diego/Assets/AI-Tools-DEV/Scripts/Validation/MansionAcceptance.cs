#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP.Editor
{
    // Attach to the editor host while a development player runs -mansion-probe client <directory>.
    public sealed class MansionAcceptance : MonoBehaviour
    {
        public const string DirectoryPath="Logs/MansionMultiplayer";
        readonly List<string> results=new(); int command=100; bool done; int footsteps;
        void OnEnable()=>EditorApplication.update+=Pump;
        void OnDisable()=>EditorApplication.update-=Pump;
        void Pump(){if(EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();}
        void CountNoise(GameplayNoise noise){if(noise.Category==NoiseCategory.Footstep)footsteps++;}
        void Check(bool pass,string message)
        {
            results.Add((pass?"PASS: ":"FAIL: ")+message);
            File.WriteAllText(DirectoryPath+"/acceptance.txt",string.Join("\n",results)+"\n"+(done?"COMPLETE":"RUNNING"));
        }
        void Send(string action,ulong target=0,int slot=-1)
        {
            var request=new MansionDevelopmentProbe.Command{id=++command,action=action,target=target.ToString(),slot=slot};
            for(int attempt=0;;attempt++)
            {
                try{File.WriteAllText(DirectoryPath+"/client.command.json",JsonUtility.ToJson(request));break;}
                catch(IOException) when(attempt<10){System.Threading.Thread.Sleep(10);}
            }
        }
        MansionDevelopmentProbe.Snapshot Client()
        {
            try{return JsonUtility.FromJson<MansionDevelopmentProbe.Snapshot>(File.ReadAllText(DirectoryPath+"/client.snapshot.json"));}catch{return new MansionDevelopmentProbe.Snapshot();}
        }
        IEnumerator Start()
        {
            Directory.CreateDirectory(DirectoryPath);
            float deadline=Time.realtimeSinceStartup+40;
            while(NetworkPlayer.Players.Count<2 && Time.realtimeSinceStartup<deadline)yield return null;
            if(NetworkPlayer.Players.Count<2){done=true;Check(false,"Two players connected within timeout");yield break;}
            var round=RoundManager.Instance;var host=NetworkPlayer.Local;var client=NetworkPlayer.Players.First(p=>!p.IsOwner);
            var enemy=FindAnyObjectByType<EnemyController>();enemy.enabled=false;enemy.GetComponent<NavMeshAgent>().isStopped=true;
            var layout=new List<string>();foreach(var r in round.Layout)layout.Add($"{r.module}:{r.quarter}:{r.position.x:F2},{r.position.z:F2}");
            yield return new WaitForSecondsRealtime(1);
            Check(Client().layout==string.Join(";",layout),"Client reconstructs exactly the authoritative room layout");
            Check(Client().props==round.Props.Count && Client().rooms==round.World.Rooms.Count,"Room and prop counts agree across peers");
            Check(host.Inventory.Current && client.Inventory.Current && client.Inventory.Current.GetComponent<PlayerFlashlight>(),"Each player starts with a normal-slot flashlight");
            Check(Client().activeCameras==1,"Remote player camera does not activate on client");
            Send("select",slot:2);yield return new WaitForSecondsRealtime(.5f);Send("light");yield return new WaitForSecondsRealtime(.5f);
            Check(client.Inventory.CurrentSlot==2 && client.GetComponent<PlayerFlashlightShortcut>().Flashlight.IsOn && Client().light,"F works from a non-equipped slot and light state replicates");
            var objectives=FindObjectsByType<NetworkPickup>().Where(i=>i.GetComponent<ObjectiveItem>()).ToArray();
            client.Motor.Teleport(new Vector3(0,.1f,-1));
            objectives[0].Release(new Vector3(0,1.4f,1),Quaternion.identity,Vector3.zero);objectives[0].GetComponent<Rigidbody>().isKinematic=true;
            yield return new WaitForSecondsRealtime(.7f);Send("interact",objectives[0].NetworkObjectId);yield return new WaitForSecondsRealtime(.7f);
            Check(objectives[0].Location.Value.Held && objectives[0].Location.Value.carrier==client.OwnerClientId,"Real client shared-ray request picks up an objective on server");
            Check(Client().inventory.Contains(objectives[0].NetworkObjectId.ToString()),"Objective appears in the client inventory");
            Check(!objectives[0].Claim(host),"A second player cannot collect the same world instance");
            Send("select",slot:objectives[0].Location.Value.slot);yield return new WaitForSecondsRealtime(.3f);
            Send("drop");yield return new WaitForSecondsRealtime(.4f);
            Check(!objectives[0].Location.Value.Held && !Client().inventory.Contains(objectives[0].NetworkObjectId.ToString()),"Client drop releases ownership and synchronizes the free slot");
            Check(objectives[0].Claim(client),"Dropped objective can be collected again");
            Check(objectives[1].Claim(client),"Second objective fills the third inventory slot");
            Check(!objectives[2].Claim(client) && !objectives[2].Location.Value.Held,"Full inventory rejects pickup without deleting world item");
            Send("select",slot:objectives[0].Location.Value.slot);yield return new WaitForSecondsRealtime(.3f);
            Send("use");yield return new WaitForSecondsRealtime(.3f);
            Check(objectives[0].Location.Value.Held,"An item without Primary Use is a safe no-op");
            var door=round.World.Doors.First();door.SetOpen(false);
            client.Motor.Teleport(door.transform.position-door.transform.forward*2+Vector3.up*.1f);
            yield return new WaitForSecondsRealtime(.8f);Send("interact",door.NetworkObjectId);yield return new WaitForSecondsRealtime(.7f);
            Check(door.Open.Value && Client().doors.Contains(door.NetworkObjectId+":True"),"Same interaction opens a synchronized ordinary door");
            Check(!door.GetComponent<PickupItem>(),"Door participation never makes a door pickupable");
            var exit=round.Exit;Check(!exit.Unlocked,"Exit starts locked");
            Send("select",slot:objectives[0].Location.Value.slot);yield return new WaitForSecondsRealtime(.4f);
            client.Motor.Teleport(exit.transform.position-exit.transform.forward*2+Vector3.up*.1f);
            yield return new WaitForSecondsRealtime(.8f);Send("interact",exit.NetworkObjectId);yield return new WaitForSecondsRealtime(.7f);
            Check(exit.Deposited.Value==1 && client.Inventory.HasSpace,"Shared exit interaction deposits an item and frees its slot");
            Check(Client().doors.Any(d=>d==exit.NetworkObjectId+":False:1/"+exit.Required.Value),"Exit progress replicates to client");
            client.Motor.Teleport(new Vector3(0,.1f,-1));yield return new WaitForSecondsRealtime(.7f);
            GameplayNoiseSystem.Emitted+=CountNoise;
            Send("drive");yield return new WaitForSecondsRealtime(.8f);Send("pause");yield return new WaitForSecondsRealtime(.4f);
            GameplayNoiseSystem.Emitted-=CountNoise;
            Check(footsteps>0,"Actual player movement emits common footstep noise");
            Vector3 pausedPosition=client.transform.position;float clock=Time.time;
            var agent=enemy.GetComponent<NavMeshAgent>();var far=round.World.Rooms.OrderByDescending(r=>r.transform.position.sqrMagnitude).First();agent.Warp(far.transform.position);agent.isStopped=false;enemy.enabled=true;
            yield return new WaitForSecondsRealtime(1.2f);
            Check(Client().paused && Time.time-clock>1 && Time.timeScale==1 && Vector3.Distance(pausedPosition,client.transform.position)<.2f,"Local pause blocks client movement without pausing time");
            Check(enemy.enabled && agent.enabled && agent.isOnNavMesh,"Enemy simulation remains active during local pause");
            var hostPosition=host.transform.position;host.Motor.Tick(new PlayerCommand{Move=Vector2.right},.05f);
            Check(Vector3.Distance(hostPosition,host.transform.position)>.001f,"Other player motor continues during client pause");
            Send("resume");yield return new WaitForSecondsRealtime(.4f);Check(!Client().paused,"Resume restores local input gate");
            enemy.chaseSpeed=0;enemy.killDistance=.2f;enemy.State.Value=EnemyState.Idle;
            GameplayNoiseSystem.Emit(enemy.transform.position+Vector3.right*2,12,NoiseCategory.Impact,objectives[1].gameObject);
            Check(enemy.State.Value==EnemyState.Investigate,"Shared impact noise causes investigation");
            client.Motor.Teleport(enemy.transform.position+enemy.transform.forward*3);Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.5f);
            Check(enemy.GetComponent<EnemyPerception>().HasLineOfSight(client) && enemy.State.Value==EnemyState.Chase,"Enemy sees and chases a living player");
            // A temporary structural wall verifies occlusion independently of room placement.
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.position=Vector3.Lerp(enemy.transform.position,client.transform.position,.5f)+Vector3.up*1.5f;blocker.transform.localScale=new Vector3(4,3,.3f);blocker.transform.rotation=Quaternion.LookRotation(client.transform.position-enemy.transform.position);Physics.SyncTransforms();
            Check(!enemy.GetComponent<EnemyPerception>().HasLineOfSight(client),"Structural geometry blocks enemy vision");Destroy(blocker);
            enemy.lostSightChaseDuration=.15f;enemy.investigationDuration=.2f;enemy.searchDuration=2;
            client.Motor.Teleport(new Vector3(0,.1f,-1));yield return new WaitForSecondsRealtime(.6f);
            Check(enemy.State.Value==EnemyState.Search || enemy.State.Value==EnemyState.Investigate,"Losing sight triggers last-known-position investigation/search");
            enemy.killDistance=1.1f;client.Motor.Teleport(enemy.transform.position+enemy.transform.forward*.7f);Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.6f);
            Check(client.Life.Value==PlayerLife.Dead && round.Phase.Value==RoundPhase.Playing,"Enemy catch kills instantly while surviving player keeps round running");
            Check(!objectives[1].Location.Value.Held && objectives[1].GetComponent<PickupItem>().CanInteract(host.Interaction),"Dead carrier's objective is recoverable");
            Check(Client().life=="Dead" && Client().activeCameras==1,"Dead client retains only its spectator camera");
            Check(enemy.GetComponent<EnemyPerception>().FindVisible()!=client,"Enemy ignores dead spectators");enemy.enabled=false;agent.isStopped=true;
            // Independent disconnect scenario: reset only the test fixture's life state.
            client.Life.Value=PlayerLife.Alive;objectives[1].Claim(client);yield return new WaitForSecondsRealtime(.5f);
            Send("menu");yield return new WaitForSecondsRealtime(2);
            Check(NetworkPlayer.Players.Count==1 && !objectives[1].Location.Value.Held,"Returning to menu disconnects and recovers carried objective");
            bool allClaimable=true;
            foreach(var item in FindObjectsByType<NetworkPickup>().Where(i=>i.GetComponent<ObjectiveItem>()).ToArray())
            {allClaimable &= item.Claim(host);exit.Interact(host.Interaction);}
            Check(allClaimable,"Every remaining recovered/world objective remains claimable");
            Check(exit.Unlocked && exit.Deposited.Value==exit.Required.Value,"All required deposits unlock the exit");
            host.Kill();Check(round.Phase.Value==RoundPhase.Lost,"All connected participants dead produces shared game over");
            // Independent escape scenario with the already-completed exit.
            host.Life.Value=PlayerLife.Alive;round.Phase.Value=RoundPhase.Playing;exit.Interact(host.Interaction);
            Check(host.Life.Value==PlayerLife.Escaped && round.Phase.Value==RoundPhase.Won,"Unlocked exit exposes escape and round-completion flow");
            done=true;Check(true,"Acceptance sequence completed");
        }
    }
}

#endif
