// Desktop debug build only: the stand-in "real room". Simple shaded colour with a faint world-space
// grid, drawn before the dream (so the dream covers it like it covers passthrough), and tinted by
// the global _CupRoomLook (brightness, contrast, saturation), which RoomLook sets where the headset
// would change passthrough. DesktopSim turns its depth writes off (_ZWrite) during the dream, so the
// dream's splat room isn't cut by stand-in walls and table, as nothing cuts it over passthrough.
Shader "Mug/DesktopRoom"
{
    Properties
    {
        _Color ("Colour", Color) = (0.7, 0.68, 0.62, 1)
        _Grid ("Grid strength", Range(0, 1)) = 0.08
        [Enum(Off, 0, On, 1)] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-100" "RenderPipeline"="UniversalPipeline" }
        ZWrite [_ZWrite]
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Grid;
            CBUFFER_END
            float4 _CupRoomLook;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float light = 0.55 + 0.45 * saturate(dot(n, normalize(float3(0.3, 0.8, -0.45))));
                float3 g = abs(frac(i.positionWS * 2.0) - 0.5);
                float3 w = 1 - abs(n);   // ignore the axis along the normal
                float grid = max(max(step(0.485, g.x) * w.x, step(0.485, g.y) * w.y), step(0.485, g.z) * w.z);
                half3 c = _Color.rgb * light * (1 - _Grid * grid);

                half lum = dot(c, half3(0.3, 0.59, 0.11));
                c = lerp(lum.xxx, c, 1 + _CupRoomLook.z);
                c = (c - 0.5) * (1 + _CupRoomLook.y) + 0.5;
                c += _CupRoomLook.x;
                return half4(saturate(c), 1);
            }
            ENDHLSL
        }
    }
}
