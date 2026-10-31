Shader "MODULMATA/Critical Durability"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _HaloColor ("Halo Color", Color) = (0.85, 0.12, 0.035, 1)
        _EdgeColor ("Edge Color", Color) = (1, 0.55, 0.08, 1)
        _CoreColor ("Hot Core Color", Color) = (1, 0.96, 0.8, 1)
        _HaloWidth ("Halo Width (Screen Height)", Range(0.01, 0.3)) = 0.12
        _EdgeWidth ("Edge Width (Screen Height)", Range(0.0005, 0.02)) = 0.003
        _GlowIntensity ("Glow Intensity", Range(0, 1)) = 0.6
        _CornerBoost ("Corner Boost", Range(0, 3)) = 1.5
        _StraightIntensity ("Straight Edge Intensity", Range(0, 1)) = 0.4
        _ChromaticOffset ("Chromatic Separation", Range(0, 0.015)) = 0.003
        _MaxOpacity ("Maximum Opacity", Range(0, 1)) = 0.65
        [HideInInspector] _Intensity ("Intensity", Range(0, 1)) = 0
        [HideInInspector] _Aspect ("Overlay Aspect", Float) = 1.777778
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
            Name "Vignette"
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

            float4 _HaloColor;
            float4 _EdgeColor;
            float4 _CoreColor;
            float4 _Color;
            float4 _ClipRect;
            float _HaloWidth;
            float _EdgeWidth;
            float _GlowIntensity;
            float _CornerBoost;
            float _StraightIntensity;
            float _ChromaticOffset;

            float _MaxOpacity;
            float _Intensity;
            float _Aspect;

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

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 t = frac(p);
                t = t * t * (3.0 - 2.0 * t);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), t.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + float2(1, 1)), t.x), t.y);
            }

            float4 OverlayLight(float2 uv)
            {
                float aspect = max(_Aspect, 0.01);
                float shortSide = min(aspect, 1.0);
                float2 p = uv * float2(aspect, 1.0);
                float2 distanceToEdge = max(min(uv, 1.0 - uv), 0.0) * float2(aspect, 1.0);
                float distance = min(distanceToEdge.x, distanceToEdge.y);

                // Patrón fijo: manchas amplias, detalle tenue y luminosidad independientes.
                float broad = Noise(p * 4.3 + float2(7.1, 13.6));
                float detail = Noise(p * 21.0 + float2(31.2, 4.8));
                float light = Noise(p * 6.1 + float2(19.4, 43.7));
                float variation = 0.65 + broad * 0.7 + (detail - 0.5) * 0.16;
                float haloWidth = clamp(_HaloWidth * variation, 0.0001, shortSide * 0.32);
                float2 haloSides = 1.0 - smoothstep(0.0, haloWidth, distanceToEdge);
                // Unión suave: evitar la intersección rígida en L de dos bandas.
                float halo = haloSides.x + haloSides.y - haloSides.x * haloSides.y;
                float cornerWidth = min(haloWidth * 1.85, shortSide * 0.44);
                float2 cornerSides = 1.0 - smoothstep(0.0, cornerWidth, distanceToEdge);
                float corners = cornerSides.x * cornerSides.y;
                float localLight = lerp(0.3, 1.2, smoothstep(0.15, 0.85, light));
                float glow = saturate((halo * _StraightIntensity + corners * _CornerBoost) * _GlowIntensity * localLight);
                float aa = max(fwidth(distance), 0.00001);
                float edgeWidth = clamp(_EdgeWidth * (0.45 + broad * 1.2 + detail * 0.35), 0.00001, haloWidth);
                float edge = 1.0 - smoothstep(0.0, min(edgeWidth * 4.0 + aa, haloWidth), distance);
                float core = 1.0 - smoothstep(0.0, edgeWidth + aa, distance);
                float edgeStrength = lerp(_StraightIntensity, 1.0, saturate(corners * 2.0)) * localLight;
                // El blanco aparece por sectores, sin formar una línea continua.
                float hotSpots = smoothstep(0.32, 0.78, light * 0.75 + detail * 0.25);

                float haloAlpha = glow * saturate(_HaloColor.a);
                float edgeAlpha = saturate(edge * edgeStrength * _EdgeColor.a);
                float coreAlpha = saturate(core * edgeStrength * hotSpots * _CoreColor.a);
                float coverage = coreAlpha + (edgeAlpha + haloAlpha * (1.0 - edgeAlpha)) * (1.0 - coreAlpha);
                float3 haloRGB = _HaloColor.rgb * haloAlpha;
                float3 edgeRGB = _EdgeColor.rgb * edgeAlpha + haloRGB * (1.0 - edgeAlpha);
                float3 rgb = _CoreColor.rgb * coreAlpha + edgeRGB * (1.0 - coreAlpha);
                float clearCenter = 1.0 - smoothstep(shortSide * 0.32, shortSide * 0.44, distance);
                return float4(rgb, coverage) * clearCenter;
            }

            float4 frag(v2f input) : SV_Target
            {
                float aspect = max(_Aspect, 0.01);
                float2 centered = (input.uv - 0.5) * 2.0;
                float periphery = smoothstep(0.3, 1.0, max(abs(centered.x), abs(centered.y)));
                float patch = Noise(input.uv * float2(aspect, 1.0) * 4.3 + float2(7.1, 13.6));
                float2 separation = centered * float2(1.0 / aspect, 1.0) * _ChromaticOffset * periphery * lerp(0.35, 1.0, patch);
                // Separar solo las máscaras del overlay. No se lee la imagen de la cámara.
                float4 green = OverlayLight(input.uv);
                float4 red = OverlayLight(input.uv + separation);
                float4 blue = OverlayLight(input.uv - separation);
                float3 rgb = float3(red.r, green.g, blue.b);
                float coverage = max(green.a, max(red.a, blue.a));
                float opacity = saturate(_MaxOpacity) * saturate(_Intensity) * input.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                    opacity *= UnityGet2DClipping(input.localPosition.xy, _ClipRect);
                #endif
                return float4(rgb * input.color.rgb * opacity, coverage * opacity);
            }
            ENDCG
        }

    }
    Fallback Off
}


