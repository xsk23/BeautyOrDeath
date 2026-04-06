Shader "Custom/URP_Outline_Hollow"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0, 1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest Mode", Float) = 8 
    }
    
    SubShader
    {
        // 渲染队列设置为 Transparent+100，确保在所有不透明物体（包括墙壁）之后渲染
        Tags 
        { 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+100" 
            "RenderType" = "Transparent" 
        }

        // ========================================================
        // Pass 1: 制作遮罩
        // 使用 SRPDefaultUnlit 标签强制 URP 渲染这个 Pass
        // ========================================================
        Pass
        {
            Name "OutlineMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            
            ZTest [_ZTestMode]
            ZWrite Off
            ColorMask 0
            Cull Off

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
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

            half4 frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // ========================================================
        // Pass 2: 绘制轮廓
        // 使用 UniversalForward 标签
        // ========================================================
        Pass
        {
            Name "OutlineDraw"
            Tags { "LightMode" = "UniversalForward" }

            ZTest [_ZTestMode]
            ZWrite Off
            Cull Front  // 剔除正面，只画背面，防止低多边形内部线条乱跳
            
            // 开启混合模式，防止黑边
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref 1
                Comp NotEqual // 只有不等于1的地方（即模型边缘外）才渲染
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
                
                // 沿着法线方向挤出
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                // 这里的挤出逻辑稍微偏移一点，解决穿墙时的碎裂感
                positionWS += normalWS * _OutlineWidth;
                
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