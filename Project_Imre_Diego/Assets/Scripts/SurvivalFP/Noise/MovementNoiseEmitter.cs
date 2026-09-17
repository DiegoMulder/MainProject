using UnityEngine;
namespace SurvivalFP
{
    [RequireComponent(typeof(NetworkPlayer))]
    public sealed class MovementNoiseEmitter : MonoBehaviour
    {
        [Min(0)] public float crouchRadius=2f, walkRadius=7f, sprintRadius=14f;
        [Min(.1f)] public float stride=1.7f;
        NetworkPlayer player;
        float travelled;
        void Awake() => player=GetComponent<NetworkPlayer>();
        void Update()
        {
            if (!player.IsServer || !player.Alive || !player.Motor.IsGrounded) { travelled=0; return; }
            travelled += player.Motor.ActualSpeed * Time.deltaTime;
            if (travelled < stride) return;
            travelled %= stride;
            float radius=player.Motor.IsCrouching ? crouchRadius : player.Motor.State==MovementState.Sprinting ? sprintRadius : walkRadius;
            GameplayNoiseSystem.Emit(transform.position,radius,NoiseCategory.Footstep,gameObject);
        }
    }
}
