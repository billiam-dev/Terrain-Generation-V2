using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration.Terrain.Tests
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(ProceduralTerrain))]
    public class TerrainBenchmarker : MonoBehaviour
    {
        enum Test
        {
            Null,
            SphereGrid
        }

        ProceduralTerrain m_Terrain;

        [SerializeField]
        Test testMode = Test.Null;

        void OnEnable()
        {
            m_Terrain = GetComponent<ProceduralTerrain>();
        }

        void OnDisable()
        {
            if (m_Terrain != null)
                m_Terrain.ClearShapes();
        }

        void Start()
        {
            if (m_Terrain)
                CreateScene();
        }

        void CreateScene()
        {
            m_Terrain.ClearShapes();

            switch (testMode)
            {
                case Test.SphereGrid:
                    int gridSize = 8;
                    float separation = 64.0f;
                    float sphereSize = 8.0f;

                    for (int x = 0; x < gridSize; x++)
                    {
                        for (int y = 0; y < gridSize; y++)
                        {
                            for (int z = 0; z < gridSize; z++)
                            {
                                float3 pos = new(x, y, z);
                                pos -= gridSize / 2.0f - 0.5f;
                                pos *= separation;

                                m_Terrain.AddShape(new Shape(pos, quaternion.identity, 1.0f, DistanceFunction.Sphere, BlendMode.Additive, 1.0f, sphereSize, 0.0f, 0.0f));
                            }
                        }
                    }

                    break;
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (m_Terrain)
                CreateScene();
        }
#endif
    }
}
