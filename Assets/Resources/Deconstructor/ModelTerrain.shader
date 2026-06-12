Shader "Unlit/ModelTerrain"
{
    Properties
    {
        _ToonSteps ("Toon Steps", Range(1, 8)) = 3
        _Ambient ("Ambient", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Name "TerrainLit"
            Tags{"LightMode" = "UniversalForward"}
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
#if UNITY_VERSION >= 202120
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
#else
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
#endif
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct matTerrain {
                float4 baseColor;
                float baseTextureScale;
                float baseColorStrength;
                int geoShaderInd;
            };

            StructuredBuffer<matTerrain> _MatTerrainData;
            Texture2DArray _Textures;
            SamplerState sampler_Textures;

            CBUFFER_START(UnityPerMaterial)
                int   _ToonSteps;
                float _Ambient;
            CBUFFER_END

            struct v2f {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                nointerpolation int material : TEXCOORD2;
            };

            float4x4 _LocalToWorld;

            struct DrawVertex {
                float3 positionOS;
                int2 material;
            };
            struct vInfo { uint axis[3]; };

            StructuredBuffer<DrawVertex> Vertices;
            StructuredBuffer<vInfo> Triangles;
            uint triAddress;
            uint vertAddress;

            float3 CalculateNormalOS(int triIndex) {
                float3 a = Vertices[vertAddress + Triangles[triIndex].axis[0]].positionOS;
                float3 b = Vertices[vertAddress + Triangles[triIndex].axis[1]].positionOS;
                float3 c = Vertices[vertAddress + Triangles[triIndex].axis[2]].positionOS;
                return cross(b - a, c - b);
            }

            v2f vert(uint vertexID : SV_VertexID) {
                v2f o = (v2f)0;
                uint vertInd = Triangles[triAddress + (vertexID / 3)].axis[vertexID % 3];
                DrawVertex input = Vertices[vertAddress + vertInd];
                o.positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.material   = input.material.x;
                o.normalWS   = normalize(mul(_LocalToWorld, float4(CalculateNormalOS(triAddress + (vertexID / 3)), 0)).xyz);
                return o;
            }

            float3 triplanar(float3 worldPos, float scale, float3 blendAxes, int texInd) {
                float3 s = worldPos / scale;
                float3 xP = _Textures.Sample(sampler_Textures, float3(s.y, s.z, texInd)).xyz * blendAxes.x;
                float3 yP = _Textures.Sample(sampler_Textures, float3(s.x, s.z, texInd)).xyz * blendAxes.y;
                float3 zP = _Textures.Sample(sampler_Textures, float3(s.x, s.y, texInd)).xyz * blendAxes.z;
                return xP + yP + zP;
            }

            float3 frag(v2f IN) : SV_Target
            {
                float3 blendAxes = abs(IN.normalWS);
                blendAxes /= blendAxes.x + blendAxes.y + blendAxes.z;

                int    mat     = IN.material;
                float3 albedo  = _MatTerrainData[mat].baseColor.xyz * _MatTerrainData[mat].baseColorStrength
                               + triplanar(IN.positionWS, _MatTerrainData[mat].baseTextureScale, blendAxes, mat)
                               * (1 - _MatTerrainData[mat].baseColorStrength);

                // Toon lighting
                Light  light   = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float  NdotL   = saturate(dot(normalize(IN.normalWS), light.direction));
                float  stepped = floor(NdotL * light.shadowAttenuation * _ToonSteps) / _ToonSteps;
                float  lit     = max(stepped, _Ambient);

                return max(albedo * lit * light.color, albedo * _Ambient);
            }
            ENDHLSL
        }
    }
}