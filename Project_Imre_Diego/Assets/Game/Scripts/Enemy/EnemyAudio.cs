using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    // The monster's voice and state cues, heard on every client from its replicated State: a sound when it starts
    // investigating a noise, while it searches, the moment a chase begins, the attack and the stagger after a kill,
    // and now and then while it roams. Footsteps, breathing and the chainsaw loops stay animation-driven on the
    // model (EnemyRandomSounds). Clips: Sound Library > ENEMY AUDIO, or the overrides here for one enemy type.
    // These sounds never feed enemy hearing.
    [RequireComponent(typeof(EnemyController))]
    public sealed class EnemyAudio : NetworkBehaviour
    {
        [Header("ENEMY AUDIO (empty = Sound Library)")]
        public SoundEvent roamVocal;
        public SoundEvent investigate, search, chaseStart, attack, stagger;
        [Tooltip("Seconds between roaming vocals (empty = Sound Library).")]
        public Vector2 roamVocalInterval;
        [Tooltip("Height of the voice above the enemy's feet.")]
        [Min(0)] public float voiceHeight = 1.6f;
        EnemyController enemy; Transform voice; float nextVocal;

        void Awake()
        {
            enemy = GetComponent<EnemyController>();
            voice = new GameObject("Voice").transform; voice.SetParent(transform, false); voice.localPosition = Vector3.up * voiceHeight;
            // The model's own sources (footsteps, breathing, chainsaw) follow the Sound effects setting and win voice stealing.
            foreach (var source in GetComponentsInChildren<AudioSource>(true)) { source.priority = 16; AudioVolumeBus.RouteSfx(source); }
        }
        public override void OnNetworkSpawn() { enemy.State.OnValueChanged += Changed; ScheduleVocal(); }
        public override void OnNetworkDespawn() => enemy.State.OnValueChanged -= Changed;

        void Changed(EnemyState old, EnemyState value)
        {
            SoundEvent cue = value switch
            {
                EnemyState.Investigate => SoundLibrary.Pick(investigate, l => l.enemyInvestigate),
                EnemyState.Search => SoundLibrary.Pick(search, l => l.enemySearch),
                EnemyState.Chase when old != EnemyState.Chase => SoundLibrary.Pick(chaseStart, l => l.enemyChaseStart),
                EnemyState.Kill => SoundLibrary.Pick(attack, l => l.enemyAttack),
                EnemyState.Stagger => SoundLibrary.Pick(stagger, l => l.enemyStagger),
                _ => null
            };
            if (cue) cue.Play(voice.position, voice);
            ScheduleVocal();
        }
        void ScheduleVocal()
        {
            var library = SoundLibrary.Instance;
            var range = roamVocalInterval != Vector2.zero ? roamVocalInterval : library ? library.roamVocalInterval : new Vector2(10, 20);
            nextVocal = Time.time + Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
        }
        void Update()
        {
            if (!IsSpawned || Time.time < nextVocal) return;
            ScheduleVocal();
            var state = enemy.State.Value;
            if (state != EnemyState.Roam && state != EnemyState.Idle) return;
            var vocal = SoundLibrary.Pick(roamVocal, l => l.enemyRoamVocal);
            if (vocal) vocal.Play(voice.position, voice);
        }
    }
}
