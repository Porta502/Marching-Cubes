Shader "Custom/TreeShader"
{
    Properties
    {
        _TrunkColor  ("Trunk Color",  Color) = (0.35, 0.22, 0.12, 1)
        _LeafColor   ("Leaf Color",   Color) = (0.15, 0.45, 0.1, 1)
        _LeafHeight  ("Leaf Start Height (local)", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert

        fixed4 _TrunkColor;
        fixed4 _LeafColor;
        float  _LeafHeight;

        struct Input
        {
            float3 worldPos;
            float3 objPos;  // object-space position
        };

        // Inject object-space Y so we can threshold trunk vs. leaves
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.objPos = v.vertex.xyz;
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // Use object-space Y to separate trunk from foliage
            fixed4 color = (IN.objPos.y >= _LeafHeight) ? _LeafColor : _TrunkColor;
            o.Albedo = color.rgb;
        }
        ENDCG
    }
}