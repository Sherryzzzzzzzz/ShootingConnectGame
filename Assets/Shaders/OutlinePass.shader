Shader "Custom/OutlinePass"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Float) = 0.03
    }
    SubShader
    {
        // Queue = Geometry+1：在普通不透明物体之后渲染，先被角色深度遮挡，
        // 只在角色轮廓（背面外扩的环形区域）处可见，且与角色绘制顺序无关。
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        LOD 100

        Pass
        {
            Name "Outline"
            Cull Front          // 只画背面（外扩网格的远离相机面）
            ZWrite Off          // 不写深度，避免错误遮挡后续不透明物体
            ZTest LEqual        // 被角色/墙体遮挡处不显示，仅轮廓环可见

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                // 世界空间归一化法线方向外扩：
                // TransformObjectToWorldNormal 用逆转置处理非均匀缩放，
                // normalize 保证外扩方向正确；宽度单位为世界米（scale=1 时与物体空间一致）。
                float3 posWS    = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                posWS += normalWS * _OutlineWidth;
                o.positionHCS = TransformWorldToHClip(posWS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
