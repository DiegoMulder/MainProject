using UnityEngine;

public class GhostWomenSounds : MonoBehaviour
{
    [SerializeField] private AudioSource scareSoundSource;
    [SerializeField] private AudioSource cryingSoundSource;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void ScareSound()
    {
        if (cryingSoundSource.isPlaying)
            cryingSoundSource.Stop();

        if (!scareSoundSource.isPlaying)
            scareSoundSource.Play();
    }

    public void CryingSound()
    {
        if (!cryingSoundSource.isPlaying)
            cryingSoundSource.Play();
    }
}
