Shader "Custom/URP_Outline_Draw"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0, 1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest Mode", Float) = 8 
    }
    
    SubShader
    {
        // 队列 101，保证在遮罩之后渲染
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+101" "RenderType" = "Transparent" }

        Pass
        {
            Name "OutlineDraw"
            Tags { "LightMode" = "UniversalForward" }

            ZTest [_ZTestMode]
            ZWrite Off
            Cull Front  // 剔除正面，缓解低多边形线条碎裂问题
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref 1
                Comp NotEqual // 避开刚刚写了 1 的地方（实现镂空）
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            float _OutlineWidth;
            float4 _OutlineColor;

            Varyings vert(Attributes input) {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                positionWS += normalWS * _OutlineWidth; // 往外推大一圈
                
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = _OutlineColor;
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                return input.color;
            }
            ENDHLSL
        }
    }
}