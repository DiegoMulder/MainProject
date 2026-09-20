using System;
using UnityEngine;

namespace SurvivalFP
{
    [DisallowMultipleComponent]
    public sealed class PlayerCamera : MonoBehaviour
    {
        [Serializable]
        public struct BobProfile
        {
            [Min(0f)] public float frequency;
            [Min(0f)] public float amplitude;
            public BobProfile(float frequency, float amplitude) { this.frequency = frequency; this.amplitude = amplitude; }
        }

        [Header("References")]
        [SerializeField] PlayerMovement movement;
        [SerializeField] Transform yawRoot;
        [SerializeField] Camera view;
        [Header("Look")]
        [SerializeField, Min(0f)] float sensitivity = 0.1f;
        [SerializeField] bool smoothMouse = true;
        [SerializeField, Min(0.001f)] float mouseSmoothTime = 0.025f;
        [SerializeField] Vector2 pitchLimits = new Vector2(-85f, 85f);
        [Header("Eye height")]
        [SerializeField, Min(0f)] float eyeInset = 0.18f;
        [SerializeField, Min(0.001f)] float eyeSmoothTime = 0.04f;
        [Header("Head bob (frequency in radians / second)")]
        [SerializeField] BobProfile walkBob = new BobProfile(10f, 0.05f);
        [SerializeField] BobProfile sprintBob = new BobProfile(14f, 0.1f);
        [SerializeField] BobProfile crouchBob = new BobProfile(6f, 0.03f);
        [Tooltip("Overall comfort scale. Zero disables bob, sway and landing motion.")]
        [SerializeField, Range(0f, 1f)] float motionScale = 0.55f;
        [SerializeField, Range(0f, 1f)] float horizontalBobRatio = 0.35f;
        [SerializeField, Min(0f)] float bobBlendSpeed = 12f;
        [Header("FOV and sway")]
        [SerializeField, Range(40f, 110f)] float baseFov = 75f;
        [SerializeField, Range(0f, 20f)] float sprintFovIncrease = 6f;
        [SerializeField, Min(0f)] float fovBlendSpeed = 7f;
        [SerializeField, Range(0f, 3f)] float strafeRoll = 0.7f;
        [SerializeField, Range(0f, 1f)] float lookSway = 0.08f;
        [Header("Landing spring")]
        [SerializeField, Min(0f)] float landingVelocityScale = 0.012f;
        [SerializeField, Min(0f)] float maximumLandingDip = 0.1f;
        [SerializeField, Min(0f)] float landingSpring = 150f;
        [SerializeField, Min(0f)] float landingDamping = 22f;

        float pitch, yaw, targetPitch, targetYaw, pitchVelocity, yawVelocity;
        float eyeHeight, eyeVelocity, phase, bobFrequency, bobAmplitude, roll;
        float landingOffset, landingVelocity;
        Vector3 bobOffset;
        Vector2 latestLook;
        PlayerPrediction prediction;
        public float Sensitivity { get => sensitivity; set => sensitivity = Mathf.Clamp(value, .01f, .5f); }
        public float LandingOffset => landingOffset;
        public bool SmoothingEnabled=>smoothMouse && mouseSmoothTime>0;
        public float BaseFov=>baseFov;
        public float TargetYaw=>targetYaw;
        public void ApplySettings(){Sensitivity=LocalSettings.Sensitivity;baseFov=LocalSettings.Fov;smoothMouse=LocalSettings.Smoothing;mouseSmoothTime=LocalSettings.SmoothTime;if(view)view.fieldOfView=baseFov;}
        public Vector3 BobOffset => bobOffset;

        void Awake()
        {
            if (!movement) movement = GetComponentInParent<PlayerMovement>();
            prediction=GetComponentInParent<PlayerPrediction>();
            if (!yawRoot && movement) yawRoot = movement.transform;
            if (!view) view = GetComponent<Camera>();
            eyeHeight = movement.Height - eyeInset;
            yaw = targetYaw = yawRoot.eulerAngles.y;
            view.fieldOfView = baseFov;
        }
        void OnEnable() { if (movement) movement.Landed += OnLanded; LocalSettings.Changed+=ApplySettings;ApplySettings(); }
        void OnDisable() { if (movement) movement.Landed -= OnLanded;LocalSettings.Changed-=ApplySettings; }

