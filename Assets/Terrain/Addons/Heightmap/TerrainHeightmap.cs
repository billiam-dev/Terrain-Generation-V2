using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain.Addons.Heightmap
{
    public class TerrainHeightmap : ScriptableObject
    {
        [SerializeField, Range(0.01f, 1000.0f)]
        float heightMultiplier = 1.0f;

        int sizeX;
        int sizeY;

        float[] heightData;

        public int SizeX
        {
            get { return sizeX; }
        }

        public int SizeY
        {
            get { return sizeY; }
        }

        public float Sample(int x, int y)
        {
            int index = (y * sizeY) + x;
            return heightData[index] * heightMultiplier;
        }

        public float Sample(int2 coord)
        {
            int index = (coord.y * sizeY) + coord.x;
            return heightData[index] * heightMultiplier;
        }

        Texture2D GenerateIcon()
        {
            Texture2D tex = new(sizeX, sizeY);

            Color[] colors = new Color[sizeX * sizeY];
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeX; y++)
                {
                    int index = (y * sizeY) + x;
                    float value = heightData[index];
                    colors[index] = new Color(value, value, value, 1.0f);
                }
            }

            tex.SetPixels(0, 0, sizeX, sizeY, colors);
            tex.Apply();

            return tex;
        }

#if UNITY_EDITOR
        static void CreateFromTexture(Texture2D texture)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath($"Assets/New Heightmap.asset");
            TerrainHeightmap asset = CreateInstance<TerrainHeightmap>();

            int sizeX = texture.width;
            int sizeY = texture.height;
            float[] heightData = new float[sizeX * sizeY];

            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    int index = (y * sizeY) + x;
                    heightData[index] = texture.GetPixel(x, y).r;
                }
            }

            asset.Initialize(sizeX, sizeY, heightData);

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        void Initialize(int sizeX, int sizeY, float[] heightData)
        {
            this.sizeX = sizeX;
            this.sizeY = sizeY;
            this.heightData = heightData;
        }

        [MenuItem("Assets/Create Terrain Heightmap")]
        static void CreateHeightmapFromImage()
        {
            Texture2D tex = Selection.activeObject as Texture2D;
            if (tex == null)
            {
                Debug.LogWarning("Could not create Terrain Heightmap asset, no texture selected.");
                return;
            }

            if (!tex.isReadable)
            {
                Debug.LogWarning("Textures must be Read/Writable to create a heightmap from.");
                return;
            }

            CreateFromTexture(tex);
        }

        [MenuItem("Assets/Create Terrain Heightmap", true)]
        static bool CreateHeightmapFromImage_IsValid()
        {
            return Selection.activeObject is Texture2D;
        }
#endif
    }
}
