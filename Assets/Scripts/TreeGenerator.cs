using UnityEngine;
using System.Collections.Generic;

public class TreeGenerator : MonoBehaviour
{
    [Header("Spawn Rules")]
    public float spawnChance = 0.015f;
    public float noiseScale = 0.08f;
    public int minSurfaceY = 21;
    public int maxSurfaceY = 44;
    public int minTreeDistance = 8;

    [Header("Trunk")]
    public float trunkRadiusMin = 0.5f;
    public float trunkRadiusMax = 1.0f;
    public int trunkHeightMin = 4;
    public int trunkHeightMax = 8;

    [Header("Branches")]
    [Range(2, 5)]
    public int branchDepth = 3;

    [Header("Leaves")]
    public float leavesRadiusXMin = 2.5f;
    public float leavesRadiusXMax = 4.0f;
    public float leavesRadiusYMin = 1.8f;
    public float leavesRadiusYMax = 3.2f;

    [Range(0f, 0.3f)]
    public float leafNoiseStrength = 0.12f;
    public float leafNoiseFreq = 0.3f;

    private FastNoise noise = new FastNoise();
    private List<Vector2> allTreeWorldPositions = new List<Vector2>();

    public void StampTrees(TerrainChunk chunk, float[,,] densityMap)
    {
        if (chunk.treeDensityMap == null) return;

        float chunkWorldX = chunk.transform.position.x;
        float chunkWorldZ = chunk.transform.position.z;
        int evictWidth = TerrainChunk.chunkWidth;
        allTreeWorldPositions.RemoveAll(p =>
            p.x >= chunkWorldX && p.x < chunkWorldX + evictWidth &&
            p.y >= chunkWorldZ && p.y < chunkWorldZ + evictWidth);

        System.Array.Clear(chunk.treeDensityMap, 0, chunk.treeDensityMap.Length);

        int width = TerrainChunk.chunkWidth;
        int height = TerrainChunk.chunkHeight;
        int candidateCount = 0;
        int noiseFail = 0, rngFail = 0, surfaceFail = 0;

        for (int x = 3; x < width - 3; x++)
            for (int z = 3; z < width - 3; z++)
            {
                float worldX = chunk.transform.position.x + x;
                float worldZ = chunk.transform.position.z + z;

                float rawNoise = noise.GetSimplex(worldX * noiseScale, worldZ * noiseScale);
                float normalizedNoise = (rawNoise + 1f) / 2f;
                if (normalizedNoise < 0.35f) { noiseFail++; continue; }

                int hash = (int)worldX * 73856093 ^ (int)worldZ * 19349663;
                System.Random rng = new System.Random(hash);
                if (rng.NextDouble() > spawnChance * normalizedNoise) { rngFail++; continue; }

                int surfaceY = -1;
                for (int y = height - 1; y >= 1; y--)
                    if (densityMap[x, y, z] > 0f && densityMap[x, y - 1, z] > 0f)
                    { surfaceY = y; break; }
                if (surfaceY < minSurfaceY || surfaceY > maxSurfaceY) { surfaceFail++; continue; }

                candidateCount++;

                Vector2 candidate = new Vector2(worldX, worldZ);
                bool tooClose = false;
                foreach (Vector2 existing in allTreeWorldPositions)
                    if (Vector2.Distance(candidate, existing) < minTreeDistance)
                    { tooClose = true; break; }
                if (tooClose) continue;

                allTreeWorldPositions.Add(candidate);

                int tH = rng.Next(trunkHeightMin, trunkHeightMax + 1);
                float tR = Mathf.Lerp(trunkRadiusMin, trunkRadiusMax, (float)rng.NextDouble());

                int chunkOriginX = (int)chunk.transform.position.x;
                int chunkOriginZ = (int)chunk.transform.position.z;

                Vector3 treeBase = new Vector3(worldX, surfaceY, worldZ);

                GrowBranchWithLeaves(
                    densityMap, chunk.treeDensityMap,
                    chunkOriginX, chunkOriginZ,
                    width, height,
                    treeBase, Vector3.up,
                    tH * 1.2f, tR * 2.0f,
                    branchDepth, rng
                );
            }

        int markerCount = 0, leafCount = 0, trunkCount = 0;
        for (int x = 0; x <= TerrainChunk.chunkWidth; x++)
            for (int y = 0; y <= TerrainChunk.chunkHeight; y++)
                for (int z = 0; z <= TerrainChunk.chunkWidth; z++)
                {
                    float v = chunk.treeDensityMap[x, y, z];
                    if (v > 0) markerCount++;
                    if (v >= 1.5f) leafCount++;
                    else if (v >= 0.5f) trunkCount++;
                }

        Debug.Log($"[Debug] StampTrees: chunk {chunk.transform.position} | noiseFail={noiseFail} rngFail={rngFail} surfaceFail={surfaceFail} candidates={candidateCount} | Voxels: {markerCount} (trunk: {trunkCount}, leaf: {leafCount})");
    }

