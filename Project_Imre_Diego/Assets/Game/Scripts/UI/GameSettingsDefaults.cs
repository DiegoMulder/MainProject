using UnityEngine;
namespace SurvivalFP
{
    [CreateAssetMenu(menuName="Survival FP/Local Settings Defaults")]
    public sealed class GameSettingsDefaults : ScriptableObject
    {
        [Range(0,1)] public float master=.8f, sfx=1, voice=1;
        [Range(.01f,.5f)] public float sensitivity=.1f;
        public float minimumFov=60, maximumFov=110, fieldOfView=75;
        public bool smoothing=true, psx=true;
        [Range(0,1)] public float smoothingStrength=.25f;
        [Min(.001f)] public float maximumSmoothTime=.09f;
    }
}
