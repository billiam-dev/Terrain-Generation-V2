using System.Collections.Generic;
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

        public bool EnableTerrainShapes = false;

        ProceduralTerrain m_Terrain;

        List<ShapeBrush> m_ShapeBrushes;
        SDFScene m_Scene;

        bool m_Initialized;

        void OnEnable()
        {
            if (m_Terrain == null)
                m_Terrain = GetComponent<ProceduralTerrain>();

            Initialize();
            m_Terrain.LoadScene(m_Scene);
        }

        void OnDisable()
        {
            if (m_Terrain != null)
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

            m_ShapeBrushes = new();
            m_Scene = new();

            UpdateScene();

            m_Initialized = true;
        }

        void Dispose()
        {
            if (!m_Initialized)
                return;

            foreach (ShapeBrush shapeBrush in m_ShapeBrushes)
                shapeBrush.OnDisabled -= OnShapeBrushDisabled;

            m_ShapeBrushes = null;
            m_Scene = null;

            m_Initialized = false;
        }

        void UpdateScene()
        {
            //
            // Surface Noise
            //

            NoiseLayer surfaceNoise = m_Scene.surfaceNoise;

            if (!surfaceNoise.IsEnabled.Equals(EnableSurface))
                surfaceNoise.IsEnabled = EnableSurface;

            if (EnableSurface)
            {
                float3 offset = new(0, SurfaceYPosition, 0);
                if (!surfaceNoise.Offset.Equals(offset))
                    surfaceNoise.Offset = offset;

                if (!surfaceNoise.Amplitude.Equals(SurfaceNoiseAmplitude))
                    surfaceNoise.Amplitude = SurfaceNoiseAmplitude;

                if (!surfaceNoise.Frequency.Equals(SurfaceNoiseFrequency))
                    surfaceNoise.Frequency = SurfaceNoiseFrequency;

                if (!surfaceNoise.Seed.Equals(SurfaceNoiseSeed))
                    surfaceNoise.Seed = SurfaceNoiseSeed;
            }

            //
            // Global Noise
            //

            NoiseLayer globalNoise = m_Scene.globalNoise;

            if (!globalNoise.IsEnabled.Equals(EnableGlobalNoise))
                globalNoise.IsEnabled = EnableGlobalNoise;

            if (EnableGlobalNoise)
            {
                if (!globalNoise.Amplitude.Equals(GlobalNoiseAmplitude))
                    globalNoise.Amplitude = GlobalNoiseAmplitude;

                if (!globalNoise.Frequency.Equals(GlobalNoiseFrequency))
                    globalNoise.Frequency = GlobalNoiseFrequency;

                if (!globalNoise.Seed.Equals(GlobalNoiseSeed))
                    globalNoise.Seed = GlobalNoiseSeed;
            }

            //
            // Terrain Shapes
            //

            ShapeQueue terrainShapes = m_Scene.terrainShapes;

            if (!terrainShapes.IsEnabled.Equals(EnableTerrainShapes))
                terrainShapes.IsEnabled = EnableTerrainShapes;

            if (EnableTerrainShapes)
            {
                ShapeBrush[] activeShapeBrushes = GetComponentsInChildren<ShapeBrush>(false);

                if (activeShapeBrushes.Length != m_ShapeBrushes.Count)
                {
                    foreach (ShapeBrush shapeBrush in activeShapeBrushes)
                    {
                        if (m_ShapeBrushes.Contains(shapeBrush))
                            continue;

                        m_ShapeBrushes.Add(shapeBrush);
                        shapeBrush.OnDisabled += OnShapeBrushDisabled;

                        terrainShapes.AddShape(shapeBrush.Shape);
                    }
                }
            }
        }

        void OnShapeBrushDisabled(ShapeBrush shapeBrush)
        {
            m_Scene.terrainShapes.RemoveShape(shapeBrush.Shape);
            
            shapeBrush.OnDisabled -= OnShapeBrushDisabled;
            m_ShapeBrushes.Remove(shapeBrush);
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
