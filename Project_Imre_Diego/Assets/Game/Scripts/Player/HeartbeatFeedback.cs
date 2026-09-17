using UnityEngine;
namespace SurvivalFP
{
    [RequireComponent(typeof(NetworkPlayer))]
    public sealed class HeartbeatFeedback : MonoBehaviour
    {
        public AudioClip heartbeatClip;
        [Min(.1f)] public float maximumDistance=20, intenseDistance=3;
        [Min(.1f)] public float minimumInterval=.38f, maximumInterval=1.35f;
        [Min(.1f)] public float smoothing=3;
        [Range(0,1)] public float maximumVolume=.65f;
        public AnimationCurve volumeCurve=AnimationCurve.EaseInOut(0,0,1,1);
        public AnimationCurve pitchCurve=AnimationCurve.Linear(0,.9f,1,1.15f);
        public float Intensity {get;private set;}
        public float Interval=>Mathf.Lerp(maximumInterval,minimumInterval,Intensity);
        NetworkPlayer player; AudioSource speaker; float nextBeat;
        void Awake()
        {
            player=GetComponent<NetworkPlayer>();
            var audio=new GameObject("Local heartbeat");audio.transform.SetParent(transform,false);
            speaker=audio.AddComponent<AudioSource>();speaker.playOnAwake=false;speaker.spatialBlend=0;
        }
        public float ProximityIntensity(Vector3 position)
        {
            float distance=maximumDistance;
            foreach(var enemy in EnemyController.Enemies)
                if(enemy && enemy.IsSpawned)distance=Mathf.Min(distance,Vector3.Distance(position,enemy.transform.position));
            return 1-Mathf.InverseLerp(Mathf.Min(intenseDistance,maximumDistance-.01f),maximumDistance,distance);
        }
        void Update()
        {
            bool listen=player.IsSpawned && player.IsOwner &&
                (player.Life.Value==PlayerLife.Alive || player.Life.Value==PlayerLife.Downed);
            if(!listen) {Intensity=0;if(speaker.isPlaying)speaker.Stop();return;}
            Intensity=Mathf.Lerp(Intensity,ProximityIntensity(player.transform.position),1-Mathf.Exp(-smoothing*Time.unscaledDeltaTime));
            speaker.volume=Mathf.Clamp01(volumeCurve.Evaluate(Intensity))*maximumVolume;
            speaker.pitch=Mathf.Clamp(pitchCurve.Evaluate(Intensity),.5f,2f);
            if(Intensity<.01f){if(speaker.isPlaying)speaker.Stop();nextBeat=Time.unscaledTime;return;}
            if(heartbeatClip && Time.unscaledTime>=nextBeat)
            {speaker.PlayOneShot(heartbeatClip);nextBeat=Time.unscaledTime+Interval;}
        }
        void OnDisable(){Intensity=0;if(speaker)speaker.Stop();}
    }
}
