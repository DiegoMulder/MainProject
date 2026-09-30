using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SurvivalFP
{
    // Baked lantern lighting that travels with a room prefab.
    //
    // Unity stores lightmaps per scene, so a room instantiated at runtime would normally lose them. The room is
    // therefore baked on its own (Survival FP > Lighting > Bake Mansion Room Lighting), once per mood, and this
    // component keeps each renderer's lightmap index and scale/offset. When the room spawns, it registers its
    // lightmaps with the scene and restores them. Lightmaps are non-directional and live in each renderer's own
    // lightmap UV space, so they stay correct wherever the generator places or rotates the room.
    //
    // Baked renderers move to the Baked Environment rendering layer. Lantern real-time lights skip that layer, so
    // the environment is lit once (by the bake) while players, enemies, doors and pickups are still lit by the
    // nearest lanterns in real time. Every other light (flashlights) lights both layers.
    [DisallowMultipleComponent]
    public sealed class RoomBakedLighting : MonoBehaviour
    {
        [Serializable] public struct BakedRenderer { public Renderer renderer; public int lightmap; public Vector4 scaleOffset; }
        [Serializable]
        public sealed class Variant
        {
            public LightingMood mood;
            public Texture2D[] lightmaps = Array.Empty<Texture2D>();
            public BakedRenderer[] renderers = Array.Empty<BakedRenderer>();
            [Tooltip("How each lantern in Lanterns burns in this bake, in the same order.")]
            public LanternLight.State[] lanternStates = Array.Empty<LanternLight.State>();
            // Baked light probes (room space) for players, enemies and props: six colours per probe, the light
            // arriving from +X, -X, +Y, -Y, +Z, -Z. Unlike raw SH this survives the room's rotation trivially.
            public Vector3[] probes = Array.Empty<Vector3>();
            public Color[] probeCube = Array.Empty<Color>();
        }

        // Rendering layer 1 ("Baked Environment"): lit by the bake and by flashlights, never by lantern lights.
        public const uint BakedLayer = 1u << 1;
        public const uint DynamicLayer = 1u << 0;

        public LanternLight[] lanterns = Array.Empty<LanternLight>();
        public Variant[] variants = Array.Empty<Variant>();
        // Off: rooms spawned afterwards ignore their bake and use fully real-time lanterns (comparison and fallback).
        public static bool Enabled = true;
        public bool HasBake => Enabled && variants != null && variants.Length > 0;

        Variant active; bool applied;

        void Awake() => Apply();

        public Variant Active { get { if (active == null && HasBake) active = Choose(); return active; } }

        // The same mood on every client: from the room's authored mood or its generated position.
        Variant Choose()
        {
            var room = GetComponent<RoomModule>();
            var mood = room ? room.ResolvedLighting : LightingMood.Lit;
            foreach (var v in variants) if (v.mood == mood) return v;
            return variants[0];
        }

        public bool TryGetState(LanternLight lantern, out LanternLight.State state)
        {
            state = LanternLight.State.Lit;
            var v = Active; if (v == null) return false;
            int index = Array.IndexOf(lanterns, lantern);
            if (index < 0 || index >= v.lanternStates.Length) return false;
            state = v.lanternStates[index]; return true;
        }

        void OnDestroy() => Applied.Remove(this);
        // Play Mode may start without a domain reload: never carry the switch or the registry into a new session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Enabled = true; Applied.Clear(); }

        public void Apply()
        {
            if (applied || !HasBake) return;
            var v = Active; applied = true;
            if (v.probes.Length > 0) Applied.Add(this);
            var indices = RegisterLightmaps(v.lightmaps);
            foreach (var entry in v.renderers)
            {
                if (!entry.renderer || entry.lightmap < 0 || entry.lightmap >= indices.Length) continue;
                entry.renderer.lightmapIndex = indices[entry.lightmap];
                entry.renderer.lightmapScaleOffset = entry.scaleOffset;
                entry.renderer.renderingLayerMask = BakedLayer;
            }
        }

        // ---------- Probe lighting for dynamic objects ----------
        static readonly List<RoomBakedLighting> Applied = new();
        static SphericalHarmonicsL2 ambientBasis, xBasis, yBasis, zBasis; static bool basisReady;
        // An AddDirectionalLight(+d) minus AddDirectionalLight(-d) pair keeps only the linear term; its gain along d is this.
        const float LinearGain = 1.882f;
        static readonly (int index, float weight)[] nearest = new (int, float)[4];

        // Baked indirect + direct light arriving at a world point, as SH for a renderer's light probe slot.
        public static bool TrySampleProbes(Vector3 world, out SphericalHarmonicsL2 sh)
        {
            sh = default;
            foreach (var room in Applied)
            {
                if (!room || !room.Contains(world)) continue;
                room.SampleLocal(room.transform.InverseTransformPoint(world), out sh);
                return true;
            }
            return false;
        }

        bool Contains(Vector3 world)
        {
            var module = GetComponent<RoomModule>(); if (!module) return false;
            var local = transform.InverseTransformPoint(world);
            foreach (var volume in module.OccupancyVolumes) { var b = volume; b.Expand(.4f); if (b.Contains(local)) return true; }
            return false;
        }

        void SampleLocal(Vector3 local, out SphericalHarmonicsL2 sh)
        {
            var v = Active; int count = 0;
            for (int i = 0; i < nearest.Length; i++) nearest[i] = (-1, 0f);
            // Four nearest probes, inverse-square weighted: smooth as a character walks through the room.
            for (int p = 0; p < v.probes.Length; p++)
            {
                float d = (v.probes[p] - local).sqrMagnitude, w = 1f / (d + .25f);
                int slot = -1;
                for (int i = 0; i < nearest.Length; i++) if (nearest[i].index < 0 || w > nearest[i].weight) { slot = i; break; }
                if (slot < 0) continue;
                for (int i = nearest.Length - 1; i > slot; i--) nearest[i] = nearest[i - 1];
                nearest[slot] = (p, w); count = Mathf.Min(count + 1, nearest.Length);
            }
            var cube = new Color[6]; float total = 0f;
            for (int i = 0; i < count; i++)
            {
                var (p, w) = nearest[i]; total += w;
                for (int f = 0; f < 6; f++) cube[f] += v.probeCube[p * 6 + f] * w;
            }
            for (int f = 0; f < 6; f++) cube[f] /= Mathf.Max(total, 1e-5f);
            sh = FromCube(cube, transform.rotation);
        }

        // Linear (L0 + L1) SH whose Evaluate reproduces the cube: average plus half the difference along each axis.
        static SphericalHarmonicsL2 FromCube(Color[] cube, Quaternion rotation)
        {
            if (!basisReady)
            {
                ambientBasis.AddAmbientLight(Color.white);
                SphericalHarmonicsL2 Linear(Vector3 axis) { var plus = new SphericalHarmonicsL2(); var minus = new SphericalHarmonicsL2(); plus.AddDirectionalLight(axis, Color.white, 1f); minus.AddDirectionalLight(-axis, Color.white, 1f); return plus + minus * -1f; }
                xBasis = Linear(Vector3.right); yBasis = Linear(Vector3.up); zBasis = Linear(Vector3.forward); basisReady = true;
            }
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++)
            {
                float average = 0f; for (int f = 0; f < 6; f++) average += cube[f][c]; average /= 6f;
                var slope = rotation * new Vector3(cube[0][c] - cube[1][c], cube[2][c] - cube[3][c], cube[4][c] - cube[5][c]) * (.5f / LinearGain);
                for (int k = 0; k < 9; k++)
                    sh[c, k] = ambientBasis[0, k] * average + xBasis[0, k] * slope.x + yBasis[0, k] * slope.y + zBasis[0, k] * slope.z;
            }
            return sh;
        }

        // Adds this bake's lightmaps to the scene's list (once per texture) and returns their scene indices.
        // LightmapSettings belongs to the loaded scene, so a scene change starts a fresh list automatically.
        static readonly List<LightmapData> scratch = new();
        static int[] RegisterLightmaps(Texture2D[] maps)
        {
            var current = LightmapSettings.lightmaps;
            scratch.Clear(); scratch.AddRange(current);
            var result = new int[maps.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                result[i] = scratch.FindIndex(d => d != null && d.lightmapColor == maps[i]);
                if (result[i] < 0) { scratch.Add(new LightmapData { lightmapColor = maps[i] }); result[i] = scratch.Count - 1; }
            }
            if (scratch.Count != current.Length)
            {
                LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
                LightmapSettings.lightmaps = scratch.ToArray();
            }
            return result;
        }
    }
}
