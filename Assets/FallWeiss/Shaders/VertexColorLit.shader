// Simple URP lit shader driven by mesh vertex colours (terrain, buildings, trees, decor).
// _Detail = 1 (terrain only): per-pixel ground texture. Three octaves of value noise on an INTEGER hash — the previous
// sin()-based hash was quasi-periodic (~4 cells) and drew an axis-aligned checkerboard on every field — rotated against the
// terrain grid, each fading with distance so nothing shimmers on the horizon. Vertices with alpha < 1 are ploughed fields:
// furrows are drawn here per pixel instead of being sampled on the 2 m vertex grid (which produced moiré).
Shader "FallWeiss/VertexColorLit"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Detail ("Ground detail", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; float _Detail;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : COLOR;
                half   fog        : TEXCOORD2;
            };

            // integer hash (PCG-style mix) -> [0,1); no visible structure between neighbouring cells
            float hash21(float2 c)
            {
                uint2 u = uint2(int2(floor(c)) + 32768);
                uint h = u.x * 374761393u ^ u.y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return float(h & 0x00FFFFFFu) * (1.0 / 16777216.0);
            }
            float vnoise(float2 q)
            {
                float2 fl = floor(q), f = frac(q); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(fl), hash21(fl + float2(1, 0)), f.x), lerp(hash21(fl + float2(0, 1)), hash21(fl + float2(1, 1)), f.x), f.y);
            }

            Varyings vert (Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color * _BaseColor;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light l = GetMainLight(sc);
                float3 n = normalize(i.normalWS);
                half nd = saturate(dot(n, l.direction));
                half3 amb = SampleSH(n);
                if (_Detail > 0.5)
                {
                    float2 p = i.positionWS.xz;
                    float dist = length(i.positionWS - GetCameraPositionWS());
                    float2 pA = float2(p.x * 0.80 - p.y * 0.60, p.x * 0.60 + p.y * 0.80);   // 37°
                    float2 pB = float2(p.x * 0.29 + p.y * 0.96, -p.x * 0.96 + p.y * 0.29);  // 73°
                    float nA = vnoise(pA * 0.033 + 3.1);             // ~30 m patches: soil, moisture, grazing
                    float nB = vnoise(pB * 0.17 + 11.0);             // ~6 m tufts
                    float nC = hash21(floor(p * 1.7));               // 0.6 m grain, near the player only
                    float k = 1 + (nA - 0.5) * 0.14
                                + (nB - 0.5) * 0.12 * saturate(1 - dist / 450)
                                + (nC - 0.5) * 0.10 * saturate(1 - dist / 90);
                    if (i.color.a < 0.75)
                    {   // ploughed field: 1.6 m furrows along the same rotated axis the CPU uses for field parcels (a = 0.5 rad)
                        float fx = dot(p, float2(0.8776, 0.4794));
                        float fur = 0.5 + 0.5 * cos(fx * 3.927);     // 2π / 1.6
                        k *= 1 - 0.09 * fur * saturate(1 - dist / 160);
                    }
                    i.color.rgb *= k;
                }
                half3 c = i.color.rgb * (amb + l.color * nd * l.shadowAttenuation);
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack Off
}
