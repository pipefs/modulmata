Shader "Custom/ScrollingFog"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "white" {}
        _FogColor ("Color", Color) = (0.2, 0.2, 0.3, 0.6)
        _ScrollSpeed ("Scroll Speed", Vector) = (0.05, 0.01, 0, 0)
        _Density ("Global Density", Range(0, 1)) = 0.7
        _Contrast ("Noise Contrast", Range(0.1, 5.0)) = 1.5
    }

    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent+10" 
            "RenderType"="Transparent" 
            "IgnoreProjector"="True" 
            "PreviewType"="Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _NoiseTex;
            float4 _NoiseTex_ST;
            fixed4 _FogColor;
            float2 _ScrollSpeed;
            float _Density;
            float _Contrast;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _NoiseTex);
                OUT.color = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 scrolledUV = IN.texcoord + (_ScrollSpeed * _Time.y);
                float noise = tex2D(_NoiseTex, scrolledUV).r;
                noise = pow(noise, _Contrast);
                float alpha = saturate(noise * _FogColor.a * _Density);
                return fixed4(_FogColor.rgb, alpha);
            }
            ENDCG
        }
    }
}