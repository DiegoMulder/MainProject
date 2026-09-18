using System;
using UnityEngine;
namespace SurvivalFP
{
    // Local preferences are deliberately absent from network state.
    public static class LocalSettings
    {
        public static event Action Changed;
        public static GameSettingsDefaults Defaults {get;private set;}
        public static float Master {get;private set;}
        public static float Sfx {get;private set;}
        public static float Voice {get;private set;}
        public static float Sensitivity {get;private set;}
        public static float Fov {get;private set;}
        public static float SmoothingStrength {get;private set;}
        public static bool Smoothing {get;private set;}
        public static bool Psx {get;private set;}
        public static bool Muted {get;private set;}
        public static float MinFov=>Defaults?Defaults.minimumFov:60;
        public static float MaxFov=>Defaults?Mathf.Max(MinFov,Defaults.maximumFov):110;
        public static float SmoothTime=>Smoothing?SmoothingStrength*(Defaults?Defaults.maximumSmoothTime:.09f):0;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){Changed=null;Defaults=null;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            Defaults=Resources.Load<GameSettingsDefaults>("Settings/Local Settings Defaults");
            Master=Mathf.Clamp01(PlayerPrefs.GetFloat("SurvivalFP.Volume",Defaults?Defaults.master:.8f));
            Sfx=Mathf.Clamp01(PlayerPrefs.GetFloat("SurvivalFP.Sfx",Defaults?Defaults.sfx:1));
            Voice=Mathf.Clamp01(PlayerPrefs.GetFloat("SurvivalFP.Voice",Defaults?Defaults.voice:1));
            Sensitivity=Mathf.Clamp(PlayerPrefs.GetFloat("SurvivalFP.Sensitivity",Defaults?Defaults.sensitivity:.1f),.01f,.5f);
            Fov=Mathf.Clamp(PlayerPrefs.GetFloat("SurvivalFP.Fov",Defaults?Defaults.fieldOfView:75),MinFov,MaxFov);
            Smoothing=PlayerPrefs.GetInt("SurvivalFP.Smoothing",!Defaults||Defaults.smoothing?1:0)!=0;
            SmoothingStrength=Mathf.Clamp01(PlayerPrefs.GetFloat("SurvivalFP.SmoothingStrength",Defaults?Defaults.smoothingStrength:.25f));
            Psx=PlayerPrefs.GetInt("SurvivalFP.Psx",!Defaults||Defaults.psx?1:0)!=0;
            Muted=PlayerPrefs.GetInt("SurvivalFP.Muted",0)!=0;
            Apply();
        }
        public static void Set(float master,float sfx,float voice,float sensitivity,float fov,bool smoothing,float strength,bool psx,bool muted)
        {
            Master=Mathf.Clamp01(master);Sfx=Mathf.Clamp01(sfx);Voice=Mathf.Clamp01(voice);
            Sensitivity=Mathf.Clamp(sensitivity,.01f,.5f);Fov=Mathf.Clamp(fov,MinFov,MaxFov);
            Smoothing=smoothing;SmoothingStrength=Mathf.Clamp01(strength);Psx=psx;Muted=muted;
            Apply();Save();
        }
        static void Apply(){AudioListener.volume=Master;Changed?.Invoke();}
        public static void Save()
        {
            PlayerPrefs.SetFloat("SurvivalFP.Volume",Master);PlayerPrefs.SetFloat("SurvivalFP.Sfx",Sfx);PlayerPrefs.SetFloat("SurvivalFP.Voice",Voice);
            PlayerPrefs.SetFloat("SurvivalFP.Sensitivity",Sensitivity);PlayerPrefs.SetFloat("SurvivalFP.Fov",Fov);
            PlayerPrefs.SetFloat("SurvivalFP.SmoothingStrength",SmoothingStrength);
            PlayerPrefs.SetInt("SurvivalFP.Smoothing",Smoothing?1:0);PlayerPrefs.SetInt("SurvivalFP.Psx",Psx?1:0);PlayerPrefs.SetInt("SurvivalFP.Muted",Muted?1:0);PlayerPrefs.Save();
        }
    }
}
