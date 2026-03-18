Shader "Hidden/DrunkRipple"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DistortionStrength ("扭曲强度 (Strength)", Range(0, 0.2)) = 0.05
        _Speed ("向内流动速度 (Speed)", Float) = 8.0
        _Frequency ("圆环密集度 (Frequency)", Float) = 15.0
        _SpiralTwist ("螺旋缠绕度 (Twist)", Float) = 4.0
    }
    SubShader
    {
        // 专用于屏幕后处理的设置
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            float _DistortionStrength;
            float _Speed;
            float _Frequency;
            float _SpiralTwist;

            fixed4 frag (v2f i) : SV_Target
            {
                float2 center = float2(0.5, 0.5);
                float2 delta = i.uv - center;

                // 【关键修复】：根据屏幕长宽比修正坐标空间，保证螺旋线是正圆而不是椭圆
                float aspect = _ScreenParams.x / _ScreenParams.y;
                delta.x *= aspect;

                // 极坐标转换：求当前像素距离中心的 半径(r) 和 角度(theta)
                float r = length(delta);
                float theta = atan2(delta.y, delta.x);

                // ==========================================
                // 构建螺旋波函数 (核心数学公式)
                // r * _Frequency：产生同心圆
                // theta * _SpiralTwist：把同心圆拧成螺旋形
                // + _Time.y * _Speed：加上时间变量，使其不断向内收缩流动
                // ==========================================
                float wave = sin(r * _Frequency + theta * _SpiralTwist + _Time.y * _Speed);

                // 遮罩：中心点(0~0.05)完全不扭曲保护准星，边缘扭曲最大
                float mask = smoothstep(0.05, 0.4, r);

                // 应用扭曲：同时在 半径方向(挤压) 和 角度方向(旋转) 产生变形
                float r_distorted = r + wave * _DistortionStrength * mask;
                float theta_distorted = theta + wave * (_DistortionStrength * 1.5) * mask;

                // 将极坐标转换回普通 UV 坐标 (记得除以刚刚乘上的屏幕比例)
                float2 baseUV;
                baseUV.x = center.x + (r_distorted * cos(theta_distorted)) / aspect;
                baseUV.y = center.y + (r_distorted * sin(theta_distorted));

                // ==========================================
                // 进阶视觉效果：RGB 色散重影 (Chromatic Aberration)
                // 让红绿蓝三原色稍微错开一点点，制造极致的眩晕感
                // ==========================================
                float2 uvR = center + ((r_distorted + 0.008 * mask) * cos(theta_distorted)) / aspect * float2(1,0) 
                           + ((r_distorted + 0.008 * mask) * sin(theta_distorted)) * float2(0,1);
                           
                float2 uvB = center + ((r_distorted - 0.008 * mask) * cos(theta_distorted)) / aspect * float2(1,0) 
                           + ((r_distorted - 0.008 * mask) * sin(theta_distorted)) * float2(0,1);

                fixed4 col;
                // 分离采样：红色通道拿扩大的UV，蓝色通道拿缩小的UV，绿色拿原本的UV
                col.r = tex2D(_MainTex, uvR).r;
                col.g = tex2D(_MainTex, baseUV).g;
                col.b = tex2D(_MainTex, uvB).b;
                col.a = 1.0;

                return col;
            }
            ENDCG
        }
    }
}