        public void Look(Vector2 delta, float dt)
        {
            latestLook = delta * sensitivity;
            targetYaw += latestLook.x;
            targetPitch = Mathf.Clamp(targetPitch - latestLook.y, pitchLimits.x, pitchLimits.y);
            if (SmoothingEnabled)
            {
                yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, mouseSmoothTime, Mathf.Infinity, dt);
                pitch = Mathf.SmoothDampAngle(pitch, targetPitch, ref pitchVelocity, mouseSmoothTime, Mathf.Infinity, dt);
            }
            else { yaw = targetYaw; pitch = targetPitch; yawVelocity=pitchVelocity=0; }
            yawRoot.rotation = Quaternion.Euler(0f, targetYaw, 0f);
        }

        void LateUpdate() => TickEffects(Mathf.Min(Time.deltaTime, 0.05f));

        public void TickEffects(float dt)
        {
            if (dt <= 0f) return;
            BobProfile profile = walkBob;
            if (movement.State == MovementState.Sprinting) profile = sprintBob;
            else if (movement.IsCrouching) profile = crouchBob;
            float weight = movement.IsGrounded ? Mathf.Clamp01(movement.ActualSpeed / 2f) : 0f;
            float blend = 1f - Mathf.Exp(-bobBlendSpeed * dt);
            bobFrequency = Mathf.Lerp(bobFrequency, profile.frequency, blend);
            bobAmplitude = Mathf.Lerp(bobAmplitude, profile.amplitude * weight * motionScale, blend);
            phase = (phase + bobFrequency * dt * Mathf.Clamp(movement.SpeedFraction, 0.3f, 1.5f)) % (Mathf.PI * 4f);
            Vector3 targetBob = new Vector3(Mathf.Sin(phase * 0.5f) * horizontalBobRatio,
                Mathf.Sin(phase), 0f) * bobAmplitude;
            bobOffset = Vector3.Lerp(bobOffset, targetBob, blend);

            // Integrate a critically damped spring with small substeps, stable during slow frames.
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f));
            float step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                landingVelocity += (-landingSpring * landingOffset - landingDamping * landingVelocity) * step;
                landingOffset = Mathf.Clamp(landingOffset + landingVelocity * step, -maximumLandingDip, maximumLandingDip);
            }
            eyeHeight = Mathf.SmoothDamp(eyeHeight, movement.Height - eyeInset, ref eyeVelocity, eyeSmoothTime, Mathf.Infinity, dt);
            // The eye must stay inside the capsule even while crouching into a low opening.
            eyeHeight = Mathf.Min(eyeHeight, movement.Height - 0.1f);
            transform.localPosition = Vector3.up * (eyeHeight + landingOffset) + bobOffset;
            if(prediction)transform.localPosition+=transform.parent.InverseTransformVector(prediction.RenderOffset);
            float sideways = Vector3.Dot(movement.HorizontalVelocity, yawRoot.right) / 7f;
            float targetRoll = Mathf.Clamp(-sideways * strafeRoll - latestLook.x * lookSway, -1.5f, 1.5f) * motionScale;
            roll = Mathf.Lerp(roll, targetRoll, blend);
            transform.localRotation = Quaternion.Euler(pitch, Mathf.DeltaAngle(targetYaw,yaw), roll);
            float targetFov = baseFov + (movement.State == MovementState.Sprinting ? sprintFovIncrease : 0f);
            view.fieldOfView = Mathf.Lerp(view.fieldOfView, targetFov, 1f - Mathf.Exp(-fovBlendSpeed * dt));
        }

        void OnLanded(float speed)
        {
            float dip = Mathf.Min(maximumLandingDip, speed * landingVelocityScale) * motionScale;
            landingVelocity -= dip * Mathf.Sqrt(landingSpring) * 2f;
        }
    }
}
