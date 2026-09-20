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

    public void Footstep()
    {
        if (footstepSounds.Length == 0)
            return;

        AudioClip clip = footstepSounds[
            Random.Range(0, footstepSounds.Length)
        ];

        footstepSource.PlayOneShot(clip);
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
}