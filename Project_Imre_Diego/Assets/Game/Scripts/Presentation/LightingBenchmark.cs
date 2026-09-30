#if UNITY_EDITOR || DEBUG
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
namespace SurvivalFP
{
    // Development tool: flies a camera through every generated room (four directions each) and reports frame
    // cost, so lighting setups can be compared on the same layout. Add it during a round; it removes itself.
    public sealed class LightingBenchmark : MonoBehaviour
    {
        public float secondsPerView = .35f;
        public string Report { get; private set; }
        public bool Done { get; private set; }

        IEnumerator Start()
        {
            var player = Camera.main;
            var cam = new GameObject("Benchmark Camera").AddComponent<Camera>();
            // Renders to the screen: the player camera's low-resolution target is owned (and resized) by the PSX presentation.
            if (player) { cam.CopyFrom(player); cam.targetTexture = null; player.enabled = false; player.tag = "Untagged"; }
            cam.tag = "MainCamera";
            var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            var textures = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory");
            var cpu = new List<double>(); var gpu = new List<double>(); var timings = new FrameTiming[1];
            long batchSum = 0, passSum = 0, casterSum = 0; int frames = 0, activeSum = 0, shadowSum = 0;
            foreach (var room in FindObjectsByType<RoomModule>())
            {
                for (int q = 0; q < 4; q++)
                {
                    cam.transform.SetPositionAndRotation(room.NavigationPosition + Vector3.up * 1.57f, Quaternion.Euler(0, room.transform.eulerAngles.y + q * 90, 0));
                    float until = Time.unscaledTime + secondsPerView; bool warm = false;
                    while (Time.unscaledTime < until)
                    {
                        yield return null;
                        if (!warm) { warm = true; continue; } // skip the frame the camera jumped
                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, timings) > 0) { cpu.Add(timings[0].cpuFrameTime); if (timings[0].gpuFrameTime > 0) gpu.Add(timings[0].gpuFrameTime); }
                        else if (main.Valid) cpu.Add(main.LastValue * 1e-6);
                        batchSum += batches.LastValue; passSum += setPass.LastValue; casterSum += casters.LastValue;
                        activeSum += LanternLight.ActiveCount; shadowSum += LanternLight.ShadowedCount; frames++;
                    }
                }
            }
            cpu.Sort(); gpu.Sort();
            string Pct(List<double> v, float p) => v.Count == 0 ? "n/a" : v[Mathf.Clamp((int)(v.Count * p), 0, v.Count - 1)].ToString("F2");
            Report = $"{frames} frames | CPU ms median {Pct(cpu, .5f)} p95 {Pct(cpu, .95f)} | GPU ms median {Pct(gpu, .5f)} p95 {Pct(gpu, .95f)}"
                + $" | batches {batchSum / Mathf.Max(1, frames)} setpass {passSum / Mathf.Max(1, frames)} shadow casters {casterSum / Mathf.Max(1, frames)}"
                + $" | real-time lanterns {activeSum / (float)Mathf.Max(1, frames):F1}, shadowed {shadowSum / (float)Mathf.Max(1, frames):F1}"
                + $" | texture memory {(textures.Valid ? (textures.LastValue / 1048576f).ToString("F0") + " MB" : "n/a")} | lightmaps {LightmapSettings.lightmaps.Length}";
            Debug.Log("Lighting benchmark: " + Report);
            main.Dispose(); batches.Dispose(); setPass.Dispose(); casters.Dispose(); textures.Dispose();
            if (player) { player.enabled = true; player.tag = "MainCamera"; }
            Destroy(cam.gameObject); Done = true; Destroy(this, .1f);
        }
    }
}
#endif
