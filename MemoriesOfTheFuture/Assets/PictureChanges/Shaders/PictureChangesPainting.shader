// The virtual painting laid over the real one.
//  - _Reveal 0..1: watercolour-bleed reveal. Pigment spreads outwards from the centre along a
//    noisy front, with a darker "tide line" at the wet edge, like a watercolour wash drying.
//  - _Feather: soft edges so the overlay melts into the paper instead of a hard cut.
//  - _Exposure / _Saturation / _Grain: match the passthrough look (passthrough is darker and
//    noisier than a clean texture), so the painting does not look pasted on.
Shader "PictureChanges/Painting"
{
    Properties
    {
        [MainTexture] _MainTex ("Image", 2D) = "white" {}
        _Reveal ("Reveal", Range(0, 1)) = 1
        _Feather ("Edge feather (UV)", Range(0, 0.05)) = 0.006
        _Exposure ("Exposure", Range(0.3, 1.5)) = 0.9
        _Saturation ("Saturation", Range(0, 1.5)) = 0.95
        _Grain ("Grain", Range(0, 0.2)) = 0.035
        _TideLine ("Tide line strength", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

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
                float _Reveal, _Feather, _Exposure, _Saturation, _Grain, _TideLine;
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

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // Look matching.
                half luma = dot(col.rgb, half3(0.299, 0.587, 0.114));
                col.rgb = lerp(luma.xxx, col.rgb, _Saturation) * _Exposure;
                float grain = hash(i.uv * 1731.0 + frac(_Time.y * 23.7)) - 0.5;
                col.rgb += grain * _Grain;

                // Soft edges.
                float2 e = min(i.uv, 1.0 - i.uv);
                float edge = smoothstep(0.0, max(_Feather, 1e-4), min(e.x, e.y));

                // Watercolour bleed: a noisy front that grows from the centre.
                float2 c = i.uv - 0.5;
                c.y *= 1.35;
                float field = length(c) * 1.25 + (fbm(i.uv * 6.0) - 0.5) * 0.55;   // 0 centre .. ~1 corners
                float front = _Reveal * 1.35 - 0.1;
                float wet = smoothstep(front, front - 0.06, field);                 // 1 inside the wash
                float tide = smoothstep(0.06, 0.0, abs(field - (front - 0.03)));    // the wet edge
                col.rgb *= 1.0 - tide * _TideLine * (1.0 - step(0.999, _Reveal)) * 0.5;

                col.a = saturate(wet + step(0.999, _Reveal)) * edge;
                return col;
            }
            ENDHLSL
        }
    }
}
