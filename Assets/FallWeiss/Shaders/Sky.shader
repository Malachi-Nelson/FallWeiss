// Gradient sky with sun disc + glow (port of the three.js skyMat). Used as RenderSettings.skybox.
Shader "FallWeiss/Sky"
{
    Properties
    {
        _Top ("Top", Color) = (0.36,0.52,0.72,1)
        _Hor ("Horizon", Color) = (0.88,0.84,0.75,1)
        _SunC ("Sun colour", Color) = (1,0.88,0.69,1)
        _SunDir ("Sun direction", Vector) = (0.6,0.25,0.3,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Hor, _SunC; float4 _SunDir;
            CBUFFER_END
            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.dir = i.pos.xyz; return o; }
            half4 frag (V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = saturate(d.y);
                float3 c = lerp(_Hor.rgb, _Top.rgb, pow(h, 0.55));
                if (d.y < 0) c = lerp(_Hor.rgb, _Hor.rgb * 0.72, saturate(-d.y * 5));
                float s = saturate(dot(d, normalize(_SunDir.xyz)));
                // disc + glow. Was 6 / .35 / .12: a 6x HDR disc through bloom was a white sheet when the sun sat low in front of the attacker.
                // The disc fades as it nears the horizon (atmosphere), the glows stay modest so the sky brightens but never blinds.
                float low = saturate(_SunDir.y * 6 + 0.2);                      // 0 at/below the horizon, 1 above ~8 deg
                c += _SunC.rgb * (pow(s, 1200) * lerp(1.2, 2.4, low) + pow(s, 64) * 0.22 + pow(s, 8) * 0.07);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
