using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class TerrainChunk : MonoBehaviour
{
    public const int chunkWidth = 24;
    public const int chunkHeight = 48;
    public const int voxelScale = 1;
    public bool densityReady = false;
    public float[,,] densityMap = new float[chunkWidth + 1, chunkHeight + 1, chunkWidth + 1];
// 木（tree）の情報を記録する配列。プール再利用時のみリセット。
public float[,,] treeStamp = new float[chunkWidth + 1, chunkHeight + 1, chunkWidth + 1];
// treeDensityMap は treeStamp の別名（他クラス互換用）
public float[,,] treeDensityMap => treeStamp;


    [Header("Marching Cubes")]
    [Range(-1f, 1f)]
    public float isoLevel = 0f;

    public int currentLOD = 1;

    [Header("Materials")]
    public Material terrainMaterial;

    private FastNoise noise = new FastNoise();

    // ─────────────────────────────────────────────────────────────
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
                    // Clamp modulator to [0, 1.5] so simplex2 never drags terrain below base
                    float modulator = Mathf.Clamp(noise.GetSimplex(worldX * 0.3f, worldZ * 0.3f) + 0.5f, 0f, 1.5f);
                    float simplex2 = noise.GetSimplex(worldX * 3f, worldZ * 3f) * 10f * modulator;

                    // Raise base height so average surface sits well above water
                    float baseLandHeight = (chunkHeight * voxelScale) * 0.55f + simplex1 + simplex2;
                    float density = baseLandHeight - worldY;

                    float caveNoise = noise.GetPerlinFractal(worldX * 5f, worldY * 10f, worldZ * 5f);
                    float caveMask = noise.GetSimplex(worldX * 0.3f, worldZ * 0.3f) + 0.3f;
                    // Only carve caves above a minimum height so the floor never opens up
                    if (worldY > 5f && caveNoise > Mathf.Max(caveMask, 0.2f))
                        density -= 10f;

                    densityMap[x, y, z] = density;
                }
        densityReady = true;
    }

    [HideInInspector] public MeshFilter meshFilter;
    [HideInInspector] public MeshCollider meshCollider;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
    }
    // ─────────────────────────────────────────────────────────────
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
    public void InitCollider()
    {
        MeshCollider col = GetComponent<MeshCollider>();
        if (col != null) col.sharedMesh = null;
    }

// プール再利用時に呼ぶ。過去の木情報を消去。
public void ResetForReuse()
{
    densityReady = false;
    System.Array.Clear(treeStamp, 0, treeStamp.Length);
    // チャンク位置とともにリセットをログ出力
    var pos = transform != null ? transform.position.ToString() : "null";
    Debug.Log($"[Debug] ResetForReuse: Cleared treeStamp for chunk at {pos}");
}
    // ─────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────
    public void BuildMesh(bool updateCollider = true)
    {
        // Debug: Check treeStamp marker spread before mesh build
        float minT = float.MaxValue, maxT = float.MinValue;
        int cntNonZero = 0;
        for (int x = 0; x <= chunkWidth; x++)
            for (int y = 0; y <= chunkHeight; y++)
                for (int z = 0; z <= chunkWidth; z++)
                {
                    float v = treeDensityMap[x, y, z];
                    if (v != 0) cntNonZero++;
                    if (v < minT) minT = v;
                    if (v > maxT) maxT = v;
                }
        var posStr = transform != null ? transform.position.ToString() : "null";
        Debug.Log($"[Debug] BuildMesh: chunk {posStr} | treeMarkers nonzero voxels:{cntNonZero} min:{minT} max:{maxT}");
        
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
        // Debug: Check treeStamp marker spread before mesh build (async)
        float minT = float.MaxValue, maxT = float.MinValue;
        int cntNonZero = 0;
        for (int x = 0; x <= chunkWidth; x++)
            for (int y = 0; y <= chunkHeight; y++)
                for (int z = 0; z <= chunkWidth; z++)
                {
                    float v = treeDensityMap[x, y, z];
                    if (v != 0) cntNonZero++;
                    if (v < minT) minT = v;
                    if (v > maxT) maxT = v;
                }
        var posStr = transform != null ? transform.position.ToString() : "null";
        Debug.Log($"[Debug] BuildMeshAsync: chunk {posStr} | treeMarkers nonzero voxels:{cntNonZero} min:{minT} max:{maxT}");
        
        // Capture both maps on the main thread before going async
        float[,,] densitySnap = (float[,,])densityMap.Clone();
        // treeSnap param kept for API compatibility but treeStamp is the source of truth
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

    // ─────────────────────────────────────────────────────────────
    public void ApplyMesh(List<Vector3> verts, List<int> tris, List<Color> colors,
                          Vector3[] normals, bool updateCollider, Mesh prebuiltMesh = null)
    {
        if (verts.Count == 0) return;

        Mesh finalMesh = prebuiltMesh;
        if (finalMesh == null)
        {
            finalMesh = new Mesh();
            finalMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
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
        {
            MeshCollider col = GetComponent<MeshCollider>();
            col.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation;
            col.sharedMesh = finalMesh;
        }
    }

    // ─────────────────────────────────────────────────────────────
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