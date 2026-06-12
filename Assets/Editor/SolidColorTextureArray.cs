using UnityEngine;
using UnityEditor;

public class SolidColorTextureArray : EditorWindow
{
    [MenuItem("Tools/Create Solid Color Texture Array")]
    static void Create()
    {
        Color[] colors = new Color[]
        {
            new Color(0.5f, 0.5f, 0.5f), // 0 = rock (gray)
            new Color(0.4f, 0.25f, 0.1f), // 1 = dirt (brown)
            new Color(0.2f, 0.6f, 0.2f),  // 2 = grass (green)
            new Color(0.9f, 0.85f, 0.6f), // 3 = sand (yellow)
            new Color(0.8f, 0.8f, 0.9f),  // 4 = snow (white-blue)
            new Color(0.3f, 0.3f, 0.35f), // 5 = stone dark (dark gray)
        };

        int res = 16; // texture resolution (16x16 is fine for flat color)
        Texture2DArray texArray = new Texture2DArray(res, res, colors.Length, TextureFormat.RGBA32, true);

        for (int i = 0; i < colors.Length; i++)
        {
            Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, true);
            Color[] pixels = new Color[res * res];
            for (int p = 0; p < pixels.Length; p++) pixels[p] = colors[i];
            tex.SetPixels(pixels);
            tex.Apply();

            for (int mip = 0; mip < tex.mipmapCount; mip++)
                texArray.SetPixels(tex.GetPixels(mip), i, mip);
        }

        texArray.Apply();
        AssetDatabase.CreateAsset(texArray, "Assets/Resources/SolidColorArray.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("Created SolidColorArray.asset in Assets/Resources/");
    }
}