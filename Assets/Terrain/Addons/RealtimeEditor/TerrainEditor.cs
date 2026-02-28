using Unity.Mathematics;
using UnityEngine;

using LevelGeneration.Terrain.Scene;

namespace LevelGeneration.Terrain.Addons.RealtimeEditor
{
    /// <summary>
    /// Provides an interface to build terrain with the Unity Editor in realtime.
    /// </summary>
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProceduralTerrain))]
    public class TerrainEditor : MonoBehaviour
    {
        /// <summary>
        /// Initialize the scene with a surface to stand on.
        /// </summary>
        [Tooltip("Initialize the scene with a surface to stand on.")]
        public bool EnableSurface = false;

        public float SurfaceNoiseAmplitude = 50.0f;
        public float SurfaceNoiseFrequency = 0.001f;
        public float SurfaceYPosition = 0.0f;
        public int SurfaceNoiseSeed = 0;

        /// <summary>
        /// Apply a 3D noise layer to the scene.
        /// </summary>
        [Tooltip("Apply a 3D noise layer to the scene.")]
        public bool EnableGlobalNoise = false;

        public float GlobalNoiseAmplitude = 4.0f;
        public float GlobalNoiseFrequency = 0.02f;
        public int GlobalNoiseSeed = 0;

        ProceduralTerrain m_Terrain;
        SDFScene m_Scene;
        bool m_Initialized;

        void Awake()
        {
            m_Terrain = GetComponent<ProceduralTerrain>();
        }

        void OnEnable()
        {
            Initialize();
            m_Terrain.LoadScene(m_Scene);
        }

        void OnDisable()
        {
            m_Terrain.UnloadScene();
            Dispose();
        }

        void Update()
        {
            UpdateScene();
        }

        void Initialize()
        {
            if (m_Initialized)
                return;

            m_Scene = new();

            // Init surface
            m_Scene.surfaceNoise.Offset = new float3(0, SurfaceYPosition, 0);
            m_Scene.surfaceNoise.Amplitude = SurfaceNoiseAmplitude;
            m_Scene.surfaceNoise.Frequency = SurfaceNoiseFrequency;
            m_Scene.surfaceNoise.Seed = SurfaceNoiseSeed;

            // Init global noise
            m_Scene.globalNoise.Amplitude = GlobalNoiseAmplitude;
            m_Scene.globalNoise.Frequency = GlobalNoiseFrequency;
            m_Scene.globalNoise.Seed = GlobalNoiseSeed;

            // Init terrain shapes
            foreach (ShapeBrush shapeBrush in GetComponentsInChildren<ShapeBrush>(false))
                m_Scene.terrainShapes.AddShape(shapeBrush.Shape);

            m_Initialized = true;
        }

        void Dispose()
        {
            if (!m_Initialized)
                return;

            m_Scene = null;

            m_Initialized = false;
        }

        void UpdateScene()
        {
            // Surface Noise
            if (EnableSurface)
            {
                NoiseLayer surfaceNoise = m_Scene.surfaceNoise;

                float3 offset = new(0, SurfaceYPosition, 0);
                if (math.any(surfaceNoise.Offset != offset))
                    surfaceNoise.Offset = offset;

                if (surfaceNoise.Amplitude != SurfaceNoiseAmplitude)
                    surfaceNoise.Amplitude = SurfaceNoiseAmplitude;

                if (surfaceNoise.Frequency != SurfaceNoiseFrequency)
                    surfaceNoise.Frequency = SurfaceNoiseFrequency;

                if (surfaceNoise.Seed != SurfaceNoiseSeed)
                    surfaceNoise.Seed = SurfaceNoiseSeed;
            }

            // Global Noise
            if (EnableGlobalNoise)
            {
                NoiseLayer globalNoise = m_Scene.globalNoise;

                if (globalNoise.Amplitude != GlobalNoiseAmplitude)
                    globalNoise.Amplitude = GlobalNoiseAmplitude;

                if (globalNoise.Frequency != GlobalNoiseFrequency)
                    globalNoise.Frequency = GlobalNoiseFrequency;

                if (globalNoise.Seed != GlobalNoiseSeed)
                    globalNoise.Seed = GlobalNoiseSeed;
            }

            // Terrain Shapes
            ShapeBrush[] shapeBrushes = GetComponentsInChildren<ShapeBrush>(false);

            if (shapeBrushes.Length != m_Scene.terrainShapes.Count)
            {
                m_Scene.terrainShapes.Clear();

                foreach (ShapeBrush shapeBrush in shapeBrushes)
                {
                    m_Scene.terrainShapes.AddShape(shapeBrush.Shape);
                }
            }

            // Note: for removing shapes prolly just add OnDisable Action to ShapeBrush
        }

        public void RandomizeSeed()
        {
            System.Random random = new((int)(Time.realtimeSinceStartupAsDouble * 1000.0));

            SurfaceNoiseSeed = random.Next();
            GlobalNoiseSeed = random.Next();

            m_Scene.surfaceNoise.Seed = SurfaceNoiseSeed;
            m_Scene.globalNoise.Seed = GlobalNoiseSeed;
        }
    }
}
