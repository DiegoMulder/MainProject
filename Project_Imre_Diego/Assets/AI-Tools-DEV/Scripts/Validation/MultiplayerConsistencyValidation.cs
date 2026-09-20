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
    public sealed class MultiplayerConsistencyValidation:MonoBehaviour
    {
        public static string DirectoryPath=>Path.GetFullPath("Assets/AI-Tools-DEV/.Runs/Consistency");
        readonly List<string> results=new();int command=2000,txNoise,rxNoise,powerNoise;PlayerCommand? hostInput;
        MansionDevelopmentProbe.Snapshot Read(){try{return JsonUtility.FromJson<MansionDevelopmentProbe.Snapshot>(File.ReadAllText(Path.Combine(DirectoryPath,"client.snapshot.json")));}catch{return null;}}
        MansionDevelopmentProbe.AnimationSnapshot Remote(NetworkPlayer p)=>Read()?.animations?.FirstOrDefault(a=>a.owner==p.OwnerClientId);
        void Check(bool value,string message){results.Add((value?"PASS ":"FAIL ")+message);File.WriteAllLines(Path.Combine(DirectoryPath,"validation.txt"),results);}
        void Update(){if(hostInput.HasValue&&NetworkPlayer.Local)NetworkPlayer.Local.SubmitMovement(hostInput.Value,NetworkPlayer.Local.View);}
        IEnumerator Until(Func<bool> condition,float seconds=20){float end=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<end)yield return null;}
        IEnumerator Send(string action,int slot=0){int id=++command;string json=JsonUtility.ToJson(new MansionDevelopmentProbe.Command{id=id,action=action,slot=slot});bool written=false;while(!written){try{File.WriteAllText(Path.Combine(DirectoryPath,"client.command.json"),json);written=true;}catch(IOException){}if(!written)yield return null;}yield return Until(()=>Read()?.command==id,5);}
        void Place(NetworkPlayer p,Vector3 position){p.Motor.Teleport(position);p.GetComponent<PlayerPrediction>().ForceState();}
        void Equip(NetworkPlayer p,PickupItem item){int slot=Array.IndexOf(p.Inventory.Slots,item);p.Inventory.ApplySelection(slot);p.Selection.Value=slot;}
        void Noise(GameplayNoise n){if(n.Category==NoiseCategory.RadioVoice)txNoise++;if(n.Category==NoiseCategory.RadioReceiver)rxNoise++;if(n.Category==NoiseCategory.Door)powerNoise++;}
        void OnDestroy()=>GameplayNoiseSystem.Emitted-=Noise;
        bool Attachment(NetworkPlayer p,bool hostOwner)
        {
            var item=p.Inventory.Current;var remote=Remote(p);if(!item||remote==null)return false;
            Vector3 thirdScale=hostOwner?Vector3.Scale(item.transform.localScale/item.firstPersonScale,item.rightHandScale):item.transform.localScale;
            return Vector3.Distance(hostOwner?remote.itemPosition:item.transform.localPosition,item.rightHandPosition)<.001f
                &&Quaternion.Angle(hostOwner?remote.itemRotation:item.transform.localRotation,Quaternion.Euler(item.rightHandEuler))<.1f
                &&Vector3.Distance(hostOwner?remote.itemScale:remote.itemScale/item.firstPersonScale,thirdScale)<.002f
                &&(hostOwner?remote.itemParent==p.Inventory.rightHandAnchor.name:item.transform.parent==p.Inventory.rightHandAnchor);
        }
        IEnumerator Speak(int count=8){for(int i=0;i<count;i++)yield return Send("radio",2);}
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);Directory.CreateDirectory(DirectoryPath);GameplayNoiseSystem.Emitted+=Noise;
            yield return Until(()=>NetworkManager.Singleton&&NetworkManager.Singleton.ConnectedClientsIds.Count==2,90);
            Check(NetworkManager.Singleton.ConnectedClientsIds.Count==2,"two Relay peers connect with protocol 3");
            if(NetworkManager.Singleton.ConnectedClientsIds.Count!=2)yield break;
            yield return new WaitForSeconds(1);Check(Read()?.configHash==NetworkManager.Singleton.NetworkConfig.GetConfig(false).ToString(),"network configuration hashes match after asset moves");
            foreach(int map in new[]{0,1})
            {
                LobbyRoster.Instance.SelectMap(map);LobbyRoster.Instance.SelectDifficulty(map==0?0:3);yield return Until(()=>Read()?.map==map);Check(Read()?.map==map,"map choice synchronized "+map);
                GameSession.Instance.StartMatch();yield return Until(()=>RoundManager.Instance&&RoundManager.Instance.Phase.Value!=RoundPhase.Generating,70);
                var round=RoundManager.Instance;if(!round||round.Phase.Value!=RoundPhase.Playing){Check(false,"round generation");yield break;}
                foreach(var e in EnemyController.Enemies){e.enabled=false;e.GetComponent<NavMeshAgent>().isStopped=true;}
                yield return Until(()=>Read()?.phase=="Playing"&&Read()?.rooms==round.Layout.Count,40);
                Check(Read()?.seed==round.Seed.Value&&Read()?.rooms==round.Layout.Count,"matching generated layout "+map);
                Check(Read()?.roomNames.All(n=>n.Contains("Slaughterhouse")== (map==1))==true,"only selected map rooms "+map);
                Check(round.Exit.name.Contains("Slaughterhouse")== (map==1),"selected map exit "+map);
                Check(Read()?.enemies==round.settings.enemyCount&&Read()?.requiredObjectives==round.settings.objectiveCount,"difficulty still configures map "+map);
                var host=NetworkPlayer.Local;var client=NetworkPlayer.Players.First(p=>!p.IsOwner);host.Controller.enabled=false;GameSession.Instance.GetComponent<ProximityVoice>().enabled=false;yield return Send("voicepause");yield return Send("stop");
                Vector3 start=round.World.SpawnPosition;Place(host,start+Vector3.left);Place(client,start+Vector3.right);yield return new WaitForSeconds(.7f);
                var hostTorch=host.Inventory.Current;var clientTorch=client.Inventory.Current;
                Check(Attachment(host,true),"host flashlight aligned on client, authored scale retained");Check(Attachment(client,false),"client flashlight aligned on host, authored scale retained");
                var radios=FindObjectsByType<WalkieTalkieUse>().Where(d=>!d.GetComponent<NetworkPickup>().Location.Value.Held).Take(2).ToArray();radios[0].GetComponent<NetworkPickup>().Claim(host);radios[1].GetComponent<NetworkPickup>().Claim(client);
                foreach(int i in Enumerable.Range(0,6)){Equip(host,i%2==0?radios[0].GetComponent<PickupItem>():hostTorch);Equip(client,i%2==0?radios[1].GetComponent<PickupItem>():clientTorch);yield return new WaitForSeconds(.25f);Check(Attachment(host,true)&&Attachment(client,false),"both item/slot local poses remain stable cycle "+i);}
                yield return Send("drive",2);yield return new WaitForSeconds(.45f);Check(Attachment(client,false),"client hand remains aligned while crouching");yield return Send("stop");yield return Send("jump");Check(Attachment(client,false),"client hand remains aligned during jump");yield return new WaitForSeconds(1.8f);
                hostInput=new PlayerCommand{Crouch=true};yield return new WaitForSeconds(.5f);Check(Attachment(host,true),"host hand remains aligned while crouching");hostInput=new PlayerCommand{JumpPressed=true};yield return null;hostInput=default(PlayerCommand);yield return new WaitForSeconds(.25f);Check(Attachment(host,true),"host hand remains aligned during jump");yield return new WaitForSeconds(1.8f);hostInput=null;
                host.GetComponent<PlayerAnimationDriver>().ReportTalking(true);yield return Send("talk",1);Check(Attachment(host,true)&&Attachment(client,false),"talking does not disturb either hand attachment");
                foreach(var p in new[]{host,client}){var item=p.Inventory.Current;var pickup=item.GetComponent<NetworkPickup>();pickup.Release(start+Vector3.up,Quaternion.identity,Vector3.zero);yield return new WaitForSeconds(.3f);pickup.Claim(p);Equip(p,item);yield return new WaitForSeconds(.4f);Check(Attachment(p,p==host),"drop/re-pick retains authored local grip and scale "+p.OwnerClientId);}
                for(int cycle=0;cycle<2;cycle++)foreach(var p in new[]{host,client})
                {
                    Place(p,start+(p==host?Vector3.left:Vector3.right));var a=p.GetComponent<PlayerAnimationDriver>().animator;var arm=a.GetComponentsInChildren<Transform>().First(t=>t.name=="Upper_Arm_L");var idleArm=arm.localRotation;var remoteIdleArm=Remote(p).armRotation;
                    p.Down();Check(p.Life.Value==PlayerLife.Downed,"down "+p.OwnerClientId+" cycle "+cycle);yield return new WaitForSeconds(.8f);
                    var remote=Remote(p);Check(a.enabled&&a.avatar&&a.runtimeAnimatorController&&remote?.animatorReady==true,"Animator/controller/avatar remain valid on both peers");
                    Check(a.GetBool("IsDown")&&remote?.isDown==true&&a.GetCurrentAnimatorClipInfo(0).Any(c=>c.clip.name=="CrawlingIdle"&&c.weight>.9f)&&remote.clips.Contains("CrawlingIdle"),"actual downed idle clip evaluates on both peers");
                    Check(Quaternion.Angle(idleArm,arm.localRotation)>15&&Quaternion.Angle(remoteIdleArm,remote.armRotation)>15,"real shoulder pose changes on both peers, not a parameter-only check");
                    if(p==host)hostInput=new PlayerCommand{Move=Vector2.up};else yield return Send("drive",1);
                    yield return new WaitForSeconds(.8f);Check(a.GetFloat("Down")>.05f&&Remote(p)?.down>.05f,"crawl speed blends on both peers");
                    Check(a.GetCurrentAnimatorClipInfo(0).Any(c=>c.clip.name=="Crawling"&&c.weight>.05f)&&Remote(p).clips.Contains("Crawling:"),"actual moving crawl clip evaluated");
                    if(p==host)hostInput=default(PlayerCommand);else yield return Send("stop");
                    Check(p.Revive(),"revive "+p.OwnerClientId);yield return new WaitForSeconds(.5f);Check(!a.GetBool("IsDown")&&Remote(p)?.isDown==false,"revive restores normal animation on both peers");Check(Attachment(p,p==host),"down/revive preserves attachment");hostInput=null;
                }
                Equip(host,radios[0].GetComponent<PickupItem>());Equip(client,radios[1].GetComponent<PickupItem>());radios[0].PrimaryUse();yield return new WaitForSeconds(.3f);txNoise=rxNoise=0;
                yield return Speak(3);Check(!client.GetComponent<PlayerRadio>().Transmitting.Value&&txNoise==0&&rxNoise==0,"OFF radio rejects speech and emits no radio noise");
                var far=round.World.Rooms.OrderByDescending(r=>(r.NavigationPosition-start).sqrMagnitude).First().NavigationPosition;Place(host,far);radios[1].PrimaryUse();yield return new WaitForSeconds(.4f);txNoise=rxNoise=0;
                yield return Speak();Check(client.GetComponent<PlayerRadio>().Transmitting.Value&&Read()?.radioTransmitting==true,"powered radio transmits from speech without V");Check(PlayerRadio.ReceiveRadio(host,client),"powered distant receiver accepts active speech");Check(txNoise>0&&rxNoise>0&&txNoise<8&&rxNoise<8,"speaker and receiver noise are emitted with throttling");
                yield return Send("radio",0);yield return new WaitForSeconds(.6f);int tx=txNoise,rx=rxNoise;yield return new WaitForSeconds(.7f);Check(!client.GetComponent<PlayerRadio>().Transmitting.Value&&txNoise==tx&&rxNoise==rx,"silence stops transmission and radio noise");
                radios[0].PrimaryUse();yield return Speak(3);Check(!PlayerRadio.ReceiveRadio(host,client)&&rxNoise==rx,"OFF receiver rejects playback and receiver noise");
                radios[1].PrimaryUse();yield return new WaitForSeconds(.6f);Check(!client.GetComponent<PlayerRadio>().Transmitting.Value,"power OFF ends transmission");Check(powerNoise>0&&radios.All(d=>d.powerOnSounds.Length>0&&d.powerOffSounds.Length>0),"power clips and small gameplay noise retained");
                Check(string.IsNullOrEmpty(Read()?.error),"client has no runtime errors "+map);client.Kill();host.Kill();yield return new WaitForSeconds(.3f);GameSession.Instance.BackToLobby();yield return Until(()=>!RoundManager.Instance&&!GameSession.Instance.Transitioning);yield return Until(()=>Read()?.scene=="MainMenu");Check(Read()?.scene=="MainMenu"&&EnemyController.Enemies.Count==0,"lobby return cleans selected world "+map);
            }
            results.Add("DONE "+results.Count(x=>x.StartsWith("PASS"))+" passed / "+results.Count(x=>x.StartsWith("FAIL"))+" failed");File.WriteAllLines(Path.Combine(DirectoryPath,"validation.txt"),results);Destroy(gameObject);
        }
    }
}
#endif
