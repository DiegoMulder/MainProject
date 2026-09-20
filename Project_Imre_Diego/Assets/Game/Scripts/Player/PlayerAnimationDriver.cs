using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    // Locomotion reuses existing replicated motor state; only discrete jump/speech state is added.
    [DefaultExecutionOrder(150)]
    public sealed class PlayerAnimationDriver:NetworkBehaviour
    {
        public Animator animator;
        [Min(.01f)] public float damping=.12f;
        public NetworkVariable<bool> Jumping=new(false),Talking=new(false);
        NetworkPlayer player;float lastSpeech,nextSpeech;bool localJump,reportedTalking;
        void Awake(){player=GetComponent<NetworkPlayer>();}
        public override void OnNetworkSpawn(){player.Motor.Jumped+=Jump;player.Motor.Landed+=Land;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;}
        public override void OnNetworkDespawn(){player.Motor.Jumped-=Jump;player.Motor.Landed-=Land;}
        void Jump(){localJump=true;if(IsServer)Jumping.Value=true;}
        void Land(float speed){localJump=false;if(IsServer)Jumping.Value=false;}
        public void ReportTalking(bool value){if(!IsOwner||(!value&&!reportedTalking)||(value&&Time.unscaledTime<nextSpeech))return;reportedTalking=value;nextSpeech=Time.unscaledTime+.1f;TalkRpc(value);}
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner)]
        void TalkRpc(bool value){if(!player.IsSpawned||player.IsGrabbed||(player.Life.Value!=PlayerLife.Alive&&player.Life.Value!=PlayerLife.Downed))return;if(value){lastSpeech=Time.unscaledTime;Talking.Value=true;}}
        void Update()
        {
            if(!IsSpawned||!animator)return;
            if(IsServer){if(Time.unscaledTime-lastSpeech>.25f||player.IsGrabbed||(!player.Alive&&player.Life.Value!=PlayerLife.Downed))Talking.Value=false;if(!player.Alive||player.IsGrabbed)Jumping.Value=false;}
            var motor=player.Motor;float speed=player.IsGrabbed?0:(IsOwner||IsServer?motor.ActualSpeed:Vector3.ProjectOnPlane(player.Velocity.Value,Vector3.up).magnitude);
            bool down=player.Life.Value==PlayerLife.Downed;
            bool crouch=!down&&(IsOwner||IsServer?motor.IsCrouching:player.Height.Value<motor.StandingHeight-.2f);
            animator.SetFloat("Blend",Mathf.Clamp01(speed/Mathf.Max(.01f,motor.AnimationRunSpeed)),damping,Time.deltaTime);
            animator.SetFloat("Crouch",crouch?Mathf.Clamp01(speed/Mathf.Max(.01f,motor.AnimationCrouchSpeed)):0,damping,Time.deltaTime);
            animator.SetFloat("Down",down?Mathf.Clamp01(speed/Mathf.Max(.01f,player.downedCrawlSpeed)):0,damping,Time.deltaTime);
            animator.SetBool("IsCrouching",crouch);animator.SetBool("IsDown",down);
            animator.SetBool("Jump",player.Alive&&!player.IsGrabbed&&(IsOwner?localJump:Jumping.Value));
            animator.SetBool("IsTalking",Talking.Value);
            if(animator.layerCount>1)animator.SetLayerWeight(1,Mathf.MoveTowards(animator.GetLayerWeight(1),Talking.Value?1:0,Time.deltaTime*10));
        }
    }
}