    void GrowBranchWithLeaves(float[,,] density, float[,,] treeMap,
                               int chunkX, int chunkZ,
                               int width, int height,
                               Vector3 start, Vector3 direction,
                               float length, float radius,
                               int depth, System.Random rng)
    {
        if (depth == 0 || length < 0.8f)
        {
            int lx = Mathf.RoundToInt(start.x) - chunkX;
            int ly = Mathf.RoundToInt(start.y);
            int lz = Mathf.RoundToInt(start.z) - chunkZ;
            float lr = Mathf.Lerp(1.2f, 2.0f, (float)rng.NextDouble());
            StampEllipsoid(density, treeMap, lx, ly, lz,
                           lr, lr * 1.2f, lr, width, height);
            return;
        }

        Vector3 end = start + direction * length;

        StampCapsule(density, treeMap,
                     chunkX, chunkZ,
                     start, end, radius,
                     width, height,
                     markerValue: 1f);

        int branchCount = rng.Next(2, 4);
        for (int i = 0; i < branchCount; i++)
        {
            float spreadAngle = Mathf.Lerp(20f, 55f, 1f - depth / (float)branchDepth);
            float randomYaw = (float)(rng.NextDouble() * 360f);
            float angleJitter = (float)(rng.NextDouble() * 15f - 7.5f);

            Vector3 axis = Mathf.Abs(direction.y) < 0.9f
                ? Vector3.Cross(direction, Vector3.up)
                : Vector3.Cross(direction, Vector3.forward);

            if (axis.sqrMagnitude < 0.001f) axis = Vector3.right;
            axis = axis.normalized;

            Vector3 newDir = Quaternion.AngleAxis(randomYaw, direction)
                           * Quaternion.AngleAxis(spreadAngle + angleJitter, axis)
                           * direction;

            if (!IsValid(newDir) || newDir.sqrMagnitude < 0.001f) continue;

            newDir = newDir.normalized;

            float newLen = length * Mathf.Lerp(0.55f, 0.72f, (float)rng.NextDouble());
            float newRadius = radius * Mathf.Lerp(0.75f, 0.85f, (float)rng.NextDouble());

            GrowBranchWithLeaves(density, treeMap,
                                 chunkX, chunkZ,
                                 width, height,
                                 end, newDir, newLen, newRadius,
                                 depth - 1, rng);
        }
    }

