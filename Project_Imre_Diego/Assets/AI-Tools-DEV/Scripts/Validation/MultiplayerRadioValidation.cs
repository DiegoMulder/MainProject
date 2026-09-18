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
    public sealed class MultiplayerRadioValidation : MonoBehaviour
    {
        public static string DirectoryPath=>Path.GetFullPath("Assets/AI-Tools-DEV/.Runs/TwoPeer");
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
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);Directory.CreateDirectory(DirectoryPath);GameplayNoiseSystem.Emitted+=Noise;
            yield return Until(()=>NetworkManager.Singleton.ConnectedClientsIds.Count==2,60);
            if(NetworkManager.Singleton.ConnectedClientsIds.Count!=2){Check(false,"second client connected");yield break;}
            Check(true,"two network peers connected");
            foreach(int difficulty in new[]{0,3})
            {
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
                var snapshot=Snapshot();
                Check(snapshot!=null && snapshot.rooms==round.Layout.Count && snapshot.requiredObjectives==round.RequiredObjectives.Value,"matching client layout size and objective requirement "+difficulty);
                Check(snapshot!=null && snapshot.localBodyHidden && snapshot.bodyVisibility.Contains(host.OwnerClientId+":On"),"client hides own body and sees host body");
                Check(client.visualBody.GetComponent<Renderer>().shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.On,"host sees client body");
                var devices=FindObjectsByType<WalkieTalkieUse>(FindObjectsSortMode.None);
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
                Check(Snapshot()?.scene=="MainMenu" && !LobbyRoster.Instance.Started.Value,"both peers return to editable lobby");
            }
            GameplayNoiseSystem.Emitted-=Noise;
            results.Add("DONE "+results.Count(r=>r.StartsWith("PASS"))+" passed / "+results.Count(r=>r.StartsWith("FAIL"))+" failed");File.WriteAllLines(Path.Combine(DirectoryPath,"validation.txt"),results);
            Destroy(gameObject);
        }
    }
}
#endif
