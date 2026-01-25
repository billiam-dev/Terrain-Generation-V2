using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain.Rendering
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProceduralTerrain))]
    public class TerrainRenderer : MonoBehaviour
    {
        ProceduralTerrain m_Terrain;

        void OnEnable()
        {
#if UNITY_EDITOR
            EditorApplication.update += Render;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= Render;
#endif            
        }

#if !UNITY_EDITOR
        void Update()
        {
            Render();
        }
#endif

        void Render()
        {
            TerrainRenderingData renderingData = m_Terrain.RenderingData;
        }

        // The clipmap level class just needs to mesh and draw the contents of a brickmap level. It has no knowledge of where the observer is.
        class ClipmapLevel
        {
            class Chunk
            {
                float3 position;
                Bounds bounds;
                Mesh mesh;

                public void DrawMesh()
                {

                }
            }

            readonly Dictionary<int3, Chunk> chunks;

            readonly int mapSize;

            public ClipmapLevel(int mapSize)
            {
                this.mapSize = mapSize;

                chunks = new(mapSize * mapSize * mapSize);
            }

            public void Render()
            {

            }
        }
    }
}
