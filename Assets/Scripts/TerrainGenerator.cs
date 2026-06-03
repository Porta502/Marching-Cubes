using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    public GameObject terrainChunk;
    public Transform player;
    public Material terrainMaterial;
    public TreeGenerator treeGenerator;

    public static Dictionary<ChunkPos, TerrainChunk> chunks = new Dictionary<ChunkPos, TerrainChunk>();

    int chunkDist = 5;
    int colliderDist = 5;

    List<TerrainChunk> pooledChunks = new List<TerrainChunk>();
    List<ChunkPos> toGenerate = new List<ChunkPos>();
    HashSet<ChunkPos> building = new HashSet<ChunkPos>();
    bool coroutineRunning = false;

    // Rate-limited mesh upload queue — drained at most 2 per frame to avoid spikes
    static readonly Queue<TerrainChunk.MeshUpload> _uploadQueue =
        new Queue<TerrainChunk.MeshUpload>();

    public static void EnqueueMeshUpload(TerrainChunk.MeshUpload upload)
        => _uploadQueue.Enqueue(upload);

    static int ChunkWorldSize => TerrainChunk.chunkWidth * TerrainChunk.voxelScale;

    // 
    void Start()
    {
        int curChunkPosX = Mathf.FloorToInt(player.position.x / ChunkWorldSize) * ChunkWorldSize;
        int curChunkPosZ = Mathf.FloorToInt(player.position.z / ChunkWorldSize) * ChunkWorldSize;

        for (int i = curChunkPosX - ChunkWorldSize * chunkDist; i <= curChunkPosX + ChunkWorldSize * chunkDist; i += ChunkWorldSize)
            for (int j = curChunkPosZ - ChunkWorldSize * chunkDist; j <= curChunkPosZ + ChunkWorldSize * chunkDist; j += ChunkWorldSize)
                BuildChunkImmediate(i, j);

        // FIX #6: ClearStampPass() must be called before stamping trees
        treeGenerator.ClearStampPass();
        foreach (var kvp in chunks)
            treeGenerator.StampTrees(kvp.Value, kvp.Value.densityMap);

        foreach (var kvp in chunks)
        {
            ChunkPos cp = kvp.Key;
            chunks.TryGetValue(new ChunkPos(cp.x + ChunkWorldSize, cp.z), out TerrainChunk nx);
            chunks.TryGetValue(new ChunkPos(cp.x, cp.z + ChunkWorldSize), out TerrainChunk nz);
            chunks.TryGetValue(new ChunkPos(cp.x + ChunkWorldSize, cp.z + ChunkWorldSize), out TerrainChunk nxz);
            kvp.Value.SyncBorderDensity(nx, nz, nxz);
            kvp.Value.SyncBorderTreeStamp(nx, nz, nxz);
            kvp.Value.currentLOD = GetLOD(cp);
            kvp.Value.BuildMesh(IsNearPlayer(cp, colliderDist));

            WaterChunk wat = kvp.Value.GetComponentInChildren<WaterChunk>();
            if (wat != null) { wat.SetLocs(kvp.Value.densityMap); wat.BuildMesh(); }
        }

        SpawnPlayerOnSurface();
    }

    // 
    float lodTimer = 0f;
    int lodUpdateIndex = 0;

    void Update()
    {
        // Drain upload queue — max 1 mesh uploads per frame to prevent spikes
        int uploads = 0;
        while (_uploadQueue.Count > 0 && uploads < 1)
        {
            var u = _uploadQueue.Dequeue();
            if (u.chunk != null && u.chunk.gameObject.activeSelf)
                u.chunk.ApplyMesh(u.verts, u.tris, u.colors, u.normals, u.updateCollider);
            uploads++;
        }

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

    // 
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

    // 
    // FIX #3: BuildMeshGPU is void  do not await .Task on it
    async void UpdateChunkLOD(ChunkPos cp, TerrainChunk chunk)
    {
        if (chunk == null || !chunk.gameObject.activeSelf) return;
        int newLOD = GetLOD(cp);
        if (newLOD == chunk.currentLOD) return;
        chunk.currentLOD = newLOD;
        await chunk.BuildMeshAsync(IsNearPlayer(cp, colliderDist));
    }

    // 
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

    // 
    void BuildChunkImmediate(int xPos, int zPos)
    {
        TerrainChunk chunk;
        if (pooledChunks.Count > 0)
        {
            chunk = pooledChunks[0];
            pooledChunks.RemoveAt(0);
            chunk.ResetForReuse();
            chunk.gameObject.SetActive(true);
            chunk.transform.position = new Vector3(xPos, 0, zPos);
        }
        else
        {
            GameObject go = Instantiate(terrainChunk, new Vector3(xPos, 0, zPos), Quaternion.identity);
            chunk = go.GetComponent<TerrainChunk>();
        }

        chunk.GetComponent<MeshRenderer>().material = terrainMaterial;
        chunk.InitCollider();
        chunk.GenerateDensity(chunk.transform.position);
        chunks.Add(new ChunkPos(xPos, zPos), chunk);
    }

    // 
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
            chunk.ResetForReuse();
            chunk.gameObject.SetActive(true);
            chunk.transform.position = new Vector3(xPos, 0, zPos);
        }
        else
        {
            GameObject go = Instantiate(terrainChunk, new Vector3(xPos, 0, zPos), Quaternion.identity);
            chunk = go.GetComponent<TerrainChunk>();
        }

        chunk.GetComponent<MeshRenderer>().material = terrainMaterial;
        chunk.InitCollider();
        chunks.Add(cp, chunk);

        Vector3 chunkOrigin = chunk.transform.position;
        await Task.Run(() => chunk.GenerateDensity(chunkOrigin));

        if (chunk == null || !chunk.gameObject.activeSelf)
        {
            building.Remove(cp);
            return;
        }

        treeGenerator.StampTrees(chunk, chunk.densityMap);

        int s = ChunkWorldSize;
        await Task.WhenAll(
            BuildNewChunkAsync(xPos, zPos),
            RebuildNeighbourAsync(xPos - s, zPos),
            RebuildNeighbourAsync(xPos, zPos - s),
            RebuildNeighbourAsync(xPos - s, zPos - s)
        );

        building.Remove(cp);
    }

    // 
    async Task BuildNewChunkAsync(int xPos, int zPos)
    {
        if (!chunks.TryGetValue(new ChunkPos(xPos, zPos), out TerrainChunk chunk)) return;

        int s = ChunkWorldSize;
        chunks.TryGetValue(new ChunkPos(xPos + s, zPos), out TerrainChunk nx);
        chunks.TryGetValue(new ChunkPos(xPos, zPos + s), out TerrainChunk nz);
        chunks.TryGetValue(new ChunkPos(xPos + s, zPos + s), out TerrainChunk nxz);

        int waited = 0;
        while (waited < 10)
        {
            bool nxReady = nx == null || nx.densityReady;
            bool nzReady = nz == null || nz.densityReady;
            bool nxzReady = nxz == null || nxz.densityReady;
            if (nxReady && nzReady && nxzReady) break;
            await Task.Delay(20);
            waited++;
        }

        chunk.SyncBorderDensity(nx, nz, nxz);
        chunk.SyncBorderTreeStamp(nx, nz, nxz);

        ChunkPos cp = new ChunkPos(xPos, zPos);
        chunk.currentLOD = GetLOD(cp);
        bool needCollider = IsNearPlayer(cp, colliderDist);

        await chunk.BuildMeshAsync(needCollider);

        WaterChunk wat = chunk.GetComponentInChildren<WaterChunk>();
        if (wat != null) { wat.SetLocs(chunk.densityMap); wat.BuildMesh(); }
    }

    // 
    async Task RebuildNeighbourAsync(int xPos, int zPos)
    {
        if (!chunks.TryGetValue(new ChunkPos(xPos, zPos), out TerrainChunk chunk)) return;
        if (chunk == null || !chunk.gameObject.activeSelf) return;

        int s = ChunkWorldSize;
        chunks.TryGetValue(new ChunkPos(xPos + s, zPos), out TerrainChunk nx);
        chunks.TryGetValue(new ChunkPos(xPos, zPos + s), out TerrainChunk nz);
        chunks.TryGetValue(new ChunkPos(xPos + s, zPos + s), out TerrainChunk nxz);

        chunk.SyncBorderDensity(nx, nz, nxz);
        chunk.SyncBorderTreeStamp(nx, nz, nxz);

        ChunkPos cp = new ChunkPos(xPos, zPos);
        // FIX #7: use needCollider variable instead of calling IsNearPlayer twice
        bool needCollider = IsNearPlayer(cp, colliderDist);
        await chunk.BuildMeshAsync(needCollider);

        await Task.CompletedTask; // keeps async signature without spinning
    }

    // 
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
            treeGenerator?.EvictChunkPositions(cp.x, cp.z);
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

    // 
    IEnumerator DelayBuildChunks()
    {
        coroutineRunning = true;
        while (toGenerate.Count > 0)
        {
            // Drain as many queued chunks as the parallel cap allows each frame
            while (toGenerate.Count > 0 && building.Count < 6)
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
}