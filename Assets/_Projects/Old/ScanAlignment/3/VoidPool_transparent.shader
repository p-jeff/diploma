Shader "Custom/URP/VoidPool"
{
    Properties
    {
        _Depth          ("Parallax Depth",      Range(0, 2))    = 0.5
        _StarDensity    ("Star Density",        Range(1, 80))   = 20.0
        _StarSize       ("Star Size",           Range(0.1, 4))  = 1.8
        _StarBrightness ("Star Brightness",     Range(0, 2))    = 1.2

        _ColorEdge      ("Color Edge",          Color)          = (0.0,  0.0,  0.0,  1)
        _ColorCenter    ("Color Center",        Color)          = (0.02, 0.02, 0.08, 1)
        _GradientStart  ("Gradient Start",      Range(0, 1))    = 0.0
        _GradientEnd    ("Gradient End",        Range(0, 1))    = 1.0

        _FadeDistance   ("Camera Fade Distance", Range(0.01, 5)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

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
                float  _FadeDistance;
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
                float3 viewDirWS   : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 positionWS  : TEXCOORD3;
            };

            float hash(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float hash1(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5);
            }

            float Stars(float2 uv)
            {
                float2 cell  = floor(uv * _StarDensity);
                float2 local = frac(uv * _StarDensity) - 0.5;

                float2 starPos    = float2(hash(cell), hash(cell + 7.3)) - 0.5;
                float  dist       = length(local - starPos);
                float  brightness = hash1(cell);

                float star = smoothstep(_StarSize * 0.04, 0.0, dist);
                return star * brightness;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.uv          = IN.uv;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS   = normalize(GetCameraPositionWS() - pos.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Parallax
                float3 vd    = normalize(IN.viewDirWS);
                float3 nrm   = normalize(IN.normalWS);
                float  vDotN = max(dot(vd, nrm), 0.001);
                float2 offset = (vd.xz / vDotN) * _Depth * 0.1;

                float2 uvFar   = IN.uv + offset * 1.0;
                float2 uvMid   = IN.uv + offset * 0.5;
                float2 uvClose = IN.uv + offset * 0.2;

                float s1 = Stars(uvFar) * 1.0;
                float s2 = Stars(uvMid   * 1.4) * 0.6;
                float s3 = Stars(uvClose * 2.0) * 0.35;
                float stars = saturate((s1 + s2 + s3) * _StarBrightness);

                // Radial gradient edges -> center
                float2 c        = IN.uv - 0.5;
                float  t        = 1.0 - saturate(length(c) * 2.0);
                float  gradient = smoothstep(_GradientStart, _GradientEnd, t);
                float3 bgColor  = lerp(_ColorEdge.rgb, _ColorCenter.rgb, gradient);

                float3 col = bgColor + float3(stars, stars, stars);

                // Fade out as camera approaches the plane
                float camDist = distance(GetCameraPositionWS(), IN.positionWS);
                float alpha   = saturate(camDist / _FadeDistance);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
