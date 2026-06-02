using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

public class GPUMarchDispatcher : MonoBehaviour
{
    [Tooltip("Drag Assets/Shaders/MarchingCubes.compute here")]
    public ComputeShader marchShader;

    public static GPUMarchDispatcher Instance { get; private set; }
    // Exposed so TerrainChunk.BakeColliderFromGPU can read back the last dispatch result
    public ComputeBuffer TriangleBuf => _triangleBuf;

    const int TRIANGLE_STRIDE = 72;
    const int MAX_TRIANGLES = 150000;

    ComputeBuffer _triangleBuf;
    ComputeBuffer _countBuf;
    ComputeBuffer _triTableBuf;
    ComputeBuffer _cornerABuf;
    ComputeBuffer _cornerBBuf;

    int _kernelIndex;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        _kernelIndex = marchShader.FindKernel("MarchChunk");

        _triangleBuf = new ComputeBuffer(MAX_TRIANGLES, TRIANGLE_STRIDE);
        _countBuf = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        _triTableBuf = new ComputeBuffer(256 * 16, sizeof(int));
        _triTableBuf.SetData(FlattenTriangulation());

        _cornerABuf = new ComputeBuffer(12, sizeof(int));
        _cornerABuf.SetData(MarchingCubesTables.cornerIndexAFromEdge);

        _cornerBBuf = new ComputeBuffer(12, sizeof(int));
        _cornerBBuf.SetData(MarchingCubesTables.cornerIndexBFromEdge);

        marchShader.SetBuffer(_kernelIndex, "_Triangulation", _triTableBuf);
        marchShader.SetBuffer(_kernelIndex, "_CornerIndexA", _cornerABuf);
        marchShader.SetBuffer(_kernelIndex, "_CornerIndexB", _cornerBBuf);
        marchShader.SetBuffer(_kernelIndex, "_Triangles", _triangleBuf);
        marchShader.SetBuffer(_kernelIndex, "_TriangleCount", _countBuf);
    }

    void OnDestroy()
    {
        _triangleBuf?.Release();
        _countBuf?.Release();
        _triTableBuf?.Release();
        _cornerABuf?.Release();
        _cornerBBuf?.Release();
    }

    // Single public dispatch method used by TerrainChunk.BuildMeshGPU().
    // Called from TerrainChunk.BuildMeshGPU().
    public void DispatchAsync(
        float[] density,
        float[] treeStamp,
        float isoLevel,
        int width,
        int height,
        int scale,
        int step,
        Action<int> onComplete)
    {
        var densityBuf = new ComputeBuffer(density.Length, sizeof(float));
        var treeBuf = new ComputeBuffer(treeStamp.Length, sizeof(float));
        densityBuf.SetData(density);
        treeBuf.SetData(treeStamp);

        _countBuf.SetData(new int[] { 0 });

        marchShader.SetBuffer(_kernelIndex, "_Density", densityBuf);
        marchShader.SetBuffer(_kernelIndex, "_TreeStamp", treeBuf);
        marchShader.SetFloat("_IsoLevel", isoLevel);
        marchShader.SetInt("_Width", width);
        marchShader.SetInt("_Height", height);
        marchShader.SetInt("_Scale", scale);
        marchShader.SetInt("_Step", step);

        int gx = Mathf.CeilToInt((width / step) / 4f);
        int gy = Mathf.CeilToInt((height / step) / 4f);
        int gz = Mathf.CeilToInt((width / step) / 4f);
        marchShader.Dispatch(_kernelIndex, gx, gy, gz);

        AsyncGPUReadback.Request(_countBuf, (countReq) =>
        {
            densityBuf.Release();
            treeBuf.Release();

            if (countReq.hasError) { onComplete?.Invoke(0); return; }

            int triCount = Mathf.Min(countReq.GetData<int>()[0], MAX_TRIANGLES);
            onComplete?.Invoke(triCount);
        });
    }

    static int[] FlattenTriangulation()
    {
        var flat = new int[256 * 16];
        for (int i = 0; i < 256; i++)
            for (int j = 0; j < 16; j++)
                flat[i * 16 + j] = MarchingCubesTables.triangulation[i, j];
        return flat;
    }

    struct GpuTriangle { public Vector3 a, b, c, colorA, colorB, colorC; }

    public class MeshData
    {
        public Vector3[] verts;
        public int[] tris;
        public Color[] colors;
        public Vector3[] normals;
    }
}