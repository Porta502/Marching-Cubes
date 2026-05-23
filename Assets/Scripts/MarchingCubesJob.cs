using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct MarchingCubesJob : IJob
{
    [ReadOnly] public NativeArray<float> densityMap;
    [ReadOnly] public NativeArray<float> treeDensityMap;
    [ReadOnly] public NativeArray<int> triTable;        // flattened triangulation (256 * 16)
    [ReadOnly] public NativeArray<int> edgeA;          // cornerIndexAFromEdge
    [ReadOnly] public NativeArray<int> edgeB;          // cornerIndexBFromEdge
    [ReadOnly] public float isoLevel;
    [ReadOnly] public int step;
    [ReadOnly] public int cw;
    [ReadOnly] public int ch;
    [ReadOnly] public int scale;

    public NativeList<float3> verts;
    public NativeList<int> tris;
    public NativeList<float4> colors;

    int Idx(int x, int y, int z) => x * (ch + 1) * (cw + 1) + y * (cw + 1) + z;

    public void Execute()
    {
        for (int x = 0; x < cw; x += step)
            for (int y = 0; y < ch; y += step)
                for (int z = 0; z < cw; z += step)
                    MarchCube(x, y, z);
    }

    void MarchCube(int x, int y, int z)
    {
        if (x + step > cw || y + step > ch || z + step > cw) return;

        float c0 = densityMap[Idx(x, y, z)];
        float c1 = densityMap[Idx(x + step, y, z)];
        float c2 = densityMap[Idx(x + step, y, z + step)];
        float c3 = densityMap[Idx(x, y, z + step)];
        float c4 = densityMap[Idx(x, y + step, z)];
        float c5 = densityMap[Idx(x + step, y + step, z)];
        float c6 = densityMap[Idx(x + step, y + step, z + step)];
        float c7 = densityMap[Idx(x, y + step, z + step)];

        int cubeIndex = 0;
        if (c0 > isoLevel) cubeIndex |= 1;
        if (c1 > isoLevel) cubeIndex |= 2;
        if (c2 > isoLevel) cubeIndex |= 4;
        if (c3 > isoLevel) cubeIndex |= 8;
        if (c4 > isoLevel) cubeIndex |= 16;
        if (c5 > isoLevel) cubeIndex |= 32;
        if (c6 > isoLevel) cubeIndex |= 64;
        if (c7 > isoLevel) cubeIndex |= 128;
        if (cubeIndex == 0 || cubeIndex == 255) return;

        float tMax = 0f;
        tMax = math.max(tMax, treeDensityMap[Idx(x, y, z)]);
        tMax = math.max(tMax, treeDensityMap[Idx(x + step, y, z)]);
        tMax = math.max(tMax, treeDensityMap[Idx(x + step, y, z + step)]);
        tMax = math.max(tMax, treeDensityMap[Idx(x, y, z + step)]);
        tMax = math.max(tMax, treeDensityMap[Idx(x, y + step, z)]);
        tMax = math.max(tMax, treeDensityMap[Idx(x + step, y + step, z)]);
        tMax = math.max(tMax, treeDensityMap[Idx(x + step, y + step, z + step)]);
        tMax = math.max(tMax, treeDensityMap[Idx(x, y + step, z + step)]);

        float4 vertColor = tMax >= 1.5f ? new float4(0, 1, 0, 1)
                         : tMax >= 0.5f ? new float4(1, 0, 0, 1)
                                        : new float4(0, 0, 1, 1);

        float3 p0 = new float3(x, y, z) * scale;
        float3 p1 = new float3(x + step, y, z) * scale;
        float3 p2 = new float3(x + step, y, z + step) * scale;
        float3 p3 = new float3(x, y, z + step) * scale;
        float3 p4 = new float3(x, y + step, z) * scale;
        float3 p5 = new float3(x + step, y + step, z) * scale;
        float3 p6 = new float3(x + step, y + step, z + step) * scale;
        float3 p7 = new float3(x, y + step, z + step) * scale;

        FixedList128Bytes<float3> corners = default;
        corners.Add(p0); corners.Add(p1); corners.Add(p2); corners.Add(p3);
        corners.Add(p4); corners.Add(p5); corners.Add(p6); corners.Add(p7);

        FixedList64Bytes<float> cDensity = default;
        cDensity.Add(c0); cDensity.Add(c1); cDensity.Add(c2); cDensity.Add(c3);
        cDensity.Add(c4); cDensity.Add(c5); cDensity.Add(c6); cDensity.Add(c7);

        int row = cubeIndex * 16;
        for (int i = 0; i < 16 && triTable[row + i] != -1; i += 3)
        {
            int a0 = edgeA[triTable[row + i]];
            int a1 = edgeB[triTable[row + i]];
            int b0 = edgeA[triTable[row + i + 1]];
            int b1 = edgeB[triTable[row + i + 1]];
            int c0e = edgeA[triTable[row + i + 2]];
            int c1e = edgeB[triTable[row + i + 2]];

            int idx = verts.Length;
            verts.Add(Interp(cDensity[a0], cDensity[a1], corners[a0], corners[a1]));
            verts.Add(Interp(cDensity[b0], cDensity[b1], corners[b0], corners[b1]));
            verts.Add(Interp(cDensity[c0e], cDensity[c1e], corners[c0e], corners[c1e]));
            colors.Add(vertColor); colors.Add(vertColor); colors.Add(vertColor);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
        }
    }

    float3 Interp(float dA, float dB, float3 pA, float3 pB)
    {
        if (math.abs(dB - dA) < 0.0001f) return pA;
        float t = (isoLevel - dA) / (dB - dA);
        return pA + t * (pB - pA);
    }
}