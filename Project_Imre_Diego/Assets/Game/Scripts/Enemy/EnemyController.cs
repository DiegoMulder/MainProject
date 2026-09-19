using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SurvivalFP
{
    public enum EnemyState
    {
        Idle,
        Roam,
        Investigate,
        Chase,
        Search,
        Kill,
        Stagger
    }

    [RequireComponent(typeof(NavMeshAgent), typeof(EnemyPerception))]
    public sealed class EnemyController : NetworkBehaviour
    {
        [Header("Movement")]
        public float roamSpeed = 2f;
        public float chaseSpeed = 5.2f;
        public float killDistance = 1.1f;

        [Header("Perception and Search")]
        public float perceptionInterval = .15f;
        public float lostSightDelay = 1.2f;
        public float investigationDuration = 5f;
        public float searchDuration = 9f;
        public float idleDuration = 2f;

        [Min(1)]
        public float memoryDuration = 10f;

        [Header("Animation")]
        public Animator animator;

        private NetworkPlayer knownPlayer;
        private ClosetHideout knownCloset;
        private float knowledgeUntil;

        public static readonly System.Collections.Generic.List<EnemyController> Enemies = new();

        public NetworkVariable<EnemyState> State = new(EnemyState.Idle);

        private readonly NetworkVariable<float> networkBlend = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkPlayer Target { get; private set; }

        public Vector3 LastKnownPosition { get; private set; }

        public float LastSeen { get; private set; }

        public ClosetHideout KnownHidingSpot =>
            Time.time <= knowledgeUntil ? knownCloset : null;

        private NavMeshAgent agent;
        private EnemyPerception perception;

        private float nextSense;
        private float stateUntil;
        private float nextSearchPoint;

        private System.Random rng;

        private bool targetVisible;
        EnemyKillSequence killSequence;
        public void ClearPlayerMemory(){Target=null;knownPlayer=null;knownCloset=null;targetVisible=false;knowledgeUntil=0;LastKnownPosition=transform.position;}

        public override void OnNetworkSpawn()
        {
            Enemies.Add(this);
            killSequence=GetComponent<EnemyKillSequence>();

            agent = GetComponent<NavMeshAgent>();
            perception = GetComponent<EnemyPerception>();

            agent.enabled = IsServer;

            if (!IsServer)
                return;

            rng = new System.Random(
                RoundManager.Instance.Seed.Value ^
                139 ^
                unchecked((int)NetworkObjectId * 397)
            );

            GameplayNoiseSystem.Emitted += Hear;
            ClosetHideout.Entering += SeeEntry;

            Change(EnemyState.Idle, idleDuration);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                GameplayNoiseSystem.Emitted -= Hear;
                ClosetHideout.Entering -= SeeEntry;
            }

            Enemies.Remove(this);
        }

        private void SeeEntry(NetworkPlayer player, ClosetHideout closet)
        {
            if (!IsServer || (killSequence&&killSequence.Busy) || !perception.CanSee(player))
                return;

            knownPlayer = player;
            knownCloset = closet;
            knowledgeUntil = Time.time + memoryDuration;

            LastKnownPosition = closet.InvestigationPosition;

            Target = null;

            Change(EnemyState.Investigate, investigationDuration);
            Go(LastKnownPosition);
        }

        private void Hear(GameplayNoise noise)
        {
            if (!IsServer || (killSequence&&killSequence.Busy) ||
                !RoundManager.Instance ||
                RoundManager.Instance.Phase.Value != RoundPhase.Playing)
                return;

            if (noise.Source &&
                noise.Source.transform.IsChildOf(transform))
                return;

            var source = noise.Source
                ? noise.Source.GetComponentInParent<NetworkPlayer>()
                : null;

            if (source &&
                !source.Alive &&
                !(source.Life.Value == PlayerLife.Downed &&
                (noise.Category == NoiseCategory.RadioVoice ||
                 noise.Category == NoiseCategory.RadioReceiver)))
                return;

            if (Vector3.Distance(transform.position, noise.Position) >
                noise.Radius * perception.hearingMultiplier)
                return;

            if (State.Value == EnemyState.Chase)
                return;

            if (KnownHidingSpot &&
                knownPlayer &&
                knownPlayer.HiddenCloset == knownCloset &&
                source != knownPlayer)
                return;

            knownCloset = source
                ? source.HiddenCloset
                : null;

            LastKnownPosition = knownCloset
                ? knownCloset.InvestigationPosition
                : noise.Position;

            Change(EnemyState.Investigate, investigationDuration);

            Go(LastKnownPosition);

            if (EnemyTargetRules.CanTarget(source))
            {
                knownPlayer = source;
                knowledgeUntil = Time.time + memoryDuration;
            }
        }

        private void Update()
        {
            UpdateAnimator();

            if (!IsServer ||
                !IsSpawned ||
                !agent.enabled ||
                !agent.isOnNavMesh)
                return;

            if (!RoundManager.Instance ||
                RoundManager.Instance.Phase.Value != RoundPhase.Playing)
            {
                agent.isStopped = true;
                networkBlend.Value = 0f;
                return;
            }

            if(killSequence&&killSequence.Busy){agent.isStopped=true;agent.velocity=Vector3.zero;networkBlend.Value=0;return;}
            if(knownPlayer&&!EnemyTargetRules.CanTarget(knownPlayer)){ClearPlayerMemory();Change(EnemyState.Roam,12);Go(RoomDestination());}
            agent.isStopped = false;

            if (Target && !EnemyTargetRules.CanTarget(Target))
            {
                Target = null;
                knownPlayer = null;
                knownCloset = null;

                Change(EnemyState.Roam, 12f);
                Go(RoomDestination());
            }

            if (Time.time >= nextSense)
            {
                nextSense = Time.time + perceptionInterval;

                NetworkPlayer seen = perception.FindVisible();

                if (seen)
                {
                    targetVisible = true;

                    knownCloset = null;
                    Target = seen;
                    knownPlayer = seen;

                    knowledgeUntil = Time.time + memoryDuration;

                    LastKnownPosition = seen.transform.position;
                    LastSeen = Time.time;

                    if (State.Value != EnemyState.Chase)
                        Change(EnemyState.Chase, 0f);
                }
                else
                {
                    targetVisible = false;
                }

                foreach (var door in RoundManager.Instance.World.Doors)
                {
                    if (door &&
                        !(door is ExitDoor) &&
                        !door.Open.Value &&
                        Vector3.Distance(transform.position, door.transform.position) < 2.5f)
                    {
                        door.SetOpen(true, gameObject);
                    }
                }
            }

            agent.speed =
                State.Value == EnemyState.Chase
                    ? chaseSpeed
                    : roamSpeed;

            switch (State.Value)
            {
                case EnemyState.Idle:
                    {
                        if (Time.time >= stateUntil)
                        {
                            Change(EnemyState.Roam, 12f);
                            Go(RoomDestination());
                        }

                        break;
                    }

                case EnemyState.Roam:
                    {
                        if (Arrived() || Time.time >= stateUntil)
                        {
                            Change(EnemyState.Idle, idleDuration);
                        }

                        break;
                    }

                case EnemyState.Investigate:
                    {
                        if (Time.time >= stateUntil)
                        {
                            Change(EnemyState.Search, searchDuration);
                            nextSearchPoint = 0f;
                        }

                        break;
                    }

                case EnemyState.Chase:
                    {
                        if (targetVisible && EnemyTargetRules.CanTarget(Target))
                        {
                            LastKnownPosition = Target.transform.position;

                            Go(LastKnownPosition);

                            if (Vector3.Distance(
                                    transform.position,
                                    Target.transform.position
                                ) <= killDistance &&
                                perception.HasLineOfSight(Target))
                            {
                                if(killSequence)killSequence.TryBegin(Target);
                                return;
                            }

                            break;
                        }

                        Target = null;

                        Go(LastKnownPosition);

                        if (Arrived())
                        {
                            Change(
                                EnemyState.Investigate,
                                investigationDuration
                            );
                        }

                        break;
                    }

                case EnemyState.Search:
                    {
                        if (Time.time >= stateUntil)
                        {
                            Change(EnemyState.Roam, 12f);
                            Go(RoomDestination());
                        }
                        else if (Time.time >= nextSearchPoint)
                        {
                            nextSearchPoint = Time.time + 2f;

                            Vector3 randomSearchPosition =
                                LastKnownPosition +
                                new Vector3(
                                    (float)rng.NextDouble() * 6f - 3f,
                                    0f,
                                    (float)rng.NextDouble() * 6f - 3f
                                );

                            Go(randomSearchPosition);
                        }

                        break;
                    }
            }

            TryCatchRememberedPlayer();
            UpdateNetworkBlend();
        }

        private void TryCatchRememberedPlayer()
        {
            if (!knownPlayer ||
                !EnemyTargetRules.CanTarget(knownPlayer) ||
                !knownCloset ||
                Time.time > knowledgeUntil ||
                knownPlayer.HiddenCloset != knownCloset ||
                Vector3.Distance(
                    transform.position,
                    knownCloset.InvestigationPosition
                ) > killDistance + agent.radius)
                return;

            Vector3 delta =
                knownCloset.entryPoint.position +
                Vector3.up -
                perception.Eye;

            foreach (var hit in Physics.RaycastAll(
                         perception.Eye,
                         delta.normalized,
                         delta.magnitude,
                         perception.obstacles,
                         QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform) &&
                    !hit.transform.IsChildOf(knownPlayer.transform) &&
                    !hit.transform.IsChildOf(knownCloset.transform))
                    return;
            }

            if (knownCloset.TryExit(knownPlayer) && killSequence && killSequence.TryBegin(knownPlayer))
            {
                knownPlayer = null;
                knownCloset = null;
                Target = null;

            }
        }

        private void UpdateNetworkBlend()
        {
            if (!IsServer)
                return;

            float speed = agent.velocity.magnitude;

            if (Mathf.Abs(networkBlend.Value - speed) > 0.05f)
            {
                networkBlend.Value = speed;
            }
        }

        private void UpdateAnimator()
        {
            if (!animator)
                return;

            animator.SetFloat(
                "Blend",
                networkBlend.Value,
                0.15f,
                Time.deltaTime
            );
        }

        private void Change(EnemyState state, float duration)
        {
            State.Value = state;
            stateUntil = Time.time + duration;

            if (state == EnemyState.Idle &&
                agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
        }

        private bool Arrived()
        {
            return !agent.pathPending &&
                   agent.remainingDistance < 0.5f;
        }

        private void Go(Vector3 point)
        {
            if (NavMesh.SamplePosition(
                    point,
                    out NavMeshHit hit,
                    3f,
                    NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }

        private Vector3 RoomDestination()
        {
            var rooms = RoundManager.Instance.World.Rooms;

            return rooms[
                rng.Next(rooms.Count)
            ].NavigationPosition;
        }
    }
}