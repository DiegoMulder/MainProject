using UnityEngine;

public class EnemyRandomSounds : MonoBehaviour
{
    [SerializeField] private Animator animator;

    [Header("Footsteps")]
    [SerializeField] private AudioSource footstepSource;
    [SerializeField] private AudioClip[] footstepSounds;

    [Header("Breathing")]
    [SerializeField] private AudioSource breathingSource;
    [SerializeField] private AudioClip[] breathingInSounds;
    [SerializeField] private AudioClip[] breathingOutSounds;
    private bool breathBool;

    [Header("Chainsaw")]
    [SerializeField] private AudioSource chainsawSource;
    [SerializeField] private AudioClip chainsawIdleSound;
    [SerializeField] private AudioClip chainsawSound;
    private bool chasing = false;
    [SerializeField] private AudioSource chainsawKillSound;
    private bool bloodToggle = false;
    [SerializeField] private GameObject bloodParticle;

    [Tooltip("Optional Sound Event for footsteps (clips, variation, 3D range). Empty = Sound Library ENEMY AUDIO, then the clips above.")]
    [SerializeField] private SurvivalFP.SoundEvent footstepEvent;
    private int lastFootstep = -1;

    // Called by the walk/run animation, so steps land exactly on the feet.
    public void Footstep()
    {
        var sound = SurvivalFP.SoundLibrary.Pick(footstepEvent, l => l.enemyFootstep);
        if (sound && sound.HasClips) { sound.Play(transform.position, transform); return; }

        if (footstepSounds == null || footstepSounds.Length == 0)
            return;

        int index = Random.Range(0, footstepSounds.Length);
        if (index == lastFootstep && footstepSounds.Length > 1) index = (index + 1) % footstepSounds.Length;
        lastFootstep = index;
        footstepSource.pitch = Random.Range(.93f, 1.05f);
        footstepSource.PlayOneShot(footstepSounds[index], Random.Range(.85f, 1f));
    }

    public void BreathingIn()
    {
        if (animator.GetFloat("Blend") > 0.1f)
            return;

        if (breathingInSounds.Length == 0)
            return;

        AudioClip clip = breathingInSounds[Random.Range(0, breathingInSounds.Length)];

        breathingSource.PlayOneShot(clip);
    }

    public void BreathingOut()
    {
        if (animator.GetFloat("Blend") > 0.1f)
            return;

        if (breathingOutSounds.Length == 0)
            return;

        AudioClip clip = breathingOutSounds[Random.Range(0, breathingOutSounds.Length)];

        breathingSource.PlayOneShot(clip);
    }

    void Update()
    {
        Chainsaw();
    }

    private void Chainsaw()
    {
        bool shouldChase = animator.GetFloat("Blend") > 3.5f;

        if (shouldChase != chasing)
        {
            chasing = shouldChase;

            chainsawSource.clip = chasing
                ? chainsawSound
                : chainsawIdleSound;

            chainsawSource.Play();
        }

        if (!chainsawSource.isPlaying)
        {
            chainsawSource.clip = chasing
                ? chainsawSound
                : chainsawIdleSound;

            chainsawSource.Play();
        }
    }

    public void ChainsawKill() => chainsawKillSound.Play();

    public void BloodToggle()
    {
        bloodToggle = !bloodToggle;

        bloodParticle.SetActive(bloodToggle);
    }
}