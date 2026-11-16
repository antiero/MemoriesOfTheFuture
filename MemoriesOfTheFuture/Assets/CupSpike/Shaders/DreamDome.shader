// The dream: a sphere around the viewer, world-locked (direction-based, so it never moves with the
// head and doesn't cause motion sickness). It spreads out from the mug's direction as the dream
// deepens (_Spread, radians), and its alpha hides passthrough where it covers. Content: a dark void
// with faint twinkling specks and a slow hypnotic spiral centred on the mug. A head-relative
// vignette (_Vignette) darkens the edges of vision, passthrough included.
Shader "CupSpike/DreamDome"
{
    Properties
    {
        _Depth ("Dream depth", Range(0, 1)) = 0
        _Spread ("Spread from the mug (rad)", Float) = 0
        _Softness ("Edge softness (rad)", Float) = 0.35
        _Vignette ("Vignette", Range(0, 1)) = 0
        _MugDir ("Direction to the mug (world)", Vector) = (0, -1, 0, 0)
        _HeadFwd ("Head forward (world)", Vector) = (0, 0, 1, 0)
        _TopColor ("Void colour, up", Color) = (0.05, 0.02, 0.08, 1)
        _BottomColor ("Void colour, down", Color) = (0.0, 0.0, 0.01, 1)
        _SpiralColor ("Spiral colour", Color) = (0.35, 0.05, 0.04, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry-40" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Front
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _BottomColor, _SpiralColor;
                float4 _MugDir, _HeadFwd;
                float _Depth, _Spread, _Softness, _Vignette;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dirWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.dirWS = ws - TransformObjectToWorld(float3(0, 0, 0));   // from the sphere's centre (the head)
                return o;
            }

            float hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dirWS);
                float3 m = normalize(_MugDir.xyz);
                float fromMug = acos(clamp(dot(d, m), -1, 1));
                float t = _Time.y;

                // Void
                half3 c = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(d.y * 0.5 + 0.5));

                // Hypnotic spiral around the mug's direction: bands rotating slowly inwards.
                float3 ax = normalize(cross(m, abs(m.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 ay = cross(m, ax);
                float around = atan2(dot(d, ay), dot(d, ax));
                float spiral = sin(around * 3 + fromMug * 9 - t * 0.9);
                spiral = smoothstep(0.55, 1.0, spiral) * saturate(1.2 - fromMug * 0.5);
                c += _SpiralColor.rgb * spiral * (0.4 + 0.6 * _Depth);

                // Faint specks that twinkle.
                float3 cell = floor(d * 90);
                float h = hash(cell);
                float speck = step(0.992, h) * (0.5 + 0.5 * sin(t * (1 + h * 3) + h * 40));
                c += speck * 0.5 * _Depth;

                // Where the dream has spread to, and the vignette.
                float dream = 1 - smoothstep(_Spread - _Softness, _Spread, fromMug);
                float off = acos(clamp(dot(d, normalize(_HeadFwd.xyz)), -1, 1));
                float vig = smoothstep(0.35, 1.0, off) * _Vignette;
                float a = saturate(max(dream, vig));
                c = lerp(half3(0, 0, 0), c, dream / max(a, 1e-4));   // vignette-only areas are black
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
