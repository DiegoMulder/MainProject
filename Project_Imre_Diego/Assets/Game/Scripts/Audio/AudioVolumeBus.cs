using UnityEngine;
namespace SurvivalFP
{
    // Every source registers once with its bus. UI only edits LocalSettings.
    // Vivox has its own device mixer and applies Master * Voice centrally.
    public sealed class AudioVolumeBus : MonoBehaviour
    {
        public AudioSource source;
        [Range(0,1)] public float authoredVolume=1;
        public static void RouteSfx(AudioSource source)
        {
            if(!source)return;
            foreach(var bus in source.GetComponents<AudioVolumeBus>())if(bus.source==source)return;
            var route=source.gameObject.AddComponent<AudioVolumeBus>();
            route.source=source;route.authoredVolume=source.volume;route.Apply();
        }
        void OnEnable(){LocalSettings.Changed+=Apply;Apply();}
        void OnDisable()=>LocalSettings.Changed-=Apply;
        void Apply(){if(source)source.volume=authoredVolume*LocalSettings.Sfx;}
    }
}
