Shader "Custom/URP/HandOutlineMask"
{
    // Depth-only standoff mask for the keep-available hand outline (see HandReadyCue / HandOutline).
    // Added as an extra material on the hand's depth-occluder SkinnedMeshRenderer ALONGSIDE the
    // colour outline. It re-renders the hand mesh as an inverted hull extruded by _EdgeOffset and
    // writes ONLY depth (no colour). Combined with the hand occluder it lays a near-depth "wall"
    // out to a silhouette inflated by _EdgeOffset.
    //
    // Its render queue (Transparent-1 = 2999) is lower than the colour outline (Transparent = 3000),
    // so this draws FIRST. The colour shell (extruded by _EdgeOffset + _Width) then fails ZTest
    // inside this wall and only survives in the thin [_EdgeOffset, _EdgeOffset + _Width] band — so
    // _EdgeOffset becomes a true standoff gap between the real hand and the line, independent of the
    // line's thickness (_Width).
    Properties
    {
        _EdgeOffset ("Edge Offset (m)", Range(0, 0.05)) = 0.006
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent-1"  // before the colour outline (Transparent), after the occluder
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HandOutlineMask"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front          // back-facing shell, like the colour outline
            ColorMask 0         // depth only
            ZWrite On
            ZTest LEqual
            Offset -1, -1       // bias the wall slightly forward so it reliably culls the colour shell

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _EdgeOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 p = IN.positionOS.xyz + normalize(IN.normalOS) * _EdgeOffset;
                OUT.positionHCS = TransformObjectToHClip(p);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
