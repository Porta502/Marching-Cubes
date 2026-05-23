using System.Collections.Generic;
using UnityEngine;

public class WaterChunk : MonoBehaviour
{
    public const int waterHeight = 26;

    void Start()
    {
        transform.localPosition = new Vector3(0, waterHeight * TerrainChunk.voxelScale, 0);
    }

    public void SetLocs(float[,,] densityMap)
    {
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        int s = TerrainChunk.voxelScale;

        for (int x = 0; x < TerrainChunk.chunkWidth; x++)
            for (int z = 0; z < TerrainChunk.chunkWidth; z++)
            {
                int surfaceY = 0;
                for (int y = TerrainChunk.chunkHeight - 1; y >= 0; y--)
                {
                    if (densityMap[x, y, z] > 0f)
                    {
                        surfaceY = y;
                        break;
                    }
                }

                if (surfaceY >= waterHeight) continue;

                int tl = verts.Count;
                verts.Add(new Vector3(x * s, 0, z * s));
                verts.Add(new Vector3(x * s, 0, (z + 1) * s));
                verts.Add(new Vector3((x + 1) * s, 0, (z + 1) * s));
                verts.Add(new Vector3((x + 1) * s, 0, z * s));

                tris.Add(tl); tris.Add(tl + 1); tris.Add(tl + 2);
                tris.Add(tl); tris.Add(tl + 2); tris.Add(tl + 3);
                tris.Add(tl + 2); tris.Add(tl + 1); tris.Add(tl);
                tris.Add(tl + 3); tris.Add(tl + 2); tris.Add(tl);
            }

        if (verts.Count == 0)
        {
            GetComponent<MeshFilter>().mesh = null;
            return;
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();

        Vector3[] normals = new Vector3[verts.Count];
        for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
        mesh.normals = normals;

        GetComponent<MeshFilter>().mesh = mesh;
    }

    public void BuildMesh() { }
}