#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
namespace SurvivalFP.Editor
{
    public sealed class MultiplayerIntegrationValidation:MonoBehaviour
    {
        public static string DirectoryPath=>Path.GetFullPath("Assets/AI-Tools-DEV/.Runs/AnimationMaps");
        PlayerCommand? hostInput;
        readonly List<string> results=new();int command=100;string pending;
        MansionDevelopmentProbe.Snapshot Read(){try{return JsonUtility.FromJson<MansionDevelopmentProbe.Snapshot>(File.ReadAllText(Path.Combine(DirectoryPath,"client.snapshot.json")));}catch{return null;}}
        void Check(bool value,string message){results.Add((value?"PASS ":"FAIL ")+message);File.WriteAllLines(Path.Combine(DirectoryPath,"validation.txt"),results);}
        void Send(string action,int slot=0){pending=JsonUtility.ToJson(new MansionDevelopmentProbe.Command{id=++command,action=action,slot=slot});Update();}
        void Update(){if(hostInput.HasValue&&NetworkPlayer.Local)NetworkPlayer.Local.SubmitMovement(hostInput.Value,NetworkPlayer.Local.View);if(pending!=null)try{File.WriteAllText(Path.Combine(DirectoryPath,"client.command.json"),pending);pending=null;}catch(IOException){}}
        IEnumerator Until(Func<bool> predicate,float timeout=20){float end=Time.realtimeSinceStartup+timeout;while(!predicate()&&Time.realtimeSinceStartup<end)yield return null;}
        MansionDevelopmentProbe.AnimationSnapshot Remote(ulong owner)=>Read()?.animations?.FirstOrDefault(a=>a.owner==owner);
        IEnumerator Talk(){for(int i=0;i<8;i++){Send("talk",1);yield return new WaitForSeconds(.12f);}}
        void Place(NetworkPlayer p,Vector3 position){p.Motor.Teleport(position);p.GetComponent<PlayerPrediction>().ForceState();}
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);Directory.CreateDirectory(DirectoryPath);
            yield return Until(()=>NetworkManager.Singleton&&NetworkManager.Singleton.ConnectedClientsIds.Count==2,60);
            if(NetworkManager.Singleton.ConnectedClientsIds.Count!=2){Check(false,"client connected");yield break;}
            Check(true,"two Relay peers connected");yield return new WaitForSeconds(1);
            Check(Read()?.names.Length==2,"both lobby names visible");
            Check(Read()?.configHash==NetworkManager.Singleton.NetworkConfig.GetConfig(false).ToString(),"matching network protocol and prefab configuration");
            foreach(int map in new[]{0,1})
            {
                LobbyRoster.Instance.SelectMap(map);LobbyRoster.Instance.SelectDifficulty(map==0?0:3);
                yield return Until(()=>Read()?.map==map);
                Check(Read()?.map==map,"map selection replicated "+map);Send("map",1-map);yield return new WaitForSeconds(.4f);Check(LobbyRoster.Instance.Map.Value==map,"client cannot change map");
                GameSession.Instance.StartMatch();yield return Until(()=>RoundManager.Instance&&RoundManager.Instance.Phase.Value!=RoundPhase.Generating,60);
                var round=RoundManager.Instance;if(!round||round.Phase.Value!=RoundPhase.Playing){Check(false,"map generation "+(round?round.Failure.Value.ToString():"timeout"));yield break;}
                foreach(var e in EnemyController.Enemies){e.enabled=false;e.GetComponent<NavMeshAgent>().isStopped=true;}
                yield return Until(()=>Read()?.phase=="Playing"&&Read()?.rooms==round.Layout.Count,40);
                Check(Read()?.rooms==round.Layout.Count&&Read()?.seed==round.Seed.Value,"same generated map on both peers");
                Check(Read()?.roomNames.All(n=>n.Contains("Slaughterhouse")== (map==1))==true,"only selected map structural assets used");
                Check(Read()?.enemies==round.settings.enemyCount&&Read()?.requiredObjectives==round.settings.objectiveCount,"shared difficulty drives map objectives and enemies");
                var host=NetworkPlayer.Local;var client=NetworkPlayer.Players.First(p=>!p.IsOwner);host.Controller.enabled=false;
                GameSession.Instance.GetComponent<ProximityVoice>().enabled=false;Send("voicepause");yield return new WaitForSeconds(.3f);
                var driver=client.GetComponent<PlayerAnimationDriver>();var animator=driver.animator;
                Check(Remote(client.OwnerClientId)?.bodyHidden==true&&Remote(host.OwnerClientId)?.bodyHidden==false,"owner body hidden, other character visible");
                Check(client.Inventory.Current.transform.parent==client.Inventory.rightHandAnchor&&Remote(client.OwnerClientId)?.itemParent=="Item Hand Anchor","one equipped item uses remote hand and local camera anchors");
                Check(client.Inventory.Current.transform.lossyScale.magnitude<3,"imported hand scale does not enlarge equipped item");
                Vector3 start=round.World.SpawnPosition+Vector3.right*2;Place(client,start);Send("stop");yield return new WaitForSeconds(.5f);
                Check(!animator.GetBool("IsDown")&&animator.GetFloat("Blend")<.1f,"idle is not forced into downed state");
                Send("drive",1);yield return new WaitForSeconds(.7f);Check(animator.GetFloat("Blend")>.1f&&Remote(client.OwnerClientId)?.blend>.1f,"walking animation on host and owning client");Send("stop");yield return new WaitForSeconds(.3f);Place(client,start);
                Send("drive",0);yield return new WaitForSeconds(.6f);Check(animator.GetFloat("Blend")>.65f,"sprint raises Blend");Send("stop");yield return new WaitForSeconds(.4f);Place(client,start);
                Send("drive",2);yield return new WaitForSeconds(.7f);Check(animator.GetBool("IsCrouching")&&animator.GetFloat("Crouch")>.1f,"crouched movement drives Crouch");Send("stop");yield return new WaitForSeconds(.6f);Check(!animator.GetBool("IsCrouching"),"standing exits crouch");Place(client,start);yield return new WaitForSeconds(.4f);
                Send("jump");yield return new WaitForSeconds(.25f);Check(driver.Jumping.Value&&animator.GetBool("Jump"),"genuine client jump synchronizes");yield return new WaitForSeconds(1.8f);Check(!animator.GetBool("Jump"),"landing clears jump state");
                hostInput=new PlayerCommand{Crouch=true};yield return new WaitForSeconds(.7f);Check(Remote(host.OwnerClientId)?.isCrouching==true&&Remote(host.OwnerClientId)?.crouch<.05f,"stationary crouch uses idle pose on remote client");hostInput=default(PlayerCommand);yield return new WaitForSeconds(.5f);hostInput=null;
                yield return Talk();Check(animator.GetBool("IsTalking")&&Remote(client.OwnerClientId)?.talking==true,"speech state and facial layer replicate");Place(client,start);Send("drive",1);yield return Until(()=>Read()?.command==command);yield return Talk();Check(animator.GetBool("IsTalking")&&animator.GetFloat("Blend")>.1f,"talking while walking preserves locomotion");Send("stop");yield return Until(()=>Read()?.command==command);Place(client,start);Send("drive",2);yield return Until(()=>Read()?.command==command);yield return Talk();Check(animator.GetBool("IsTalking")&&animator.GetBool("IsCrouching"),"talking while crouching preserves posture");Send("stop");
                yield return new WaitForSeconds(.6f);Check(!animator.GetBool("IsTalking")&&animator.GetLayerWeight(1)<.01f,"speech release restores base face");
                var radio=FindObjectsByType<WalkieTalkieUse>().First();radio.GetComponent<NetworkPickup>().Claim(client);client.Inventory.ApplySelection(radio.GetComponent<NetworkPickup>().Location.Value.slot);client.Selection.Value=client.Inventory.CurrentSlot;radio.PrimaryUse();yield return new WaitForSeconds(.4f);
                Check(radio.Powered.Value&&radio.powerOnSounds.Length>0&&radio.powerOffSounds.Length>0,"radio toggles with assigned ON/OFF cues");
                Check(client.Inventory.Current.transform.parent==client.Inventory.rightHandAnchor,"slot switch keeps radio on right hand");
                var e0=EnemyController.Enemies[0];var sequence=e0.GetComponent<EnemyKillSequence>();Place(client,start);e0.GetComponent<NavMeshAgent>().Warp(start+Vector3.forward*1.1f);
                float began=Time.time;Check(sequence.TryBegin(client),"server starts kill on alive player");Check(!sequence.TryBegin(host),"busy enemy rejects second victim");yield return new WaitForSeconds(.4f);
                Check(client.IsGrabbed&&!client.CanMove&&Remote(client.OwnerClientId)?.grabbed==true,"victim lock and kill state replicate");
                Check(Remote(client.OwnerClientId)!=null&&Remote(client.OwnerClientId).killCameraError>=0&&Remote(client.OwnerClientId).killCameraError<.02f,"victim camera follows animated enemy head");
                Check(round.Phase.Value==RoundPhase.Playing&&client.Alive,"kill presentation precedes downing and end UI");
                if(EnemyController.Enemies.Count>1)Check(!EnemyController.Enemies[1].GetComponent<EnemyKillSequence>().Busy,"other enemy remains independent");
                yield return Until(()=>client.Life.Value==PlayerLife.Downed,10);Check(client.Life.Value==PlayerLife.Downed&&Time.time-began>=3.8f,"full four-second animation completes before downing");
                Check(e0.State.Value==EnemyState.Stagger,"post-kill stagger starts");Check(!EnemyTargetRules.CanTarget(client)&&!e0.GetComponent<EnemyPerception>().CanSee(client)&&!sequence.TryBegin(client),"downed player excluded from vision and attacks");
                yield return new WaitForSeconds(.4f);Check(animator.GetBool("IsDown")&&Remote(client.OwnerClientId)?.isDown==true,"downed animation synchronized");
                Send("drive",1);yield return new WaitForSeconds(.8f);Check(animator.GetFloat("Down")>.05f,"crawl speed drives Down blend");Send("stop");
                Check(client.Revive(),"revive succeeds");yield return new WaitForSeconds(.4f);Check(!animator.GetBool("IsDown"),"revive restores locomotion");
                client.Down();yield return new WaitForSeconds(2.1f);Check(!sequence.Busy,"stagger ends without hardcoded kill duration");
                Place(host,round.World.SpawnPosition);e0.GetComponent<NavMeshAgent>().Warp(host.transform.position+Vector3.forward*1.1f);Check(sequence.TryBegin(host),"final survivor grabbed");
                yield return Until(()=>host.Life.Value==PlayerLife.Downed,10);float completed=Time.time;Check(round.Phase.Value==RoundPhase.Playing,"end screen stays hidden after animation until delay");yield return new WaitForSeconds(3);Check(round.Phase.Value==RoundPhase.Playing,"end screen not shown early");yield return Until(()=>round.Phase.Value==RoundPhase.Lost,6);Check(round.Phase.Value==RoundPhase.Lost&&Time.time-completed>=4.8f,"five-second post-animation defeat delay");
                Check(string.IsNullOrEmpty(Read()?.error),"client has no runtime errors");GameSession.Instance.BackToLobby();yield return Until(()=>!RoundManager.Instance&&!GameSession.Instance.Transitioning);yield return Until(()=>Read()?.scene=="MainMenu");Check(Read()?.scene=="MainMenu"&&EnemyController.Enemies.Count==0,"both peers return to lobby with world cleanup");Check(LobbyRoster.Instance.Map.Value==map,"map selection persists between rounds");
            }
            results.Add("DONE "+results.Count(r=>r.StartsWith("PASS"))+" passed / "+results.Count(r=>r.StartsWith("FAIL"))+" failed");File.WriteAllLines(Path.Combine(DirectoryPath,"validation.txt"),results);Destroy(gameObject);
        }
    }
}
#endif
