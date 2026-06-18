Shader "Custom/URP/GroundGlow"
{
    // A soft, additive radial disc drawn flat on the ground under the hero plant.
    // Alpha falls off from the centre to the edge (computed from UV), so no texture
    // is needed. Additive blend so it reads as a glow over MR passthrough.
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Softness ("Softness", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One   // additive glow
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 d = IN.uv - 0.5;
                float r = saturate(length(d) * 2.0);          // 0 centre .. 1 edge
                float a = saturate(1.0 - r);
                a = pow(a, lerp(4.0, 1.0, saturate(_Softness))); // softer = wider falloff
                a *= _Color.a;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
