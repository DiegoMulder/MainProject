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
        MansionSettings runtimeSettings;
        public NetworkVariable<int> Difficulty=new(1),TargetRooms=new(0),RequiredObjectives=new(0);
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
            if(IsServer){runtimeSettings=Instantiate(settings);settings=runtimeSettings;Difficulty.Value=LobbyRoster.Instance?LobbyRoster.Instance.Difficulty.Value:1;
                if(settings.difficultyConfig)settings.difficultyConfig.Apply(settings,Difficulty.Value);TargetRooms.Value=settings.roomCount;RequiredObjectives.Value=settings.objectiveCount;}
            if(IsServer) StartCoroutine(Setup()); else if(Ready.Value) StartCoroutine(BuildClientWorld());
        }
        public override void OnNetworkDespawn() { Ready.OnValueChanged-=OnReady; if(Instance==this) Instance=null;if(runtimeSettings)Destroy(runtimeSettings); }
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
                if(!ExitPlacement.ValidatePhysical(settings.exit,exitSocket.transform,World.SpawnPosition,out var exitError))throw new InvalidOperationException(exitError);
                Exit=Instantiate(settings.exit,exitSocket.transform.position,exitSocket.transform.rotation);
                Exit.GetComponent<NetworkObject>().Spawn(); Exit.Required.Value=settings.objectiveCount;
                var rng=new System.Random(Seed.Value^719);
                var anchors=World.ItemAnchors.Where(a=>a.RoomIndex!=0).OrderBy(_=>rng.Next()).ToList();
                if(settings.preferDifferentRooms) anchors=anchors.GroupBy(a=>a.RoomIndex).SelectMany(g=>g.Take(1)).Concat(anchors).Distinct().ToList();
                if(anchors.Count<settings.objectiveCount+settings.medkitCount+settings.radioCount) throw new InvalidOperationException("Not enough reachable surfaces for configured objectives, medkits and radios. Add surface anchors or reduce item counts.");
                for(int i=0;i<settings.objectiveCount;i++)
                {
                    var item=Instantiate(settings.objective,anchors[i].transform.position,anchors[i].transform.rotation);
                    item.SafeAnchor=anchors[i].transform.position;
                    string kind=settings.objectiveTypes!=null && settings.objectiveTypes.Length>0?settings.objectiveTypes[i%settings.objectiveTypes.Length]:"Seal";
                    outstanding[kind]=outstanding.GetValueOrDefault(kind)+1;
                    item.GetComponent<NetworkObject>().Spawn();
                    item.GetComponent<ObjectiveItem>().Kind.Value=new FixedString64Bytes(kind);
                }
                if(settings.medkitCount>0 && !settings.medkit)throw new InvalidOperationException("Assign the medkit network prefab.");
                for(int i=0;i<settings.medkitCount;i++)
                {
                    var anchor=anchors[settings.objectiveCount+i];
                    var kit=Instantiate(settings.medkit,anchor.transform.position,anchor.transform.rotation);
                    kit.SafeAnchor=anchor.transform.position;kit.GetComponent<NetworkObject>().Spawn();
                }
                if(settings.radioCount>0 && !settings.walkieTalkie)throw new InvalidOperationException("Assign the Walkie Talkie network prefab.");
                for(int i=0;i<settings.radioCount;i++){var anchor=anchors[settings.objectiveCount+settings.medkitCount+i];var radio=Instantiate(settings.walkieTalkie,anchor.transform.position,anchor.transform.rotation);radio.SafeAnchor=anchor.transform.position;radio.GetComponent<NetworkObject>().Spawn();}
                Ready.Value=true; Phase.Value=RoundPhase.Playing;
                foreach(var id in NetworkManager.ConnectedClientsIds.ToArray()) SpawnPlayer(id);
                SpawnEnemies();
            }
            catch(Exception ex) { Failure.Value=new FixedString512Bytes(ex.Message.Length>400?ex.Message.Substring(0,400):ex.Message); Phase.Value=RoundPhase.Failed; Debug.LogException(ex); }
        }
        void SpawnEnemies()
        {
            if(settings.enemyCount==0)return;
            if(!settings.enemy)throw new InvalidOperationException("Assign the enemy network prefab.");
            var positions=new List<Vector3>();
            // Reserve every position before spawning any monster, so bad content fails cleanly.
            foreach(var room in World.Rooms.OrderByDescending(r=>(r.NavigationPosition-World.SpawnPosition).sqrMagnitude))
            {
                if(!NavMesh.SamplePosition(room.NavigationPosition,out var sample,1.2f,NavMesh.AllAreas))continue;
                if(NetworkPlayer.Players.Any(p=>Vector3.Distance(p.transform.position,sample.position)<8f))continue;
                if(positions.Any(p=>Vector3.Distance(p,sample.position)<3f))continue;
                var path=new NavMeshPath();
                if(!NavMesh.CalculatePath(World.SpawnPosition,sample.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
                positions.Add(sample.position);
                if(positions.Count==settings.enemyCount)break;
            }
            if(positions.Count<settings.enemyCount)throw new InvalidOperationException("Not enough separated, reachable enemy spawn positions. Add rooms or reduce Enemy Count.");
            foreach(var position in positions)Instantiate(settings.enemy,position,Quaternion.identity).GetComponent<NetworkObject>().Spawn();
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
