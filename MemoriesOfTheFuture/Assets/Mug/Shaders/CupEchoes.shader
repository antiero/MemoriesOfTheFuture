// Echoes from the held cup: a soft disc around the mug, facing the viewer, that shows the real room
// (the passthrough camera's image, reprojected onto the disc) warped by ripples running outward from
// the cup, with ghost copies of the cup swelling out of it and fading. The disc is drawn in grey with
// a pale tint, matching the drained passthrough, and fades out towards its edge, so it melts into the
// real passthrough around it. It fades out over the mug itself (_Clear), so the real mug stays
// crisp, and ignores depth: the real table has none on the headset, and this keeps the desktop the same.
//
// The image (_EchoTex) is projected with _EchoWorldToCam (world -> camera space, +Z forward) and the
// camera's viewport mapping (_CamUV: viewport = xy/z * _CamUV.xy + _CamUV.zw), set by CupEchoes.
// On the headset it's the passthrough camera's image; in the desktop test build, a copy of the view.
Shader "Mug/CupEchoes"
{
    Properties
    {
        _EchoTex ("Camera image", 2D) = "black" {}
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Centre ("Cup centre (world)", Vector) = (0, 0, 0, 0)
        _CamUV ("Camera viewport mapping", Vector) = (1, 1, 0.5, 0.5)
        _Radius ("Disc radius (m)", Float) = 0.3
        _Clear ("Clear over the mug: from, to (m)", Vector) = (0.05, 0.085, 0, 0)
        _Expand ("Echo growth (x the real size)", Float) = 1.8
        _Speed ("Echoes per second", Float) = 0.3
        _Ripple ("Ripple strength (m)", Float) = 0.012
        _Mono ("Monochrome", Range(0, 1)) = 0.85
        _Tint ("Tint", Color) = (0.8, 0.88, 1, 1)
        _RingColor ("Wavefront colour", Color) = (0.9, 0.25, 0.2, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_EchoTex); SAMPLER(sampler_EchoTex);
            float4x4 _EchoWorldToCam;   // global, set by CupEchoes (world -> passthrough camera space)

            CBUFFER_START(UnityPerMaterial)
                float4 _EchoTex_ST;
                float4 _Centre, _CamUV, _Clear;
                half4 _Tint, _RingColor;
                float _Intensity, _Radius, _Expand, _Speed, _Ripple, _Mono;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                return o;
            }

            // The real room as seen at world point q, and how usable that sample is (0 off the image).
            half4 Room(float3 q)
            {
                float3 cs = mul(_EchoWorldToCam, float4(q, 1)).xyz;
                if (cs.z <= 0.01) return 0;
                float2 uv = cs.xy / cs.z * _CamUV.xy + _CamUV.zw;
                half3 c = SAMPLE_TEXTURE2D_LOD(_EchoTex, sampler_EchoTex, uv, 0).rgb;
                float2 e = min(uv, 1 - uv);
                return half4(c, saturate(min(e.x, e.y) * 20));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 C = _Centre.xyz;
                float3 off = i.posWS - C;
                float r = length(off);
                float rn = r / _Radius;
                if (rn >= 1 || _Intensity <= 0.001) return 0;
                float3 dir = off / max(r, 1e-4);
                float t = _Time.y;

                // Heat-haze ripples running outward, strongest near the cup.
                float wave = sin(r * 70 - t * 5.5) * (0.65 + 0.35 * sin(t * 1.3 + r * 23));
                float3 swirl = normalize(cross(dir, float3(0.3, 1, 0.2))) * sin(t * 0.8 + r * 30) * 0.4;
                float k = _Ripple * _Intensity * (1 - rn);
                half4 room = Room(i.posWS + (dir * wave + swirl) * k);

                // Ghost copies of the cup: each one swells out of it and fades, four at a time.
                half3 echo = 0; half ew = 0; half ring = 0;
                [unroll] for (int n = 0; n < 4; n++)
                {
                    float ph = frac(t * _Speed + n * 0.25);
                    float s = 1 + ph * _Expand;
                    float w = sin(ph * PI); w *= w;
                    half4 g = Room(C + off / s);
                    echo += g.rgb * w * g.a;
                    ew += w * g.a;
                    // A faint wavefront at the edge of each echo.
                    float front = (rn - ph) * 9;
                    ring += exp(-front * front) * w;
                }
                echo /= max(ew, 1e-3);
                half3 c = lerp(room.rgb, echo, 0.45 * saturate(ew));

                // Drained, with a pale tint, and a faint red pulse on the wavefronts.
                half grey = dot(c, half3(0.299, 0.587, 0.114));
                c = lerp(c, grey * _Tint.rgb, _Mono);
                c += _RingColor.rgb * ring * 0.12 * _Intensity;

                float edge = (1 - smoothstep(0.45, 1.0, rn)) * smoothstep(_Clear.x, _Clear.y, r);
                float a = _Intensity * edge * room.a;
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
