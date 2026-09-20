#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace SurvivalFP.Editor
{
    public sealed class SettingsRadioValidation : MonoBehaviour
    {
        public static string ReportPath="Assets/AI-Tools-DEV/Reports/SettingsRadioValidation.txt";
        readonly List<string> results=new();
        int impacts;
        void Check(bool pass,string name){results.Add((pass?"PASS ":"FAIL ")+name);Save();}
        void Save(){Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));File.WriteAllLines(ReportPath,results);}
        void Noise(GameplayNoise n){if(n.Category==NoiseCategory.Impact)impacts++;}
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);Application.runInBackground=true;
            GameplayNoiseSystem.Emitted+=Noise;
            if(!Unity.Netcode.NetworkManager.Singleton.IsListening)GameSession.Instance.Host();
            yield return new WaitForSeconds(.4f);
            var authored=AssetDatabase.LoadAssetAtPath<MansionSettings>("Assets/Game/Data/Maps/Mansion/MansionContentSet.asset");
            bool random=authored.randomSeed;int seed=authored.seed;
            for(int run=0;run<6;run++)
            {
                int difficulty=run%2==0?0:3;
                LobbyRoster.Instance.SelectDifficulty(difficulty);
                authored.randomSeed=false;authored.seed=101+run;
                GameSession.Instance.StartMatch();
                float until=Time.realtimeSinceStartup+45;
                while((!RoundManager.Instance||RoundManager.Instance.Phase.Value==RoundPhase.Generating)&&Time.realtimeSinceStartup<until)yield return null;
                var round=RoundManager.Instance;
                Check(round && round.Phase.Value==RoundPhase.Playing,"round "+run+" generated: "+(round?round.Failure.Value.ToString():"missing"));
                if(!round)break;
                if(round.Phase.Value==RoundPhase.Playing)
                {
                    foreach(var enemy in EnemyController.Enemies)enemy.enabled=false;
                    var profile=authored.difficultyConfig.Get(difficulty);
                    Check(round.Layout.Count==profile.targetRoomCount && round.Exit.Required.Value==profile.requiredObjectiveCount,"difficulty room/objective counts "+run);
                    LobbyRoster.Instance.SelectDifficulty(1);
                    Check(LobbyRoster.Instance.Difficulty.Value==difficulty && round.Difficulty.Value==difficulty,"difficulty frozen during round "+run);
                    Check(ExitPlacement.ValidatePhysical(round.settings.exit,round.World.Connector(round.ExitRoom.Value,round.ExitConnector.Value).transform,round.World.SpawnPosition,out var reason),"exit physical clearance and reachability "+run+" "+reason);
                    Check(FindObjectsByType<WalkieTalkieUse>().Length==authored.radioCount,"radio spawn count "+run);
                    if(run==0)yield return PlayerChecks(round);
                }
                round.Phase.Value=RoundPhase.Lost;GameSession.Instance.BackToLobby();
                until=Time.realtimeSinceStartup+20;
                while((RoundManager.Instance||GameSession.Instance.Transitioning)&&Time.realtimeSinceStartup<until)yield return null;
                Check(!RoundManager.Instance && LobbyRoster.Instance && !LobbyRoster.Instance.Started.Value,"return to editable lobby "+run);
            }
            authored.randomSeed=random;authored.seed=seed;
            GameplayNoiseSystem.Emitted-=Noise;
            results.Add("DONE "+results.Count(r=>r.StartsWith("PASS"))+" passed / "+results.Count(r=>r.StartsWith("FAIL"))+" failed");Save();
            GameSession.Instance.ReturnToMenu();Destroy(gameObject);
        }
        IEnumerator PlayerChecks(RoundManager round)
        {
            var p=NetworkPlayer.Local;p.Controller.enabled=false;
            float master=LocalSettings.Master,sfx=LocalSettings.Sfx,voice=LocalSettings.Voice,sensitivity=LocalSettings.Sensitivity,fov=LocalSettings.Fov,strength=LocalSettings.SmoothingStrength;
            bool smooth=LocalSettings.Smoothing,psx=LocalSettings.Psx,muted=LocalSettings.Muted;
            LocalSettings.Set(.7f,.4f,.3f,.15f,96,true,1,false,true);
            Check(p.CameraMotion.Sensitivity==.15f && p.View.GetComponent<Camera>().fieldOfView==96,"local sensitivity and FOV apply immediately");
            p.CameraMotion.Look(new Vector2(100,0),.016f);p.CameraMotion.TickEffects(.016f);
            Check(Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.y,p.View.eulerAngles.y))>1,"camera smooths visible look while body responds immediately");
            LocalSettings.Set(.7f,.4f,.3f,.15f,96,true,0,false,true);
            p.CameraMotion.Look(new Vector2(100,0),.016f);p.CameraMotion.TickEffects(.016f);
            Check(Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.y,p.View.eulerAngles.y))<.01f,"zero smoothing has raw response");
            LocalSettings.Load();Check(LocalSettings.Fov==96 && LocalSettings.Sfx==.4f && LocalSettings.Muted,"central preferences save and reload");
            Check(p.visualBody.GetComponentsInChildren<Renderer>().All(r=>r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),"local body hidden with shadows retained");
            var source=p.GetComponentInChildren<AudioSource>();var bus=source.GetComponents<AudioVolumeBus>().FirstOrDefault(b=>b.source==source);
            Check(bus && Mathf.Abs(source.volume-bus.authoredVolume*.4f)<.001f,"central SFX bus gain");
            LocalSettings.Set(master,sfx,voice,sensitivity,fov,smooth,strength,psx,muted);
            var item=p.Inventory.Current;var pickup=item.GetComponent<NetworkPickup>();
            for(int mode=0;mode<4;mode++)
            {
                if(!item.Held)pickup.Claim(p);
                p.Motor.Teleport(round.World.SpawnPosition);
                for(int i=0;i<12;i++)p.Motor.Tick(new PlayerCommand{Crouch=mode!=0},.02f);
                p.CameraMotion.TickEffects(.05f);
                p.View.rotation=Quaternion.Euler(75,0,0);
                GameObject wall=null;
                if(mode==2){wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="DEV drop obstacle";wall.transform.position=p.transform.position+new Vector3(0,1,.8f);wall.transform.localScale=new Vector3(3,2,.2f);}
                if(mode==3){var stairs=round.World.Rooms.First(r=>r.staircase);p.Motor.Teleport(stairs.transform.TransformPoint(new Vector3(0,1.2f,-2)));p.CameraMotion.TickEffects(.05f);}
                bool valid=SafeItemDrop.TryFind(item,p.transform,p.View,out var point,out var rotation);
                Check(valid,"safe drop candidate "+new[]{"standing","crouched looking down","beside wall","stairs"}[mode]);
                if(valid){pickup.Release(point,rotation,rotation*Vector3.forward+Vector3.up*.5f);yield return new WaitForSeconds(1.2f);Check(item.transform.position.y>-.1f,"drop stays above floor "+mode);}
                if(wall)Destroy(wall);
            }
            Check(impacts>0,"dropped items still emit collision GameplayNoise");
            var device=FindObjectsByType<WalkieTalkieUse>().First();
            device.GetComponent<NetworkPickup>().Claim(p);p.Inventory.ApplySelection(Array.IndexOf(p.Inventory.Slots,device.GetComponent<PickupItem>()));
            p.Inventory.UsePrimary();yield return null;
            Check(device.Powered.Value,"modular radio primary use powers on");
            var radio=p.GetComponent<PlayerRadio>();int voiceEvents=0;
            void Heard(GameplayNoise n){if(n.Category==NoiseCategory.RadioVoice)voiceEvents++;}
            GameplayNoiseSystem.Emitted+=Heard;
            for(int i=0;i<20;i++){radio.Report(true);yield return new WaitForSeconds(.05f);}
            Check(radio.Transmitting.Value && voiceEvents>0 && voiceEvents<=3,"server radio permission and throttled transmit noise");
            device.Powered.Value=false;yield return new WaitForSeconds(.2f);radio.Report(true);yield return null;
            Check(!radio.Transmitting.Value,"powered-off radio rejects transmission");
            GameplayNoiseSystem.Emitted-=Heard;
        }
    }
}
#endif
