Shader "Custom/SimpleOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0, 1, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.03
        // 定义 ZTest 变量：4 代表 LEqual (正常透视), 8 代表 Always (穿墙显示)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest Mode", Float) = 8 
    }
    
    SubShader
    {
        // 渲染队列设为 Transparent+1，确保在普通半透明物体之后渲染
        Tags { "Queue" = "Transparent+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        // ========================================================
        // Pass 1: 制作遮罩 (Mask)
        // 这一步不画颜色，只在 Stencil Buffer 里标记 "这里有角色"
        // ========================================================
        Pass
        {
            Name "Mask"
            ZTest [_ZTestMode]     // 动态控制 (透视效果关键)
            ZWrite on      // 不写入深度
            ColorMask 0     // 【关键】不输出任何颜色 (隐形)
            Cull Off        // 双面都进行标记

            Stencil
            {
                Ref 1       // 标记值为 1
                Comp Always // 总是通过比较
                Pass Replace// 写入标记值
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                // 这里只渲染原始模型位置，不进行扩充
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(0,0,0,0);
            }
            ENDCG
        }

        // ========================================================
        // Pass 2: 绘制轮廓 (Outline)
        // 这一步绘制扩充后的模型，但避开 Pass 1 标记过的区域
        // ========================================================
        Pass
        {
            Name "Outline"
            ZTest [_ZTestMode] // 动态控制
            ZWrite Off
            Cull Front      // 剔除正面，只画背面
            Blend SrcAlpha OneMinusSrcAlpha // 支持半透明

            Stencil
            {
                Ref 1
                Comp NotEqual // 【关键】只有当模板值 不等于 1 时才渲染
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            float _OutlineWidth;
            float4 _OutlineColor;
    
            v2f vert (appdata v)
            {
                v2f o;
                float3 norm = normalize(v.normal);
                // 沿法线扩充
                float4 clipPos = UnityObjectToClipPos(v.vertex + norm * _OutlineWidth);
                o.pos = clipPos;
                o.color = _OutlineColor;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}