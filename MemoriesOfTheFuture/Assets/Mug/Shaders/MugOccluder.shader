// Invisible blocker for the real mug: writes depth, and colour and alpha (0,0,0,0), so the real mug
// shows through in passthrough even when virtual things (the dream) were drawn behind it first,
// and virtual things drawn later behind it are hidden. Used on the proxy mug's mesh.
Shader "Mug/MugOccluder"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-30" "RenderPipeline"="UniversalPipeline" }
        ZWrite On
        Blend Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
