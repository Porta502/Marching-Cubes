using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

public class TerrainChunk : MonoBehaviour
{
    //  GPU Procedural Draw fields 
    [HideInInspector] public ComputeBuffer gpuTriangleBuf; // triangle buffer lives on GPU
    [HideInInspector] public ComputeBuffer gpuArgsBuf;     // DrawProceduralIndirect args
    [HideInInspector] public int gpuTriCount = 0; // triangle count
    [HideInInspector] public Material gpuMaterial;    // per-chunk material instance

    //  Chunk dimensions 
    public const int chunkWidth = 24;
    public const int chunkHeight = 48;
    public const int voxelScale = 1;
    public Shader proceduralShader;

    public bool densityReady = false;
    public float[,,] densityMap = new float[chunkWidth + 1, chunkHeight + 1, chunkWidth + 1];

    // tree
    public float[,,] treeStamp = new float[chunkWidth + 1, chunkHeight + 1, chunkWidth + 1];
    public float[,,] treeDensityMap => treeStamp;

    [Header("Marching Cubes")]
    [Range(-1f, 1f)]
    public float isoLevel = 0f;

    public int currentLOD = 1;

    [Header("Materials")]
    public Material terrainMaterial;

    private FastNoise noise = new FastNoise();

    // 
    public void GenerateDensity(Vector3 origin)
    {
        for (int x = 0; x <= chunkWidth; x++)
            for (int y = 0; y <= chunkHeight; y++)
                for (int z = 0; z <= chunkWidth; z++)
                {
                    float worldX = origin.x + x * voxelScale;
                    float worldY = origin.y + y * voxelScale;
                    float worldZ = origin.z + z * voxelScale;

                    float simplex1 = noise.GetSimplex(worldX * 0.8f, worldZ * 0.8f) * 10f;
                    float modulator = Mathf.Clamp(noise.GetSimplex(worldX * 0.3f, worldZ * 0.3f) + 0.5f, 0f, 1.5f);
                    float simplex2 = noise.GetSimplex(worldX * 3f, worldZ * 3f) * 10f * modulator;

                    float baseLandHeight = (chunkHeight * voxelScale) * 0.55f + simplex1 + simplex2;
                    float density = baseLandHeight - worldY;

                    float caveNoise = noise.GetPerlinFractal(worldX * 5f, worldY * 10f, worldZ * 5f);
                    float caveMask = noise.GetSimplex(worldX * 0.3f, worldZ * 0.3f) + 0.3f;
                    if (worldY > 5f && caveNoise > Mathf.Max(caveMask, 0.2f))
                        density -= 10f;

                    densityMap[x, y, z] = density;
                }
        densityReady = true;
    }

