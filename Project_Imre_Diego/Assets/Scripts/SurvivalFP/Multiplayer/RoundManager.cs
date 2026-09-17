using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP
{
    public enum RoundPhase { Generating, Playing, Lost, Won, Failed }
    [RequireComponent(typeof(MansionWorld))]
    public sealed class RoundManager : NetworkBehaviour
    {
        public static RoundManager Instance {get;private set;}
        public MansionSettings settings;
        public NetworkVariable<int> Seed=new(0),ExitRoom=new(0),ExitConnector=new(0);
        public NetworkVariable<bool> Ready=new(false);
        public NetworkVariable<int> ExpectedRooms=new(0),ExpectedProps=new(0);
        public NetworkVariable<RoundPhase> Phase=new(RoundPhase.Generating);
        public NetworkVariable<FixedString512Bytes> Failure=new(default);
        public NetworkList<RoomPlacement> Layout;
        public NetworkList<ConnectionPlacement> Connections;
        public NetworkList<PropPlacement> Props;
        public MansionWorld World {get;private set;}
        public ExitDoor Exit {get;private set;}
        readonly Dictionary<string,int> outstanding=new();
        void Awake() { Layout=new(); Connections=new(); Props=new(); World=GetComponent<MansionWorld>(); }
        public override void OnNetworkSpawn()
        {
            Instance=this; Ready.OnValueChanged+=OnReady;
            if(IsServer) StartCoroutine(Setup()); else if(Ready.Value) StartCoroutine(BuildClientWorld());
        }
        public override void OnNetworkDespawn() { Ready.OnValueChanged-=OnReady; if(Instance==this) Instance=null; }
        void OnReady(bool old,bool ready) { if(ready && !World.Built && !IsServer) StartCoroutine(BuildClientWorld()); }
        IEnumerator BuildClientWorld()
        {
            // Ready can deserialize before the NetworkLists in the same update.
            // Wait until the complete manifest has arrived, then construct exactly once.
            yield return null;
            while(IsSpawned && Ready.Value && (ExpectedRooms.Value<4 || Layout.Count!=ExpectedRooms.Value || Connections.Count!=ExpectedRooms.Value-1 || Props.Count!=ExpectedProps.Value))yield return null;
            if(IsSpawned && Ready.Value && !World.Built)World.Build(this,false);
        }
        IEnumerator Setup()
        {
            yield return null;
            try
            {
                Seed.Value=settings.randomSeed?unchecked((int)DateTime.UtcNow.Ticks):settings.seed;
                var plan=new MansionLayoutPlanner(); plan.Generate(settings,Seed.Value);
                ExpectedRooms.Value=plan.Rooms.Count;ExpectedProps.Value=plan.Props.Count;
                foreach(var room in plan.Rooms) Layout.Add(room);
                foreach(var connection in plan.Connections) Connections.Add(connection);
                foreach(var prop in plan.Props) Props.Add(prop);
                ExitRoom.Value=plan.ExitRoom; ExitConnector.Value=plan.ExitConnector;
                World.Build(this,true);
                foreach(var connection in Connections)
                {
                    var socket=World.Connector(connection.a,connection.ac);
                    if(!socket.allowDoor) continue;
                    var door=Instantiate(settings.door,socket.transform.position,socket.transform.rotation);
                    door.GetComponent<NetworkObject>().Spawn(); World.Doors.Add(door);
                }
                var exitSocket=World.Connector(ExitRoom.Value,ExitConnector.Value);
                Exit=Instantiate(settings.exit,exitSocket.transform.position,exitSocket.transform.rotation);
                Exit.Required.Value=settings.objectiveCount; Exit.GetComponent<NetworkObject>().Spawn();
                var rng=new System.Random(Seed.Value^719);
                var anchors=World.ItemAnchors.Where(a=>a.RoomIndex!=0).OrderBy(_=>rng.Next()).ToList();
                if(settings.preferDifferentRooms) anchors=anchors.GroupBy(a=>a.RoomIndex).SelectMany(g=>g.Take(1)).Concat(anchors).Distinct().ToList();
                if(anchors.Count<settings.objectiveCount+settings.medkitCount) throw new InvalidOperationException("Not enough reachable surfaces for configured objectives and medkits. Add surface anchors or reduce item counts.");
                for(int i=0;i<settings.objectiveCount;i++)
                {
                    var item=Instantiate(settings.objective,anchors[i].transform.position,anchors[i].transform.rotation);
                    item.SafeAnchor=anchors[i].transform.position;
                    string kind=settings.objectiveTypes!=null && settings.objectiveTypes.Length>0?settings.objectiveTypes[i%settings.objectiveTypes.Length]:"Seal";
                    item.GetComponent<ObjectiveItem>().Kind.Value=new FixedString64Bytes(kind);
                    outstanding[kind]=outstanding.GetValueOrDefault(kind)+1;
                    item.GetComponent<NetworkObject>().Spawn();
                }
                if(settings.medkitCount>0 && !settings.medkit)throw new InvalidOperationException("Assign the medkit network prefab.");
                for(int i=0;i<settings.medkitCount;i++)
                {
                    var anchor=anchors[settings.objectiveCount+i];
                    var kit=Instantiate(settings.medkit,anchor.transform.position,anchor.transform.rotation);
                    kit.SafeAnchor=anchor.transform.position;kit.GetComponent<NetworkObject>().Spawn();
                }
                Ready.Value=true; Phase.Value=RoundPhase.Playing;
                foreach(var id in NetworkManager.ConnectedClientsIds.ToArray()) SpawnPlayer(id);
                var farthest=World.Rooms.OrderByDescending(r=>(r.transform.position-World.SpawnPosition).sqrMagnitude).First();
                if(!NavMesh.SamplePosition(farthest.NavigationPosition,out var spawn,1.2f,NavMesh.AllAreas)) throw new InvalidOperationException("No enemy spawn on generated navigation.");
                var enemy=Instantiate(settings.enemy,spawn.position,Quaternion.identity); enemy.GetComponent<NetworkObject>().Spawn();
            }
            catch(Exception ex) { Failure.Value=new FixedString512Bytes(ex.Message.Length>400?ex.Message.Substring(0,400):ex.Message); Phase.Value=RoundPhase.Failed; Debug.LogException(ex); }
        }
        public void SpawnPlayer(ulong id)
        {
            if(!IsServer || Phase.Value!=RoundPhase.Playing || !NetworkManager.ConnectedClients.ContainsKey(id) || NetworkManager.ConnectedClients[id].PlayerObject) return;
            int index=NetworkPlayer.Players.Count;
            Vector3 point=World.SpawnPosition+new Vector3((index%3-1)*1.2f,0,(index/3)*1.2f);
            var player=Instantiate(settings.player,point,Quaternion.identity);
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(id);
            var torch=Instantiate(settings.flashlight,point+Vector3.up,Quaternion.identity); torch.SafeAnchor=point;
            torch.GetComponent<NetworkObject>().Spawn(); torch.Claim(player);
        }
        public bool Needs(string kind)=>outstanding.TryGetValue(kind,out int remaining) && remaining>0;
        public void Deposit(string kind) { if(IsServer && Needs(kind)) outstanding[kind]--; }
        public void ReleaseItems(NetworkPlayer player)
        {
            if(!IsServer) return;
            foreach(var item in FindObjectsByType<NetworkPickup>())
            {
                if(!item.IsSpawned || !item.Location.Value.Held || item.Location.Value.carrier!=player.OwnerClientId) continue;
                if(item.GetComponent<ObjectiveItem>()) item.Release(World.RecoveryPosition(item,player.transform.position),Quaternion.identity,Vector3.zero);
                else item.GetComponent<NetworkObject>().Despawn();
            }
        }
        public void EvaluateRound()
        {
            if(!IsServer || Phase.Value!=RoundPhase.Playing) return;
            var participants=NetworkPlayer.Players.Where(p=>p && p.IsSpawned).ToList();
            if(participants.Any(p=>p.Alive)) return;
            Phase.Value=participants.Any(p=>p.Life.Value==PlayerLife.Escaped)?RoundPhase.Won:RoundPhase.Lost;
            // With no living reviver, waiting five minutes cannot change the result.
            foreach(var player in participants.Where(p=>p.Life.Value==PlayerLife.Downed)) player.Kill();
        }
    }
}
