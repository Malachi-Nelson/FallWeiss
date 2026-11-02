// Unlit alpha-blended (or additive) shader for clouds, smoke, fire, rings, beams, rain and tracers.
// Multiplies texture * _Color * vertex colour (so particles work too).
Shader "FallWeiss/Transparent"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        [HideInInspector] _DstBlend ("Dst blend", Float) = 10
        [HideInInspector] _FogOn ("Fog", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha [_DstBlend]
        ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; half4 _Color; float _DstBlend; float _FogOn;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; half fog : TEXCOORD1; };
            V vert (A i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = TRANSFORM_TEX(i.uv, _MainTex); o.col = i.col * _Color; o.fog = ComputeFogFactor(o.pos.z); return o;
            }
            half4 frag (V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.col;
                if (_FogOn > 0.5) { if (_DstBlend < 5) c.rgb *= ComputeFogIntensity(i.fog); else c.rgb = MixFog(c.rgb, i.fog); }
                return c;
            }
            ENDHLSL
        }
    }
}
