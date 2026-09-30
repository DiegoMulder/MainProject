using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SurvivalFP
{
    // Gives a moving object (player, enemy, pickup, door, closet) the baked indirect light of the room it is in.
    //
    // Unity's light probes belong to a scene and cannot travel with runtime-spawned room prefabs, so each room's
    // bake carries its own probe grid (RoomBakedLighting). A few times a second this samples the nearest probes
    // at the object's position and hands the result to its renderers as their light probe. Outside a baked room
    // the renderers fall back to Unity's normal probe/ambient lighting. Local only, never networked.
    [DisallowMultipleComponent]
    public sealed class DynamicProbeLighting : MonoBehaviour
    {
        [Tooltip("Seconds between samples. Staggered per object.")]
        [SerializeField, Min(.02f)] float interval = .15f;
        [Tooltip("Sample point above the object's pivot (chest height for characters).")]
        [SerializeField] float height = 1f;

        readonly List<Renderer> renderers = new();
        static readonly SphericalHarmonicsL2[] one = new SphericalHarmonicsL2[1];
        static MaterialPropertyBlock block;
        float next; bool custom;

        void OnEnable()
        {
            renderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer || r is SkinnedMeshRenderer) renderers.Add(r);
            next = Time.time + Random.value * interval;
        }

        void LateUpdate()
        {
            if (Time.time < next) return;
            next = Time.time + interval;
            block ??= new MaterialPropertyBlock();
            bool found = RoomBakedLighting.TrySampleProbes(transform.position + Vector3.up * height, out one[0]);
            if (!found && !custom) return;
            foreach (var r in renderers)
            {
                if (!r) continue;
                if (found)
                {
                    r.lightProbeUsage = LightProbeUsage.CustomProvided;
                    r.GetPropertyBlock(block); block.CopySHCoefficientArraysFrom(one); r.SetPropertyBlock(block);
                }
                else r.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
            custom = found;
        }
    }
}
