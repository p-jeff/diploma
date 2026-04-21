Shader "Custom/URP/VoidPool"
{
    Properties
    {
        _Depth          ("Parallax Depth",   Range(0, 2))  = 0.5
        _StarDensity    ("Star Density",     Range(1, 40)) = 15.0
        _StarSize       ("Star Size",        Range(0.1, 4))= 1.8
        _StarBrightness ("Star Brightness",  Range(0, 2))  = 1.2
        _ColorEdge      ("Color Edge",       Color)        = (0.0,  0.0,  0.0,  1)
        _ColorCenter    ("Color Center",     Color)        = (0.02, 0.02, 0.08, 1)
        _GradientStart  ("Gradient Start",   Range(0, 1))  = 0.0
        _GradientEnd    ("Gradient End",     Range(0, 1))  = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _Depth;
                float  _StarDensity;
                float  _StarSize;
                float  _StarBrightness;
                float4 _ColorEdge;
                float4 _ColorCenter;
                float  _GradientStart;
                float  _GradientEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 parallaxUV  : TEXCOORD1; // pre-baked parallax offset
            };

            // Cheap hash - no sin/cos
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(p * 18.5453);
            }

            // Single star layer
            float Stars(float2 uv)
            {
                float2 cell  = floor(uv * _StarDensity);
                float2 local = frac(uv * _StarDensity) - 0.5;
                float2 off   = hash2(cell) - 0.5;
                float  dist  = length(local - off);
                float  bri   = hash2(cell + 33.1).x;
                return smoothstep(_StarSize * 0.04, 0.0, dist) * bri;
            }

            // ── Vertex: do parallax offset here, not per fragment ────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.uv          = IN.uv;

                // Parallax offset in vertex shader (cheap)
                float3 viewDirWS = normalize(GetCameraPositionWS() - pos.positionWS);
                float3 normalWS  = TransformObjectToWorldNormal(IN.normalOS);
                float  vDotN     = max(dot(viewDirWS, normalWS), 0.001);
                OUT.parallaxUV   = IN.uv + (viewDirWS.xz / vDotN) * _Depth * 0.1;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Single star layer on parallax UV
                float stars = saturate(Stars(IN.parallaxUV) * _StarBrightness);

                // Radial gradient edges -> center
                float2 c        = IN.uv - 0.5;
                float  t        = 1.0 - saturate(length(c) * 2.0);
                float  gradient = smoothstep(_GradientStart, _GradientEnd, t);
                float3 bgColor  = lerp(_ColorEdge.rgb, _ColorCenter.rgb, gradient);

                return half4(bgColor + stars, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
