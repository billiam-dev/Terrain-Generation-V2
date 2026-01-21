using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public partial class ProceduralTerrain : MonoBehaviour
    {
        Scene m_Scene;
        DensityBrickMap m_BrickMap; // Note: eventually this can be converted to BrickMapLevels, for multiple LODs stretching into the horizen.
        DensityEvaluator m_DensityEvaluator;

        ShapeBrush[] m_ShapeBrushes;
        HashSet<int3> m_ModifiedBricks; // Note: this might be better off inside DensityBrickMap.

        public float WorldCellSize => k_TerrainScale;                // Uniform size in world units of a single cell.
        public float WorldBrickSize => k_BrickSize * k_TerrainScale; // Uniform size in world units of a single brick.

        const float k_TerrainScale = 1.0f;    // The size of a single cell in world units, effectively controls the scale of the whole terrain.
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_CellsPerBrick = 4096;     // The total number of cells contained in a single brick (brickSize ^ 3).
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level.

        void OnEnable()
        {
            m_Scene.Allocate();
            m_BrickMap.Allocate(k_BrickmapLevelSize, k_BrickSize);
            m_DensityEvaluator.Allocate(k_CellsPerBrick);
            m_ModifiedBricks = new();

            InitializeDebug();

            // Build initial shape queue.
            m_ShapeBrushes = GetComponentsInChildren<ShapeBrush>(false);
            foreach (ShapeBrush shapeBrush in m_ShapeBrushes)
            {
                m_Scene.AddShape(shapeBrush.EvaluateNewShape(out float3 oldPosition, out float3 oldVolume, out float3 newPosition, out float3 newVolume));
                MarkModifiedBricksFromAABB(oldPosition, oldVolume);
                MarkModifiedBricksFromAABB(newPosition, newVolume);
                shapeBrush.IsDirty = false;
            }

            m_DebugInfo.shapeCount = m_Scene.NumShapes;

#if UNITY_EDITOR
            EditorApplication.update += UpdateBrickMap;
#endif
        }

        void OnDisable()
        {
            m_Scene.Dispose();
            m_BrickMap.Dispose();
            m_DensityEvaluator.Dispose();
            m_ModifiedBricks = null;

#if UNITY_EDITOR
            EditorApplication.update -= UpdateBrickMap;
#endif
        }

        void Update()
        {
            UpdateScene();

#if !UNITY_EDITOR
            UpdateBrickMap();
#endif
        }

        void UpdateScene()
        {
            for (int i = 0; i < m_ShapeBrushes.Length; i++)
            {
                ShapeBrush shapeBrush = m_ShapeBrushes[i];

                if (shapeBrush.IsDirty)
                {
                    m_Scene.EditShape(i, shapeBrush.EvaluateNewShape(out float3 oldPosition, out float3 oldVolume, out float3 newPosition, out float3 newVolume));
                    MarkModifiedBricksFromAABB(oldPosition, oldVolume);
                    MarkModifiedBricksFromAABB(newPosition, newVolume);
                    shapeBrush.IsDirty = false;
                }
            }
        }

        void UpdateBrickMap()
        {
            Camera camera = GetCamera();
            if (!camera)
                return;

            Stopwatch.Start(ref m_DebugInfo.mapUpdateTime);

            float3 scaledCameraPos = camera.transform.position * (1.0f / k_TerrainScale);
            int3 originIndex = (int3)math.floor(scaledCameraPos / k_BrickSize);

            m_BrickMap.UpdateMap(originIndex, m_Scene, k_TerrainScale, ref m_DensityEvaluator, ref m_DebugInfo);

            Stopwatch.End(ref m_DebugInfo.mapUpdateTime);

            if (m_ModifiedBricks.Count == 0)
                return;

            Stopwatch.Start(ref m_DebugInfo.recomputationTime);

            int3[] recomputeQueue = new int3[m_ModifiedBricks.Count];
            m_ModifiedBricks.CopyTo(recomputeQueue);

            m_DebugInfo.recomputedBricks = recomputeQueue.Length;

            foreach (int3 brickIndex in recomputeQueue)
            {
                m_BrickMap.EvaluateDensity(brickIndex, m_Scene, k_TerrainScale, ref m_DensityEvaluator, ref m_DebugInfo);
                m_ModifiedBricks.Remove(brickIndex);
            }

            Stopwatch.End(ref m_DebugInfo.recomputationTime);

            m_DebugInfo.numBricks = m_BrickMap.NumBricks;
            m_DebugInfo.numBricksAllocated = m_BrickMap.NumBricksAllocated;
        }

        /// <summary>
        /// Register bricks within a world AABB for recomputation.
        /// </summary>
        void MarkModifiedBricksFromAABB(float3 boundsCentre, float3 boundsVolume)
        {
            if (math.all(boundsVolume == 0))
                return;

            GetBrickVolumeFromAABB(boundsCentre, boundsVolume, out int3 initialIndex, out int3 volume);

            for (int x = 0; x < volume.x; x++)
                for (int y = 0; y < volume.y; y++)
                    for (int z = 0; z < volume.z; z++)
                        m_ModifiedBricks.Add(initialIndex + new int3(x, y, z));
        }

        /// <summary>
        /// Get a set if itterable brick indices from a world AABB.
        /// </summary>
        void GetBrickVolumeFromAABB(float3 boundsCentre, float3 boundsVolume, out int3 initialIndex, out int3 volume)
        {
            ComputeIndices(boundsCentre, out _, out int3 brickIndex, out int3 localCellIndex);

            // Scale volume by inverse terrain scale.
            boundsVolume *= 1.0f / k_TerrainScale;

            // Snap the volume to the brick grid and output the result.
            volume = (int3)math.ceil(boundsVolume.xyz / k_BrickSize) + 1;

            // Compute the central position of the volume.
            int3 centreIndex = brickIndex;

            // For even volumes, the centre must be offset by +1 when the volume's local centre within the brick is on the positive half.
            int halfBrickSize = k_BrickSize / 2;
            if (volume.x % 2 == 0)
            {
                if (localCellIndex.x >= halfBrickSize)
                    centreIndex.x++;
            }
            if (volume.y % 2 == 0)
            {
                if (localCellIndex.y >= halfBrickSize)
                    centreIndex.y++;
            }
            if (volume.z % 2 == 0)
            {
                if (localCellIndex.z >= halfBrickSize)
                    centreIndex.z++;
            }

            // Convert the central volume position to a brick index and offset by half the volume to get the initial brick index.
            initialIndex = centreIndex - (volume / 2);
        }

        /// <summary>
        /// Takes a 3D position in world space out outputs its indices within the terrain.
        /// </summary>
        public void ComputeIndices(float3 positionWS, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex)
        {
            // Scale position by inverse terrain scale.
            positionWS *= 1.0f / k_TerrainScale;

            // Output the global cell index of the position.
            globalCellIndex = (int3)math.floor(positionWS);

            // Output the brick index containing the position.
            brickIndex = (int3)math.floor(positionWS / k_BrickSize);

            // Ouput the cells index within it's encompassing brick.
            localCellIndex = globalCellIndex - (brickIndex * k_BrickSize);
        }

        /// <summary>
        /// Sample the density cache at the given world space position.
        /// </summary>
        public float SampleDensity(float3 positionWS)
        {
            ComputeIndices(positionWS, out _, out int3 brickIndex, out int3 localCellIndex);
            return m_BrickMap.Sample(brickIndex, localCellIndex);
        }

        /// <summary>
        /// Sample the density cache at the given indices.
        /// </summary>
        public float SampleDensity(int3 brickIndex, int3 cellIndex)
        {
            return m_BrickMap.Sample(brickIndex, cellIndex);
        }

        Camera GetCamera() =>
#if UNITY_EDITOR
            Camera.current;
#else
            Camera.main;
#endif

        struct DensityBrickMap : IDisposable
        {
            /*
             * Note: this should be revised to look like this:
             * void*[] map;
             * List<DistanceBrick> bricks;
             *
             * However, the map would have to be managed very carefully when the player moves around the scene.
             * The benefits are fixed memory usage for the pointer map and extremely fast lookup.
            */

            unsafe class DensityBrick : IDisposable
            {
                NativeArray<float> density;

                public bool IsAllocated => density.IsCreated;

                public void Allocate(int size) => density = new(size * size * size, Allocator.Persistent);

                public void Dispose() => density.Dispose();

                public void CopyDenstiy(NativeArray<float> density) => this.density.CopyFrom(density);

                public float Sample(int i) => density[i];

                public void* GetUnsafePtr() => density.GetUnsafePtr();
            }

            Dictionary<int3, DensityBrick> bricks;

            int mapSize;    // Number of bricks per axis contained in this brick map.
            int brickSize;  // Number of cells per axis contained in a single brick.
            int3 lastOriginIndex;
            int numBricksAllocated;

            /// <summary>
            /// How many bricks are in the brick map.
            /// </summary>
            public readonly int NumBricks => bricks.Count;

            /// <summary>
            /// How many bricks have had their density arrays allocated. This is an indication of how much memory the brick map is using.
            /// </summary>
            public readonly int NumBricksAllocated => numBricksAllocated;

            /// <summary>
            /// Allocate an empty brick map.
            /// </summary>
            public void Allocate(int mapSize, int brickSize)
            {
                this.mapSize = mapSize;
                this.brickSize = brickSize;

                // Allocate brick map with a capacity of (mapSize ^ 3).
                bricks = new(mapSize * mapSize * mapSize);

                // Initialize last origin post to out of bounds value so the first UpdatMap will pass.
                lastOriginIndex = int.MaxValue;
            }

            /// <summary>
            /// Dipose all bricks and the brick map.
            /// </summary>
            public void Dispose()
            {
                // Dispose all bricks in brick map.
                foreach (int3 brickIndex in bricks.Keys)
                    EnsureBrickDisposed(brickIndex);

                // Dispose brick map.
                bricks = null;

                numBricksAllocated = 0;
            }

            /// <summary>
            /// Update the declared bricks based on the position of the observer.
            /// </summary>
            public void UpdateMap(int3 originIndex, Scene scene, float terrainScale, ref DensityEvaluator densityEvaluator, ref DebugInfo debugInfo)
            {
                // Early return if the view point has not changed.
                if (math.all(originIndex == lastOriginIndex))
                    return;

                lastOriginIndex = originIndex;

                // Remove out of bounds entries (loop through existing entries).
                int3[] loadedBricks = new int3[bricks.Keys.Count];
                bricks.Keys.CopyTo(loadedBricks, 0);

                foreach (int3 brickIndex in loadedBricks)
                {
                    if (!InBounds(brickIndex, originIndex))
                    {
                        EnsureBrickDisposed(brickIndex);
                        bricks.Remove(brickIndex);
                    }
                }

                // Add in bounds entries (loop through intended entry indices).
                for (int x = 0; x < mapSize; x++)
                {
                    for (int y = 0; y < mapSize; y++)
                    {
                        for (int z = 0; z < mapSize; z++)
                        {
                            int3 brickIndex = originIndex + new int3(x, y, z) - (mapSize / 2);
                            
                            if (!bricks.ContainsKey(brickIndex))
                            {
                                bricks.Add(brickIndex, new DensityBrick());
                                EvaluateDensity(brickIndex, scene, terrainScale, ref densityEvaluator, ref debugInfo);
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Evaluate the density function of a given brick.
            /// </summary>
            public void EvaluateDensity(int3 brickIndex, Scene scene, float terrainScale, ref DensityEvaluator densityEvaluator, ref DebugInfo debugInfo)
            {
                // Skip bricks which have not been loaded.
                if (!bricks.ContainsKey(brickIndex))
                    return;

                DensityEvaluationResult result = densityEvaluator.ExecuteJob(scene.Shapes, brickIndex, brickSize, terrainScale);
                if (result.IntersectsSurface)
                {
                    EnsureBrickAllocated(brickIndex);
                    bricks[brickIndex].CopyDenstiy(result.Density);
                }
                else
                {
                    EnsureBrickDisposed(brickIndex);
                }

                debugInfo.AddJobTime(result.ExecutionTime);
            }

            void EnsureBrickAllocated(int3 brickIndex)
            {
                if (!bricks[brickIndex].IsAllocated)
                {
                    bricks[brickIndex].Allocate(brickSize);
                    numBricksAllocated++;
                }
            }

            void EnsureBrickDisposed(int3 brickIndex)
            {
                if (bricks[brickIndex].IsAllocated)
                {
                    bricks[brickIndex].Dispose();
                    numBricksAllocated--;
                }
            }

            /// <summary>
            /// Sample the density function of a given brick at a given local cell index.
            /// </summary>
            public readonly float Sample(int3 brickIndex, int3 cellIndex)
            {
                if (!bricks.ContainsKey(brickIndex))
                    return 0.0f;

                // Flatten cell position to 1d density index.
                int densityIndex = (cellIndex.z * brickSize * brickSize) + (cellIndex.y * brickSize) + cellIndex.x;

                return bricks[brickIndex].Sample(densityIndex);
            }

            /// <summary>
            /// Returns a list of brick indices that have been density allocated.
            /// </summary>
            public readonly int3[] GetAllocatedBrickIndices()
            {
                int3[] allocatedBricks = new int3[numBricksAllocated];
                int i = 0;

                foreach (int3 brickIndex in bricks.Keys)
                {
                    if (bricks[brickIndex].IsAllocated)
                    {
                        allocatedBricks[i] = brickIndex;
                        i++;
                    }
                }

                return allocatedBricks;
            }

            readonly bool InBounds(int3 brickIndex, int3 originIndex) => math.all(brickIndex < originIndex + mapSize) && math.all(brickIndex > originIndex - mapSize);
        }

        struct Scene : IDisposable
        {
            public readonly NativeList<Shape> Shapes => shapes;
            public readonly int NumShapes => shapes.Length;

            NativeList<Shape> shapes;

            public void Allocate()
            {
                shapes = new(Allocator.Persistent);
            }

            public void Dispose()
            {
                shapes.Dispose();
            }

            public void AddShape(Shape shape) => shapes.Add(shape);

            public void RemoveShape(int index) => shapes.RemoveAt(index);

            public void EditShape(int index, Shape shape) => shapes[index] = shape;

            public void Clear() => shapes.Clear();
        }
    }
}
