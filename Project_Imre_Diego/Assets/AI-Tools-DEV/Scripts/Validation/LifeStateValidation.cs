#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP.Editor
{
    // Explicitly started in the editor; never part of a shipping player.
    public sealed class LifeStateValidation:MonoBehaviour
    {
        public static string DirectoryPath=>Path.GetFullPath("Assets/AI-Tools-DEV/.Runs/LifeState");
        readonly List<string> results=new();int command=100,noise,tx,rx;
        bool hostDrive;int hostMode;float hostSeconds;Vector3 previousHost;float maximumHostStep;
        void Update()
        {
            var p=NetworkPlayer.Local;if(!hostDrive||!p)return;
            p.CameraMotion.Look(new Vector2(160*Time.deltaTime/p.CameraMotion.Sensitivity,0),Time.deltaTime);
            p.SubmitMovement(new PlayerCommand{Move=Vector2.up,Sprint=hostMode==0,Crouch=hostMode==2},p.View);
            if(hostSeconds>0)maximumHostStep=Mathf.Max(maximumHostStep,Vector3.Distance(previousHost,p.transform.position));previousHost=p.transform.position;hostSeconds+=Time.deltaTime;
        }
        MansionDevelopmentProbe.Snapshot Read(){try{return JsonUtility.FromJson<MansionDevelopmentProbe.Snapshot>(File.ReadAllText(Path.Combine(DirectoryPath,"client.snapshot.json")));}catch{return null;}}
        MansionDevelopmentProbe.AnimationSnapshot Remote(NetworkPlayer p)=>Read()?.animations?.FirstOrDefault(a=>a.owner==p.OwnerClientId);
        void Check(bool value,string message){results.Add((value?"PASS ":"FAIL ")+message);Save();}
        void Save()=>File.WriteAllLines(Path.Combine(DirectoryPath,"validation.txt"),results);
        IEnumerator Until(Func<bool> condition,float seconds=20){float end=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<end)yield return null;}
        IEnumerator Send(string action,int slot=0)
        {
            int id=++command;string json=JsonUtility.ToJson(new MansionDevelopmentProbe.Command{id=id,action=action,slot=slot});bool written=false;
            while(!written){try{File.WriteAllText(Path.Combine(DirectoryPath,"client.command.json"),json);written=true;}catch(IOException){}if(!written)yield return null;}
            yield return Until(()=>Read()?.command==id,5);Check(Read()?.command==id,"client command "+action);
        }
        void Place(NetworkPlayer p,Vector3 position)=>p.Motor.Teleport(position);
        void Equip(NetworkPlayer p,PickupItem item){int slot=Array.IndexOf(p.Inventory.Slots,item);p.Inventory.ApplySelection(slot);p.Selection.Value=slot;}
        void Noise(GameplayNoise n){noise++;if(n.Category==NoiseCategory.RadioVoice)tx++;if(n.Category==NoiseCategory.RadioReceiver)rx++;}
        void OnDestroy()=>GameplayNoiseSystem.Emitted-=Noise;
        IEnumerator Speak(){for(int i=0;i<5;i++)yield return Send("radio",2);}
        IEnumerator Shot(string name){yield return Send("screenshot");yield return new WaitForSeconds(.4f);File.Copy(Path.Combine(DirectoryPath,"client-view.png"),Path.Combine(DirectoryPath,name+".png"),true);}
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);Directory.CreateDirectory(DirectoryPath);GameplayNoiseSystem.Emitted+=Noise;
            yield return Until(()=>NetworkManager.Singleton&&NetworkManager.Singleton.ConnectedClientsIds.Count==2,90);
            Check(NetworkManager.Singleton.ConnectedClientsIds.Count==2,"two Relay peers on protocol 4");
            if(NetworkManager.Singleton.ConnectedClientsIds.Count!=2)yield break;
            GameSession.Instance.StartMatch();yield return Until(()=>RoundManager.Instance&&RoundManager.Instance.Phase.Value==RoundPhase.Playing,90);
            var round=RoundManager.Instance;if(!round||round.Phase.Value!=RoundPhase.Playing){Check(false,"round generation");yield break;}
            foreach(var e in EnemyController.Enemies){e.enabled=false;e.GetComponent<NavMeshAgent>().isStopped=true;}
            yield return Until(()=>Read()?.phase=="Playing");
            Check(Read()?.configHash==NetworkManager.Singleton.NetworkConfig.GetConfig(false).ToString(),"matching network configuration");
            var host=NetworkPlayer.Local;var client=NetworkPlayer.Players.First(p=>!p.IsOwner);host.Controller.enabled=false;
            GameSession.Instance.GetComponent<ProximityVoice>().enabled=false;yield return Send("voicepause");yield return Send("stop");
            var start=round.World.SpawnPosition;Place(host,start+Vector3.left*3);Place(client,start);yield return new WaitForSeconds(.5f);
            Place(host,start+Vector3.forward*2);yield return Send("measure",120);yield return Send("drive",1);yield return new WaitForSeconds(2);yield return Send("stop");
            Check(Read().position.z<host.transform.position.z-.6f&&Vector3.Distance(Read().position,client.transform.position)<.2f,"client predicts solid teammate collision without passing through");
            Check(Read().maximumCorrection<.2f,"stationary teammate collision avoids large reconciliation");
            Place(host,start+Vector3.left*3);
            foreach(int fps in new[]{120,60,30})foreach(int smoothing in new[]{0,1})
            {
                yield return Send("stop");yield return Send("turn",0);Place(client,start);yield return new WaitForSeconds(.4f);
                yield return Send("smoothing",smoothing);yield return Send("measure",fps);yield return Send("drive",1);yield return new WaitForSeconds(1.1f);yield return Send("stop");
                var s=Read();results.Add($"MEASURE fps={fps} smoothing={smoothing} frames={s.movementFrames} rootZero={s.zeroMovementFrames} presentationZero={s.zeroPresentationFrames} maxRenderStep={s.maximumPresentationStep:F4} correction={s.maximumCorrection:F4}");
                Check(s.movementFrames>15&&s.zeroPresentationFrames<=2,"continuous client presentation at "+fps+" FPS, mouse smoothing "+smoothing);
                Check(s.maximumCorrection<.1f,"no large movement correction during straight walk");
            }
            // Sustained circles exercise changing yaw and both speed/posture modes without external input.
            Place(client,start);yield return Send("turn",160);yield return Send("measure",120);yield return Send("drive",1);
            hostDrive=true;for(int i=0;i<12;i++){yield return new WaitForSeconds(15);hostMode=i%3;yield return Send("drive",i%3);if(i%2==0)yield return Send("jump");}hostDrive=false;
            results.Add($"HOST sustainedSeconds={hostSeconds:F1} maximumFrameStep={maximumHostStep:F3}");
            yield return Send("stop");yield return Send("turn",0);var sustained=Read();results.Add($"SUSTAINED seconds={sustained.measuredSeconds:F1} frames={sustained.movementFrames} correction={sustained.maximumCorrection:F3} renderZero={sustained.zeroPresentationFrames}");
            Check(string.IsNullOrEmpty(sustained.error),"sustained movement has no client errors");
            Place(host,start+Vector3.left);Place(client,start+Vector3.right);yield return new WaitForSeconds(.5f);
            var torch=client.Inventory.Current;
            var radios=FindObjectsByType<WalkieTalkieUse>(FindObjectsSortMode.None).Where(d=>!d.GetComponent<NetworkPickup>().Location.Value.Held).Take(2).ToArray();
            Check(radios.Length==2,"two world radios available");if(radios.Length!=2)yield break;
            radios[0].GetComponent<NetworkPickup>().Claim(host);radios[1].GetComponent<NetworkPickup>().Claim(client);
            Equip(host,radios[0].GetComponent<PickupItem>());Equip(client,radios[1].GetComponent<PickupItem>());radios[0].PrimaryUse();radios[1].PrimaryUse();yield return new WaitForSeconds(.5f);
            tx=rx=0;yield return Speak();Check(client.GetComponent<PlayerRadio>().Transmitting.Value&&PlayerRadio.ReceiveRadio(host,client),"equipped ON client transmits to nearby host");Check(tx>0&&rx>0,"nearby transmitter and receiver emit radio noise");
            radios[0].PrimaryUse();yield return Speak();Check(!PlayerRadio.ReceiveRadio(host,client),"OFF receiver rejects radio audio");radios[0].PrimaryUse();
            radios[1].PrimaryUse();yield return Speak();Check(!client.GetComponent<PlayerRadio>().Transmitting.Value,"OFF equipped transmitter rejects speech");radios[1].PrimaryUse();
            Equip(client,torch);Check(!client.GetComponent<PlayerRadio>().Transmitting.Value,"unequipping immediately stops transmission");yield return Speak();Check(!client.GetComponent<PlayerRadio>().Transmitting.Value,"inventory-only radio cannot transmit");
            Equip(client,radios[1].GetComponent<PickupItem>());yield return new WaitForSeconds(.4f);yield return Speak();client.Down();
            Check(!client.GetComponent<PlayerRadio>().Transmitting.Value&&!client.CanUseItems,"down immediately stops radio and item use");
            yield return new WaitForSeconds(.8f);noise=0;foreach(NoiseCategory category in Enum.GetValues(typeof(NoiseCategory)))GameplayNoiseSystem.Emit(client.transform.position,100,category,client.gameObject);
            yield return Send("speech");yield return Speak();yield return Send("use");yield return Send("light");
            Check(noise==0,"downed speech, radio and all player noise categories suppressed");Check(radios[1].Powered.Value&&!client.GetComponent<PlayerRadio>().Transmitting.Value,"downed primary action cannot toggle or transmit");
            Check(!EnemyTargetRules.CanTarget(client)&&Remote(client)?.isDown==true,"downed body animates but is not targetable");
            Check(host.GetComponent<DownedVignette>().Opacity==0&&Remote(client)?.vignetteOpacity>.1f,"only downed client has vignette");yield return Shot("vignette-start");
            double now=NetworkManager.Singleton.ServerTime.Time;client.BleedOutStartedAt.Value=now-150;client.BleedOutAt.Value=now+150;yield return new WaitForSeconds(1.2f);
            float middle=Remote(client).vignetteOpacity;Check(middle>.35f&&middle<.6f,"vignette follows synchronized halfway timer");yield return Shot("vignette-half");
            now=NetworkManager.Singleton.ServerTime.Time;client.BleedOutStartedAt.Value=now-295;client.BleedOutAt.Value=now+15;yield return new WaitForSeconds(1.2f);Check(Remote(client).vignetteOpacity>middle+.2f,"vignette strengthens near bleed-out");yield return Shot("vignette-late");
            Check(client.Revive(),"client revival succeeds");yield return new WaitForSeconds(1.5f);Check(Remote(client).vignetteOpacity<.001f&&client.CanUseItems,"revive clears vignette and restores item eligibility");
            // Exercise real perception with a physical occluder and an otherwise clear sight mask.
            var enemy=EnemyController.Enemies[0];var agent=enemy.GetComponent<NavMeshAgent>();var perception=enemy.GetComponent<EnemyPerception>();
            Place(host,round.World.Rooms.OrderByDescending(r=>(r.NavigationPosition-start).sqrMagnitude).First().NavigationPosition);
            Place(client,start+Vector3.forward*2);agent.Warp(start+Vector3.back*2);enemy.transform.rotation=Quaternion.identity;enemy.roamSpeed=enemy.chaseSpeed=0;perception.obstacles=1<<2;perception.fieldOfView=360;enemy.enabled=true;
            yield return Until(()=>enemy.Target==client,3);Check(enemy.Target==client,"alive visible client acquired");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Temporary LOS validation wall";wall.layer=2;wall.transform.position=start+Vector3.up*2;wall.transform.localScale=new Vector3(10,5,.3f);Physics.SyncTransforms();
            yield return new WaitForSeconds(.7f);Check(enemy.Target==client&&enemy.State.Value==EnemyState.Chase,"lost sight retains target during two-second grace");
            wall.SetActive(false);yield return new WaitForSeconds(.35f);Check(enemy.Target==client&&Time.time-enemy.LastSeen<.3f,"reacquiring LOS refreshes pursuit");
            wall.SetActive(true);yield return new WaitForSeconds(2.4f);Check(!enemy.Target,"expired grace releases live target for last-known search");
            wall.SetActive(false);yield return Until(()=>enemy.Target==client,3);wall.SetActive(true);yield return new WaitForSeconds(.3f);client.Down();Check(!enemy.Target&&!enemy.KnownHidingSpot,"down during grace immediately clears target and hiding memory");
            noise=0;GameplayNoiseSystem.Emit(start,100,NoiseCategory.Impact,wall);Check(noise==1&&enemy.State.Value==EnemyState.Investigate,"environmental noise still attracts enemy while player is down");
            enemy.enabled=false;agent.isStopped=true;Destroy(wall);client.Revive();yield return new WaitForSeconds(.8f);
            host.Down();yield return new WaitForSeconds(1);Check(host.GetComponent<DownedVignette>().Opacity>.1f&&Remote(client).vignetteOpacity<.001f,"host down vignette stays local");Check(Remote(host).bodyVisible,"downed host body stays visible to client");host.Revive();yield return new WaitForSeconds(1.2f);
            client.Down();client.BleedOutAt.Value=NetworkManager.Singleton.ServerTime.Time-.1;yield return new WaitForSeconds(.7f);
            Check(client.Life.Value==PlayerLife.Dead&&client.IsSpawned,"bleed-out retains dead network player for spectator");
            Check(client.GetComponentsInChildren<Renderer>(true).All(r=>r.forceRenderingOff)&&!client.GetComponent<PlayerAnimationDriver>().animator.enabled,"dead client body and animator hidden on host");
            Check(Remote(client).vignetteOpacity==0&&!Remote(client).bodyVisible&&!Remote(client).itemVisible&&!Remote(client).animatorReady,"dead client presentation cleared on client");
            Check(!string.IsNullOrEmpty(Read()?.spectating),"dead client spectates living teammate");Check(string.IsNullOrEmpty(Read()?.error),"client has no runtime errors");
            results.Add("DONE "+results.Count(x=>x.StartsWith("PASS"))+" passed / "+results.Count(x=>x.StartsWith("FAIL"))+" failed");Save();Destroy(gameObject);
        }
    }
}
#endif
