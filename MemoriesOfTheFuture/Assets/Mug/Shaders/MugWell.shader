// The bottomless well seen through the mug's opening. Draws only where the portal mask marked
// the stencil, ignoring depth (so it goes "through" the real mug's floor). The walls darken with
// depth and carry faint rings, which give strong parallax as the head moves. _Reveal fades it
// in over the real mug's interior (alpha also drives passthrough, so 0 = the real mug).
Shader "Mug/MugWell"
{
    Properties
    {
        _Reveal ("Reveal", Range(0, 1)) = 0
        _RimColor ("Colour at the rim", Color) = (0.16, 0.15, 0.14, 1)
        _DeepColor ("Colour deep down", Color) = (0, 0, 0, 1)
        _Depth ("Depth to full dark (m)", Float) = 0.6
        _RingSpacing ("Ring spacing (m)", Float) = 0.06
        _RingStrength ("Ring strength", Range(0, 1)) = 0.35
        _StencilRef ("Stencil Ref", Int) = 7
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry-10" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Stencil { Ref [_StencilRef] Comp Equal }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor, _DeepColor;
                float _Reveal, _Depth, _RingSpacing, _RingStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float depthBelowRim : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.depthBelowRim = -v.positionOS.y;   // the well mesh has its origin at the rim
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = max(i.depthBelowRim, 0);
                float k = saturate(d / _Depth);
                half3 c = lerp(_RimColor.rgb, _DeepColor.rgb, sqrt(k));
                float ring = 1 - smoothstep(0.0, 0.25, abs(frac(d / _RingSpacing) - 0.5) * 2);
                c *= 1 - _RingStrength * ring * (1 - k);
                return half4(c, _Reveal);
            }
            ENDHLSL
        }
    }
}
