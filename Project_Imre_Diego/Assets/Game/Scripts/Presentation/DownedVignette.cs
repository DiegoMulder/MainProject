using UnityEngine;
namespace SurvivalFP
{
    // Screen-space presentation after the PSX world image; no extra camera or network traffic.
    [DefaultExecutionOrder(950),RequireComponent(typeof(NetworkPlayer))]
    public sealed class DownedVignette:MonoBehaviour
    {
        [Range(0,1)] public float minimumIntensity=.15f,maximumIntensity=1f;
        [Range(0,1)] public float minimumOpacity=.12f,maximumOpacity=.8f;
        public Color colour=new(.55f,.015f,.025f,1);
        public AnimationCurve progression=AnimationCurve.EaseInOut(0,0,1,1);
        [Min(.01f)] public float fadeTime=.25f;
        public float Intensity {get;private set;}
        public float Opacity {get;private set;}
        NetworkPlayer player;Texture2D mask;float intensityVelocity,opacityVelocity;
        void Awake()=>player=GetComponent<NetworkPlayer>();
        void LateUpdate()
        {
            if(!player.IsSpawned||!player.IsOwner||player.Life.Value==PlayerLife.Dead||player.Life.Value==PlayerLife.Escaped){ResetEffect();return;}
            bool down=player.Life.Value==PlayerLife.Downed;
            float progress=Mathf.Clamp01(progression.Evaluate(player.BleedOutProgress));
            Intensity=Mathf.SmoothDamp(Intensity,down?Mathf.Lerp(minimumIntensity,maximumIntensity,progress):0,ref intensityVelocity,fadeTime,Mathf.Infinity,Time.unscaledDeltaTime);
            Opacity=Mathf.SmoothDamp(Opacity,down?Mathf.Lerp(minimumOpacity,maximumOpacity,progress):0,ref opacityVelocity,fadeTime,Mathf.Infinity,Time.unscaledDeltaTime);
        }
        void ResetEffect(){Intensity=Opacity=intensityVelocity=opacityVelocity=0;}
        void OnGUI()
        {
            if(!player||!player.IsSpawned||!player.IsOwner||Opacity<.001f)return;
            if(!mask)
            {
                const int size=128;mask=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Bleed-out vignette",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.DontSave};
                var pixels=new Color[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++){float dx=2f*x/(size-1)-1,dy=2f*y/(size-1)-1;float edge=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.35f,1.15f,Mathf.Sqrt(dx*dx+dy*dy)));pixels[y*size+x]=new Color(1,1,1,edge);}
                mask.SetPixels(pixels);mask.Apply(false,true);
            }
            var oldColour=GUI.color;int oldDepth=GUI.depth;GUI.depth=50;
            GUI.color=new Color(colour.r,colour.g,colour.b,colour.a*Opacity);
            float scale=Mathf.Lerp(1.5f,1,Intensity);float w=Screen.width*scale,h=Screen.height*scale;
            GUI.DrawTexture(new Rect((Screen.width-w)*.5f,(Screen.height-h)*.5f,w,h),mask,ScaleMode.StretchToFill,true);
            GUI.color=oldColour;GUI.depth=oldDepth;
        }
        void OnDisable()=>ResetEffect();
        void OnDestroy(){if(mask)Destroy(mask);}
    }
}
