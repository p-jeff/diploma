Shader "Custom/URP/FruitOrb"
{
    // A cheap additive glowing orb for canopy "fruit" context targets. View-dependent
    // fresnel rim + soft core so a unit sphere reads as a glowing ball in passthrough.
    // Additive (Blend One One) so it glows against the MR passthrough background.
    Properties
    {
        _Color ("Color", Color) = (1, 0.78, 0.32, 1)
        _Intensity ("Intensity", Float) = 1
        _FresnelPower ("Fresnel Power", Float) = 2
        _CoreAlpha ("Core Glow", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewWS      : TEXCOORD1;
            };

            float4 _Color;
            float  _Intensity;
            float  _FresnelPower;
            float  _CoreAlpha;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS = GetWorldSpaceViewDir(pos.positionWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewWS);
                float ndv = saturate(dot(n, v));
                float fres = pow(1.0 - ndv, _FresnelPower);     // bright rim
                float glow = saturate(fres + _CoreAlpha * ndv); // soft filled core
                return half4(_Color.rgb * glow * _Intensity, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
