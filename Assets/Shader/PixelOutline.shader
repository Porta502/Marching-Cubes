Shader "Hidden/PixelOutline"
{
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _OutlineColor;
            float  _DepthThreshold;
            float  _NormalThreshold;

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv    = input.texcoord;
                float2 texel = 1.0 / _ScreenParams.xy;

                float  d  = SampleSceneDepth(uv);
                float3 n  = SampleSceneNormals(uv);

                float  dR = SampleSceneDepth(uv + float2( texel.x, 0));
                float  dU = SampleSceneDepth(uv + float2(0,  texel.y));
                float3 nR = SampleSceneNormals(uv + float2( texel.x, 0));
                float3 nU = SampleSceneNormals(uv + float2(0,  texel.y));

                float depthDiff  = abs(d - dR) + abs(d - dU);
                float normalDiff = length(n - nR) + length(n - nU);

                bool isEdge = depthDiff > _DepthThreshold || normalDiff > _NormalThreshold;

                float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                return isEdge ? _OutlineColor : color;
            }
            ENDHLSL
        }
    }
}