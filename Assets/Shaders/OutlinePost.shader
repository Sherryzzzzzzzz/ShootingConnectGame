Shader "Hidden/OutlinePost"
{
    Properties
    {
        _OutlineWidth ("Outline Width (px)", Float) = 1
        _DepthThreshold ("Depth Threshold (m)", Float) = 0.05
        _NormalThreshold ("Normal Threshold", Float) = 0.15
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        ZWrite Off Cull Off

        Pass
        {
            Name "OutlinePost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _OutlineWidth;
            float _DepthThreshold;
            float _NormalThreshold;
            half4 _OutlineColor;

            // 当前像素与 4 邻域的深度边缘（线性化深度，单位米，远近距离都正确）
            float DepthEdge(float2 uv, float2 texel)
            {
                float d  = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float dL = LinearEyeDepth(SampleSceneDepth(uv - float2(texel.x, 0)), _ZBufferParams);
                float dR = LinearEyeDepth(SampleSceneDepth(uv + float2(texel.x, 0)), _ZBufferParams);
                float dU = LinearEyeDepth(SampleSceneDepth(uv - float2(0, texel.y)), _ZBufferParams);
                float dD = LinearEyeDepth(SampleSceneDepth(uv + float2(0, texel.y)), _ZBufferParams);

                float maxD = max(max(d, dL), max(dR, max(dU, dD)));
                float minD = min(min(d, dL), min(dR, min(dU, dD)));
                return maxD - minD; // 邻域深度跨度
            }

            // 法线夹角差：1 - dot(n0, n1)。对零向量（未渲染区域）安全，且与法线编码无关。
            float AngleDiff(float3 a, float3 b)
            {
                float la = length(a);
                float lb = length(b);
                if (la < 1e-4 || lb < 1e-4) return 0.0;
                return 1.0 - saturate(dot(a / la, b / lb));
            }

            float NormalEdge(float2 uv, float2 texel)
            {
                float3 n  = SampleSceneNormals(uv);
                float3 nL = SampleSceneNormals(uv - float2(texel.x, 0));
                float3 nR = SampleSceneNormals(uv + float2(texel.x, 0));
                float3 nU = SampleSceneNormals(uv - float2(0, texel.y));
                float3 nD = SampleSceneNormals(uv + float2(0, texel.y));
                return max(max(AngleDiff(n, nL), AngleDiff(n, nR)),
                           max(AngleDiff(n, nU), AngleDiff(n, nD)));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv    = input.texcoord.xy;
                float2 texel = _CameraDepthTexture_TexelSize.xy * _OutlineWidth;

                float depthEdge  = DepthEdge(uv, texel);
                float normalEdge = NormalEdge(uv, texel);

                float edge = (depthEdge  > _DepthThreshold  || normalEdge > _NormalThreshold) ? 1.0 : 0.0;

                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return edge > 0.5 ? _OutlineColor : color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
