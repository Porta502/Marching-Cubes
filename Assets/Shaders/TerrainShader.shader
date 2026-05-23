Shader "Custom/TerrainShader"
{
    Properties
    {
        _GrassColor      ("Grass Color",    Color) = (0.33, 0.58, 0.22, 1)
        _DirtColor       ("Dirt Color",     Color) = (0.5,  0.4,  0.3,  1)
        _StoneColor      ("Stone Color",    Color) = (0.5,  0.5,  0.5,  1)
        _SnowColor       ("Snow Color",     Color) = (0.9,  0.95, 1.0,  1)
        _TrunkColor      ("Trunk Color",    Color) = (0.38, 0.22, 0.08, 1)
        _LeavesColor     ("Leaves Color",   Color) = (0.18, 0.49, 0.13, 1)
        _SlopeThreshold  ("Slope Threshold", Float) = 0.4
        _StoneHeight     ("Stone Height",   Float) = 20
        _SnowHeight      ("Snow Height",    Float) = 45
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert

        fixed4 _GrassColor, _DirtColor, _StoneColor, _SnowColor;
        fixed4 _TrunkColor, _LeavesColor;
        float  _SlopeThreshold, _StoneHeight, _SnowHeight;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 vertColor : COLOR;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertColor = v.color;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // Red = trunk, Green = leaves (from vertex color)
            if (IN.vertColor.r > 0.5) { o.Albedo = _TrunkColor.rgb;  return; }
            if (IN.vertColor.g > 0.5) { o.Albedo = _LeavesColor.rgb; return; }

            // Terrain coloring by height + slope
            float slope = 1.0 - IN.worldNormal.y;
            fixed4 color;
            if (IN.worldPos.y >= _SnowHeight)
                color = (slope > _SlopeThreshold) ? _StoneColor : _SnowColor;
            else if (IN.worldPos.y >= _StoneHeight)
                color = (slope > _SlopeThreshold) ? _DirtColor : _GrassColor;
            else
                color = _StoneColor;

            o.Albedo = color.rgb;
        }
        ENDCG
    }
}