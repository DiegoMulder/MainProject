using System;
using UnityEngine;

namespace SurvivalFP
{
    // A player's body sounds: footsteps (walk / sprint / crouch sets), jump, soft or hard landing, crouching down and
    // standing up, cloth rustle and, for your own character, breathing after a sprint.
    // It runs on every client: your own character from its motor (instant), everyone else from their replicated
    // movement, so teammates are heard walking past. Clips live in the Sound Library (PLAYER AUDIO); a Sound Event
    // assigned here overrides the library for this prefab. Enemy hearing of steps stays in MovementNoiseEmitter;
    // landings are reported here, once, on the server.
    [DisallowMultipleComponent]
    public sealed class PlayerAudio : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerMovement movement;
        [Tooltip("Legacy fallback source, used only when no Sound Library exists.")]
        [SerializeField] AudioSource footsteps;
        [SerializeField] AudioSource body;
        [Tooltip("Legacy fallback clips, used only when no Sound Library exists.")]
        [SerializeField] AudioClip[] footstepClips;
        [SerializeField] AudioClip jumpClip;
        [SerializeField] AudioClip landingClip;
        [Header("Overrides (empty = Sound Library)")]
        [SerializeField] SoundEvent walkSteps;
        [SerializeField] SoundEvent sprintSteps, crouchSteps, jump, landSoft, landHard, crouchDown, standUp, clothing, breathing;
        [Header("Stride distance (metres per footfall, matched to the walk and run cycles)")]
        [SerializeField, Min(0.1f)] float walkStride = 1.7f;
        [SerializeField, Min(0.1f)] float sprintStride = 2.15f;
        [SerializeField, Min(0.1f)] float crouchStride = 1.2f;
        [Header("Legacy fallback volumes")]
        [SerializeField, Range(0f, 1f)] float walkVolume = 0.45f;
        [SerializeField, Range(0f, 1f)] float sprintVolume = 0.65f;
        [SerializeField, Range(0f, 1f)] float crouchVolume = 0.18f;
        [SerializeField, Range(0f, 1f)] float jumpVolume = 0.3f;
        [SerializeField, Range(0f, 1f)] float landingVolume = 0.7f;
        [SerializeField, Range(0f, 0.2f)] float pitchVariation = 0.055f;
        [Header("Breathing (own character only)")]
        [SerializeField, Min(.3f)] float breathInterval = 1.5f;
        [SerializeField, Range(0f, 1f)] float breathBelowStamina = .45f;
        public event Action<AudioClip> SoundPlayed;
        float distance, airborneFall, nextBreath, lastStep;
        int previousStep = -1;
        bool wasGrounded = true, wasCrouching, subscribed;
        NetworkPlayer player; PlayerStamina stamina;

        bool Local => !player || !player.IsSpawned || player.IsOwner;
        bool Server => player && player.IsSpawned && player.IsServer;
        static SoundLibrary Lib => SoundLibrary.Instance;

        void Awake()
        {
            if (!movement) movement = GetComponent<PlayerMovement>();
            player = GetComponent<NetworkPlayer>(); stamina = GetComponent<PlayerStamina>();
            AudioVolumeBus.RouteSfx(footsteps); AudioVolumeBus.RouteSfx(body);
        }
        void OnEnable()
        {
            if (!movement) movement = GetComponent<PlayerMovement>();
            wasGrounded = true; airborneFall = 0; distance = 0;
            wasCrouching = movement && movement.IsCrouching;
            if (!movement || subscribed) return;
            movement.Simulated += Tick; movement.Jumped += OnJump; movement.Landed += OnLanding; subscribed = true;
        }
        void OnDisable()
        {
            if (movement && subscribed) { movement.Simulated -= Tick; movement.Jumped -= OnJump; movement.Landed -= OnLanding; }
            subscribed = false; distance = 0f;
            if (footsteps) footsteps.Stop();
            if (body) body.Stop();
        }

        // Own character: driven by the motor's simulation step.
        void Tick(float dt)
        {
            if (!Local) return;
            Step(movement.IsGrounded, movement.ActualSpeed, movement.State, movement.IsCrouching, dt);
            Breathe();
        }
        void OnJump() { if (Local) Jump(); }
        void OnLanding(float speed) { if (Local) Land(speed); }

        // Everyone else: driven by what the server replicates (Velocity, Gait, Grounded).
        void Update()
        {
            if (Local) return;
            var gait = (MovementState)player.Gait.Value;
            var velocity = player.Velocity.Value;
            bool grounded = player.Grounded.Value;
            if (!grounded) airborneFall = Mathf.Max(airborneFall, -velocity.y);
            if (grounded != wasGrounded)
            {
                if (!grounded && velocity.y > 1f) Jump();
                else if (grounded) Land(airborneFall);
                wasGrounded = grounded;
                if (grounded) airborneFall = 0;
            }
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            Step(grounded, speed, gait, gait == MovementState.Crouching, Time.deltaTime);
        }

