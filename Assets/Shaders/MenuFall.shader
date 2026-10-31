Shader "MODULMATA/Menu Fall"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _StreakColor ("Streak Color", Color) = (0.45, 0.7, 0.67, 0.65)
        _Intensity ("Intensity", Range(0, 1)) = 0.7
        _Speed ("Speed (Screen Heights Per Second)", Range(0, 3)) = 0.65
        _Density ("Density", Range(0, 1)) = 0.35
        _Length ("Streak Length (Screen Height)", Range(0.02, 0.5)) = 0.12
        [HideInInspector] _Visibility ("Visibility", Range(0, 1)) = 0
        [HideInInspector] _EffectTime ("Effect Time", Float) = 0
        [HideInInspector] _Aspect ("Overlay Aspect", Float) = 1.777778
        [HideInInspector] _Focus ("Robot Position", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 localPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _StreakColor, _Color, _ClipRect, _Focus;
            float _Intensity, _Speed, _Density, _Length, _Visibility, _EffectTime, _Aspect;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.localPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            float Hash(float2 p)
            {
                float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float StreakLayer(float2 p, float layerSpeed, float lengthScale, float densityScale, float seed)
            {
                // Coordenadas medidas en alturas de pantalla; píxeles virtuales de 1/180.
                const float pixel = 1.0 / 180.0;
                const float columnWidth = 10.0 / 180.0;
                float column = floor(p.x / columnWidth);
                float columnRandom = Hash(float2(column, seed));
                float x = (column + lerp(0.15, 0.85, columnRandom)) * columnWidth;
                x = (floor(x / pixel) + 0.5) * pixel;
                float cycle = 0.7 + columnRandom * 0.5;
                // Restar tiempo al patrón lo desplaza hacia +Y: las paredes suben al caer.
                float flow = p.y - _EffectTime * max(_Speed, 0.0) * layerSpeed + columnRandom * cycle;
                float row = floor(flow / cycle);
                float within = flow - row * cycle;
                float random = Hash(float2(column + seed, row));
                float active = 1.0 - step(saturate(_Density) * densityScale, random);
                float length = clamp(_Length * lengthScale * lerp(0.65, 1.3, random), pixel * 2.0, cycle * 0.8);
                float along = abs(within - cycle * 0.5);
                float tip = 1.0 - smoothstep(length * 0.25, length * 0.5, along);
                float across = 1.0 - smoothstep(pixel * 0.3, pixel * 0.85, abs(p.x - x));
                return active * tip * across * lerp(0.4, 1.0, Hash(float2(row, column + seed * 3.0)));
            }

            float4 frag(v2f input) : SV_Target
            {
                float aspect = max(_Aspect, 0.01);
                float2 resolution = float2(aspect, 1.0) * 180.0;
                float2 p = (floor(input.uv * resolution) + 0.5) / 180.0;
                float farLayer = StreakLayer(p, 0.55, 0.65, 1.0, 7.0) * 0.3;
                float nearLayer = StreakLayer(p + float2(0.024, 0.31), 1.15, 1.5, 0.55, 31.0) * 0.8;
                float sides = smoothstep(0.12, 0.75, abs(input.uv.x - 0.5) * 2.0);
                float focusDistance = length((input.uv - _Focus.xy) * float2(aspect, 1.0));
                float clearRobot = smoothstep(0.12, 0.3, focusDistance);
                float screenFade = smoothstep(0.0, 0.08, input.uv.y) *
                    (1.0 - smoothstep(0.92, 1.0, input.uv.y));
                float alpha = saturate(farLayer + nearLayer) * sides * clearRobot * screenFade;
                alpha *= saturate(_Intensity) * saturate(_Visibility) * _StreakColor.a * input.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                    alpha *= UnityGet2DClipping(input.localPosition.xy, _ClipRect);
                #endif
                return float4(_StreakColor.rgb * input.color.rgb * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
