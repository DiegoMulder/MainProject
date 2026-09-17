using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    public struct StaminaSnapshot : INetworkSerializeByMemcpy
    { public float current,recovery;public bool exhausted; }
    public struct MotorSnapshot : INetworkSerializeByMemcpy
    {
        public Vector3 position,horizontal,slide,normal;
        public float vertical,height,heightVelocity,coyote,buffer,airLimit,actualSpeed;
        public MovementState state;
        public bool grounded,sliding,crouching,ceiling;
        public StaminaSnapshot stamina;
    }
    public struct MovementInput : INetworkSerializeByMemcpy
    {
        public int sequence,epoch;public Vector2 move;public float yaw,pitch;
        public bool sprint,crouch,jump;
        public PlayerCommand Command=>new PlayerCommand {Move=move,Sprint=sprint,Crouch=crouch,JumpPressed=jump};
    }
}
