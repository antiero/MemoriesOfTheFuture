// Rounded rectangle / ring for the hand menu, prompts and scanning guide.
// Drawn on a unit quad; _Size is the quad's size in metres so corner radius and border
// stay round whatever the aspect. _Ring > 0 draws a progress ring instead (0..1 = fill).
Shader "PictureChanges/UI"
{
    Properties
    {
        _Color ("Fill", Color) = (0.08, 0.08, 0.1, 0.72)
        _BorderColor ("Border", Color) = (1, 1, 1, 0.35)
        _Size ("Size (m)", Vector) = (0.1, 0.1, 0, 0)
        _Radius ("Corner radius (m)", Float) = 0.012
        _Border ("Border width (m)", Float) = 0.0012
        _Ring ("Ring mode (0 off, else progress 0..1)", Float) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _BorderColor;
                float4 _Size;
                float _Radius, _Border, _Ring, _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 size = max(_Size.xy, 1e-4);
                float2 p = (i.uv - 0.5) * size;                  // metres from centre
                float aa = max(fwidth(p.x), 1e-5) * 1.2;

                if (_Ring > 0)
                {
                    // Progress ring: thin circle, filled clockwise from 12 o'clock.
                    float r = min(size.x, size.y) * 0.5 - _Border;
                    float d = abs(length(p) - r) - _Border;
                    float a = atan2(p.x, p.y) / (2.0 * PI) + 0.5;   // 0..1 clockwise from top
                    float on = step(a, saturate(_Ring - 1e-4));
                    half4 track = _BorderColor;
                    half4 fill = _Color;
                    half4 col = lerp(track, fill, on);
                    col.a *= saturate(0.5 - d / aa) * _Opacity;
                    return col;
                }

                float2 q = abs(p) - (size * 0.5 - _Radius);
                float dist = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _Radius;   // <0 inside
                float inside = saturate(0.5 - dist / aa);
                float border = saturate(0.5 - (abs(dist + _Border * 0.5) - _Border * 0.5) / aa);
                half4 col = _Color;
                col.rgb = lerp(col.rgb, _BorderColor.rgb, border * _BorderColor.a);
                col.a = max(col.a * inside, border * _BorderColor.a) * _Opacity;
                return col;
            }
            ENDHLSL
        }
    }
}
