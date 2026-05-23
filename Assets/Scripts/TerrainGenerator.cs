using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public class TerrainGenerator : MonoBehaviour
{
    NativeArray<int> _triTable;
    NativeArray<int> _edgeA;
    NativeArray<int> _edgeB;
    [SerializeField] ComputeShader _marchShader; 
    ComputeBuffer _densityBuffer;
    ComputeBuffer _vertexBuffer;
    ComputeBuffer _triangleBuffer;
    ComputeBuffer _counterBuffer;
    public GameObject terrainChunk;
    public Transform player;
    public Material terrainMaterial;
    public TreeGenerator treeGenerator;


    public static Dictionary<ChunkPos, TerrainChunk> chunks = new Dictionary<ChunkPos, TerrainChunk>();

    int chunkDist = 4;
    int colliderDist = 4;

    List<TerrainChunk> pooledChunks = new List<TerrainChunk>();
    List<ChunkPos> toGenerate = new List<ChunkPos>();
    HashSet<ChunkPos> building = new HashSet<ChunkPos>();
    bool coroutineRunning = false;

    static int ChunkWorldSize => TerrainChunk.chunkWidth * TerrainChunk.voxelScale;

    // ─────────────────────────────────────────────────────────────
    NativeArray<float> _sharedDensity;
    NativeArray<float> _sharedTreeDensity;
    void Start()
    {
        int cw = TerrainChunk.chunkWidth;
        int ch = TerrainChunk.chunkHeight;
        int size = (cw + 1) * (ch + 1) * (cw + 1);
        _sharedDensity = new NativeArray<float>(size, Allocator.Persistent);
        _sharedTreeDensity = new NativeArray<float>(size, Allocator.Persistent);

        int[,] tri2D = MarchingCubesTables.triangulation;
        _triTable = new NativeArray<int>(256 * 16, Allocator.Persistent);
        for (int r = 0; r < 256; r++)
            for (int c = 0; c < 16; c++)
                _triTable[r * 16 + c] = tri2D[r, c];

        _edgeA = new NativeArray<int>(MarchingCubesTables.cornerIndexAFromEdge, Allocator.Persistent);
        _edgeB = new NativeArray<int>(MarchingCubesTables.cornerIndexBFromEdge, Allocator.Persistent);

        int curChunkPosX = Mathf.FloorToInt(player.position.x / ChunkWorldSize) * ChunkWorldSize;
        int curChunkPosZ = Mathf.FloorToInt(player.position.z / ChunkWorldSize) * ChunkWorldSize;

        for (int i = curChunkPosX - ChunkWorldSize * chunkDist; i <= curChunkPosX + ChunkWorldSize * chunkDist; i += ChunkWorldSize)
            for (int j = curChunkPosZ - ChunkWorldSize * chunkDist; j <= curChunkPosZ + ChunkWorldSize * chunkDist; j += ChunkWorldSize)
                BuildChunkImmediate(i, j);

        foreach (var kvp in chunks)
        {
            System.Array.Clear(kvp.Value.treeDensityMap, 0, kvp.Value.treeDensityMap.Length);
            treeGenerator.StampTrees(kvp.Value, kvp.Value.densityMap);
        }

        foreach (var kvp in chunks)
        {
            ChunkPos cp = kvp.Key;
            chunks.TryGetValue(new ChunkPos(cp.x + ChunkWorldSize, cp.z), out TerrainChunk nx);
            chunks.TryGetValue(new ChunkPos(cp.x, cp.z + ChunkWorldSize), out TerrainChunk nz);
            chunks.TryGetValue(new ChunkPos(cp.x + ChunkWorldSize, cp.z + ChunkWorldSize), out TerrainChunk nxz);
            kvp.Value.SyncBorderDensity(nx, nz, nxz);
            kvp.Value.currentLOD = GetLOD(cp);

#pragma warning disable CS4014
            DispatchMarchingCubes(kvp.Value, IsNearPlayer(cp, colliderDist));
#pragma warning restore CS4014

            WaterChunk wat = kvp.Value.GetComponentInChildren<WaterChunk>();
            if (wat != null) { wat.SetLocs(kvp.Value.densityMap); wat.BuildMesh(); }
        }

        SpawnPlayerOnSurface();
    }

    // ─────────────────────────────────────────────────────────────
    float lodTimer = 0f;
    int lodUpdateIndex = 0;

    void Update()
    {
        LoadChunks();

        lodTimer += Time.deltaTime;
        if (lodTimer > 0.05f)
        {
            lodTimer = 0f;
            var keys = new List<ChunkPos>(chunks.Keys);
            if (keys.Count == 0) return;
            lodUpdateIndex = (lodUpdateIndex + 1) % keys.Count;
            ChunkPos cp = keys[lodUpdateIndex];
            if (chunks.TryGetValue(cp, out TerrainChunk ch))
                UpdateChunkLOD(cp, ch);
        }
    }

    // ─────────────────────────────────────────────────────────────
    int GetLOD(ChunkPos cp)
    {
        float dist = Vector2.Distance(
            new Vector2(player.position.x, player.position.z),
            new Vector2(cp.x, cp.z));
        if (dist < ChunkWorldSize * 4f) return 1;
        if (dist < ChunkWorldSize * 7f) return 2;
        return 4;
    }

    bool IsNearPlayer(ChunkPos cp, int distInChunks)
    {
        int px = Mathf.FloorToInt(player.position.x / ChunkWorldSize) * ChunkWorldSize;
        int pz = Mathf.FloorToInt(player.position.z / ChunkWorldSize) * ChunkWorldSize;
        return Mathf.Abs(cp.x - px) <= ChunkWorldSize * distInChunks
            && Mathf.Abs(cp.z - pz) <= ChunkWorldSize * distInChunks;
    }

    // ─────────────────────────────────────────────────────────────
    async void UpdateChunkLOD(ChunkPos cp, TerrainChunk chunk)
    {
        if (chunk == null || !chunk.gameObject.activeSelf) return;
        int newLOD = GetLOD(cp);
        if (newLOD == chunk.currentLOD) return;
        chunk.currentLOD = newLOD;
        bool needCollider = IsNearPlayer(cp, colliderDist);
        await DispatchMarchingCubes(chunk, needCollider);
    }

    // ─────────────────────────────────────────────────────────────
    void SpawnPlayerOnSurface()
    {
        Vector3 rayStart = new Vector3(player.position.x, 9999f, player.position.z);
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, Mathf.Infinity))
            player.position = hit.point + Vector3.up * 2f;
        else
            player.position = new Vector3(player.position.x,
                TerrainChunk.chunkHeight * TerrainChunk.voxelScale * 0.6f,
                player.position.z);
    }

    // ─────────────────────────────────────────────────────────────
    void BuildChunkImmediate(int xPos, int zPos)
    {
        TerrainChunk chunk;
        if (pooledChunks.Count > 0)
        {
            chunk = pooledChunks[0];
            pooledChunks.RemoveAt(0);
            chunk.densityReady = false;
            chunk.gameObject.SetActive(true);
            chunk.transform.position = new Vector3(xPos, 0, zPos);
        }
        else
        {
            GameObject go = Instantiate(terrainChunk, new Vector3(xPos, 0, zPos), Quaternion.identity);
            chunk = go.GetComponent<TerrainChunk>();
        }
        chunk.GetComponent<MeshRenderer>().material = terrainMaterial;
        chunk.InitCollider(); // ← clear stale collider from pooled chunk
        chunk.GenerateDensity(chunk.transform.position);
        chunks.Add(new ChunkPos(xPos, zPos), chunk);
    }

    // ─────────────────────────────────────────────────────────────
    async Task BuildChunkAsync(int xPos, int zPos)
    {
        ChunkPos cp = new ChunkPos(xPos, zPos);
        if (chunks.ContainsKey(cp) || building.Contains(cp)) return;
        building.Add(cp);

        TerrainChunk chunk;
        if (pooledChunks.Count > 0)
        {
            chunk = pooledChunks[0];
            pooledChunks.RemoveAt(0);
            chunk.densityReady = false;
            chunk.gameObject.SetActive(true);
            chunk.transform.position = new Vector3(xPos, 0, zPos);
        }
        else
        {
            GameObject go = Instantiate(terrainChunk, new Vector3(xPos, 0, zPos), Quaternion.identity);
            chunk = go.GetComponent<TerrainChunk>();
        }
        chunk.GetComponent<MeshRenderer>().material = terrainMaterial;
        chunk.InitCollider(); // ← clear stale collider from pooled chunk
        chunks.Add(cp, chunk);

        Vector3 chunkOrigin = chunk.transform.position;

        await Task.Run(() =>
        {
            chunk.GenerateDensity(chunkOrigin);
        });

        if (chunk == null || !chunk.gameObject.activeSelf)
        {
            building.Remove(cp);
            return;
        }

        treeGenerator?.StampTrees(chunk, chunk.densityMap);

        int s = ChunkWorldSize;
        await Task.WhenAll(
            SyncAndBuildAsync(xPos, zPos),
            SyncAndBuildAsync(xPos - s, zPos),
            SyncAndBuildAsync(xPos, zPos - s),
            SyncAndBuildAsync(xPos - s, zPos - s)
        );

        building.Remove(cp);
    }
    // ─────────────────────────────────────────────────────────────
    async Task SyncAndBuildAsync(int xPos, int zPos)
    {
        if (!chunks.TryGetValue(new ChunkPos(xPos, zPos), out TerrainChunk chunk)) return;

        int s = ChunkWorldSize;
        chunks.TryGetValue(new ChunkPos(xPos + s, zPos), out TerrainChunk nx);
        chunks.TryGetValue(new ChunkPos(xPos, zPos + s), out TerrainChunk nz);
        chunks.TryGetValue(new ChunkPos(xPos + s, zPos + s), out TerrainChunk nxz);

        int waited = 0;
        while (waited < 20)
        {
            bool nxReady = nx == null || nx.densityReady;
            bool nzReady = nz == null || nz.densityReady;
            bool nxzReady = nxz == null || nxz.densityReady;
            if (nxReady && nzReady && nxzReady) break;
            await Task.Delay(50);
            waited++;
        }

        chunk.SyncBorderDensity(nx, nz, nxz);

        ChunkPos cp = new ChunkPos(xPos, zPos);
        chunk.currentLOD = GetLOD(cp);
        bool needCollider = IsNearPlayer(cp, colliderDist);

        // ── ONLY CHANGE: skip collider update if player is standing on this chunk ──
        bool playerOnThisChunk = IsNearPlayer(cp, 0);
        await DispatchMarchingCubes(chunk, needCollider, skipColliderIfPlayer: playerOnThisChunk);
        // ─────────────────────────────────────────────────────────────────────────────

        WaterChunk wat = chunk.GetComponentInChildren<WaterChunk>();
        if (wat != null) { wat.SetLocs(chunk.densityMap); wat.BuildMesh(); }
    }
    async Task DispatchMarchingCubes(TerrainChunk chunk, bool needsCollider, bool skipColliderIfPlayer = false)
    {
        if (chunk == null || !chunk.gameObject.activeSelf) return;

        int cw = TerrainChunk.chunkWidth;
        int ch = TerrainChunk.chunkHeight;
        int size = (cw + 1) * (ch + 1) * (cw + 1);
        int maxVerts = cw * ch * cw * 5 * 3;

        float[] densityFlat = new float[size];
        for (int x = 0; x <= cw; x++)
            for (int y = 0; y <= ch; y++)
                for (int z = 0; z <= cw; z++)
                    densityFlat[x * (ch + 1) * (cw + 1) + y * (cw + 1) + z] = chunk.densityMap[x, y, z];

        ComputeBuffer densityBuf = null;
        ComputeBuffer vertBuf = null;
        ComputeBuffer counterBuf = null;
        Vector3[] verts = null;
        int[] tris = null;

        try
        {
            densityBuf = new ComputeBuffer(size, sizeof(float));
            vertBuf = new ComputeBuffer(maxVerts, 3 * sizeof(float), ComputeBufferType.Append);
            counterBuf = new ComputeBuffer(1, sizeof(int));   // plain, NOT IndirectArguments

            densityBuf.SetData(densityFlat);
            vertBuf.SetCounterValue(0);
            counterBuf.SetData(new int[] { 0 });                      // reset to 0

            int kernel = _marchShader.FindKernel("March");
            _marchShader.SetBuffer(kernel, "_Densities", densityBuf);
            _marchShader.SetBuffer(kernel, "_Vertices", vertBuf);
            _marchShader.SetBuffer(kernel, "_VertexCounter", counterBuf);
            _marchShader.SetInt("_ChunkWidth", cw);
            _marchShader.SetInt("_ChunkHeight", ch);
            _marchShader.SetFloat("_IsoLevel", chunk.isoLevel);
            _marchShader.SetInt("_Step", chunk.currentLOD);
            _marchShader.SetInt("_Scale", TerrainChunk.voxelScale);

            int groups = Mathf.CeilToInt(cw / 8f);
            _marchShader.Dispatch(kernel, groups, groups, 1);

            // Read vertex count — just 4 bytes, negligible cost
            int[] countArr = new int[1];
            counterBuf.GetData(countArr);
            int vertCount = countArr[0];
            if (vertCount <= 0) return;

            // Async readback — only the verts we actually wrote
            bool done = false;
            AsyncGPUReadback.Request(vertBuf, vertCount * 3 * sizeof(float), 0, req =>
            {
                if (!req.hasError) verts = req.GetData<Vector3>().ToArray();
                done = true;
            });
            while (!done) await Task.Yield();

            if (verts == null || verts.Length == 0) return;

            // Flat non-indexed: just sequential indices
            tris = new int[verts.Length];
            for (int i = 0; i < tris.Length; i++) tris[i] = i;
        }
        finally
        {
            densityBuf?.Release();
            vertBuf?.Release();
            counterBuf?.Release();
        }

        if (chunk == null || !chunk.gameObject.activeSelf) return;

        Mesh finalMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        finalMesh.vertices = verts;
        finalMesh.triangles = tris;
        finalMesh.RecalculateNormals();

        if (needsCollider && !skipColliderIfPlayer)
        {
            int meshID = finalMesh.GetInstanceID();
            await Task.Run(() => Physics.BakeMesh(meshID, false));
        }

        if (chunk == null || !chunk.gameObject.activeSelf) return;
        chunk.meshFilter.mesh = finalMesh;
        if (needsCollider && chunk.meshCollider != null)
            chunk.meshCollider.sharedMesh = skipColliderIfPlayer
                ? chunk.meshCollider.sharedMesh : finalMesh;
    }
    ChunkPos curChunk = new ChunkPos(-1, -1);
    void LoadChunks()
    {
        int curChunkPosX = Mathf.FloorToInt(player.position.x / ChunkWorldSize) * ChunkWorldSize;
        int curChunkPosZ = Mathf.FloorToInt(player.position.z / ChunkWorldSize) * ChunkWorldSize;
        if (curChunk.x == curChunkPosX && curChunk.z == curChunkPosZ) return;
        curChunk.x = curChunkPosX;
        curChunk.z = curChunkPosZ;

        for (int i = curChunkPosX - ChunkWorldSize * chunkDist; i <= curChunkPosX + ChunkWorldSize * chunkDist; i += ChunkWorldSize)
            for (int j = curChunkPosZ - ChunkWorldSize * chunkDist; j <= curChunkPosZ + ChunkWorldSize * chunkDist; j += ChunkWorldSize)
            {
                ChunkPos cp = new ChunkPos(i, j);
                // ← also skip chunks currently being built
                if (!chunks.ContainsKey(cp) && !toGenerate.Contains(cp) && !building.Contains(cp))
                    toGenerate.Add(cp);
            }

        var toDestroy = new List<ChunkPos>();
        foreach (var c in chunks)
        {
            ChunkPos cp = c.Key;
            if (Mathf.Abs(curChunkPosX - cp.x) > ChunkWorldSize * (chunkDist + 3) ||
                Mathf.Abs(curChunkPosZ - cp.z) > ChunkWorldSize * (chunkDist + 3))
                toDestroy.Add(cp);
        }

        toGenerate.RemoveAll(cp =>
            Mathf.Abs(curChunkPosX - cp.x) > ChunkWorldSize * (chunkDist + 1) ||
            Mathf.Abs(curChunkPosZ - cp.z) > ChunkWorldSize * (chunkDist + 1));

        foreach (ChunkPos cp in toDestroy)
        {
            chunks[cp].gameObject.SetActive(false);
            pooledChunks.Add(chunks[cp]);
            chunks.Remove(cp);
            building.Remove(cp); 
        }

        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        int cws = ChunkWorldSize;
        toGenerate.Sort((a, b) =>
        {
            Bounds boundsA = new Bounds(new Vector3(a.x + cws * 0.5f, 0, a.z + cws * 0.5f), new Vector3(cws, 100, cws));
            Bounds boundsB = new Bounds(new Vector3(b.x + cws * 0.5f, 0, b.z + cws * 0.5f), new Vector3(cws, 100, cws));
            bool inFA = GeometryUtility.TestPlanesAABB(frustumPlanes, boundsA);
            bool inFB = GeometryUtility.TestPlanesAABB(frustumPlanes, boundsB);
            if (inFA != inFB) return inFA ? -1 : 1;
            float dA = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(player.position.x, player.position.z));
            float dB = Vector2.Distance(new Vector2(b.x, b.z), new Vector2(player.position.x, player.position.z));
            return dA.CompareTo(dB);
        });

        if (!coroutineRunning)
            StartCoroutine(DelayBuildChunks());
    }

    // ─────────────────────────────────────────────────────────────
    IEnumerator DelayBuildChunks()
    {
        coroutineRunning = true;
        while (toGenerate.Count > 0)
        {
            float frameStart = Time.realtimeSinceStartup;

            while (toGenerate.Count > 0 && (Time.realtimeSinceStartup - frameStart) < 0.004f)
            {
                int batchX = toGenerate[0].x;
                int batchZ = toGenerate[0].z;
                toGenerate.RemoveAt(0);

#pragma warning disable CS4014
                BuildChunkAsync(batchX, batchZ);
#pragma warning restore CS4014
            }

            yield return null;
        }
        coroutineRunning = false;
    }
    void OnDestroy()
    {
        if (_sharedDensity.IsCreated) _sharedDensity.Dispose();
        if (_sharedTreeDensity.IsCreated) _sharedTreeDensity.Dispose();

        if (_triTable.IsCreated) _triTable.Dispose();
        if (_edgeA.IsCreated) _edgeA.Dispose();
        if (_edgeB.IsCreated) _edgeB.Dispose();

        _densityBuffer?.Release();
        _vertexBuffer?.Release();
        _triangleBuffer?.Release();
        _counterBuffer?.Release();
    }
}

public struct ChunkPos
{
    public int x, z;
    public ChunkPos(int x, int z) { this.x = x; this.z = z; }
}