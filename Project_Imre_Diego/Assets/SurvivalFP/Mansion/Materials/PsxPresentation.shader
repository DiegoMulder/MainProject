Shader "SurvivalFP/PSX Presentation"
{
    Properties
    {
        _MainTex ("World", 2D) = "white" {}
        _Levels ("Colour levels", Float) = 32
        _Dither ("Dithering", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Overlay" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Levels, _Dither;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float2 pixel=floor(i.uv*_MainTex_TexelSize.zw);
                float2 uv=(pixel+.5)*_MainTex_TexelSize.xy;
                float3 colour=tex2D(_MainTex,uv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                colour=LinearToGammaSpace(colour);
                #endif
                uint x=(uint)pixel.x%4u, y=(uint)pixel.y%4u;
                float4 row=y==0?float4(0,8,2,10):y==1?float4(12,4,14,6):y==2?float4(3,11,1,9):float4(15,7,13,5);
                float dither=((row[x]+.5)/16-.5)*_Dither;
                float levels=max(3,_Levels-1);
                colour=floor(saturate(colour)*levels+.5+dither)/levels;
                #ifndef UNITY_COLORSPACE_GAMMA
                colour=GammaToLinearSpace(saturate(colour));
                #endif
                return float4(saturate(colour),1);
            }
            ENDCG
        }
    }
}
