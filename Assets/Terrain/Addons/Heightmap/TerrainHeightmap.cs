using UnityEngine;

namespace LevelGeneration.Terrain.Addons.Heightmap
{
    public class TerrainHeightmap : ScriptableObject
    {
        readonly int sizeX;
        readonly int sizeY;

        readonly float[] heightData;

        public int SizeX
        {
            get { return sizeX; }
        }

        public int SizeY
        {
            get { return sizeY; }
        }

        public float[] HeightData
        {
            get { return heightData; }
        }
    }
}
