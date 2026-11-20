// The mug's opening: a disc at the rim that marks the stencil buffer wherever the opening is
// visible (depth-tested against the occluder, so the outside wall still hides it). Draws nothing.
Shader "Mug/MugPortalMask"
{
    Properties
    {
        _StencilRef ("Stencil Ref", Int) = 7
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-20" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest LEqual
        ColorMask 0
        Cull Off
        Stencil { Ref [_StencilRef] Comp Always Pass Replace }

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