    // 
    [HideInInspector] public MeshFilter meshFilter;
    [HideInInspector] public MeshCollider meshCollider;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
    }

    // 
    public void InitCollider()
    {
        MeshCollider col = GetComponent<MeshCollider>();
        if (col != null) col.sharedMesh = null;
    }

    // 
    public void InitGPUBuffers()
    {
        gpuMaterial = new Material(proceduralShader);

        // FIX #14: release old buffers before allocating new ones to prevent GPU memory leak
        ReleaseGPUBuffers();

        // 72 bytes = Triangle struct (6  float3: a, b, c, colorA, colorB, colorC)
        gpuTriangleBuf = new ComputeBuffer(150000, 72);

        // DrawProceduralIndirect args: [vertexCount, instanceCount, startVertex, startInstance, padding]
        gpuArgsBuf = new ComputeBuffer(5, sizeof(int), ComputeBufferType.IndirectArguments);
        gpuArgsBuf.SetData(new int[] { 0, 1, 0, 0, 0 });

        gpuMaterial = new Material(Shader.Find("Custom/TerrainProcedural"));
        gpuMaterial.SetBuffer("_Triangles", gpuTriangleBuf);
    }

    public void SetGPUTriCount(int count)
    {
        gpuTriCount = count;
        // vertex count = triangles  3
        gpuArgsBuf.SetData(new int[] { count * 3, 1, 0, 0, 0 });
    }

    public void ReleaseGPUBuffers()
    {
        gpuTriangleBuf?.Release(); gpuTriangleBuf = null;
        gpuArgsBuf?.Release(); gpuArgsBuf = null;
        gpuTriCount = 0;
    }

    void OnDestroy() => ReleaseGPUBuffers();

    // 
    // FIX #14: ResetForReuse releases GPU buffers so InitGPUBuffers can reallocate cleanly
    public void ResetForReuse()
    {
        densityReady = false;
        System.Array.Clear(treeStamp, 0, treeStamp.Length);
        System.Array.Clear(_flatDensity, 0, _flatDensity.Length);
        System.Array.Clear(_flatTreeStamp, 0, _flatTreeStamp.Length);
        ReleaseGPUBuffers();
    }

    // 
    // FIX #11: BakeColliderAsync moved OUT of ApplyMesh as a proper class-level IEnumerator
    IEnumerator BakeColliderAsync(Mesh mesh)
    {
        int meshID = mesh.GetInstanceID();
        bool bakeDone = false;

        // FIX #10: actually wait for bake to finish before assigning
        Task.Run(() =>
        {
            Physics.BakeMesh(meshID, false);
            bakeDone = true;
        });

        // Wait until the thread pool task sets bakeDone = true
        yield return new WaitUntil(() => bakeDone);

        // One extra frame so Unity registers the baked data
        yield return null;

        if (this == null || !gameObject.activeSelf) yield break;
        var col = GetComponent<MeshCollider>();
        if (col != null)
        {
            col.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation;
            col.sharedMesh = mesh;
        }
    }

    // 
    public static Vector3[] ComputeNormals(Vector3[] verts, int[] tris)
    {
        Vector3[] normals = new Vector3[verts.Length];
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            Vector3 normal = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).normalized;
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }
        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].normalized;
        return normals;
    }

    // 
    public void SyncBorderDensity(TerrainChunk neighborX, TerrainChunk neighborZ, TerrainChunk neighborXZ)
    {
        if (neighborX != null)
            for (int y = 0; y <= chunkHeight; y++)
                for (int z = 0; z <= chunkWidth; z++)
                    densityMap[chunkWidth, y, z] = neighborX.densityMap[0, y, z];

        if (neighborZ != null)
            for (int y = 0; y <= chunkHeight; y++)
                for (int x = 0; x <= chunkWidth; x++)
                    densityMap[x, y, chunkWidth] = neighborZ.densityMap[x, y, 0];

        if (neighborXZ != null)
            for (int y = 0; y <= chunkHeight; y++)
                densityMap[chunkWidth, y, chunkWidth] = neighborXZ.densityMap[0, y, 0];
    }

    public void SyncBorderTreeStamp(TerrainChunk neighborX, TerrainChunk neighborZ, TerrainChunk neighborXZ)
    {
        if (neighborX != null)
            for (int y = 0; y <= chunkHeight; y++)
                for (int z = 0; z <= chunkWidth; z++)
                    treeStamp[chunkWidth, y, z] = Mathf.Max(treeStamp[chunkWidth, y, z],
                        neighborX.treeStamp[0, y, z]);

        if (neighborZ != null)
            for (int y = 0; y <= chunkHeight; y++)
                for (int x = 0; x <= chunkWidth; x++)
                    treeStamp[x, y, chunkWidth] = Mathf.Max(treeStamp[x, y, chunkWidth],
                        neighborZ.treeStamp[x, y, 0]);

        if (neighborXZ != null)
            for (int y = 0; y <= chunkHeight; y++)
                treeStamp[chunkWidth, y, chunkWidth] = Mathf.Max(
                    treeStamp[chunkWidth, y, chunkWidth],
                    neighborXZ.treeStamp[0, y, 0]);
    }

    // 
    // Flat arrays reused across frames  no alloc per build
    float[] _flatDensity = new float[(chunkWidth + 1) * (chunkHeight + 1) * (chunkWidth + 1)];
    float[] _flatTreeStamp = new float[(chunkWidth + 1) * (chunkHeight + 1) * (chunkWidth + 1)];

    public float[] FlattenDensity()
    {
        int W = chunkWidth + 1, H = chunkHeight + 1;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                for (int z = 0; z < W; z++)
                    _flatDensity[x * H * W + y * W + z] = densityMap[x, y, z];
        return _flatDensity;
    }

    public float[] FlattenTreeStamp()
    {
        int W = chunkWidth + 1, H = chunkHeight + 1;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                for (int z = 0; z < W; z++)
                    _flatTreeStamp[x * H * W + y * W + z] = treeStamp[x, y, z];
        return _flatTreeStamp;
    }

    // 
    // Every frame: draw the GPU triangle buffer directly  no Mesh object involved
    void Update()
    {
        if (gpuTriCount <= 0 || gpuMaterial == null || gpuTriangleBuf == null) return;

        Graphics.DrawProceduralIndirect(
            gpuMaterial,
            new Bounds(transform.position + Vector3.up * chunkHeight * 0.5f,
                       Vector3.one * chunkWidth * 2f),
            MeshTopology.Triangles,
            gpuArgsBuf);
    }

    // 
    // BuildMeshGPU: dispatches compute shader, stores count, optionally bakes collider
    public void BuildMeshGPU(bool updateCollider = true)
    {
        if (GPUMarchDispatcher.Instance == null)
        {
            Debug.LogWarning("[TerrainChunk] GPUMarchDispatcher not ready, skipping BuildMeshGPU");
            return;
        }
        if (gpuTriangleBuf == null) InitGPUBuffers();

        float[] flatDensity = FlattenDensity();
        float[] flatTree = FlattenTreeStamp();

        GPUMarchDispatcher.Instance.DispatchAsync(
            flatDensity, flatTree,
            isoLevel, chunkWidth, chunkHeight, voxelScale, currentLOD,
            (triCount) =>
            {
                if (this == null || !gameObject.activeSelf) return;
                SetGPUTriCount(triCount);

                if (updateCollider)
                    StartCoroutine(BakeColliderFromGPU());
            });
    }

    // 
    // Reads back only triangle data needed for physics  runs after GPU render
    IEnumerator BakeColliderFromGPU()
    {
        yield return new WaitForEndOfFrame();

        bool readbackDone = false;
        Mesh colliderMesh = null;

        int snapCount = gpuTriCount; // snapshot before async gap
        var dispatcher = GPUMarchDispatcher.Instance;
        if (dispatcher == null) { readbackDone = true; yield break; }
        AsyncGPUReadback.Request(dispatcher.TriangleBuf, snapCount * 72, 0, (req) =>
        {
            if (req.hasError || this == null) { readbackDone = true; return; }

            var raw = req.GetData<GpuTriangle>();
            int triCount = raw.Length; // use actual slice length, not the (possibly stale) field
            var verts = new Vector3[triCount * 3];
            var tris = new int[triCount * 3];

            for (int i = 0; i < triCount; i++)
            {
                verts[i * 3] = raw[i].a;
                verts[i * 3 + 1] = raw[i].b;
                verts[i * 3 + 2] = raw[i].c;
                tris[i * 3] = i * 3;
                tris[i * 3 + 1] = i * 3 + 1;
                tris[i * 3 + 2] = i * 3 + 2;
            }

            colliderMesh = new Mesh();
            colliderMesh.indexFormat = IndexFormat.UInt32;
            colliderMesh.vertices = verts;
            colliderMesh.triangles = tris;
            readbackDone = true;
        });

        yield return new WaitUntil(() => readbackDone);

        if (colliderMesh != null)
            yield return StartCoroutine(BakeColliderAsync(colliderMesh));
    }

    // FIX #12: removed .ContinueWith(TaskScheduler.FromCurrentSynchronizationContext())
    // which can be null in Unity  BakeColliderAsync handles assignment safely on main thread
    IEnumerator AssignCollider(Mesh mesh)
    {
        yield return null;
        yield return null;
        if (this == null || !gameObject.activeSelf) yield break;
        var col = GetComponent<MeshCollider>();
        if (col != null)
        {
            col.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation;
            col.sharedMesh = mesh;
        }
    }

    // Blittable struct matching the compute shader Triangle layout (6  float3 = 72 bytes)
    struct GpuTriangle { public Vector3 a, b, c, colorA, colorB, colorC; }

    // 
    // Legacy CPU mesh path  still used by Start() for initial world build
    public void BuildMesh(bool updateCollider = true)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var colors = new List<Color>();
        Vector3[] normals = null;

        int step = currentLOD;
        for (int x = 0; x < chunkWidth; x += step)
            for (int y = 0; y < chunkHeight; y += step)
                for (int z = 0; z < chunkWidth; z += step)
                    MarchCube(new Vector3Int(x, y, z), verts, tris, colors, step);

        if (verts.Count > 0)
            normals = ComputeNormals(verts.ToArray(), tris.ToArray());

        ApplyMesh(verts, tris, colors, normals, updateCollider);
    }

    public async Task BuildMeshAsync(bool updateCollider = true, float[,,] treeSnap = null)
    {
        float[,,] densitySnap = (float[,,])densityMap.Clone();
        float[,,] treeRead = (float[,,])treeStamp.Clone();
        float iso = isoLevel;
        int s = voxelScale;
        int step = currentLOD;

        var verts = new List<Vector3>();
        var tris = new List<int>();
        var colors = new List<Color>();
        Vector3[] normals = null;

        await Task.Run(() =>
        {
            for (int x = 0; x < chunkWidth; x += step)
                for (int y = 0; y < chunkHeight; y += step)
                    for (int z = 0; z < chunkWidth; z += step)
                        MarchCubeStatic(new Vector3Int(x, y, z), verts, tris, colors,
                            step, densitySnap, treeRead, iso, s);

            if (verts.Count > 0)
                normals = ComputeNormals(verts.ToArray(), tris.ToArray());
        });

        if (this == null || gameObject == null || !gameObject.activeSelf) return;
        ApplyMesh(verts, tris, colors, normals, updateCollider);
    }

    // 
    public void ApplyMesh(List<Vector3> verts, List<int> tris, List<Color> colors,
        Vector3[] normals, bool updateCollider, Mesh prebuiltMesh = null)
    {
        if (verts.Count == 0) return;

        Mesh finalMesh = prebuiltMesh;
        if (finalMesh == null)
        {
            finalMesh = new Mesh();
            finalMesh.indexFormat = IndexFormat.UInt32;
            finalMesh.vertices = verts.ToArray();
            finalMesh.triangles = tris.ToArray();
            finalMesh.colors = colors.ToArray();

            if (normals != null && normals.Length == verts.Count)
                finalMesh.normals = normals;
            else
                finalMesh.RecalculateNormals();
        }

        GetComponent<MeshFilter>().mesh = finalMesh;

        if (updateCollider)
            StartCoroutine(BakeColliderAsync(finalMesh));
    }

    // 
    public void MarchCube(Vector3Int coord, List<Vector3> verts, List<int> tris, List<Color> colors, int step = 1)
    {
        MarchCubeStatic(coord, verts, tris, colors, step, densityMap, treeDensityMap, isoLevel, voxelScale);
    }

    public static void MarchCubeStatic(Vector3Int coord,
        List<Vector3> verts, List<int> tris, List<Color> colors,
        int step,
        float[,,] densityMap, float[,,] treeDensityMap,
        float isoLevel, int s)
    {
        int x = coord.x, y = coord.y, z = coord.z;
        if (x + step > chunkWidth || y + step > chunkHeight || z + step > chunkWidth) return;

        float[] cubeCorners = new float[8]
        {
            densityMap[x,        y,        z       ],
            densityMap[x + step, y,        z       ],
            densityMap[x + step, y,        z + step],
            densityMap[x,        y,        z + step],
            densityMap[x,        y + step, z       ],
            densityMap[x + step, y + step, z       ],
            densityMap[x + step, y + step, z + step],
            densityMap[x,        y + step, z + step],
        };

        int cubeIndex = 0;
        for (int i = 0; i < 8; i++)
            if (cubeCorners[i] > isoLevel) cubeIndex |= (1 << i);
        if (cubeIndex == 0 || cubeIndex == 255) return;

        float tMax = 0;
        tMax = Mathf.Max(tMax, treeDensityMap[x, y, z]);
        tMax = Mathf.Max(tMax, treeDensityMap[x + step, y, z]);
        tMax = Mathf.Max(tMax, treeDensityMap[x + step, y, z + step]);
        tMax = Mathf.Max(tMax, treeDensityMap[x, y, z + step]);
        tMax = Mathf.Max(tMax, treeDensityMap[x, y + step, z]);
        tMax = Mathf.Max(tMax, treeDensityMap[x + step, y + step, z]);
        tMax = Mathf.Max(tMax, treeDensityMap[x + step, y + step, z + step]);
        tMax = Mathf.Max(tMax, treeDensityMap[x, y + step, z + step]);

        Color vertColor = tMax >= 1.5f ? new Color(0, 1, 0)
                        : tMax >= 0.5f ? new Color(1, 0, 0)
                        : new Color(0, 0, 1);

        Vector3[] corners = new Vector3[8]
        {
            new Vector3(x,        y,        z       ) * s,
            new Vector3(x + step, y,        z       ) * s,
            new Vector3(x + step, y,        z + step) * s,
            new Vector3(x,        y,        z + step) * s,
            new Vector3(x,        y + step, z       ) * s,
            new Vector3(x + step, y + step, z       ) * s,
            new Vector3(x + step, y + step, z + step) * s,
            new Vector3(x,        y + step, z + step) * s,
        };

        for (int i = 0; MarchingCubesTables.triangulation[cubeIndex, i] != -1; i += 3)
        {
            int a0 = MarchingCubesTables.cornerIndexAFromEdge[MarchingCubesTables.triangulation[cubeIndex, i]];
            int a1 = MarchingCubesTables.cornerIndexBFromEdge[MarchingCubesTables.triangulation[cubeIndex, i]];
            int b0 = MarchingCubesTables.cornerIndexAFromEdge[MarchingCubesTables.triangulation[cubeIndex, i + 1]];
            int b1 = MarchingCubesTables.cornerIndexBFromEdge[MarchingCubesTables.triangulation[cubeIndex, i + 1]];
            int c0 = MarchingCubesTables.cornerIndexAFromEdge[MarchingCubesTables.triangulation[cubeIndex, i + 2]];
            int c1 = MarchingCubesTables.cornerIndexBFromEdge[MarchingCubesTables.triangulation[cubeIndex, i + 2]];

            int idx = verts.Count;
            verts.Add(Interp(cubeCorners[a0], cubeCorners[a1], corners[a0], corners[a1], isoLevel));
            verts.Add(Interp(cubeCorners[b0], cubeCorners[b1], corners[b0], corners[b1], isoLevel));
            verts.Add(Interp(cubeCorners[c0], cubeCorners[c1], corners[c0], corners[c1], isoLevel));
            colors.Add(vertColor); colors.Add(vertColor); colors.Add(vertColor);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
        }
    }

    static Vector3 Interp(float dA, float dB, Vector3 pA, Vector3 pB, float iso)
    {
        if (Mathf.Abs(dB - dA) < 0.0001f) return pA;
        float t = (iso - dA) / (dB - dA);
        return pA + t * (pB - pA);
    }
}