using System;
using UnityEngine;

namespace SurvivalFP
{
    [DisallowMultipleComponent]
    public sealed class PlayerStamina : MonoBehaviour
    {
        [Header("Capacity and recovery")]
        [SerializeField, Min(1f)] float maximum = 100f;
        [SerializeField, Min(0f)] float sprintDrain = 10f;
        [SerializeField, Min(0f)] float regeneration = 5f;
        [SerializeField, Min(0f)] float regenerationDelay = 1.5f;
        [Tooltip("Prevents repeated sprint stuttering after exhaustion.")]
        [SerializeField, Min(0f)] float sprintRestartThreshold = 15f;
        [Header("Jump")]
        [SerializeField] bool jumpConsumesStamina = true;
        [SerializeField, Min(0f)] float jumpCost = 12f;

        public float Current { get; private set; }
        public float Maximum => maximum;
        public float Normalized => Current / maximum;
        public bool Exhausted { get; private set; }
        public bool CanSprint => !Exhausted && Current > 0f;
        public bool CanJump => !jumpConsumesStamina || Current >= jumpCost;
        public event Action<float, float> Changed;
        float recoveryTimer;

        public StaminaSnapshot Capture()=>new StaminaSnapshot {current=Current,recovery=recoveryTimer,exhausted=Exhausted};
        public void Restore(StaminaSnapshot value){Current=value.current;recoveryTimer=value.recovery;Exhausted=value.exhausted;}
        void Awake() => Restore();
        public void Restore()
        {
            Current = maximum;
            Exhausted = false;
            recoveryTimer = 0f;
            Changed?.Invoke(Current, maximum);
        }

        public bool TrySpendJump()
        {
            if (!CanJump) return false;
            if (jumpConsumesStamina && jumpCost > 0f) Spend(jumpCost);
            return true;
        }

        public void Tick(bool sprinting, float dt)
        {
            if (sprinting) { Spend(sprintDrain * dt); return; }
            // Only the part of this frame after the delay contributes to regeneration.
            float recoveryDt = Mathf.Max(0f, dt - recoveryTimer);
            recoveryTimer = Mathf.Max(0f, recoveryTimer - dt);
            SetCurrent(Current + regeneration * recoveryDt);
            if (Current >= Mathf.Min(sprintRestartThreshold, maximum)) Exhausted = false;
        }

        void Spend(float amount)
        {
            recoveryTimer = regenerationDelay;
            SetCurrent(Current - amount);
            if (Current <= 0f) Exhausted = true;
        }

        void SetCurrent(float value)
        {
            float next = Mathf.Clamp(value, 0f, maximum);
            if (Mathf.Approximately(next, Current)) return;
            Current = next;
            Changed?.Invoke(Current, maximum);
        }
    }
}
