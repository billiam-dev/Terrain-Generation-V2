using LevelGeneration.Terrain.Rendering;
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
        SDFScene m_Scene;
        DensityCache m_DensityCache;

        Camera m_DrawingCamera;
        float3 m_ObserverPosition;

        public float WorldCellSize => k_WorldScale;                // Uniform size in world units of a single cell.
        public float WorldBrickSize => k_BrickSize * k_WorldScale; // Uniform size in world units of a single brick.

        const float k_WorldScale = 1.0f;      // The size of a single cell in world units, effectively controls the scale of the whole terrain.
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level.
        const int k_NumBrickMapLevels = 1;    // The number of brickmap levels, each doubling the grid size of the previous level.

        void OnEnable()
        {
            Initialize();

#if UNITY_EDITOR
            EditorApplication.update += Render;
#endif
        }

        void OnDisable()
        {
            Dispose();

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

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            DrawDebugGizmos();
            DrawSamplerGizmo();
        }
#endif

        void OnGUI()
        {
            DisplayDebugGUI();
        }

        void Initialize()
        {
            m_Scene = new();
            m_DensityCache = new(k_NumBrickMapLevels, k_BrickmapLevelSize, k_BrickSize, k_WorldScale);

            InitializeRendering();
            InitializeDebugGUI();
        }

        void Dispose()
        {
            m_Scene = null;
            m_DensityCache = null;

            CleanupRendering();
        }

        void Render()
        {
            // Find the current drawing camera (TODO).
            m_DrawingCamera = Camera.current;

            if (!m_DrawingCamera)
                return;

            m_RenderingData.Camera = m_DrawingCamera;

            // Update the observer position.
#if UNITY_EDITOR
            m_ObserverPosition = DetachCamera ? transform.position : m_DrawingCamera.transform.position;
#else
            m_ObserverPosition = m_DrawingCamera.transform.position;
#endif

            // Update the density cache based on the observer position and shape updates.
            m_DensityCache.Update(m_ObserverPosition, m_Scene, m_RenderingData, ref m_DebugInfo);

            // Render the terrain using the complete density cache.
            DrawClipmapLevels();
        }

        /// <summary>
        /// Add a shape to the terrain, returns an index which acts as a handle to that shape.
        /// </summary>
        public void AddShape(Shape shape)
        {
            shape.ComputeVolume(out float3 position, out float3 volume);
            MarkBoundsAsModified(position, volume);

            m_Scene.AddShape(shape);

            m_DebugInfo.shapeCount = m_Scene.NumShapes;
        }

        /// <summary>
        /// Remove a shape from the terrain using its index handle.
        /// </summary>
        public bool RemoveShape(int index)
        {
            if (index < m_Scene.NumShapes)
            {
                m_Scene.Shapes[index].ComputeVolume(out float3 position, out float3 volume);
                MarkBoundsAsModified(position, volume);

                m_Scene.RemoveShape(index);

                m_DebugInfo.shapeCount = m_Scene.NumShapes;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Replace an existing shape using its index handle.
        /// </summary>
        public bool ReplaceShape(int index, Shape shape)
        {
            if (index < m_Scene.NumShapes)
            {
                // Mark old shape bricks as modified.
                m_Scene.Shapes[index].ComputeVolume(out float3 position, out float3 volume);
                MarkBoundsAsModified(position, volume);

                // Mark new shape bricks as modified.
                shape.ComputeVolume(out position, out volume);
                MarkBoundsAsModified(position, volume);
                
                m_Scene.ReplaceShape(index, shape);

                m_DebugInfo.shapeCount = m_Scene.NumShapes;

                return true;
            }

            return false;
        }

        void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume) => m_DensityCache.MarkBoundsAsModified(boundsCentre, boundsVolume, m_RenderingData);

        /// <summary>
        /// Clear all shapes in the scene.
        /// </summary>
        public void ClearShapes()
        {
            m_DensityCache?.ClearDensity();
            m_Scene?.Clear();
        }

        /// <summary>
        /// Sample the density cache at the given indices.
        /// </summary>
        public float SampleDensity(int3 brickIndex, int3 cellIndex)
        {
            return m_DensityCache.SampleDensityCache(brickIndex, cellIndex);
        }

        partial class DensityCache
        {
            partial class SparseBrickMap
            {
                class Brick
                {
                    NativeArray<float> density;

                    internal bool IsAllocated => density.IsCreated;

                    internal void Allocate(int size) => density = new(size * size * size, Allocator.Persistent);

                    internal void Dispose() => density.Dispose();

                    internal void CopyDenstiy(NativeArray<float> density) => this.density.CopyFrom(density);

                    internal float Sample(int i) => density[i];

                    unsafe internal IntPtr GetUnsafePtr() => new(density.GetUnsafePtr());
                }

                /*
                 * Note: Dictionary<int3, Brick> should be revised to look like this:
                 * void*[] map;
                 * List<Brick> bricks;
                 *
                 * However, the map would have to be managed very carefully when the player moves around the scene.
                 * The benefits are fixed memory usage for the pointer map and extremely fast lookup.
                */

                readonly Dictionary<int3, Brick> bricks;
                readonly List<int3> recomputeQueue;

                readonly int mapSize;      // Number of bricks per axis contained in this brick map.
                readonly int brickSize;    // Number of cells per axis contained in a single brick.
                readonly int level;        // The level index if this brick map. Higher levels work with larger bricks at greater distances from the view origin.
                readonly float worldScale; // The world scale of this brick map.

                readonly int halfMapSize;
                readonly int sizeMultiplier;

                int numBricksAllocated;
                int3 originIndex;

                internal SparseBrickMap(int mapSize, int brickSize, int level, float worldScale)
                {
                    this.mapSize = mapSize;
                    this.brickSize = brickSize;
                    this.level = level;
                    this.worldScale = worldScale;

                    halfMapSize = mapSize / 2;
                    sizeMultiplier = (int)math.pow(2, level);

                    // TODO: (brickSize * sizeMultiplier) is being done like 1000 times, do it in here.
                    // ^ Separate brick points per axis and brick size?

                    bricks = new(mapSize * mapSize * mapSize);
                    recomputeQueue = new();

                    numBricksAllocated = 0;
                    originIndex = int.MaxValue;
                }

                ~SparseBrickMap()
                {
                    foreach (int3 brickIndex in bricks.Keys)
                        EnsureBrickDisposed(brickIndex);
                }

                internal void Update(float3 observerPosition, SDFScene scene, DensityEvaluator densityEvaluator, TerrainRenderingData renderingData, ref TerrainDebugInfo debugInfo)
                {
                    Stopwatch.Start(ref debugInfo.mapUpdateTime);

                    // Calculate which brick index the observer is located at this brickmap level.
                    int3 newOriginIndex = GetOriginIndex(observerPosition);

                    // If the origin index is different this frame...
                    if (math.all(newOriginIndex != originIndex))
                    {
                        // Save the new orogin index and update the brickmap.
                        originIndex = newOriginIndex;

                        // Remove out of bounds entries (loop through existing entries).
                        int3[] loadedBricks = new int3[bricks.Keys.Count];
                        bricks.Keys.CopyTo(loadedBricks, 0);

                        foreach (int3 brickIndex in loadedBricks)
                        {
                            if (!BrickInBounds(brickIndex))
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
                                    int3 brickIndex = newOriginIndex + new int3(x, y, z) - halfMapSize;

                                    if (!bricks.ContainsKey(brickIndex))
                                    {
                                        bricks.Add(brickIndex, new Brick());
                                        EvaluateDensity(brickIndex, scene, worldScale, densityEvaluator, renderingData, ref debugInfo);
                                    }
                                }
                            }
                        }
                    }

                    // Process recompute queue and clear for next frame.
                    if (recomputeQueue.Count > 0)
                    {
                        Stopwatch.Start(ref debugInfo.recomputationTime);

                        foreach (int3 brickIndex in recomputeQueue)
                            EvaluateDensity(brickIndex, scene, worldScale, densityEvaluator, renderingData, ref debugInfo);

                        recomputeQueue.Clear();

                        Stopwatch.End(ref debugInfo.recomputationTime);

                        debugInfo.recomputedBricks = recomputeQueue.Count;
                    }

                    Stopwatch.End(ref debugInfo.mapUpdateTime);

                    debugInfo.numBricks = bricks.Count;
                    debugInfo.numBricksAllocated = numBricksAllocated;
                }

                internal void ClearDensity()
                {
                    if (bricks != null)
                    {
                        foreach (int3 brickIndex in bricks.Keys)
                            EnsureBrickDisposed(brickIndex);
                    }
                }

                internal void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume, TerrainRenderingData renderingData)
                {
                    if (math.any(boundsVolume == 0))
                        return;

                    GetBrickVolumeFromAABB(boundsCentre, boundsVolume, out int3 initialIndex, out int3 volume);

                    for (int x = 0; x < volume.x; x++)
                    {
                        for (int y = 0; y < volume.y; y++)
                        {
                            for (int z = 0; z < volume.z; z++)
                            {
                                int3 brickIndex = initialIndex + new int3(x, y, z);

                                if (!BrickInBounds(brickIndex))
                                    continue;

                                // Add to brick recompute queue, if not already.
                                if (!recomputeQueue.Contains(brickIndex))
                                    recomputeQueue.Add(brickIndex);

                                // Tell the renderer to remesh the brick.
                                renderingData.FlagBrickPendingRemesh(level, brickIndex);
                            }
                        }
                    }
                }

                internal void GetBrickVolumeFromAABB(float3 boundsCentre, float3 boundsVolume, out int3 initialIndex, out int3 volume)
                {
                    ComputeIndices(boundsCentre, out _, out int3 brickIndex, out int3 localCellIndex);

                    // Scale volume by inverse terrain scale.
                    boundsVolume *= 1.0f / worldScale;

                    // Snap the volume to the brick grid and output the result.
                    volume = (int3)math.ceil(boundsVolume.xyz / (brickSize * sizeMultiplier)) + 1;

                    // Compute the central position of the volume.
                    int3 centreIndex = brickIndex;

                    // For even volumes, the centre must be offset by +1 when the volume's local centre within the brick is on the positive half.
                    int halfBrickSize = (brickSize * sizeMultiplier) / 2;
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

                void ComputeIndices(float3 positionWS, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex)
                {
                    // Scale position by inverse terrain scale.
                    positionWS *= 1.0f / (sizeMultiplier * worldScale);

                    // Output the global cell index of the position.
                    globalCellIndex = (int3)math.floor(positionWS);

                    // Output the brick index containing the position.
                    brickIndex = (int3)math.floor(positionWS / brickSize);

                    // Ouput the cells index within it's encompassing brick.
                    localCellIndex = globalCellIndex - (brickIndex * brickSize);
                }

                void EvaluateDensity(int3 brickIndex, SDFScene scene, float terrainScale, DensityEvaluator densityEvaluator, TerrainRenderingData renderingData, ref TerrainDebugInfo debugInfo)
                {
                    // Skip bricks which have not been loaded.
                    if (!bricks.ContainsKey(brickIndex))
                        return;

                    // TODO: only execute job with intersecting shapes!!

                    DensityEvaluationResult result = densityEvaluator.ExecuteJob(scene.Shapes, brickIndex, brickSize, level, terrainScale);
                    if (result.IntersectsSurface)
                    {
                        EnsureBrickAllocated(brickIndex);
                        bricks[brickIndex].CopyDenstiy(result.Density);

                        renderingData.RegisterBrick(level, brickIndex, bricks[brickIndex].GetUnsafePtr());
                    }
                    else
                    {
                        EnsureBrickDisposed(brickIndex);

                        renderingData.DeregisterBrick(level, brickIndex);
                    }

                    debugInfo.densityJobTimes.AddTime(result.ExecutionTime);
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

                internal float SampleDensityCache(int3 brickIndex, int3 cellIndex)
                {
                    if (!bricks.ContainsKey(brickIndex) || !bricks[brickIndex].IsAllocated)
                        return 0.0f;

                    // Flatten cell position to 1d density index.
                    int densityIndex = (cellIndex.z * brickSize * brickSize) + (cellIndex.y * brickSize) + cellIndex.x;

                    return bricks[brickIndex].Sample(densityIndex);
                }

                int3 GetOriginIndex(float3 observerPosition)
                {
                    // Scale the observer position by the inverse terrain scale.
                    observerPosition *= 1.0f / worldScale;

                    // Offset the observer position by half the brick size in world units so that
                    // it uses the halfway point within the brick as the boundary for shifting the
                    // origin index, rather than the corner of the brick.

                    observerPosition += brickSize * sizeMultiplier * worldScale / 2.0f;

                    return (int3)math.floor(observerPosition / (brickSize * sizeMultiplier));
                }

                bool BrickInBounds(int3 brickIndex) => math.all(brickIndex < originIndex + halfMapSize) && math.all(brickIndex > originIndex - halfMapSize);
            }

            readonly SparseBrickMap[] brickMapLevels;
            readonly DensityEvaluator densityEvaluator;

            internal DensityCache(int numLevels, int mapLevelSize, int brickSize, float worldScale)
            {
                brickMapLevels = new SparseBrickMap[numLevels];

                for (int i = 0; i < numLevels; i++)
                    brickMapLevels[i] = new(mapLevelSize, brickSize, i, worldScale);

                densityEvaluator = new();
                densityEvaluator.Allocate(brickSize * brickSize * brickSize);
            }

            ~DensityCache()
            {
                densityEvaluator.Dispose();
            }

            internal void Update(float3 observerPosition, SDFScene scene, TerrainRenderingData renderingData, ref TerrainDebugInfo debugInfo)
            {
                foreach (SparseBrickMap brickMap in brickMapLevels)
                    brickMap.Update(observerPosition, scene, densityEvaluator, renderingData, ref debugInfo);
            }

            internal void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume, TerrainRenderingData renderingData)
            {
                foreach (SparseBrickMap level in brickMapLevels)
                    level.MarkBoundsAsModified(boundsCentre, boundsVolume, renderingData);
            }

            internal void ClearDensity()
            {
                foreach (SparseBrickMap level in brickMapLevels)
                    level.ClearDensity();
            }

            internal float SampleDensityCache(int3 brickIndex, int3 cellIndex)
            {
                // TODO: Compute level to sample at.
                return brickMapLevels[0].SampleDensityCache(brickIndex, cellIndex);
            }
        }

        class SDFScene
        {
            NativeList<Shape> shapes;

            internal NativeList<Shape> Shapes => shapes;
            internal int NumShapes => shapes.Length;

            internal SDFScene() => shapes = new(Allocator.Persistent);

            ~SDFScene() => shapes.Dispose();

            internal void AddShape(Shape shape) => shapes.Add(shape);

            internal void RemoveShape(int index) => shapes.RemoveAt(index);

            internal void ReplaceShape(int index, Shape shape) => shapes[index] = shape;

            internal void Clear() => shapes.Clear();
        }
    }
}
