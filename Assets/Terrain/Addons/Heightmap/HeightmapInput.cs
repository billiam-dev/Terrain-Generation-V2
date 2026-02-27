using UnityEngine;

namespace LevelGeneration.Terrain.Addons.Heightmap
{
    [RequireComponent(typeof(ProceduralTerrain))]
    public class HeightmapInput : MonoBehaviour
    {
        [SerializeField]
        TerrainHeightmap m_Heightmap;

        ProceduralTerrain m_Terrain;

        void OnEnable()
        {
            m_Terrain = GetComponent<ProceduralTerrain>();
        }
    }
}
