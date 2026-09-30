using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace SurvivalFP
{
    public enum LightingMood { Auto, Lit, Dim, Dark }

    // A lantern or candle flame: presentation only, never gameplay.
    //
    // State: whether it burns steadily, gutters or has gone out. In a baked room it comes from the room's bake
    // (RoomBakedLighting), which already lit the walls for that state; otherwise from its world position and its
    // room's mood. Either way every client sees the same mansion with no networking.
    //
    // Glow: the glass/flame material's emission always follows the flame, even when its real-time light is
    // off, so distant lanterns still read as lit.
    //
    // Real-time light: in a baked room it lights only the dynamic rendering layer (players, enemies, doors,
    // pickups) and never casts shadows; the environment gets its light and shadows from the bake. Each client
    // activates only the lights around its own camera (fading with hysteresis so nothing pops) and caps the
    // total. Lanterns in unbaked rooms light everything, and the few nearest get hard PSX-style shadows.
    // Major lights (chandeliers) reach much further.
    [DisallowMultipleComponent]
    public sealed class LanternLight : MonoBehaviour
    {
        public enum State { Lit, Guttering, Out }
        public enum Priority { Normal, Major }

        [SerializeField] Light lamp;
        [Tooltip("The renderer whose emissive material (glass, flame) glows with this light. Only materials with emission enabled react.")]
        [SerializeField] Renderer glass;
        [Tooltip("Override the room's mood for this lantern. Auto follows the room.")]
        [SerializeField] LightingMood mood = LightingMood.Auto;
        [Tooltip("Never guttering or out (a chandelier, a light the level is composed around).")]
        [SerializeField] bool alwaysBurning;
        [Tooltip("Major lights (chandeliers) stay active from much further away and are preferred when the light budget is full.")]
        [SerializeField] Priority priority = Priority.Normal;
        [SerializeField, Min(0f)] float intensity = 2.6f;
        [Tooltip("Keep lanterns local: a pool of light, not half the mansion.")]
        [SerializeField, Min(0.5f)] float range = 6f;
        [SerializeField] Color flameColour = new(1f, .58f, .27f);
        [SerializeField, Min(0f)] float glassGlow = 2.2f;
        [SerializeField, Range(0f, .3f)] float flicker = .07f;

        public State Current { get; private set; }
        public bool LightActive => active;
        public static int ActiveCount { get; private set; }
        public static int ShadowedCount { get; private set; }
        public static int BurningCount => Burning.Count;
        // Optional point that shadow casters are chosen around instead of the camera (menu staging).
        public static Vector3? ShadowFocus;

        // Activation distances (metres from the local camera). A normal lantern's pool of light has faded out
        // well before its activation edge, so the switch is never visible; hysteresis stops flicker at the edge.
        const float NormalOn = 20f, NormalOff = 26f, MajorOn = 45f, MajorOff = 55f;
        const int MaxActiveLights = 40, ShadowCasters = 4; const float ShadowDistance = 14f, FadeSpeed = 2.5f;

        static readonly List<LanternLight> Burning = new();
        static readonly List<LanternLight> scratch = new();
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static MaterialPropertyBlock block;
        static int evaluatedFrame = -1; static float nextEvaluation;
        float seed, strength, gutterTimer, gutterDip = 1f, weight;
        bool active, baked;
        public bool Baked => baked;
        public float Intensity => intensity;
        public float Range => range;
        public Color FlameColour => flameColour;
        public bool AlwaysBurning => alwaysBurning;

        void OnEnable()
        {
            if (!lamp) lamp = GetComponentInChildren<Light>(true);
            var p = transform.position;
            seed = Hash(Mathf.Round(p.x * 10), Mathf.Round(p.y * 10), Mathf.Round(p.z * 10));
            // In a baked room the bake already decided (and lit the walls with) this lantern's state.
            var bake = GetComponentInParent<RoomBakedLighting>();
            var bakedState = State.Lit;
            baked = bake && bake.TryGetState(this, out bakedState);
            Current = baked ? bakedState : alwaysBurning ? State.Lit : Resolve(seed);
            strength = Current == State.Lit ? Mathf.Lerp(.8f, 1.1f, Frac(seed * 7.3f)) : Current == State.Guttering ? Mathf.Lerp(.35f, .6f, Frac(seed * 3.1f)) : 0f;
            if (lamp)
            {
                lamp.type = LightType.Point; lamp.color = flameColour; lamp.range = range;
                lamp.shadows = LightShadows.None; lamp.shadowStrength = .9f; lamp.shadowNearPlane = .1f;
                lamp.renderMode = LightRenderMode.ForcePixel; lamp.lightmapBakeType = LightmapBakeType.Realtime;
                // Baked room: the walls already hold this light, so it only lights players, enemies and props.
                lamp.renderingLayerMask = baked ? (int)RoomBakedLighting.DynamicLayer : -1;
                var data = lamp.GetUniversalAdditionalLightData();
                if (data) data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                lamp.enabled = false; weight = 0f; active = false; // the budget decides, next evaluation
            }
            if (Current != State.Out) Burning.Add(this);
            nextEvaluation = 0f; // evaluate promptly when rooms are built
            Apply(1f);
        }
        void OnDisable() { Burning.Remove(this); if (lamp) { lamp.shadows = LightShadows.None; lamp.enabled = false; } active = false; }

        // Unbaked rooms: room mood first (a dark room has mostly dead lanterns), then this lantern's own roll.
        State Resolve(float roll)
        {
            var roomMood = mood;
            var room = GetComponentInParent<RoomModule>();
            if (roomMood == LightingMood.Auto) roomMood = room ? room.ResolvedLighting : LightingMood.Lit;
            return StateFor(roomMood, Frac(roll * 13.7f));
        }
        // Shared with the room baker, so baked and real-time rooms follow the same odds.
        // Every lantern burns: a dead lantern read as a broken light, so the mood only decides how many gutter
        // (a dimmer, stuttering flame that still lights its wall). Out remains for scripted use only.
        public static State StateFor(LightingMood roomMood, float own)
        {
            switch (roomMood)
            {
                case LightingMood.Lit: return own < .9f ? State.Lit : State.Guttering;
                case LightingMood.Dim: return own < .6f ? State.Lit : State.Guttering;
                default: return own < .35f ? State.Lit : State.Guttering;
            }
        }

        void Update()
        {
            if (Current == State.Out) return;
            if (evaluatedFrame != Time.frameCount) { evaluatedFrame = Time.frameCount; EvaluateBudget(); }
            float t = Time.time;
            // A slow breath plus a quicker lick of the flame.
            float wave = (Mathf.PerlinNoise(seed * 50f, t * 1.3f) - .5f) * 2f * flicker + (Mathf.PerlinNoise(seed * 91f, t * 7f) - .5f) * flicker * .6f;
            if (Current == State.Guttering)
            {
                gutterTimer -= Time.deltaTime;
                if (gutterTimer <= 0f) { gutterTimer = Random.Range(.08f, 2.4f); gutterDip = Random.value < .35f ? Random.Range(.35f, .7f) : 1f; } // a stutter, never a blackout
                wave *= 2.5f;
            }
            else gutterDip = 1f;
            weight = Mathf.MoveTowards(weight, active ? 1f : 0f, FadeSpeed * Time.deltaTime);
            if (lamp) lamp.enabled = weight > .001f;
            Apply(Mathf.Max(0f, (1f + wave) * gutterDip));
        }

        void Apply(float factor)
        {
            float lit = strength * factor;
            if (lamp && lamp.enabled) lamp.intensity = intensity * lit * weight;
            if (!glass) return;
            block ??= new MaterialPropertyBlock();
            glass.GetPropertyBlock(block);
            block.SetColor(EmissionColor, flameColour * (glassGlow * lit));
            glass.SetPropertyBlock(block);
        }

        // A few times a second, per client, from this client's own camera: which lights may be real-time,
        // and which of those cast shadows. Nothing here is networked or depends on the host.
        static void EvaluateBudget()
        {
            if (Time.unscaledTime < nextEvaluation) return;
            nextEvaluation = Time.unscaledTime + .2f;
            var cam = Camera.main; if (!cam) return;
            Vector3 eye = cam.transform.position, forward = cam.transform.forward;
            scratch.Clear();
            foreach (var lantern in Burning)
            {
                if (!lantern.lamp) continue;
                float d = Vector3.Distance(eye, lantern.transform.position);
                bool major = lantern.priority == Priority.Major;
                float on = major ? MajorOn : NormalOn, off = major ? MajorOff : NormalOff;
                lantern.active = lantern.active ? d < off : d < on;
                if (lantern.active) scratch.Add(lantern);
            }
            // Over budget: keep the most relevant (major, near, in front) and fade the rest out.
            scratch.Sort((a, b) => Score(a, eye, forward).CompareTo(Score(b, eye, forward)));
            for (int i = MaxActiveLights; i < scratch.Count; i++) scratch[i].active = false;
            ActiveCount = Mathf.Min(scratch.Count, MaxActiveLights);
            // Shadows only for the nearest few, around the camera or a staged focus point.
            Vector3 shadowEye = ShadowFocus ?? eye;
            scratch.RemoveRange(ActiveCount, scratch.Count - ActiveCount);
            scratch.Sort((a, b) => Score(a, shadowEye, forward).CompareTo(Score(b, shadowEye, forward)));
            // Baked lanterns already cast their (soft, baked) shadows into the room and only light dynamic
            // objects in real time, so shadow maps are spent only on lanterns in unbaked rooms.
            int casters = 0;
            for (int i = 0; i < scratch.Count; i++)
            {
                bool cast = !scratch[i].baked && casters < ShadowCasters && (scratch[i].transform.position - shadowEye).sqrMagnitude < ShadowDistance * ShadowDistance;
                if (cast) casters++;
                scratch[i].lamp.shadows = cast ? LightShadows.Hard : LightShadows.None;
            }
            ShadowedCount = casters;
            foreach (var lantern in Burning) if (lantern.lamp && !lantern.active) lantern.lamp.shadows = LightShadows.None;
        }
        static float Score(LanternLight lantern, Vector3 eye, Vector3 forward)
        {
            var offset = lantern.transform.position - eye;
            float behind = Vector3.Dot(offset, forward) < -2f ? 400f : 0f;
            float major = lantern.priority == Priority.Major ? -900f : 0f;
            return offset.sqrMagnitude + behind + major;
        }

        static float Hash(float x, float y, float z) => Frac(Mathf.Sin(x * 12.9898f + y * 78.233f + z * 37.719f) * 43758.5453f);
        static float Frac(float v) => v - Mathf.Floor(v);
    }
}
