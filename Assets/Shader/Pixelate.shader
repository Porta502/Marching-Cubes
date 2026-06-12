Shader "Hidden/Pixelate"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            int _PixelSize;

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 res = float2(
                    _ScreenParams.x / _PixelSize,
                    _ScreenParams.y / _PixelSize
                );
                float2 pixelated = (floor(uv * res) + 0.5) / res;
                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, pixelated);
            }
            ENDHLSL
        }
    }
}