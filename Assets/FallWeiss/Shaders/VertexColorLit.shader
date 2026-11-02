// Simple URP lit shader driven by mesh vertex colours (terrain, buildings, trees, decor).
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
                {   // break up flat vertex colours: two octaves of cheap value noise + fine grain, fading with distance
                    float2 p = i.positionWS.xz;
                    float n1 = frac(sin(dot(floor(p * 1.7), float2(12.9898, 78.233))) * 43758.5453);
                    float2 q = p * 0.18; float2 f = frac(q); float2 fl = floor(q); f = f * f * (3 - 2 * f);
                    float a = frac(sin(dot(fl, float2(127.1, 311.7))) * 43758.5), b = frac(sin(dot(fl + float2(1,0), float2(127.1, 311.7))) * 43758.5);
                    float c2 = frac(sin(dot(fl + float2(0,1), float2(127.1, 311.7))) * 43758.5), d2 = frac(sin(dot(fl + float2(1,1), float2(127.1, 311.7))) * 43758.5);
                    float n2 = lerp(lerp(a, b, f.x), lerp(c2, d2, f.x), f.y);
                    float dist = length(i.positionWS - GetCameraPositionWS());
                    float k = 1 + ((n1 - 0.5) * 0.10 * saturate(1 - dist / 90) + (n2 - 0.5) * 0.16);
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
