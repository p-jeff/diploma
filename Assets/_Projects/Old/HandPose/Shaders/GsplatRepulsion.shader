// Based on Gsplat/ProximityScale (MIT License)
// Added: world-space splat repulsion — splats physically displace away from a target point

Shader "Gsplat/Repulsion"
{
    Properties {}
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            ZWrite Off
            Blend One OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require compute
            #pragma multi_compile SH_BANDS_0 SH_BANDS_1 SH_BANDS_2 SH_BANDS_3
            #pragma multi_compile UNCOMPRESSED SPARK

            #include "UnityCG.cginc"
            #include "Packages/wu.yize.gsplat/Runtime/Shaders/Gsplat.hlsl"
            #ifdef UNCOMPRESSED
            #include "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatUncompressed.hlsl"
            #endif
            #ifdef SPARK
            #include "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSpark.hlsl"
            #endif

            bool _GammaToLinear;
            int _SplatCount;
            int _SplatInstanceSize;
            int _SHDegree;
            float4x4 _MATRIX_M;
            float _Brightness;
            float _ScaleFactor;
            StructuredBuffer<uint> _OrderBuffer;

            // Animation uniforms (compatible with GsplatHueAnimator)
            float _GsplatHueShift;
            float4 _GsplatTintColor;
            float _GsplatOpacityMul;

            // Repulsion uniforms (set by GsplatHandRepulsion.cs)
            float3 _GsplatRepulsionCenter;   // world-space hand/target position
            float  _GsplatRepulsionRadius;   // sphere of influence (metres)
            float  _GsplatRepulsionStrength; // max displacement at centre (metres)

            // RGB <-> HSV (carried over for hue-shift compat)
            float3 GsplatRGBtoHSV(float3 rgb)
            {
                float cmax = max(rgb.r, max(rgb.g, rgb.b));
                float cmin = min(rgb.r, min(rgb.g, rgb.b));
                float delta = cmax - cmin;
                float h = 0;
                if (delta > 0.0001)
                {
                    if (cmax == rgb.r)      h = fmod((rgb.g - rgb.b) / delta, 6.0);
                    else if (cmax == rgb.g) h = (rgb.b - rgb.r) / delta + 2.0;
                    else                    h = (rgb.r - rgb.g) / delta + 4.0;
                    h /= 6.0;
                    if (h < 0) h += 1.0;
                }
                float s = (cmax > 0.0001) ? delta / cmax : 0;
                return float3(h, s, cmax);
            }

            float3 GsplatHSVtoRGB(float3 hsv)
            {
                float h = hsv.x * 6.0;
                float s = hsv.y;
                float v = hsv.z;
                float c = v * s;
                float x = c * (1.0 - abs(fmod(h, 2.0) - 1.0));
                float m = v - c;
                float3 rgb;
                if      (h < 1.0) rgb = float3(c, x, 0);
                else if (h < 2.0) rgb = float3(x, c, 0);
                else if (h < 3.0) rgb = float3(0, c, x);
                else if (h < 4.0) rgb = float3(0, x, c);
                else if (h < 5.0) rgb = float3(x, 0, c);
                else              rgb = float3(c, 0, x);
                return rgb + m;
            }

            struct appdata
            {
                float4 vertex : POSITION;
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceID : SV_InstanceID;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            bool InitSource(appdata v, out SplatSource source)
            {
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                source.order = v.instanceID * _SplatInstanceSize + asuint(v.vertex.z);
                #else
                source.order = unity_InstanceID * _SplatInstanceSize + asuint(v.vertex.z);
                #endif

                if (source.order >= _SplatCount)
                    return false;

                source.id = _OrderBuffer[source.order];
                source.cornerUV = float2(v.vertex.x, v.vertex.y) * _ScaleFactor;
                return true;
            }

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = discardVec;

                SplatSource source;
                if (!InitSource(v, source))
                    return o;

                SplatCenter center;
                SplatCorner corner;
                float4 color;
                if (!InitSplatData(source, mul(UNITY_MATRIX_V, _MATRIX_M), center, corner, color))
                    return o;

                #ifndef SH_BANDS_0
                float3 dir = normalize(mul(center.view, (float3x3)center.modelView));
                float3 sh[SH_COEFFS];
                InitSH(source.id, sh);
                color.rgb += EvalSH(sh, dir, _SHDegree);
                #endif

                ClipCorner(corner, color.w);

                // --- World-space hand repulsion ---
                // Read splat local position (same buffer already read by InitSplatData,
                // so this is a GPU cache hit — no extra bandwidth cost).
                #ifdef UNCOMPRESSED
                float3 repLocalPos = _PositionBuffer[source.id];
                #endif
                #ifdef SPARK
                float3 repLocalPos, _repScale; float4 _repColor, _repQuat;
                UnpackSplat(_PackedSplatsBuffer[source.id], _repColor, repLocalPos, _repScale, _repQuat);
                #endif

                float3 repWorldPos = mul(_MATRIX_M, float4(repLocalPos, 1.0)).xyz;
                float3 toSplat = repWorldPos - _GsplatRepulsionCenter;
                float repDist = length(toSplat);

                // Only displace splats within the radius and when repulsion is active
                [branch]
                if (_GsplatRepulsionRadius > 0.001 && repDist < _GsplatRepulsionRadius && repDist > 0.001)
                {
                    // Quadratic falloff: strongest at center, zero at radius
                    float t = 1.0 - saturate(repDist / _GsplatRepulsionRadius);
                    t = t * t;

                    float3 pushDir = toSplat / repDist;
                    float3 displaced = repWorldPos + pushDir * (t * _GsplatRepulsionStrength);

                    // Reproject displaced world position to clip space.
                    // corner.offset (covariance ellipse) stays unchanged — only the centre moves.
                    center.proj = mul(UNITY_MATRIX_VP, float4(displaced, 1.0));
                }
                // -----------------------------------

                o.vertex = center.proj + float4(corner.offset.x, _ProjectionParams.x * corner.offset.y, 0, 0);
                o.color = color;
                o.uv = corner.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float A = dot(i.uv, i.uv);
                if (A > 1.0) discard;

                float2 absUV = abs(i.uv);
                float maxUV = max(absUV.x, absUV.y);

                float falloff = -exp((maxUV - _ScaleFactor * 1.16) * 25 * _ScaleFactor);
                float alpha = (exp(-A * 4.0) + falloff) * i.color.a;

                if (alpha < 1.0 / 255.0) discard;

                float3 col = i.color.rgb;

                if (abs(_GsplatHueShift) > 0.001)
                {
                    float3 hsv = GsplatRGBtoHSV(saturate(col));
                    hsv.x = frac(hsv.x + _GsplatHueShift);
                    col = GsplatHSVtoRGB(hsv);
                }

                float3 tint = (_GsplatTintColor.a > 0.0001) ? _GsplatTintColor.rgb : float3(1, 1, 1);
                col *= tint;

                col *= _Brightness;

                if (_GammaToLinear)
                    col = GammaToLinearSpace(col);

                float opacityMul = (_GsplatOpacityMul > 0.0001) ? _GsplatOpacityMul : 1.0;
                alpha *= opacityMul;

                return float4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
