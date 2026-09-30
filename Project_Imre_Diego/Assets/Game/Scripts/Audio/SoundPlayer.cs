using System.Collections.Generic;
using UnityEngine;
namespace SurvivalFP
{
    // Plays Sound Events through a small pool of AudioSources that lives for the whole session, so footsteps,
    // doors and impacts never create or destroy objects. When every voice is busy the least important one that
    // is closest to finishing is taken over (a footstep gives way to the monster, never the other way round).
    public static class SoundPlayer
    {
        const int StartVoices = 24, MaxVoices = 48;
        sealed class Voice
        {
            public AudioSource source; public Transform follow; public bool followed, looping;
            public SoundBus bus; public float volume, started;
        }
        static readonly List<Voice> voices = new();
        static Transform root;
        // Every sound that actually starts (for debugging and automated checks).
        public static event System.Action<SoundEvent, AudioSource> Played;

        // Lower AudioSource priority = kept first when Unity runs out of real voices.
        static int Priority(SoundBus bus) => bus switch
        {
            SoundBus.Cue => 0, SoundBus.Enemy => 16, SoundBus.UI => 32, SoundBus.Interaction => 64, SoundBus.Player => 96, _ => 160
        };

        public static AudioSource Play(SoundEvent sound, int index, Vector3 position, Transform follow = null, float volumeScale = 1f, float pitchScale = 1f)
        {
            var clip = sound ? sound.Clip(index) : null;
            if (!clip || volumeScale <= 0f || !Application.isPlaying) return null;
            var voice = Take(sound.bus);
            if (voice == null) return null;
            Configure(voice, sound, follow, position, volumeScale, pitchScale);
            voice.source.clip = clip; voice.source.loop = false; voice.looping = false;
            voice.source.Play();
            Played?.Invoke(sound, voice.source);
            return voice.source;
        }

        // A sound that keeps playing until Stop (radio static, a held drone). The voice is reserved until then.
        public static AudioSource PlayLoop(SoundEvent sound, Vector3 position, Transform follow = null, float volumeScale = 1f)
        {
            int index = sound ? sound.PickIndex() : -1;
            var clip = sound ? sound.Clip(index) : null;
            if (!clip || !Application.isPlaying) return null;
            var voice = Take(sound.bus);
            if (voice == null) return null;
            Configure(voice, sound, follow, position, volumeScale, 1f);
            voice.source.clip = clip; voice.source.loop = true; voice.looping = true;
            voice.source.time = Random.Range(0f, clip.length * .9f);
            voice.source.Play();
            Played?.Invoke(sound, voice.source);
            return voice.source;
        }
        public static void Stop(AudioSource source)
        {
            if (!source) return;
            foreach (var v in voices) if (v.source == source) { v.source.Stop(); v.looping = false; v.follow = null; v.followed = false; return; }
        }

        static void Configure(Voice voice, SoundEvent sound, Transform follow, Vector3 position, float volumeScale, float pitchScale)
        {
            var s = voice.source;
            voice.bus = sound.bus; voice.follow = follow; voice.followed = follow; voice.started = Time.unscaledTime;
            voice.volume = sound.volume * (1f - Random.Range(0f, sound.volumeVariation)) * volumeScale;
            s.transform.position = follow ? follow.position : position;
            bool ui = sound.bus == SoundBus.UI;
            s.spatialBlend = ui ? 0f : sound.spatialBlend;
            s.minDistance = sound.minDistance; s.maxDistance = Mathf.Max(sound.minDistance + .1f, sound.maxDistance);
            s.rolloffMode = AudioRolloffMode.Custom; s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            s.reverbZoneMix = ui ? 0f : sound.reverbMix;
            s.pitch = Mathf.Clamp(sound.pitch * pitchScale * (1f + Random.Range(-sound.pitchVariation, sound.pitchVariation)), .1f, 3f);
            s.priority = Priority(sound.bus);
            s.ignoreListenerPause = ui;
            var library = SoundLibrary.Instance;
            s.outputAudioMixerGroup = library ? library.output : null;
            Apply(voice);
        }

        // Natural inverse-square-like falloff that still reaches exactly zero at max distance.
        static AnimationCurve rolloff;
        static AnimationCurve Rolloff => rolloff ??= new AnimationCurve(
            new Keyframe(0f, 1f, 0f, -3.2f), new Keyframe(.15f, .55f, -2.1f, -2.1f), new Keyframe(.45f, .2f, -.7f, -.7f), new Keyframe(1f, 0f, -.15f, 0f));

        static void Apply(Voice voice)
        {
            var library = SoundLibrary.Instance;
            float bus = library ? library.BusVolume(voice.bus) : 1f;
            voice.source.volume = Mathf.Clamp01(voice.volume * bus * LocalSettings.Sfx);
        }

        static Voice Take(SoundBus bus)
        {
            EnsureRoot();
            Voice best = null;
            foreach (var v in voices) if (!v.looping && !v.source.isPlaying) return v;
            if (voices.Count < MaxVoices) return Add();
            // Steal: the least important bus first, then the oldest sound on it. Loops are never stolen.
            foreach (var v in voices)
            {
                if (v.looping || Priority(v.bus) < Priority(bus)) continue;
                if (best == null || Priority(v.bus) > Priority(best.bus) || (v.bus == best.bus && v.started < best.started)) best = v;
            }
            if (best != null) best.source.Stop();
            return best;
        }

        static void EnsureRoot()
        {
            if (root) return;
            voices.Clear();
            var go = new GameObject("Sound Pool") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SoundPoolDriver>();
            root = go.transform;
            for (int i = 0; i < StartVoices; i++) Add();
            LocalSettings.Changed -= ApplyAll; LocalSettings.Changed += ApplyAll;
        }
        static Voice Add()
        {
            var go = new GameObject("Voice " + voices.Count);
            go.transform.SetParent(root, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false; s.dopplerLevel = 0f; s.spread = 30f;
            var v = new Voice { source = s };
            voices.Add(v); return v;
        }
        static void ApplyAll() { foreach (var v in voices) if (v.source && v.source.isPlaying) Apply(v); }

        // Keeps followed sounds attached (a step moves with the walker, static with the radio).
        internal static void Follow()
        {
            foreach (var v in voices)
            {
                if (!v.followed || !v.source.isPlaying) continue;
                if (!v.follow) { if (v.looping) { v.source.Stop(); v.looping = false; } v.followed = false; continue; }
                v.source.transform.position = v.follow.position;
            }
        }
        public static int ActiveVoices { get { int n = 0; foreach (var v in voices) if (v.source && v.source.isPlaying) n++; return n; } }
        public static int PooledVoices => voices.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { voices.Clear(); root = null; Played = null; LocalSettings.Changed -= ApplyAll; }
    }

    sealed class SoundPoolDriver : MonoBehaviour { void LateUpdate() => SoundPlayer.Follow(); }
}
