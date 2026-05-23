using System.Collections.Generic;
using UnityEngine;

public class TerrainEditor : MonoBehaviour
{
    public Camera playerCamera;
    public float reach = 5f;
    public float editStrength = 15f;
    public float editRadius = 4f;

    public Transform player;
    public float buildSafeRadius = 2.5f;

    void Update()
    {
        if (Input.GetMouseButton(0)) EditTerrain(false);
        if (Input.GetMouseButton(1)) EditTerrain(true);
    }

    void EditTerrain(bool add)
    {
        Ray ray = playerCamera.ScreenPointToRay(new Vector2(Screen.width / 2, Screen.height / 2));
        if (!Physics.Raycast(ray, out RaycastHit hit, reach)) return;

        Vector3 hitPoint = hit.point;

        if (add)
        {
            Vector3 playerFeet = player.position - new Vector3(0, 1f, 0);
            if (Vector3.Distance(hitPoint, playerFeet) < buildSafeRadius) return;
        }

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
                            // voxel world position = chunk origin + voxel index * scale
                            Vector3 voxelWorld = chunk.transform.position
                                                + new Vector3(x * s, y * s, z * s);

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