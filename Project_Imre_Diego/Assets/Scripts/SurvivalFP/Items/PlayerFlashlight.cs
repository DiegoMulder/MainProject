using UnityEngine;

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
        [Header("Toggle and fade")]
        [SerializeField] bool startOn;
        [SerializeField, Min(0f)] float toggleCooldown = 0.2f;
        [SerializeField, Min(0f)] float fadeSpeed = 14f;
        [Tooltip("Spot light intensity calibrated for the URP scene lighting.")]
        [SerializeField, Min(0f)] float onIntensity = 12.6953125f;
        public bool IsOn { get; private set; }
        public float Intensity => beam ? beam.intensity : 0f;
        public Light Beam => beam;
        float cooldown;
        PickupItem item;

        public void PrimaryUse()
        {
            if (item && item.Held) Toggle();
        }

        public void Configure(Light lightSource, AudioSource audioSource, AudioClip switchClip)
        {
            beam = lightSource;
            toggleAudio = audioSource;
            if (switchClip) toggleSound = switchClip;
        }

        public void Toggle()
        {
            if (!beam || cooldown > 0f) return;
            IsOn = !IsOn;
            cooldown = toggleCooldown;
            if (toggleAudio && toggleSound) toggleAudio.PlayOneShot(toggleSound);
        }

        public void ApplyNetworkState(bool on) { IsOn = on; }

        void Awake()
        {
            item = GetComponent<PickupItem>();
            if (!beam) beam = GetComponentInChildren<Light>(true);
            if (!beam || beam.transform == transform)
            {
                // Legacy pickups had the Light on their physics/model root.
                // A child beam can aim independently without rotating the held item.
                if (beam) beam.enabled = false;
                var template = Resources.Load<GameObject>("FlashlightBeam");
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
            beam.type = LightType.Spot;
            beam.range = 40f;
            beam.spotAngle = 62f;
            beam.innerSpotAngle = 32f;
            beam.color = new Color(1f, .94f, .82f);
            beam.shadows = LightShadows.Soft;
            if (!toggleAudio) toggleAudio = GetComponent<AudioSource>();
            toggleAudio.playOnAwake = false;
            toggleAudio.spatialBlend = 0f;
            if (!toggleSound) toggleSound = Resources.Load<AudioClip>("switch_002");
            IsOn = startOn;
            beam.intensity = startOn ? onIntensity : 0f;
            beam.enabled = startOn;
        }

        // The item owns its light lifecycle. PlayerController only forwards inventory input.
        void Update() => Tick(false, Time.deltaTime);

        // Run after PlayerCamera's LateUpdate, including its bob and pitch.
        void LateUpdate() => AimAtView();

        public void AimAtView()
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
                target = hit.point;
            }
            Vector3 direction = target - beam.transform.position;
            if (direction.sqrMagnitude > .000001f)
                beam.transform.rotation = Quaternion.LookRotation(direction, view.up);
        }

        public void Tick(bool togglePressed, float dt)
        {
            if (!beam) return;
            cooldown = Mathf.Max(0f, cooldown - dt);
            if (togglePressed) Toggle();
            if (IsOn) beam.enabled = true;
            beam.intensity = Mathf.Lerp(beam.intensity, IsOn ? onIntensity : 0f, 1f - Mathf.Exp(-fadeSpeed * dt));
            if (!IsOn && beam.intensity < 0.05f) { beam.intensity = 0f; beam.enabled = false; }
        }
    }
}
