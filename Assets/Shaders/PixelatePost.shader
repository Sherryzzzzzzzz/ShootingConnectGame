Shader "Hidden/PixelatePost"
{
    Properties
    {
        _PixelSize ("Pixel Size (px)", Float) = 4
        _PixelStrength ("Pixel Strength", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        ZWrite Off Cull Off

        Pass
        {
            Name "PixelatePost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _PixelSize;
            float _PixelStrength;

            // 像素化：把 UV 对齐到像素网格（每 _PixelSize 像素取一次色），再与原画面混合
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord.xy;

                float2 screenPx = uv * _ScreenParams.xy;
                float2 snappedPx = floor(screenPx / max(_PixelSize, 0.5)) * max(_PixelSize, 0.5);
                float2 pixelUV = snappedPx / _ScreenParams.xy;

                half4 pixelColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, pixelUV);
                half4 original   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                return lerp(original, pixelColor, saturate(_PixelStrength));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
