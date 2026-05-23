using System.Collections.Generic;
using UnityEngine;

public class TerrainModifier : MonoBehaviour
{
    public LayerMask groundLayer;
    public float maxDist = 4f;
    public float editStrength = 15f;
    public float editRadius = 1.5f;
    public Transform player;
    public float buildSafeRadius = 2f;

    void Update()
    {
        bool leftClick = Input.GetMouseButtonDown(0);
        bool rightClick = Input.GetMouseButtonDown(1);
        if (!leftClick && !rightClick) return;

        Ray ray = new Ray(transform.position, transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDist, groundLayer)) return;

        Vector3 hitPoint = hit.point;

        // Safety: don't build inside the player
        if (leftClick)
        {
            Vector3 playerFeet = player.position - new Vector3(0, 1f, 0);
            if (Vector3.Distance(hitPoint, playerFeet) < buildSafeRadius) return;
        }

        bool add = leftClick; // left = add density (place), right = remove (dig)

        int s = TerrainChunk.voxelScale;
        int chunkSize = TerrainChunk.chunkWidth * s;

        int minCX = Mathf.FloorToInt((hitPoint.x - editRadius) / chunkSize) * chunkSize;
        int maxCX = Mathf.FloorToInt((hitPoint.x + editRadius) / chunkSize) * chunkSize;
        int minCZ = Mathf.FloorToInt((hitPoint.z - editRadius) / chunkSize) * chunkSize;
        int maxCZ = Mathf.FloorToInt((hitPoint.z + editRadius) / chunkSize) * chunkSize;

        for (int cx = minCX; cx <= maxCX; cx += chunkSize)
            for (int cz = minCZ; cz <= maxCZ; cz += chunkSize)
            {
                if (!TerrainGenerator.chunks.TryGetValue(new ChunkPos(cx, cz), out TerrainChunk chunk)) continue;
                if (!chunk.gameObject.activeSelf) continue;

                bool modified = false;

                for (int x = 0; x <= TerrainChunk.chunkWidth; x++)
                    for (int y = 0; y <= TerrainChunk.chunkHeight; y++)
                        for (int z = 0; z <= TerrainChunk.chunkWidth; z++)
                        {
                            Vector3 voxelWorld = chunk.transform.position + new Vector3(x * s, y * s, z * s);
                            float dist = Vector3.Distance(voxelWorld, hitPoint);
                            if (dist > editRadius) continue;

                            float delta = editStrength * 0.05f * (1f - dist / editRadius);
                            if (add) chunk.densityMap[x, y, z] += delta;
                            else chunk.densityMap[x, y, z] -= delta;

                            modified = true;
                        }

                if (modified)
                    chunk.BuildMesh(true);
            }
    }
}