#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
namespace SurvivalFP.Editor
{
    public sealed class FeedbackTwoValidation : MonoBehaviour
    {
        public static string DirectoryPath=>Path.GetFullPath("Assets/AI-Tools-DEV/.Runs/Feedback2Relay");
        DifficultyConfig editedConfig; int savedEnemies;
        void OnDestroy(){if(editedConfig)editedConfig.profiles[3].enemyCount=savedEnemies;GameplayNoiseSystem.Emitted-=Noise;}
        readonly List<string> results=new();int command=100,transmitNoise,receiveNoise;
        MansionDevelopmentProbe.Snapshot Snapshot()
        {try{return JsonUtility.FromJson<MansionDevelopmentProbe.Snapshot>(File.ReadAllText(Path.Combine(DirectoryPath,"client.snapshot.json")));}catch{return null;}}
        void Check(bool pass,string name){results.Add((pass?"PASS ":"FAIL ")+name);File.WriteAllLines(Path.Combine(DirectoryPath,"validation.txt"),results);}
        string pendingCommand;
        void Send(string action,int slot=0){pendingCommand=JsonUtility.ToJson(new MansionDevelopmentProbe.Command{id=++command,action=action,slot=slot});Update();}
        void Update(){if(pendingCommand==null)return;try{File.WriteAllText(Path.Combine(DirectoryPath,"client.command.json"),pendingCommand);pendingCommand=null;}catch(IOException){}}
        void Noise(GameplayNoise n){if(n.Category==NoiseCategory.RadioVoice)transmitNoise++;if(n.Category==NoiseCategory.RadioReceiver)receiveNoise++;}
        IEnumerator Until(Func<bool> ready,float seconds=20){float end=Time.realtimeSinceStartup+seconds;while(!ready()&&Time.realtimeSinceStartup<end)yield return null;}
        IEnumerator Speak(int repeats=8){for(int i=0;i<repeats;i++){Send("radio",2);yield return new WaitForSeconds(.12f);}}
        IEnumerator DropChecks()
        {
            var origin=new Vector3(1000,10,1000);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=origin-Vector3.up*.5f;floor.transform.localScale=new Vector3(12,1,12);
            var player=new GameObject("Drop validation player");player.transform.position=origin;
            var capsule=player.AddComponent<CharacterController>();capsule.radius=.35f;capsule.height=2;capsule.center=Vector3.up;
            var inventory=player.AddComponent<PlayerInventory>();
            var view=new GameObject("View").transform;view.SetParent(player.transform);view.localPosition=Vector3.up*1.8f;
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Items/Flashlight.prefab");
            var item=Instantiate(source,origin+Vector3.up*3,Quaternion.identity).GetComponent<PickupItem>();item.SetHeld(view);
            foreach(bool crouch in new[]{false,true})foreach(float pitch in new[]{0f,-70f,70f})
            {
                capsule.height=crouch?1:2;capsule.center=Vector3.up*(capsule.height*.5f);view.localPosition=Vector3.up*(capsule.height-.18f);view.rotation=Quaternion.Euler(pitch,25,0);
                bool safe=SafeItemDrop.TryFind(item,player.transform,view,out var point,out var rotation);
                Check(safe,"safe camera-directed drop crouch="+crouch+" pitch="+pitch);
                if(safe){Check(Quaternion.Angle(rotation,view.rotation)<.01f&&Vector3.Dot(inventory.DropVelocity(view).normalized,view.forward)>.999f,"drop rotation and impulse preserve full camera direction");Check(point.y+item.DropCenterOffset(rotation).y-item.DropRadius>=origin.y,"item clears ground");}
            }
            capsule.height=2;capsule.center=Vector3.up;view.localPosition=Vector3.up*1.8f;view.rotation=Quaternion.identity;
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=origin+new Vector3(0,1.5f,.85f);wall.transform.localScale=new Vector3(6,3,.2f);Physics.SyncTransforms();
            bool found=SafeItemDrop.TryFind(item,player.transform,view,out var nearWall,out var ignored);
            Check(!found||nearWall.z+item.DropRadius<=wall.GetComponent<Collider>().bounds.min.z+.001f,"drop cannot cross a wall");
            wall.SetActive(false);
            wall.transform.position=origin+new Vector3(0,.4f,.8f);wall.transform.localScale=new Vector3(1,.8f,.7f);wall.SetActive(true);view.rotation=Quaternion.Euler(60,0,0);
            found=SafeItemDrop.TryFind(item,player.transform,view,out var furniture,out var furnitureRotation);
            Check(!found||!Physics.OverlapSphere(furniture+item.DropCenterOffset(furnitureRotation),item.DropRadius).Contains(wall.GetComponent<Collider>()),"drop clears furniture or safely retains item");
            wall.SetActive(false);wall.transform.position=origin+new Vector3(0,.15f,0);wall.transform.localScale=new Vector3(3,.3f,3);wall.SetActive(true);player.transform.position=origin+Vector3.up*.3f;view.rotation=Quaternion.Euler(70,0,0);
            found=SafeItemDrop.TryFind(item,player.transform,view,out var stair,out var stairRotation);Check(found&&stair.y+item.DropCenterOffset(stairRotation).y-item.DropRadius>=origin.y+.3f,"drop clears a stair tread");
            wall.SetActive(false);player.transform.position=origin;view.rotation=Quaternion.identity;
            bool impact=false;void Impact(GameplayNoise n){if(n.Category==NoiseCategory.Impact&&n.Source==item.gameObject)impact=true;}
            GameplayNoiseSystem.Emitted+=Impact;
            found=SafeItemDrop.TryFind(item,player.transform,view,out var release,out var releaseRotation);if(found)item.Drop(release,releaseRotation,inventory.DropVelocity(view));
            yield return new WaitForSeconds(2);
            GameplayNoiseSystem.Emitted-=Impact;
            Check(found&&!item.GetComponent<Rigidbody>().isKinematic&&impact,"released item uses physics and emits impact noise");
            Check(item.GetComponent<ImpactNoiseEmitter>().ChooseClip()>=0,"impact audio clip remains configured");
            Destroy(item.gameObject);Destroy(player);Destroy(wall);Destroy(floor);yield return null;
        }
        void EnemyChecks(NetworkPlayer host)
        {
            var enemies=EnemyController.Enemies.ToArray();
            Check(enemies.All(e=>Vector3.Distance(e.transform.position,RoundManager.Instance.World.SpawnPosition)>7),"monsters spawn away from players");
            Check(enemies.SelectMany((a,i)=>enemies.Skip(i+1).Select(b=>Vector3.Distance(a.transform.position,b.transform.position))).All(d=>d>=3),"monster spawns are separated");
            foreach(var e in enemies)e.State.Value=EnemyState.Idle;
            GameplayNoiseSystem.Emit(enemies[0].transform.position,10000,NoiseCategory.Door,null);
            Check(enemies.All(e=>e.State.Value==EnemyState.Investigate),"all monsters independently hear an in-range event");
            foreach(var e in enemies)e.State.Value=EnemyState.Idle;
            GameplayNoiseSystem.Emit(enemies[0].transform.position,.1f,NoiseCategory.Door,null);
            Check(enemies.Count(e=>e.State.Value==EnemyState.Investigate)==1,"only nearby monster hears a short-range event");
            var heartbeat=host.GetComponent<HeartbeatFeedback>();var position=enemies.Last().transform.position+Vector3.up*4;
            float nearest=enemies.Min(e=>Vector3.Distance(e.transform.position,position));
            float expected=1-Mathf.InverseLerp(Mathf.Min(heartbeat.intenseDistance,heartbeat.maximumDistance-.01f),heartbeat.maximumDistance,nearest);
            Check(Mathf.Abs(heartbeat.ProximityIntensity(position)-expected)<.001f,"heartbeat uses nearest monster");
        }
        IEnumerator DoorChecks(NetworkPlayer player,DoorInteractable prefab)
        {
            Vector3 saved=player.transform.position;
            foreach(float yaw in new[]{0f,90f,180f,270f})
            {
                var door=Instantiate(prefab,new Vector3(1100,10,1100),Quaternion.Euler(0,yaw,0));door.NetworkObject.Spawn();
                foreach(float side in new[]{1f,-1f})
                {
                    player.Motor.Teleport(door.transform.TransformPoint(new Vector3(0,0,side*1.5f)));Physics.SyncTransforms();
                    door.SetOpen(true,player.gameObject);yield return new WaitForSeconds(1f);
                    Check(door.Open.Value&&Mathf.Sign(door.SwingAngle.Value)==side,"door opens away yaw="+yaw+" side="+side);
                    yield return Until(()=>Snapshot()?.doors.Contains(door.NetworkObjectId+":True:"+door.SwingAngle.Value)==true,3);
                    Check(Snapshot()?.doors.Contains(door.NetworkObjectId+":True:"+door.SwingAngle.Value)==true,"door direction synchronized to client");
                    door.SetOpen(false,player.gameObject);yield return new WaitForSeconds(1f);
                    Check(Quaternion.Angle(door.hinge.localRotation,Quaternion.identity)<.1f,"door closes from either direction");
                }
                door.NetworkObject.Despawn();yield return null;
            }
            player.Motor.Teleport(saved);
        }
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);Directory.CreateDirectory(DirectoryPath);GameplayNoiseSystem.Emitted+=Noise;
            yield return Until(()=>NetworkManager.Singleton.ConnectedClientsIds.Count==2,60);
            if(NetworkManager.Singleton.ConnectedClientsIds.Count!=2){Check(false,"second client connected");yield break;}
            Check(true,"two network peers connected");
            Check(Snapshot()?.configHash==NetworkManager.Singleton.NetworkConfig.GetConfig(false).ToString(),"identical network configuration hashes over Relay");
            yield return DropChecks();int iteration=0;
            foreach(int difficulty in new[]{0,3,3})
            {
                iteration++;
                if(iteration==3){editedConfig=LobbyRoster.Instance.difficultyConfig;savedEnemies=editedConfig.profiles[3].enemyCount;editedConfig.profiles[3].enemyCount=3;}
                LobbyRoster.Instance.SelectDifficulty(difficulty);
                yield return Until(()=>Snapshot()?.difficulty==difficulty);
                Check(Snapshot()?.difficulty==difficulty,"client sees difficulty "+difficulty);
                Send("difficulty",1);yield return new WaitForSeconds(.5f);
                Check(LobbyRoster.Instance.Difficulty.Value==difficulty,"client cannot override host difficulty");
                GameSession.Instance.StartMatch();
                yield return Until(()=>RoundManager.Instance && RoundManager.Instance.Phase.Value!=RoundPhase.Generating,45);
                yield return Until(()=>Snapshot()?.phase=="Playing",30);
                var round=RoundManager.Instance;
                if(!round||round.Phase.Value!=RoundPhase.Playing){Check(false,"round generated");yield break;}
                foreach(var enemy in EnemyController.Enemies)enemy.enabled=false;
                var host=NetworkPlayer.Local;host.Controller.enabled=false;Send("pause");
                var client=NetworkPlayer.Players.First(p=>!p.IsOwner);
                GameSession.Instance.GetComponent<ProximityVoice>().enabled=false;Send("voicepause");yield return new WaitForSeconds(.3f);
                yield return Until(()=>Snapshot()?.enemies==round.settings.enemyCount);
                Check(EnemyController.Enemies.Count==round.settings.enemyCount&&Snapshot()?.enemies==round.settings.enemyCount,"server and client enemy count "+round.settings.enemyCount);
                var placements=new List<string>();foreach(var r in round.Layout)placements.Add($"{r.module}:{r.quarter}:{r.position.x:F2},{r.position.y:F2},{r.position.z:F2}");string layout=string.Join(";",placements);
                Check(Snapshot()?.layout==layout,"identical complete mansion layout");
                Check(FindObjectsByType<NetworkManager>().Length==1&&FindObjectsByType<GameSession>().Length==1,"single persistent networking foundation");
                EnemyChecks(host);
                yield return DoorChecks(host,round.settings.door);
                var snapshot=Snapshot();
                Check(snapshot!=null && snapshot.rooms==round.Layout.Count && snapshot.requiredObjectives==round.RequiredObjectives.Value,"matching client layout size and objective requirement "+difficulty);
                Check(snapshot!=null && snapshot.localBodyHidden && snapshot.bodyVisibility.Contains(host.OwnerClientId+":On"),"client hides own body and sees host body");
                Check(client.visualBody.GetComponent<Renderer>().shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.On,"host sees client body");
                var devices=FindObjectsByType<WalkieTalkieUse>();
                devices[0].GetComponent<NetworkPickup>().Claim(host);devices[1].GetComponent<NetworkPickup>().Claim(client);
                devices[0].Powered.Value=devices[1].Powered.Value=true;
                yield return Until(()=>Snapshot()?.radioOn==true);
                Check(Snapshot()?.radioOn==true,"radio ownership and power replicate");
                host.Motor.Teleport(round.World.SpawnPosition);client.Motor.Teleport(round.World.SpawnPosition+Vector3.right*2);client.GetComponent<PlayerPrediction>().ForceState();
                yield return Speak();
                Check(!PlayerRadio.ReceiveRadio(host,client),"nearby players use proximity without duplicate radio audio");
                var far=round.World.Rooms.OrderByDescending(r=>(r.NavigationPosition-host.transform.position).sqrMagnitude).First();
                client.Motor.Teleport(far.NavigationPosition+Vector3.up*.1f);client.GetComponent<PlayerPrediction>().ForceState();
                int tx=transmitNoise,rx=receiveNoise;
                yield return Speak(12);
                Check(PlayerRadio.ReceiveRadio(host,client),"active distant radio eligible for reception");
                Check(transmitNoise>tx && receiveNoise>rx && transmitNoise-tx<=4,"transmit and receiver noise emitted at throttled intervals");
                devices[1].Powered.Value=false;yield return Speak(4);
                Check(!client.GetComponent<PlayerRadio>().Transmitting.Value && !PlayerRadio.ReceiveRadio(host,client),"OFF radio rejects remote transmission");
                devices[1].Powered.Value=true;client.Down();yield return Speak(8);
                Check(client.Life.Value==PlayerLife.Downed && client.GetComponent<PlayerRadio>().Transmitting.Value,"downed player retains radio exception");
                Check(client.visualBody.GetComponent<Renderer>().shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.On && Mathf.Abs(Mathf.DeltaAngle(client.visualBody.localEulerAngles.x,90))<1,"remote downed body remains visible and prone");
                client.Kill();yield return Speak(4);yield return new WaitForSeconds(.3f);
                Check(!client.GetComponent<PlayerRadio>().Transmitting.Value && !PlayerRadio.ReceiveRadio(host,client),"dead spectator cannot transmit");
                Check(Snapshot()?.spectating==host.DisplayName,"client spectates surviving host");
                Check(string.IsNullOrEmpty(Snapshot()?.error),"client reports no errors");
                round.Phase.Value=RoundPhase.Lost;GameSession.Instance.BackToLobby();
                yield return Until(()=>!RoundManager.Instance && !GameSession.Instance.Transitioning);
                yield return Until(()=>Snapshot()?.scene=="MainMenu");
                Check(EnemyController.Enemies.Count==0,"all monsters cleaned up");
                Check(Snapshot()?.scene=="MainMenu" && !LobbyRoster.Instance.Started.Value,"both peers return to editable lobby");
            }
            GameplayNoiseSystem.Emitted-=Noise;
            results.Add("DONE "+results.Count(r=>r.StartsWith("PASS"))+" passed / "+results.Count(r=>r.StartsWith("FAIL"))+" failed");File.WriteAllLines(Path.Combine(DirectoryPath,"validation.txt"),results);
            Destroy(gameObject);
        }
    }
}
#endif
