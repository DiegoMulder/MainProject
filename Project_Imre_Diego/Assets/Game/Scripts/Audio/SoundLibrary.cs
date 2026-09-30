using UnityEngine;
namespace SurvivalFP
{
    // The one place to change the game's sounds: Assets/Game/Resources/Audio/Sound Library.
    // Every slot is a Sound Event asset (clips, volume, variation, 3D range, enemy hearing). A component that has
    // its own Sound Event assigned uses that instead, so one special door or item can sound different.
    [CreateAssetMenu(menuName = "Survival FP/Sound Library", fileName = "Sound Library")]
    public sealed class SoundLibrary : ScriptableObject
    {
        [Header("MIX  (bus volume, multiplied by the player's Sound effects setting)")]
        [Range(0f, 1f)] public float environmentVolume = .6f;
        [Range(0f, 1f)] public float playerVolume = .75f;
        [Range(0f, 1f)] public float interactionVolume = .85f;
        [Range(0f, 1f)] public float enemyVolume = 1f;
        [Range(0f, 1f)] public float cueVolume = 1f;
        [Range(0f, 1f)] public float uiVolume = .5f;
        [Tooltip("Mixer group used for every pooled sound (optional). Leave empty to play straight to the listener.")]
        public UnityEngine.Audio.AudioMixerGroup output;

        [Header("PLAYER AUDIO")]
        public SoundEvent walkSteps;
        public SoundEvent sprintSteps;
        public SoundEvent crouchSteps;
        public SoundEvent jump;
        public SoundEvent landSoft;
        public SoundEvent landHard;
        [Tooltip("Fall speed (m/s) at which a landing uses the hard set.")]
        [Min(0f)] public float hardLandingSpeed = 6.5f;
        public SoundEvent crouchDown;
        public SoundEvent standUp;
        [Tooltip("Cloth and belt rustle layered under some steps.")]
        public SoundEvent clothing;
        [Range(0f, 1f)] public float clothingChance = .22f;
        [Tooltip("Heard by the local player only while sprinting or out of stamina. Leave empty for none.")]
        public SoundEvent breathing;

        [Header("DOOR AUDIO")]
        public SoundEvent doorHandle;
        public SoundEvent doorOpen;
        public SoundEvent doorClose;
        public SoundEvent doorLatch;

        [Header("CLOSET AUDIO")]
        public SoundEvent closetOpen;
        public SoundEvent closetClose;
        public SoundEvent closetEnter;
        public SoundEvent closetExit;

        [Header("ITEM AUDIO")]
        public SoundEvent itemPickup;
        public SoundEvent itemDropLight;
        public SoundEvent itemDropHeavy;
        [Tooltip("Rigidbody mass (kg) from which an item uses the heavy drop set.")]
        [Min(0f)] public float heavyItemMass = 1.5f;

        [Header("FLASHLIGHT AUDIO")]
        public SoundEvent flashlightOn;
        public SoundEvent flashlightOff;

        [Header("WALKIE-TALKIE AUDIO")]
        public SoundEvent walkieOn;
        public SoundEvent walkieOff;
        [Tooltip("Squelch on the talker's radio when they key the radio.")]
        public SoundEvent transmitStart;
        public SoundEvent transmitEnd;
        [Tooltip("Squelch on every receiving radio when a transmission starts and ends.")]
        public SoundEvent receiveStart;
        public SoundEvent receiveEnd;
        [Tooltip("Looping hiss on a receiving radio while someone talks.")]
        public SoundEvent receiveStatic;

        [Header("ENEMY AUDIO")]
        public SoundEvent enemyFootstep;
        [Tooltip("Occasional sounds while roaming or idle.")]
        public SoundEvent enemyRoamVocal;
        public Vector2 roamVocalInterval = new(9f, 20f);
        public SoundEvent enemyInvestigate;
        public SoundEvent enemySearch;
        [Tooltip("Played once when a chase begins (the chase loop itself lives on the enemy model).")]
        public SoundEvent enemyChaseStart;
        public SoundEvent enemyAttack;
        public SoundEvent enemyStagger;

        [Header("IMPORTANT CUES")]
        public SoundEvent downed;

        [Header("UI AUDIO  (never heard by the enemy)")]
        public SoundEvent uiHover;
        public SoundEvent uiClick;
        public SoundEvent uiBack;
        public SoundEvent uiToggle;
        public SoundEvent uiSlider;

        static SoundLibrary instance; static bool looked;
        public static SoundLibrary Instance
        {
            get
            {
                if (!instance && !looked) { looked = true; instance = Resources.Load<SoundLibrary>("Audio/Sound Library"); }
                return instance;
            }
        }
        public float BusVolume(SoundBus bus) => bus switch
        {
            SoundBus.Environment => environmentVolume, SoundBus.Player => playerVolume, SoundBus.Interaction => interactionVolume,
            SoundBus.Enemy => enemyVolume, SoundBus.Cue => cueVolume, _ => uiVolume
        };
        // A component's own event wins; otherwise the library's.
        public static SoundEvent Pick(SoundEvent own, System.Func<SoundLibrary, SoundEvent> fallback)
        {
            if (own) return own;
            var library = Instance;
            return library ? fallback(library) : null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { instance = null; looked = false; }
    }
}