        void Step(bool grounded, float speed, MovementState state, bool crouching, float dt)
        {
            if (crouching != wasCrouching)
            {
                wasCrouching = crouching;
                var transition = crouching ? SoundLibrary.Pick(crouchDown, l => l.crouchDown) : SoundLibrary.Pick(standUp, l => l.standUp);
                Play(transition, 1f);
            }
            if (!grounded || speed < 0.15f) { distance = 0f; return; }
            bool sprinting = state == MovementState.Sprinting;
            float stride = crouching ? crouchStride : sprinting ? sprintStride : walkStride;
            // The first step after standing still lands quickly, like a real first footfall.
            if (distance == 0f && Time.time - lastStep > .6f) distance = stride * .6f;
            distance += speed * dt;
            if (distance < stride) return;
            distance %= stride; lastStep = Time.time;
            var steps = crouching ? SoundLibrary.Pick(crouchSteps, l => l.crouchSteps)
                : sprinting ? SoundLibrary.Pick(sprintSteps, l => l.sprintSteps) : SoundLibrary.Pick(walkSteps, l => l.walkSteps);
            if (steps && steps.HasClips)
            {
                Play(steps, 1f);
                var cloth = SoundLibrary.Pick(clothing, l => l.clothing);
                float chance = Lib ? Lib.clothingChance : .2f;
                if (!crouching && cloth && UnityEngine.Random.value < (sprinting ? chance * 1.6f : chance)) Play(cloth, sprinting ? 1f : .7f);
                return;
            }
            LegacyStep(crouching, sprinting);
        }
        void Jump()
        {
            distance = 0f;
            var sound = SoundLibrary.Pick(jump, l => l.jump);
            if (sound && sound.HasClips) Play(sound, 1f); else Legacy(body, jumpClip, jumpVolume);
            if (Server && sound) sound.EmitHearing(transform.position, gameObject);
        }
        void Land(float fallSpeed)
        {
            distance = 0f;
            float hardSpeed = Lib ? Lib.hardLandingSpeed : 6.5f;
            bool hard = fallSpeed >= hardSpeed;
            var sound = hard ? SoundLibrary.Pick(landHard, l => l.landHard) : SoundLibrary.Pick(landSoft, l => l.landSoft);
            // Louder the further you fell; a tiny hop off a step is barely a thud.
            float weight = Mathf.Lerp(.35f, 1f, Mathf.InverseLerp(1.5f, hardSpeed * 1.4f, fallSpeed));
            if (fallSpeed < 1.2f) return;
            if (sound && sound.HasClips) Play(sound, weight);
            else Legacy(body, landingClip, landingVolume * Mathf.InverseLerp(2f, 12f, fallSpeed));
            if (Server && sound) sound.EmitHearing(transform.position, gameObject, Mathf.Lerp(.5f, 1.3f, weight));
        }
        void Breathe()
        {
            if (!player || !player.IsSpawned || !stamina || Time.time < nextBreath) return;
            var sound = SoundLibrary.Pick(breathing, l => l.breathing);
            if (!sound || !sound.HasClips) return;
            float left = stamina.Current / Mathf.Max(1f, stamina.Maximum);
            bool winded = movement.State == MovementState.Sprinting || left < breathBelowStamina;
            if (!winded) return;
            nextBreath = Time.time + breathInterval * Mathf.Lerp(.75f, 1.2f, left);
            Play(sound, Mathf.Lerp(1f, .45f, left));
        }

        void Play(SoundEvent sound, float volume)
        {
            if (!sound) return;
            var source = sound.Play(transform.position, transform, volume);
            if (source) SoundPlayed?.Invoke(source.clip);
        }
        void LegacyStep(bool crouching, bool sprinting)
        {
            if (footstepClips == null || footstepClips.Length == 0) return;
            int index = UnityEngine.Random.Range(0, footstepClips.Length);
            if (index == previousStep && footstepClips.Length > 1) index = (index + 1) % footstepClips.Length;
            previousStep = index;
            Legacy(footsteps, footstepClips[index], crouching ? crouchVolume : sprinting ? sprintVolume : walkVolume);
        }
        void Legacy(AudioSource source, AudioClip clip, float volume)
        {
            if (!source || !clip || volume <= 0f) return;
            source.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clip, volume);
            SoundPlayed?.Invoke(clip);
        }
    }
}
