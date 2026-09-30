using UnityEngine;
namespace SurvivalFP
{
    // Mixing layers, quietest first: the world sits under the players, interactions over them, the monster over
    // those, and the cues that keep you alive (heartbeat, chase, downed) above everything. UI never reaches the game.
    public enum SoundBus { Environment, Player, Interaction, Enemy, Cue, UI }

    // One sound, fully described in the Inspector: which clips, how loud, how it varies, how far it carries in 3D,
    // and whether (and how far) the monster hears it. Playback and hearing are separate calls on purpose:
    // Play() only makes sound on this machine; EmitHearing() only tells the server's enemies. UI sounds have
    // hearing switched off and are never passed to EmitHearing, so a menu click can never alert the monster.
    [CreateAssetMenu(menuName = "Survival FP/Sound Event", fileName = "New Sound Event")]
    public sealed class SoundEvent : ScriptableObject
    {
        [Header("Clips")]
        [Tooltip("One is picked at random each time. Leave empty to silence this sound without breaking anything.")]
        public AudioClip[] clips = System.Array.Empty<AudioClip>();
        [Tooltip("Never play the same clip twice in a row (when there is more than one).")]
        public bool avoidRepeat = true;

        [Header("Loudness and variation")]
        [Range(0f, 1f)] public float volume = .7f;
        [Tooltip("Each play is up to this much quieter, so repeats never sound identical.")]
        [Range(0f, .5f)] public float volumeVariation = .08f;
        [Range(.3f, 2f)] public float pitch = 1f;
        [Tooltip("Random pitch offset either way. Keep it subtle (0.02–0.08) for footsteps and wood.")]
        [Range(0f, .4f)] public float pitchVariation = .05f;
        public SoundBus bus = SoundBus.Interaction;

        [Header("3D")]
        [Tooltip("0 = heard in the head (UI, own breathing), 1 = placed in the world.")]
        [Range(0f, 1f)] public float spatialBlend = 1f;
        [Tooltip("Full volume inside this distance.")]
        [Min(.05f)] public float minDistance = 1.5f;
        [Tooltip("Silent beyond this distance.")]
        [Min(.1f)] public float maxDistance = 18f;
        [Tooltip("Old houses carry sound: a little reverb bleed makes wood sound heavy and rooms sound large.")]
        [Range(0f, 1.1f)] public float reverbMix = 1f;

        [Header("Enemy hearing")]
        [Tooltip("When on, the gameplay code that plays this sound also reports it to the enemies (server only).")]
        public bool alertsEnemy;
        [Tooltip("How far the enemy hears it, in metres (before a closet muffles it or the enemy's own hearing multiplier).")]
        [Min(0f)] public float hearingRadius = 6f;
        [Tooltip("What kind of noise the enemy thinks it is.")]
        public NoiseCategory hearingCategory = NoiseCategory.Other;

        int last = -1;
        public bool HasClips { get { if (clips != null) foreach (var c in clips) if (c) return true; return false; } }

        // Picks a clip index (optionally never the previous one). -1 when there is nothing to play.
        public int PickIndex()
        {
            if (clips == null || clips.Length == 0) return -1;
            int count = 0, chosen = -1;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] && (!avoidRepeat || i != last || clips.Length == 1) && Random.Range(0, ++count) == 0) chosen = i;
            if (chosen < 0) for (int i = 0; i < clips.Length; i++) if (clips[i]) { chosen = i; break; }
            last = chosen; return chosen;
        }
        public AudioClip Clip(int index) => clips != null && index >= 0 && index < clips.Length ? clips[index] : null;

        // Plays locally. follow keeps the sound attached to a moving thing (a walking player, a swinging door).
        public AudioSource Play(Vector3 position, Transform follow = null, float volumeScale = 1f, float pitchScale = 1f)
            => SoundPlayer.Play(this, PickIndex(), position, follow, volumeScale, pitchScale);
        public AudioSource PlayIndex(int index, Vector3 position, Transform follow = null, float volumeScale = 1f, float pitchScale = 1f)
            => SoundPlayer.Play(this, index, position, follow, volumeScale, pitchScale);

        // Server only: tells the enemies. radiusScale lets the caller make a hard landing louder than a soft one.
        public void EmitHearing(Vector3 position, GameObject source, float radiusScale = 1f)
        {
            if (alertsEnemy && hearingRadius > 0f && bus != SoundBus.UI)
                GameplayNoiseSystem.Emit(position, hearingRadius * radiusScale, hearingCategory, source);
        }
    }
}
