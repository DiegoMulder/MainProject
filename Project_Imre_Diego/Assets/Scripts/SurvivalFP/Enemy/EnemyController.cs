using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP
{
    public enum EnemyState { Idle, Roam, Investigate, Chase, Search }
    [RequireComponent(typeof(NavMeshAgent),typeof(EnemyPerception))]
    public sealed class EnemyController : NetworkBehaviour
    {
        [Header("Movement")]
        public float roamSpeed=2f, chaseSpeed=5.2f, killDistance=1.1f;
        [Header("Perception and search")]
        public float perceptionInterval=.15f, lostSightDelay=1.2f, investigationDuration=5f, searchDuration=9f, idleDuration=2f;
        [Min(1)] public float memoryDuration=10;
        NetworkPlayer knownPlayer; ClosetHideout knownCloset; float knowledgeUntil;
        public static readonly System.Collections.Generic.List<EnemyController> Enemies=new();
        public NetworkVariable<EnemyState> State=new(EnemyState.Idle);
        public NetworkPlayer Target {get; private set;}
        public Vector3 LastKnownPosition {get; private set;}
        public float LastSeen {get; private set;}
        public ClosetHideout KnownHidingSpot=>Time.time<=knowledgeUntil?knownCloset:null;
        NavMeshAgent agent; EnemyPerception perception; float nextSense, stateUntil, nextSearchPoint; System.Random rng;
        public override void OnNetworkSpawn()
        {
            Enemies.Add(this);
            agent=GetComponent<NavMeshAgent>(); perception=GetComponent<EnemyPerception>(); agent.enabled=IsServer;
            if(!IsServer) return;
            rng=new System.Random(RoundManager.Instance.Seed.Value^139);
            GameplayNoiseSystem.Emitted+=Hear;ClosetHideout.Entering+=SeeEntry; Change(EnemyState.Idle,idleDuration);
        }
        public override void OnNetworkDespawn() { GameplayNoiseSystem.Emitted-=Hear;ClosetHideout.Entering-=SeeEntry;Enemies.Remove(this); }
        void SeeEntry(NetworkPlayer player,ClosetHideout closet)
        {
            if(!IsServer || !perception.CanSee(player))return;
            knownPlayer=player;knownCloset=closet;knowledgeUntil=Time.time+memoryDuration;
            LastKnownPosition=closet.InvestigationPosition;Target=null;
            Change(EnemyState.Investigate,investigationDuration);Go(LastKnownPosition);
        }
        void Hear(GameplayNoise noise)
        {
            if(!IsServer || !RoundManager.Instance || RoundManager.Instance.Phase.Value!=RoundPhase.Playing) return;
            if(noise.Source && noise.Source.transform.IsChildOf(transform))return;
            var source=noise.Source?noise.Source.GetComponentInParent<NetworkPlayer>():null;
            if(source && source.Life.Value!=PlayerLife.Alive && source.Life.Value!=PlayerLife.Downed)return;
            if(Vector3.Distance(transform.position,noise.Position)>noise.Radius*perception.hearingMultiplier) return;
            if(State.Value==EnemyState.Chase && source!=Target)return;
            // Do not replace fresh, identified hiding evidence with incidental ambient noise.
            if(KnownHidingSpot && knownPlayer && knownPlayer.HiddenCloset==knownCloset && source!=knownPlayer)return;
            knownCloset=source?source.HiddenCloset:null;
            LastKnownPosition=knownCloset?knownCloset.InvestigationPosition:noise.Position; Change(EnemyState.Investigate,investigationDuration); Go(LastKnownPosition);
            if(source && source.Alive){knownPlayer=source;knowledgeUntil=Time.time+memoryDuration;}
        }
        void Update()
        {
            if(!IsServer || !IsSpawned || !agent.enabled || !agent.isOnNavMesh) return;
            if(!RoundManager.Instance || RoundManager.Instance.Phase.Value!=RoundPhase.Playing) { agent.isStopped=true; return; }
            agent.isStopped=false;
            if(Time.time>=nextSense)
            {
                nextSense=Time.time+perceptionInterval;
                var seen=perception.FindVisible();
                if(seen) { knownCloset=null;Target=seen;knownPlayer=seen;knowledgeUntil=Time.time+memoryDuration; LastKnownPosition=seen.transform.position; LastSeen=Time.time; if(State.Value!=EnemyState.Chase) Change(EnemyState.Chase,0); }
                foreach(var door in RoundManager.Instance.World.Doors)
                    if(door && !(door is ExitDoor) && !door.Open.Value && Vector3.Distance(transform.position,door.transform.position)<2.5f) door.SetOpen(true,gameObject);
            }
            agent.speed=State.Value==EnemyState.Chase?chaseSpeed:roamSpeed;
            switch(State.Value)
            {
                case EnemyState.Idle:
                    if(Time.time>=stateUntil) { Change(EnemyState.Roam,12); Go(RoomDestination()); } break;
                case EnemyState.Roam:
                    if(Arrived() || Time.time>=stateUntil) Change(EnemyState.Idle,idleDuration); break;
                case EnemyState.Investigate:
                    if(Arrived() || Time.time>=stateUntil) { Change(EnemyState.Search,searchDuration); nextSearchPoint=0; } break;
                case EnemyState.Chase:
                    if(!Target || !Target.Alive || Time.time-LastSeen>lostSightDelay)
                    { Target=null; Change(EnemyState.Investigate,investigationDuration); Go(LastKnownPosition); break; }
                    Go(LastKnownPosition);
                    if(Vector3.Distance(transform.position,Target.transform.position)<=killDistance && perception.HasLineOfSight(Target))
                    { Target.Down(); Target=null;knownPlayer=null; Change(EnemyState.Search,searchDuration); }
                    break;
                case EnemyState.Search:
                    if(Time.time>=stateUntil) { Change(EnemyState.Roam,12); Go(RoomDestination()); }
                    else if(Time.time>=nextSearchPoint)
                    { nextSearchPoint=Time.time+2; Go(LastKnownPosition+new Vector3((float)rng.NextDouble()*6-3,0,(float)rng.NextDouble()*6-3)); }
                    break;
            }
            TryCatchRememberedPlayer();
        }
        void TryCatchRememberedPlayer()
        {
            if(!knownPlayer || !knownPlayer.Alive || !knownCloset || Time.time>knowledgeUntil ||
                knownPlayer.HiddenCloset!=knownCloset || Vector3.Distance(transform.position,knownCloset.InvestigationPosition)>killDistance+agent.radius)return;
            // Evidence bypasses the closet door only, never walls or another floor.
            Vector3 delta=knownCloset.entryPoint.position+Vector3.up-perception.Eye;
            foreach(var hit in Physics.RaycastAll(perception.Eye,delta.normalized,delta.magnitude,perception.obstacles,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(knownPlayer.transform) && !hit.transform.IsChildOf(knownCloset.transform))return;
            if(knownCloset.Catch(knownPlayer))
            {knownPlayer=null;knownCloset=null;Target=null;Change(EnemyState.Search,searchDuration);}
        }
        void Change(EnemyState state,float duration) { State.Value=state; stateUntil=Time.time+duration; if(state==EnemyState.Idle && agent.isOnNavMesh) agent.ResetPath(); }
        bool Arrived()=>!agent.pathPending && agent.remainingDistance<.5f;
        void Go(Vector3 point) { if(NavMesh.SamplePosition(point,out var hit,3,NavMesh.AllAreas)) agent.SetDestination(hit.position); }
        Vector3 RoomDestination() { var rooms=RoundManager.Instance.World.Rooms; return rooms[rng.Next(rooms.Count)].NavigationPosition; }
    }
}
