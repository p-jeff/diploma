// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT
// Modified: hue/tint/opacity + saturation + shockwave uniforms for animation

Shader "Gsplat/Standard"
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
            #include "Gsplat.hlsl"
            #ifdef UNCOMPRESSED
            #include "GsplatUncompressed.hlsl"
            #endif
            #ifdef SPARK
            #include "GsplatSpark.hlsl"
            #endif


            bool _GammaToLinear;
            int _SplatCount;
            int _SplatInstanceSize;
            int _SHDegree;
            float4x4 _MATRIX_M;
            float _Brightness;
            float _ScaleFactor;
            StructuredBuffer<uint> _OrderBuffer;

            // Animation uniforms (set via Shader.SetGlobal*)
            float  _GsplatHueShift;
            float4 _GsplatTintColor;
            float  _GsplatOpacityMul;
            float  _GsplatDesat;            // 0 = identity (default), 1 = max desaturation envelope
            float4 _GsplatShockCenter;      // xyz = world-space center
            float4 _GsplatShockAxis;        // xyz = direction; if length<0.5 -> radial mode
            float  _GsplatShockProgress;    // distance along axis (or radius) where wave front sits, in meters
            float  _GsplatShockBandWidth;   // soft transition width in meters

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
                float c = hsv.z * hsv.y;
                float x = c * (1.0 - abs(fmod(h, 2.0) - 1.0));
                float3 rgb;
                if      (h < 1) rgb = float3(c, x, 0);
                else if (h < 2) rgb = float3(x, c, 0);
                else if (h < 3) rgb = float3(0, c, x);
                else if (h < 4) rgb = float3(0, x, c);
                else if (h < 5) rgb = float3(x, 0, c);
                else            rgb = float3(c, 0, x);
                return rgb + (hsv.z - c);
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
                float4 color: COLOR;
                float  waveT : TEXCOORD1;   // 0 = ahead of wave (greyscale), 1 = behind (colored)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Per-splat shockwave evaluation. modelPos = local-space center of this splat.
            // Returns waveT in [0,1]: 0 = "ahead of wave" (not yet swept), 1 = "behind" (already swept).
            // The fragment uses this to drive desaturation off; default-zero uniforms are harmless because
            // the desat envelope (_GsplatDesat) defaults to 0 (no effect).
            float EvalShockwave(float3 modelPos)
            {
                float3 worldPos = mul(_MATRIX_M, float4(modelPos, 1.0)).xyz;
                float3 d3 = worldPos - _GsplatShockCenter.xyz;

                float axisLen = length(_GsplatShockAxis.xyz);
                float dist = (axisLen < 0.5)
                    ? length(d3)
                    : dot(d3, _GsplatShockAxis.xyz / axisLen);

                float bw = max(_GsplatShockBandWidth, 0.0001);
                return 1.0 - smoothstep(_GsplatShockProgress - bw, _GsplatShockProgress, dist);
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = discardVec;
                o.waveT = 1.0;

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

                o.vertex = center.proj + float4(corner.offset.x, _ProjectionParams.x * corner.offset.y, 0, 0);
                o.color = color;
                o.uv = corner.uv;

                // Read object-space splat center for the wave evaluation.
                // SplatCenter does not carry the local position directly, but the model-view
                // origin can be recovered: worldPos = center.modelView * (0,0,0,1) is the splat origin in view space;
                // simpler: re-load position from the buffer directly via source.id when available.
                #ifdef UNCOMPRESSED
                {
                    float3 localPos = _PositionBuffer[source.id];
                    o.waveT = EvalShockwave(localPos);
                }
                #else
                {
                    // Spark path: skip per-splat wave evaluation; use a homogeneous fallback (no shockwave).
                    o.waveT = 1.0;
                }
                #endif

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

                // Hue shift
                if (abs(_GsplatHueShift) > 0.001)
                {
                    float3 hsv = GsplatRGBtoHSV(saturate(col));
                    hsv.x = frac(hsv.x + _GsplatHueShift);
                    col = GsplatHSVtoRGB(hsv);
                }

                // Tint (default white = no effect)
                col *= _GsplatTintColor.rgb;

                // Brightness
                col *= _Brightness;

                // Desaturation envelope, modulated per-splat by the wave.
                // Default _GsplatDesat = 0 (no effect). waveT defaults to 1 (no wave => "behind", colored).
                // effectiveDesat = baseDesat * (1 - waveT)  -> ahead of wave = grey, behind = colored.
                float effectiveDesat = saturate(_GsplatDesat * (1.0 - i.waveT));
                if (effectiveDesat > 0.001)
                {
                    float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
                    col = lerp(col, float3(lum, lum, lum), effectiveDesat);
                }

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
