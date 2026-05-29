// GPUMarchDispatcher.cs
// Attach this as a component on an empty GameObject called "GPUMarchDispatcher".
// Assign the MarchingCubes compute shader in the Inspector.
// TerrainChunk.BuildMeshGPU() calls DispatchAsync() on this singleton.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

public class GPUMarchDispatcher : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Tooltip("Drag Assets/Shaders/MarchingCubes.compute here")]
    public ComputeShader marchShader;

    // ── Singleton ────────────────────────────────────────────────────────────
    public static GPUMarchDispatcher Instance { get; private set; }

    // ── Internal constants ───────────────────────────────────────────────────
    // Triangle struct = 3 positions + 3 colors = 6 float3 = 18 floats = 72 bytes
    const int TRIANGLE_STRIDE = 72;

    // Worst case: every voxel cube emits 5 triangles.
    // chunkWidth=24, chunkHeight=48 → 24*48*24 = 27648 cubes → 138240 triangles max.
    const int MAX_TRIANGLES = 150000;

    // GPU buffers — allocated once, reused every dispatch (avoids GC alloc)
    ComputeBuffer _triangleBuf;
    ComputeBuffer _countBuf;
    ComputeBuffer _triTableBuf;
    ComputeBuffer _cornerABuf;
    ComputeBuffer _cornerBBuf;

    int _kernelIndex;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        _kernelIndex   = marchShader.FindKernel("MarchChunk");

        // Persistent output buffers
        _triangleBuf = new ComputeBuffer(MAX_TRIANGLES, TRIANGLE_STRIDE);
        _countBuf    = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        // Upload the triangulation tables once — these never change
        _triTableBuf = new ComputeBuffer(256 * 16, sizeof(int));
        _triTableBuf.SetData(FlattenTriangulation());

        _cornerABuf = new ComputeBuffer(12, sizeof(int));
        _cornerABuf.SetData(MarchingCubesTables.cornerIndexAFromEdge);

        _cornerBBuf = new ComputeBuffer(12, sizeof(int));
        _cornerBBuf.SetData(MarchingCubesTables.cornerIndexBFromEdge);

        marchShader.SetBuffer(_kernelIndex, "_Triangulation", _triTableBuf);
        marchShader.SetBuffer(_kernelIndex, "_CornerIndexA",  _cornerABuf);
        marchShader.SetBuffer(_kernelIndex, "_CornerIndexB",  _cornerBBuf);
        marchShader.SetBuffer(_kernelIndex, "_Triangles",     _triangleBuf);
        marchShader.SetBuffer(_kernelIndex, "_TriangleCount", _countBuf);
    }

    void OnDestroy()
    {
        _triangleBuf?.Release();
        _countBuf   ?.Release();
        _triTableBuf?.Release();
        _cornerABuf ?.Release();
        _cornerBBuf ?.Release();
    }

    // ── Public API ────────────────────────────────────────────────────────────
    // Called from TerrainChunk.BuildMeshGPU().
    // Returns a TaskCompletionSource so the caller can await the result
    // without blocking the main thread.
    public void DispatchAsync(
        float[] density,      // flat array from TerrainChunk.FlattenDensity()
        float[] treeStamp,    // flat array from TerrainChunk.FlattenTreeStamp()
        float   isoLevel,
        int     width,        // TerrainChunk.chunkWidth
        int     height,       // TerrainChunk.chunkHeight
        int     scale,        // TerrainChunk.voxelScale
        int     step,         // currentLOD
        Action<MeshData> onComplete)   // called on main thread when GPU is done
    {
        // Upload density + treeStamp to temporary per-dispatch buffers
        var densityBuf = new ComputeBuffer(density.Length,   sizeof(float));
        var treeBuf    = new ComputeBuffer(treeStamp.Length, sizeof(float));
        densityBuf.SetData(density);
        treeBuf   .SetData(treeStamp);

        // Reset counter
        int[] zero = { 0 };
        _countBuf.SetData(zero);

        // Set per-dispatch parameters
        marchShader.SetBuffer(_kernelIndex, "_Density",   densityBuf);
        marchShader.SetBuffer(_kernelIndex, "_TreeStamp", treeBuf);
        marchShader.SetFloat ("_IsoLevel", isoLevel);
        marchShader.SetInt   ("_Width",    width);
        marchShader.SetInt   ("_Height",   height);
        marchShader.SetInt   ("_Scale",    scale);
        marchShader.SetInt   ("_Step",     step);

        // How many thread groups to dispatch.
        // Kernel is [numthreads(4,4,4)], each thread handles one voxel cube.
        // Number of cubes per axis = Width/Step → groups = ceil(Width/Step / 4)
        int gx = Mathf.CeilToInt((width  / step) / 4f);
        int gy = Mathf.CeilToInt((height / step) / 4f);
        int gz = Mathf.CeilToInt((width  / step) / 4f);
        marchShader.Dispatch(_kernelIndex, gx, gy, gz);

        // Async readback — no stall, result arrives next frame
        AsyncGPUReadback.Request(_triangleBuf, (triReq) =>
        {
            // Also read back the count so we know how many triangles were written
            AsyncGPUReadback.Request(_countBuf, (countReq) =>
            {
                // Release temporary per-dispatch buffers immediately
                densityBuf.Release();
                treeBuf   .Release();

                if (triReq.hasError || countReq.hasError) { onComplete(null); return; }

                int triCount = countReq.GetData<int>()[0];
                if (triCount <= 0)                        { onComplete(null); return; }
                triCount = Mathf.Min(triCount, MAX_TRIANGLES);

                var rawTris = triReq.GetData<GpuTriangle>();

                // Unpack into Unity Mesh arrays
                var verts  = new Vector3[triCount * 3];
                var colors = new Color  [triCount * 3];
                var tris   = new int    [triCount * 3];

                for (int i = 0; i < triCount; i++)
                {
                    var t = rawTris[i];
                    int b = i * 3;
                    verts [b]   = t.a;  verts [b+1] = t.b;  verts [b+2] = t.c;
                    colors[b]   = new Color(t.colorA.x, t.colorA.y, t.colorA.z);
                    colors[b+1] = new Color(t.colorB.x, t.colorB.y, t.colorB.z);
                    colors[b+2] = new Color(t.colorC.x, t.colorC.y, t.colorC.z);
                    tris  [b]   = b;  tris[b+1] = b+1;  tris[b+2] = b+2;
                }

                var normals = TerrainChunk.ComputeNormals(verts, tris);
                onComplete(new MeshData { verts = verts, tris = tris, colors = colors, normals = normals });
            });
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static int[] FlattenTriangulation()
    {
        var flat = new int[256 * 16];
        for (int i = 0; i < 256; i++)
            for (int j = 0; j < 16; j++)
                flat[i * 16 + j] = MarchingCubesTables.triangulation[i, j];
        return flat;
    }

    // Matches the Triangle struct layout in the compute shader (72 bytes)
    struct GpuTriangle
    {
        public Vector3 a, b, c;
        public Vector3 colorA, colorB, colorC;
    }
}

// Passed from the GPU readback callback back to TerrainChunk
public class MeshData
{
    public Vector3[] verts;
    public int[]     tris;
    public Color[]   colors;
    public Vector3[] normals;
}