    void StampCapsule(float[,,] density, float[,,] treeMap,
                      int chunkX, int chunkZ,
                      Vector3 a, Vector3 b, float radius,
                      int width, int height,
                      float markerValue)
    {
        Vector3 ab = b - a;
        float len = ab.magnitude;
        if (len < 0.001f) return;
        if (len < 0.01f || !IsValid(a) || !IsValid(b)) return;
        Vector3 dir = ab / len;
        if (!IsValid(dir)) return;

        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius - 1) - chunkX);
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius + 1) - chunkX);
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius - 1));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius + 1));
        int minZ = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.z, b.z) - radius - 1) - chunkZ);
        int maxZ = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.z, b.z) + radius + 1) - chunkZ);

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    Vector3 p = new Vector3(chunkX + x, y, chunkZ + z);
                    float t = Mathf.Clamp01(Vector3.Dot(p - a, dir) / len);
                    float dist = Vector3.Distance(p, a + dir * (t * len));

                    if (dist <= radius)
                    {
                        float d = 1f - (dist / radius) * 0.4f;
                        if (density[x, y, z] < d) density[x, y, z] = d;
                        if (treeMap[x, y, z] < markerValue) treeMap[x, y, z] = markerValue;
                    }
                }
    }

    void StampEllipsoid(float[,,] density, float[,,] treeMap,
                    int cx, int cy, int cz,
                    float rX, float rY, float rZ,
                    int width, int height,
                    float trunkExcludeRadius = 0f,
                    // NEW: neighbour maps for cross-border leaf bleed
                    float[,,] neighbourXMap = null, int neighbourXOffset = 0,
                    float[,,] neighbourZMap = null, int neighbourZOffset = 0,
                    float[,,] neighbourXZMap = null)
    {
        int lx = Mathf.CeilToInt(rX + leafNoiseStrength * rX) + 1;
        int ly = Mathf.CeilToInt(rY + leafNoiseStrength * rY) + 1;
        int lz = Mathf.CeilToInt(rZ + leafNoiseStrength * rZ) + 1;

        for (int x = -lx; x <= lx; x++)
            for (int y = -ly; y <= ly; y++)
                for (int z = -lz; z <= lz; z++)
                {
                    int wx = cx + x, wy = cy + y, wz = cz + z;
                    if (wy < 0 || wy >= height) continue;

                    // ... (ellipsoid math stays the same)
                    float n = noise.GetSimplex(wx * leafNoiseFreq, wy * leafNoiseFreq, wz * leafNoiseFreq);
                    float surfaceOffset = n * leafNoiseStrength;
                    float ellipsoid = (x * x) / (rX * rX) + (y * y) / (rY * rY) + (z * z) / (rZ * rZ);

                    if (ellipsoid <= 1.0f + surfaceOffset)
                    {
                        float bottomPinch = y < 0 ? Mathf.Lerp(1.0f, 1.6f, -y / rY) : 1.0f;
                        float topTaper = y > 0 ? Mathf.Lerp(1.0f, 1.35f, y / rY) : 1.0f;
                        float adjusted = ellipsoid * bottomPinch * topTaper;

                        if (adjusted <= 1.0f + surfaceOffset)
                        {
                            float d = Mathf.Clamp(0.55f - adjusted * 0.1f, 0.51f, 0.58f);

                            // Determine which chunk this voxel belongs to
                            if (wx >= 0 && wx < width && wz >= 0 && wz < width)
                            {
                                // Normal case: inside this chunk
                                if (density[wx, wy, wz] < d) density[wx, wy, wz] = d;
                                if (treeMap[wx, wy, wz] < 1.5f) treeMap[wx, wy, wz] = 2f;
                            }
                            else if (wx >= width && wz >= 0 && wz < width && neighbourXMap != null)
                            {
                                // Spills into +X neighbour
                                int nx = wx - width;
                                if (nx < neighbourXMap.GetLength(0))
                                {
                                    if (neighbourXMap[nx, wy, wz] < d) neighbourXMap[nx, wy, wz] = d;
                                    if (neighbourXZMap == null) // reuse neighbourXMap's treeMap
                                        ; // treeMap for neighbour is passed separately ? see Part 2
                                }
                            }
                            // (similar blocks for -X, +Z, -Z)
                        }
                    }
                }
    }

    bool IsValid(Vector3 v)
    {
        return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }

    public void EvictChunkPositions(float chunkWorldX, float chunkWorldZ)
    {
        int width = TerrainChunk.chunkWidth;
        allTreeWorldPositions.RemoveAll(p =>
            p.x >= chunkWorldX && p.x < chunkWorldX + width &&
            p.y >= chunkWorldZ && p.y < chunkWorldZ + width);
    }

    public void ClearStampPass() { }
}