Shader "Custom/URP/HandOutline"
{
    // Green silhouette line for the keep-available hand cue. Added as an EXTRA material on the
    // hand's depth-occluder SkinnedMeshRenderer (see HandReadyCue), re-rendering the hand mesh as
    // an inverted hull:
    //   - vertices pushed OUT along their normals by (_EdgeOffset + _Width),
    //   - Cull Front draws only the back-facing shell,
    //   - ZTest LEqual lets earlier depth cull the shell over the hand body.
    //
    // The standoff GAP is produced by the companion 'Custom/URP/HandOutlineMask' material (queue
    // Transparent-1, draws first): it writes a depth wall out to a silhouette inflated by
    // _EdgeOffset, so this colour shell is culled inside it and only the thin
    // [_EdgeOffset, _EdgeOffset + _Width] band survives, standing OFF the hand edge.
    //   - _Width  = line thickness (keep small for a thin line).
    //   - _EdgeOffset = passthrough gap between the real hand and the line (must match the mask).
    //   - _Blur   = fades the line's outer edge via the back-face grazing term (0 = crisp).
    // Additive over MR passthrough; the green lives in air outside the silhouette, never on skin.
    Properties
    {
        _Color ("Color", Color) = (0.35, 1, 0.45, 1)
        _Width ("Outline Width (m)", Range(0, 0.03)) = 0.003
        _EdgeOffset ("Edge Offset (m)", Range(0, 0.05)) = 0.006
        _Blur ("Blur", Range(0, 1)) = 0.2
        _Smoothing ("Edge Smoothing (px)", Range(0, 4)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"   // after the mask (Transparent-1) and the occluder
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HandOutline"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front          // draw the back-facing shell only
            ZWrite Off
            ZTest LEqual        // occluder + standoff mask depth cull everything but the thin band
            Blend SrcAlpha One  // additive glow over MR passthrough

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Width;
                float  _EdgeOffset;
                float  _Blur;
                float  _Smoothing;
            CBUFFER_END

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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 nOS = normalize(IN.normalOS);
                float3 pOS = IN.positionOS.xyz + nOS * (_EdgeOffset + _Width);
                float3 pWS = TransformObjectToWorld(pOS);
                OUT.positionHCS = TransformWorldToHClip(pWS);
                OUT.normalWS    = TransformObjectToWorldNormal(nOS);
                OUT.viewWS      = GetWorldSpaceViewDir(pWS); // surface -> camera
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 N = normalize(IN.normalWS);
                half3 V = normalize(IN.viewWS);
                // Back-facing term: 0 at the shell's grazing outer edge, rising as the face turns
                // away from the camera. Fading over the edge width softens the outer edge.
                half facing = saturate(-dot(N, V));
                // Edge width = the artistic _Blur, widened to at least _Smoothing screen pixels
                // (fwidth) so the edge and corners anti-alias instead of reading hard/jagged.
                half edge = max(_Blur, fwidth(facing) * _Smoothing);
                half a = smoothstep(0.0h, max(edge, 1e-4h), facing);
                return half4(_Color.rgb, _Color.a * a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
