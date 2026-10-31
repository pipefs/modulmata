Shader "MODULMATA/DashAfterImage"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.18
        _ScanlineDensity ("Scanline Density", Range(1, 128)) = 48
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _ScanlineStrength;
            float _ScanlineDensity;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 sprite = tex2D(_MainTex, input.uv) * input.color * _Color;
                float scanline = sin(input.uv.y * _ScanlineDensity * 6.2831853) * 0.5 + 0.5;
                sprite.rgb *= 1.0 - scanline * _ScanlineStrength;
                return sprite;
            }
            ENDCG
        }
    }
}
