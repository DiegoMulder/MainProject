using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SurvivalFP
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PickupItem), typeof(AudioSource))]
    [DefaultExecutionOrder(200)]
    public sealed class PlayerFlashlight : MonoBehaviour, IPrimaryUse
    {
        [Header("References")]
        [SerializeField] Light beam;
        [SerializeField] AudioSource toggleAudio;
        [SerializeField] AudioClip toggleSound;
        [SerializeField] Renderer flashlightRenderer;
        [SerializeField] Material flashlight_Mat_On;
        [SerializeField] Material flashlight_Mat_Off;
        [Header("Toggle and fade")]
        [SerializeField] bool startOn;
        [SerializeField, Min(0f)] float toggleCooldown = 0.2f;
        [SerializeField, Min(0f)] float fadeSpeed = 14f;
        [Tooltip("Spot light intensity calibrated for the URP scene lighting.")]
        [SerializeField, Min(0f)] float onIntensity = 12.6953125f;
        [Header("FLASHLIGHT AUDIO (empty = Sound Library)")]
        [SerializeField] SoundEvent onSound;
        [SerializeField] SoundEvent offSound;
        [Header("Gameplay hearing")]
        [Tooltip("Used when the on/off Sound Event does not set its own enemy hearing.")]
        [Min(0)] public float toggleNoiseRadius = 3f;
        public bool IsOn { get; private set; }
        public float Intensity => beam ? beam.intensity : 0f;
        public Light Beam => beam;
        float cooldown;
        PickupItem item;
        [Tooltip("Metres the beam reaches. Keep it short enough that the far end of a hall stays dark.")]
        [SerializeField, Min(2f)] float beamRange = 22f;
        [Header("Beam aiming")]
        [SerializeField, Min(.1f)] float aimResponse = 12f;
        [SerializeField, Min(.5f)] float minimumAimDistance = 2f;

        public void PrimaryUse()
        {
            if (item && item.CanUse) Toggle();
        }

        public void Configure(Light lightSource, AudioSource audioSource, AudioClip switchClip)
        {
            beam = lightSource;
            toggleAudio = audioSource;
            if (switchClip) toggleSound = switchClip;
        }

        public void Toggle()
        {
            if(item&&item.Held&&!item.CanUse)return;
            if (!beam || cooldown > 0f) return;
            var network = GetComponent<NetworkPickup>();
            if (network && network.IsSpawned && !network.IsServer) return;
            IsOn = !IsOn;
            cooldown = toggleCooldown;
            var holder = GetComponentInParent<NetworkPlayer>();
            var where = holder ? holder.transform.position : transform.position;
            var who = holder ? holder.gameObject : gameObject;
            var heard = Switch(IsOn);
            if (heard && heard.alertsEnemy) heard.EmitHearing(where, who);
            else GameplayNoiseSystem.Emit(where, toggleNoiseRadius, NoiseCategory.Flashlight, who);
            if (network && network.IsSpawned) network.PlaySwitchSound();
            else PlaySwitchSound(IsOn);
        }

        public void ApplyNetworkState(bool on) { IsOn = on; }
        SoundEvent Switch(bool on) => on ? SoundLibrary.Pick(onSound, l => l.flashlightOn) : SoundLibrary.Pick(offSound, l => l.flashlightOff);
        public void PlaySwitchSound() => PlaySwitchSound(IsOn);
        public void PlaySwitchSound(bool on)
        {
            var sound = Switch(on);
            if (sound && sound.HasClips) { sound.Play(transform.position, transform); return; }
            if (toggleAudio && toggleSound) toggleAudio.PlayOneShot(toggleSound);
        }

        void Awake()
        {
            item = GetComponent<PickupItem>();
            if (!beam) beam = GetComponentInChildren<Light>(true);
            if (!beam || beam.transform == transform)
            {
                // Legacy pickups had the Light on their physics/model root.
                // A child beam can aim independently without rotating the held item.
                if (beam) beam.enabled = false;
                var template = Resources.Load<GameObject>("Flashlight/FlashlightBeam");
                GameObject child;
                if (template) child = Instantiate(template, transform);
                else
                {
                    child = new GameObject("Flashlight Beam", typeof(Light));
                    child.transform.SetParent(transform, false);
                    child.transform.localPosition = Vector3.forward * .22f;
                }
                child.name = "Flashlight Beam";
                beam = child.GetComponentInChildren<Light>(true);
            }
            ConfigureBeam();
            if (!toggleAudio) toggleAudio = GetComponent<AudioSource>();
            toggleAudio.playOnAwake = false;
            toggleAudio.spatialBlend = GetComponent<NetworkPickup>() ? 1f : 0f;
            AudioVolumeBus.RouteSfx(toggleAudio);
            toggleAudio.minDistance = 1f; toggleAudio.maxDistance = 8f;
            if (!toggleSound) toggleSound = Resources.Load<AudioClip>("Flashlight/switch_002");
            IsOn = startOn;
            beam.intensity = startOn ? onIntensity : 0f;
            beam.enabled = startOn;
            UpdateFlashlightMaterial();
        }

        // A focused, slightly harsh torch beam: a tight hot centre, a quick falloff to a dim ring, darkness beyond.
        // URP takes a light's rendering layers from its additional light data, not Light.renderingLayerMask:
        // the beam must reach every layer, including the Mansion's Baked Environment layer.
        void ConfigureBeam()
        {
            beam.type = LightType.Spot;
            beam.range = beamRange;
            beam.spotAngle = 56f;
            beam.innerSpotAngle = 18f;
            beam.color = new Color(1f, .93f, .8f);
            beam.cookie = Cookie;
            beam.renderMode = LightRenderMode.ForcePixel;
            //beam.lightmapBakeType = LightmapBakeType.Realtime;
            // One hard spot shadow per torch keeps the beam from shining through walls and doors.
            beam.shadows = LightShadows.Hard; beam.shadowStrength = .92f; beam.shadowNearPlane = .2f;
            var data = beam.GetUniversalAdditionalLightData();
            data.renderingLayers = (UnityEngine.RenderingLayerMask)uint.MaxValue;
            // Shadows come from everything except this client's own player (see NetworkPlayer.LocalBodyRenderingLayer):
            // the local body is invisible to its own camera, so it must not throw a shadow either.
            data.customShadowLayers = true;
            data.shadowRenderingLayers = (UnityEngine.RenderingLayerMask)~NetworkPlayer.LocalBodyRenderingLayer;
            ApplyShadowTier();
        }
        // The local player's own torch gets a sharper shadow than teammates' torches.
        void ApplyShadowTier()
        {
            var holder = GetComponentInParent<NetworkPlayer>();
            bool mine = !holder || holder == NetworkPlayer.Local;
            if (mine == shadowTierMine && shadowTierSet) return;
            shadowTierMine = mine; shadowTierSet = true;
            beam.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier = mine
                ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium
                : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
        }
        bool shadowTierMine, shadowTierSet;

        // Generated lens pattern: a hot core, a faint reflector ring and a soft dark edge, lightly mottled.
        static Texture2D cookie;
        static Texture2D Cookie
        {
            get
            {
                if (cookie) return cookie;
                const int size = 128;
                cookie = new Texture2D(size, size, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave, name = "Flashlight Cookie" };
                var rng = new System.Random(11);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + .5f) / size * 2f - 1f, dy = (y + .5f) / size * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                        float core = Mathf.Exp(-r * r * 9f);
                        float ring = .16f * Mathf.Exp(-Mathf.Pow((r - .62f) / .07f, 2f));
                        float body = .42f * (1f - Mathf.SmoothStep(.35f, .95f, r));
                        float grit = 1f - .08f * (float)rng.NextDouble();
                        cookie.SetPixel(x, y, new Color(Mathf.Clamp01((core * .6f + body + ring) * grit), 0, 0, 1));
                    }
                cookie.Apply(false, true);
                return cookie;
            }
        }

        // The item owns its light lifecycle. PlayerController only forwards inventory input.
        void Update() => Tick(false, Time.deltaTime);

        // Run after PlayerCamera's LateUpdate, including its bob and pitch.
        void LateUpdate() => AimAtView();

        public void AimAtView() => AimAtView(Time.deltaTime);

        public void AimAtView(float dt)
        {
            if (!beam || beam.transform == transform) return;
            var view = item ? item.HolderView : null;
            if (!item || !item.Held || !view)
            {
                beam.transform.localRotation = Quaternion.identity;
                return;
            }
            var camera = view.GetComponent<Camera>();
            Ray ray = camera ? camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f)) : new Ray(view.position, view.forward);
            Vector3 target = ray.GetPoint(beam.range);
            float nearest = beam.range;
            foreach (var hit in Physics.RaycastAll(ray, beam.range, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(view.root) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                target = ray.GetPoint(Mathf.Max(minimumAimDistance, hit.distance));
            }
            Vector3 direction = target - beam.transform.position;
            if (direction.sqrMagnitude > .000001f)
            {
                Quaternion desired = Quaternion.Inverse(beam.transform.parent.rotation) * Quaternion.LookRotation(direction, view.up);
                beam.transform.localRotation = Quaternion.Slerp(beam.transform.localRotation, desired,
                    1f - Mathf.Exp(-aimResponse * Mathf.Max(0f,dt)));
            }
        }

        public void Tick(bool togglePressed, float dt)
        {
            if (!beam) return;

            cooldown = Mathf.Max(0f, cooldown - dt);

            if (togglePressed)
                Toggle();

            if (IsOn)
                beam.enabled = true;
            // A switched-off torch never holds a shadow slot; ownership can change when it is picked up.
            beam.shadows = IsOn ? LightShadows.Hard : LightShadows.None;
            ApplyShadowTier();

            beam.intensity = Mathf.Lerp(
                beam.intensity,
                IsOn ? onIntensity : 0f,
                1f - Mathf.Exp(-fadeSpeed * dt)
            );

            if (!IsOn && beam.intensity < 0.05f)
            {
                beam.intensity = 0f;
                beam.enabled = false;
            }

            UpdateFlashlightMaterial();
        }

        private void UpdateFlashlightMaterial()
        {
            if (!flashlightRenderer)
                return;

            flashlightRenderer.sharedMaterial = IsOn
                ? flashlight_Mat_On
                : flashlight_Mat_Off;
        }
    }
}
