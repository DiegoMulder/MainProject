using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP
{
    [DefaultExecutionOrder(200)]
    public sealed class EnemyKillSequence:NetworkBehaviour
    {
        public Animator animator;
        public Transform killCameraPoint;
        [Min(0)] public float postKillStaggerDuration=2;
        [Min(.1f)] public float maximumGrabDistance=2;
        public NetworkVariable<ulong> Victim=new(ulong.MaxValue);
        public bool Busy=>enemy&&(enemy.State.Value==EnemyState.Kill||enemy.State.Value==EnemyState.Stagger);
        EnemyController enemy;NavMeshAgent agent;double staggerUntil;bool entered;
        void Awake(){enemy=GetComponent<EnemyController>();agent=GetComponent<NavMeshAgent>();}
        public override void OnNetworkSpawn(){Victim.OnValueChanged+=Changed;if(Victim.Value!=ulong.MaxValue)PlayKill();}
        public override void OnNetworkDespawn(){Victim.OnValueChanged-=Changed;if(IsServer&&Victim.Value!=ulong.MaxValue)Release(false);}
        NetworkPlayer FindVictim()=>NetworkPlayer.Players.Find(p=>p&&p.NetworkObjectId==Victim.Value);
        void Changed(ulong old,ulong current){if(current!=ulong.MaxValue)PlayKill();else if(animator)animator.CrossFade("Base Layer.Blend Tree",.12f);}
        void PlayKill(){entered=false;if(animator){animator.ResetTrigger("Kill");animator.SetTrigger("Kill");}}
        public bool TryBegin(NetworkPlayer victim)
        {
            if(!IsServer||Busy||!EnemyTargetRules.CanTarget(victim)||!animator||!killCameraPoint||Vector3.Distance(transform.position,victim.transform.position)>maximumGrabDistance)return false;
            if(victim.HiddenCloset&&!victim.HiddenCloset.TryExit(victim))return false;
            agent.isStopped=true;agent.ResetPath();agent.velocity=Vector3.zero;agent.updateRotation=false;
            Vector3 direction=Vector3.ProjectOnPlane(victim.transform.position-transform.position,Vector3.up);if(direction.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(direction);
            enemy.ClearPlayerMemory();enemy.State.Value=EnemyState.Kill;victim.GrabbedBy.Value=NetworkObjectId;
            victim.Velocity.Value=Vector3.zero;Victim.Value=victim.NetworkObjectId;RoundManager.Instance.BeginKill();return true;
        }
        void Update()
        {
            if(!IsSpawned||!IsServer)return;
            if(enemy.State.Value==EnemyState.Kill)
            {
                var victim=FindVictim();if(!victim||!victim.IsSpawned||victim.Life.Value!=PlayerLife.Alive){Release(false);return;}
                var state=animator.GetCurrentAnimatorStateInfo(0);
                if(state.IsName("Kill")){entered=true;if(state.normalizedTime>=1&&!animator.IsInTransition(0))Release(true);}
                else if(entered)Release(true);
            }
            else if(enemy.State.Value==EnemyState.Stagger&&NetworkManager.ServerTime.Time>=staggerUntil)
            {agent.updateRotation=true;agent.isStopped=false;enemy.State.Value=EnemyState.Idle;}
        }
        void Release(bool completed)
        {
            var victim=FindVictim();
            if(victim){victim.GrabbedBy.Value=ulong.MaxValue;if(completed)victim.Down();}
            Victim.Value=ulong.MaxValue;enemy.ClearPlayerMemory();enemy.State.Value=EnemyState.Stagger;staggerUntil=NetworkManager.ServerTime.Time+postKillStaggerDuration;
            if(RoundManager.Instance)RoundManager.Instance.EndKill(completed);
        }
    }
}
