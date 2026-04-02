Shader "Custom/URP_Outline"
{
    Properties
    {
        // 这三个属性名完美对应了你 PropTarget.cs 里的 C# 代码！
        _OutlineColor ("Outline Color", Color) = (1, 1, 0, 1)
        _OutlineWidth ("Outline Width", Range(0.0, 0.2)) = 0.03
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest Mode", Float) = 8 // 8是Always(穿墙透视), 4是LEqual(被遮挡)
    }
    SubShader
    {
        // 声明这是 URP 管线，并在透明队列稍后渲染
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" }

        Pass
        {
            Name "Outline"
            // 剔除正面，渲染背面，从而形成外扩的描边
            Cull Front
            // 关闭深度写入，防止描边自己挡住自己
            ZWrite Off
            // 深度测试模式，由 C# 代码动态控制
            ZTest [_ZTestMode]
            // 开启透明混合支持
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                // 沿着法线方向把顶点往外挤，形成变大的外壳
                float3 pos = input.positionOS.xyz + input.normalOS * _OutlineWidth;
                output.positionCS = TransformObjectToHClip(pos);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 直接返回 C# 设置的颜色
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}