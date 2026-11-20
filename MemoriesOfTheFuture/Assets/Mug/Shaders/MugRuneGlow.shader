// The client's rune, glowing far down at the bottom of the well. Additive, inside the portal
// stencil only, drawn after the well. The pulse is slow and uneven, like a heartbeat.
// With _StencilComp = Always and _ZTestMode = LEqual it also draws the runes floating in the dream.
Shader "Mug/MugRuneGlow"
{
    Properties
    {
        _MainTex ("Rune (alpha = strokes)", 2D) = "white" {}
        _Color ("Glow colour", Color) = (1, 0.25, 0.1, 1)
        _Intensity ("Intensity", Float) = 1.6
        _Reveal ("Reveal", Range(0, 1)) = 0
        _StencilRef ("Stencil Ref", Int) = 7
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comp (Equal = inside the mug only)", Float) = 3
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest (Always = through the mug floor)", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry-5" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest [_ZTestMode]
        Cull Off
        Blend SrcAlpha One, One One
        Stencil { Ref [_StencilRef] Comp [_StencilComp] }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _Intensity, _Reveal;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                float t = _Time.y;
                float pulse = 0.65 + 0.35 * pow(saturate(sin(t * 2.1) * 0.5 + 0.5), 3) + 0.15 * sin(t * 5.3);
                half k = a * _Reveal * pulse;
                return half4(_Color.rgb * _Intensity * k, k);
            }
            ENDHLSL
        }
    }
}
