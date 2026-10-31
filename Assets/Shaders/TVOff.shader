Shader "MODULMATA/TV Off"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Progress ("Power Off Progress", Range(0, 1)) = 0
        _BeamColor ("Beam Color", Color) = (0.75, 0.95, 1, 1)
        _LineWidth ("Line Half Height", Range(0.001, 0.02)) = 0.004
        _GlowIntensity ("Glow Intensity", Range(0, 2)) = 0.8
        _GlowSpread ("Glow Spread", Range(1, 10)) = 5
        _PointRadius ("Final Point Radius", Range(0.002, 0.04)) = 0.012
        [HideInInspector] _Aspect ("Screen Aspect", Float) = 1.777778
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

            float _Progress;
            float _Aspect;
            float _LineWidth;
            float _GlowIntensity;
            float _GlowSpread;
            float _PointRadius;
            float4 _BeamColor;
            float4 _Color;
            float4 _ClipRect;

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

            float4 frag(v2f input) : SV_Target
            {
                float p = saturate(_Progress);
                float2 offset = abs(input.uv - 0.5) * 2.0;
                float width = max(_LineWidth, 0.001);
                float aspect = max(_Aspect, 0.01);
                float2 aa = max(fwidth(offset), float2(0.00001, 0.00001));
                float black = 0.0;

                if (p > 0.0 && p < 0.6)
                {
                    // Cerrar la zona visible; el shader no captura ni desplaza la escena.
                    float aperture = lerp(1.0, width, smoothstep(0.0, 0.6, p));
                    black = smoothstep(aperture - aa.y, aperture + aa.y, offset.y);
                }
                else if (p >= 0.6)
                {
                    black = 1.0;
                }

                // Línea y punto independientes: la línea pierde longitud Y altura.
                float halfLength = 1.0 - smoothstep(0.6, 0.84, p);
                float lineHeight = width * halfLength;
                float2 radialOffset = float2(max(offset.x - halfLength, 0.0) * aspect, offset.y) / max(lineHeight, 0.00001);
                float squaredDistance = dot(radialOffset, radialOffset);
                float appearance = smoothstep(0.45, 0.6, p);
                float pointBlend = smoothstep(0.76, 0.84, p);
                float2 pointOffset = float2(offset.x * aspect, offset.y) / max(_PointRadius, 0.002);
                float pointDistance = dot(pointOffset, pointOffset);
                float lineCore = exp2(-4.0 * squaredDistance) * appearance * (1.0 - pointBlend);
                float pointCore = exp2(-4.0 * pointDistance) * pointBlend * (1.0 - smoothstep(0.9, 1.0, p));
                float core = max(lineCore, pointCore);
                float spread = max(_GlowSpread, 1.0);
                // El halo de la línea pierde intensidad al reducirse su longitud.
                float lineGlow = exp2(-2.0 * squaredDistance / (spread * spread)) * halfLength * appearance;
                // Después de la pausa, el halo del punto decae antes que su núcleo.
                float pointGlow = exp2(-2.0 * pointDistance / (spread * spread)) * pointBlend * (1.0 - smoothstep(0.84, 1.0, p));
                float glow = (lineGlow + pointGlow) * _GlowIntensity;

                float opacity = input.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                    opacity *= UnityGet2DClipping(input.localPosition.xy, _ClipRect);
                #endif
                // Color premultiplicado: negro opaco fuera de la abertura y haz sobre ella.
                float alpha = max(black, saturate(core + glow) * _BeamColor.a) * opacity;
                float3 coreColor = lerp(_BeamColor.rgb, float3(1.0, 1.0, 1.0), core);
                float3 rgb = (coreColor * core + _BeamColor.rgb * glow) * input.color.rgb * _BeamColor.a * opacity;
                return float4(rgb, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
