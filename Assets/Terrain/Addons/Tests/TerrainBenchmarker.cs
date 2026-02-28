using Unity.Mathematics;
using UnityEngine;

using LevelGeneration.Terrain.Scene;

namespace LevelGeneration.Terrain.Addons.Tests
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(ProceduralTerrain))]
    public class TerrainBenchmarker : MonoBehaviour
    {
        enum TestMode
        {
            SphereGrid
        }

        [SerializeField]
        TestMode testMode = TestMode.SphereGrid;

        ProceduralTerrain m_Terrain;
        SDFScene m_Scene;

        void Awake()
        {
            m_Terrain = GetComponent<ProceduralTerrain>();
        }

        void OnEnable()
        {
            m_Scene = new();
            m_Terrain.LoadScene(m_Scene);

            UpdateScene();
        }

        void OnDisable()
        {
            m_Terrain.UnloadScene();
            m_Scene = null;
        }

        void UpdateScene()
        {
            m_Scene.Clear();

            switch (testMode)
            {
                case TestMode.SphereGrid:
                    MakeSphereGrid();
                    break;
            }
        }

        //
        // Test scene fillers.
        //
        void MakeSphereGrid()
        {
            const int gridSize = 4;
            const float separation = 64.0f;
            const float sphereSize = 8.0f;

            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    for (int z = 0; z < gridSize; z++)
                    {
                        float3 pos = new(x, y, z);
                        pos -= gridSize / 2.0f - 0.5f;
                        pos *= separation;

                        m_Scene.terrainShapes.AddShape(new Shape(pos, quaternion.identity, 1.0f, DistanceFunction.Sphere, BlendMode.Additive, sphereSize));
                    }
                }
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (m_Scene != null)
                UpdateScene();
        }
#endif
    }
}
