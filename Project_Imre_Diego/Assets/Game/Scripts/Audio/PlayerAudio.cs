using System;
using UnityEngine;

namespace SurvivalFP
{
    [DisallowMultipleComponent]
    public sealed class PlayerAudio : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerMovement movement;
        [SerializeField] AudioSource footsteps;
        [SerializeField] AudioSource body;
        [SerializeField] AudioClip[] footstepClips;
        [SerializeField] AudioClip jumpClip;
        [SerializeField] AudioClip landingClip;
        [Header("Stride distance (metres per footfall)")]
        [SerializeField, Min(0.1f)] float walkStride = 1.7f;
        [SerializeField, Min(0.1f)] float sprintStride = 2.15f;
        [SerializeField, Min(0.1f)] float crouchStride = 1.2f;
        [Header("Volume and variation")]
        [SerializeField, Range(0f, 1f)] float walkVolume = 0.45f;
        [SerializeField, Range(0f, 1f)] float sprintVolume = 0.65f;
        [SerializeField, Range(0f, 1f)] float crouchVolume = 0.18f;
        [SerializeField, Range(0f, 1f)] float jumpVolume = 0.3f;
        [SerializeField, Range(0f, 1f)] float landingVolume = 0.7f;
        [SerializeField, Range(0f, 0.2f)] float pitchVariation = 0.055f;
        public event Action<AudioClip> SoundPlayed;
        float distance;
        int previousStep = -1;

        void Awake() { if (!movement) movement = GetComponent<PlayerMovement>(); AudioVolumeBus.RouteSfx(footsteps);AudioVolumeBus.RouteSfx(body); }
        void OnEnable()
        {
            if (!movement) movement = GetComponent<PlayerMovement>();
            movement.Simulated += Tick;
            movement.Jumped += OnJump;
            movement.Landed += OnLanding;
        }
        void OnDisable()
        {
            if (!movement) return;
            movement.Simulated -= Tick;
            movement.Jumped -= OnJump;
            movement.Landed -= OnLanding;
            distance = 0f;
            if (footsteps) footsteps.Stop();
            if (body) body.Stop();
        }
        void Tick(float dt)
        {
            if (!movement.IsGrounded || movement.ActualSpeed < 0.15f) { distance = 0f; return; }
            bool sprinting = movement.State == MovementState.Sprinting;
            float stride = movement.IsCrouching ? crouchStride : sprinting ? sprintStride : walkStride;
            distance += movement.ActualSpeed * dt;
            if (distance < stride || footstepClips == null || footstepClips.Length == 0) return;
            distance %= stride;
            int index = UnityEngine.Random.Range(0, footstepClips.Length);
            if (index == previousStep && footstepClips.Length > 1) index = (index + 1) % footstepClips.Length;
            previousStep = index;
            Play(footsteps, footstepClips[index], movement.IsCrouching ? crouchVolume : sprinting ? sprintVolume : walkVolume);
        }
        void OnJump() { distance = 0f; Play(body, jumpClip, jumpVolume); }
        void OnLanding(float speed)
        {
            distance = 0f;
            Play(body, landingClip, landingVolume * Mathf.InverseLerp(2f, 12f, speed));
        }
        void Play(AudioSource source, AudioClip clip, float volume)
        {
            if (!source || !clip || volume <= 0f) return;
            source.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clip, volume);
            SoundPlayed?.Invoke(clip);
        }
    }
}
