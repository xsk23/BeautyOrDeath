Shader "Custom/URP_Outline_Mask"
{
    Properties {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest Mode", Float) = 8
    }
    SubShader {
        // 队列 100，确保比下面的描边先渲染
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "RenderPipeline"="UniversalPipeline" }
        Pass {
            Name "Mask"
            Tags { "LightMode" = "UniversalForward" } // 伪装成正常光照Pass，骗过URP让它必须渲染
            
            ZTest [_ZTestMode]
            ZWrite Off
            ColorMask 0 // 隐形涂料，不显示颜色
            Cull Off

            Stencil {
                Ref 1
                Comp Always
                Pass Replace // 在屏幕上刻下数字 1
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes input) {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}