using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGeneration.Terrain
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public partial class ProceduralTerrain : MonoBehaviour
    {
        /*
         * Notes:
         * There are two major tasks remaining:
         * 
         * - Relevant shape ONLY density JOB (only execute density jobs with intersecting shapes)
         * - Inter-level density sampling.
         * 
         * No clue how I'm gonna do the first one, but the second is a more intersting problem.
         * 
         * Currently, each brickmap level is adjoined by an extra set of bricks that are not meshed, but provide the edge data necessary to mesh.
         * However, this results in greater complexity (the whole pointer system) and lots of processing.
         * And the density JOB still has to compute those extra points in order to decide whether it should allocate or not.
         * 
         * The second solution: compute all bricks as 19x19x19, plus the extra data for mesh transitions.
         * Note that those extra indices will have to be calculated using shapes which intersect the adjacenet brick, or gaps may be created.
         * However, this reduces the complexity of the transition mesh generation and density sampling - and could remove the need for a density cache; though I still might want that for pathfinding.
         * 
        */

        SDFScene m_Scene;
        DensityCache m_DensityCache;
        bool m_IsInitialized;

        const float k_WorldScale = 1.0f;      // The size of a single cell in world units, effectively controls the scale of the whole terrain.
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level that can be converted into meshes and rendered.
        const int k_NumBrickmapLevels = 1;    // The number of brickmap levels, each doubling the grid size of the previous level.

        public static readonly float EmptyDensityValue = 32.0f;
        public static readonly float FullDensityValue = -32.0f;

        void OnEnable()
        {
            Initialize();
            RenderPipelineManager.beginCameraRendering += RenderTerrain;

#if UNITY_EDITOR
            EditorApplication.update += UpdateTerrain;
#endif
        }

        void OnDisable()
        {
            Dispose();
            RenderPipelineManager.beginCameraRendering -= RenderTerrain;

#if UNITY_EDITOR
            EditorApplication.update -= UpdateTerrain;
#endif
        }

#if !UNITY_EDITOR
        void Update()
        {
            UpdateTerrain();
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

        public void Initialize()
        {
            m_Scene = new();
            m_DensityCache = new(k_NumBrickmapLevels, k_BrickmapLevelSize, k_BrickSize, k_WorldScale);

            m_DensityCache.Allocate();

            InitializeRendering();
            InitializeDebugGUI();

            m_IsInitialized = true;
        }

        public void Dispose()
        {
            m_DensityCache.Dispose();

            m_Scene = null;
            m_DensityCache = null;

            CleanupRendering();

            m_IsInitialized = false;
        }

        void UpdateTerrain()
        {
            // The rendering data object acts as an intermediary between the density cache and the mesh generation.
            // It is created at the start of the framed, filled with all information necessary to mesh the brickmap levels and then destroyed.
            RenderingData renderingData = new();

            // Build rendering data (density cache etc.).
            Stopwatch.Start(ref m_DebugInfo.brickmapUpdateTime);
            UpdateBrickmap(ref renderingData);
            Stopwatch.End(ref m_DebugInfo.brickmapUpdateTime);

            // Update clipmaps from rendering data.
            Stopwatch.Start(ref m_DebugInfo.clipmapUpdateTime);
            UpdateClipmap(renderingData);
            Stopwatch.End(ref m_DebugInfo.clipmapUpdateTime);
        }

        void UpdateBrickmap(ref RenderingData renderingData)
        {
            // Find the observing camera.
#if UNITY_EDITOR
            Camera camera;

            if (Application.isPlaying || DetachCamera)
                camera = Camera.main;
            else
                camera = Camera.current;
#else
            Camera camera = Camera.main;
#endif

            if (!camera)
                return;

            // Update the density cache based on the observer position and shape updates.
            m_DensityCache.Update(camera.transform.position, m_Scene, ref renderingData, ref m_DebugInfo);

            renderingData.observerCamera = camera;
        }

        /// <summary>
        /// Add a shape to the terrain, returns an index which acts as a handle to that shape.
        /// </summary>
        public void AddShape(Shape shape)
        {
            if (!m_IsInitialized)
                return;

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
            if (!m_IsInitialized)
                return false;

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
            if (!m_IsInitialized)
                return false;

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

        void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume) => m_DensityCache.MarkBoundsAsModified(boundsCentre, boundsVolume);

        /// <summary>
        /// Clear all shapes in the scene.
        /// </summary>
        public void ClearShapes()
        {
            if (!m_IsInitialized)
                return;

            m_DensityCache?.ClearDensity();
            m_Scene?.Clear();
        }

        /// <summary>
        /// Sample the density cache at the given indices.
        /// </summary>
        public float SampleDensity(int3 brickIndex, int3 cellIndex) => m_DensityCache.SampleDensityCache(brickIndex, cellIndex);

        partial class DensityCache : IDisposable
        {
            partial class SparseBrickMap : IDisposable
            {
                class Brick : IDisposable
                {
                    public NativeArray<float> density;

                    // 0 = Empty
                    // 1 = Full
                    // 2 = Partial (Intersects surface)
                    public int state;

                    public bool IsAllocated => density.IsCreated;

                    public void Allocate(int size) => density = new(size * size * size, Allocator.Persistent);

                    public void Dispose() => density.Dispose();

                    unsafe public IntPtr GetDensityPtr() => new(density.GetUnsafePtr());
                }

                readonly int mapSize;              // Number of bricks per axis contained in this brick map.
                readonly int brickSize;            // Number of points per axis contained in a single brick.
                readonly float worldScale;         // The world scale of this brick map.
                readonly int level;                // The level index if this brick map. Higher levels work with larger bricks at greater distances from the view origin
                
                readonly int halfMapSize;          // Half the number of bricks per axis contained in this brick map.
                readonly int quarterMapSize;       // One fourth the number of bricks per axis contained in this brick map.
                readonly int levelScale;           // The scale multiplier relative to the smallest brickmap level, derrived from the level index with the equation (2 ^ level).

                readonly Dictionary<int3, Brick> bricks;
                readonly List<int3> modifiedBricks;

                int numBricksAllocated;            // How many bricks are density allocated.
                int3 originIndex;                  // The global brick index in which this map currently originates.
                int3 lowerGridOffset;              // The local offset of the brickmap contained within this one.

                public int3 LocalOriginIndex => originIndex;

                public SparseBrickMap(int mapSize, int brickSize, float worldScale, int level)
                {
                    // Add 2 to account for one brick on the outside of each level. These bricks are required for meshing.
                    this.mapSize = mapSize;

                    this.brickSize = brickSize;
                    this.worldScale = worldScale;
                    this.level = level;

                    halfMapSize = mapSize / 2;
                    quarterMapSize = mapSize / 4;

                    levelScale = (int)math.pow(2, level);

                    bricks = new(mapSize * mapSize * mapSize);
                    modifiedBricks = new();

                    numBricksAllocated = 0;
                    originIndex = int.MaxValue;
                }

                public void Dispose()
                {
                    foreach (int3 brickIndex in bricks.Keys)
                        EnsureBrickDisposed(brickIndex);
                }

                public void Update(float3 observerPosition, int3 lowerGridOriginIndex, SDFScene scene, DensityEvaluator densityEvaluator, ref BrickmapRenderingData renderingData, ref TerrainDebugInfo debugInfo)
                {
                    // Calculate the brick index in which the observer is located (local within this brickmap level).
                    int3 newOriginIndex = GetOriginIndex(observerPosition);

                    int3 newLowerGridOffset = 0;
                    if (level > 0)
                        newLowerGridOffset = (lowerGridOriginIndex - newOriginIndex - newOriginIndex) / 2;

                    // If the origin index is different this frame; update loaded bricks.
                    if (math.any(newOriginIndex != originIndex) || math.any(newLowerGridOffset != lowerGridOffset))
                    {
                        // Save new origin indices.
                        originIndex = newOriginIndex;
                        lowerGridOffset = newLowerGridOffset;

                        // Copy brick map keys.
                        int3[] bricksCopy = new int3[bricks.Keys.Count];
                        bricks.Keys.CopyTo(bricksCopy, 0);

                        // Remove out of bounds entries (loop through existing entries).
                        foreach (int3 brickIndex in bricksCopy)
                        {
                            if (!BrickInBounds(brickIndex) || BrickOverlapsPreviousLevel(brickIndex))
                            {
                                // Ensure density data for brick is disposed and remove from map.
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
                                    // Find the index position of this brick, using the origin index calculated from the observer position.
                                    int3 brickIndex = newOriginIndex + new int3(x, y, z) - halfMapSize;

                                    if (!bricks.ContainsKey(brickIndex) && !BrickOverlapsPreviousLevel(brickIndex))
                                    {
                                        // Add brick to map.
                                        bricks.Add(brickIndex, new Brick());

                                        // Mark brick as modified.
                                        if (!modifiedBricks.Contains(brickIndex))
                                            modifiedBricks.Add(brickIndex);
                                    }
                                }
                            }
                        }
                    }

                    // Re-evaluate density of all modified bricks and clear the list.
                    renderingData.modifiedBricks = new();

                    if (modifiedBricks.Count > 0)
                    {
                        double t = 0.0;
                        Stopwatch.Start(ref t);

                        foreach (int3 brickIndex in modifiedBricks)
                        {
                            if (bricks.ContainsKey(brickIndex))
                            {
                                // Re-evaluate density.
                                bool changeDetected = EvaluateDensity(brickIndex, scene, densityEvaluator, ref debugInfo);

                                // Inform the renderer that the density data at this brick has changed.
                                if (changeDetected)
                                    renderingData.modifiedBricks.Add(brickIndex);

                                debugInfo.numBricksModified++;
                            }
                        }

                        Stopwatch.End(ref t);
                        debugInfo.bricksEvaluationTime += t;

                        Debug.Log($"l:{level} -> Evaluate {debugInfo.numBricksModified} modifications (list size = {modifiedBricks.Count}). {debugInfo.numDensityJOBs} JOBS in {Stopwatch.ToMilliseconds(debugInfo.bricksEvaluationTime)}ms");

                        modifiedBricks.Clear();
                    }

                    // TODO: allocing and filling this dict is taking WAAAY to long!!

                    // Update rendering data.
                    renderingData.allocatedBricks = new(numBricksAllocated);
                    if (numBricksAllocated > 0)
                    {
                        foreach (int3 brickIndex in bricks.Keys)
                        {
                            Brick brick = bricks[brickIndex];

                            if (brick.state == 2)
                            {
                                renderingData.allocatedBricks.Add(brickIndex, new BrickRenderingData()
                                {
                                    state = brick.state,
                                    densityPtr = brick.GetDensityPtr()
                                });
                            }
                        }
                    }

                    renderingData.originIndex = originIndex;
                    renderingData.size = mapSize;

                    // Update debug info.
                    debugInfo.numBricksLoaded += bricks.Count;
                    debugInfo.numBricksAllocated += numBricksAllocated;
                }

                bool EvaluateDensity(int3 brickIndex, SDFScene scene, DensityEvaluator densityEvaluator, ref TerrainDebugInfo debugInfo)
                {
                    double totalTime = 0.0;
                    Stopwatch.Start(ref totalTime);

                    Brick brick = bricks[brickIndex];

                    double shapesTime = 0.0;
                    Stopwatch.Start(ref shapesTime);

                    NativeList<Shape> intersectingShapes = GetIntersectingShapes(brickIndex, scene); // This is causing this function to take waaaay longer than it should!!

                    Stopwatch.End(ref shapesTime);

                    // If there are no intersecting shapes, early return.
                    // True is returned if the brick state was not already empty.
                    int numShapes = intersectingShapes.Length;

                    if (intersectingShapes.Length == 0)
                    {
                        intersectingShapes.Dispose();

                        if (brick.state == 0)
                            return false;

                        brick.state = 0;
                        return true;
                    }

                    // Execute density evaluation job.
                    DensityEvaluationResult result = densityEvaluator.ExecuteJob(intersectingShapes, brickIndex, brickSize, worldScale, levelScale);
                    debugInfo.numDensityJOBs++;
                    debugInfo.densityJobTimes.AddTime(result.ExecutionTime);

                    intersectingShapes.Dispose();

                    // If the new state is equal to the old state and the state != partial, return false.
                    if (brick.state == result.DensityState && brick.state != 2)
                        return false;

                    brick.state = result.DensityState;

                    if (brick.state == 2)
                    {
                        EnsureBrickAllocated(brickIndex);
                        brick.density.CopyFrom(result.Density);
                    }
                    else
                    {
                        EnsureBrickDisposed(brickIndex);
                    }

                    Stopwatch.End(ref totalTime);
                    Debug.Log($"Full density evaluation time: {Stopwatch.ToMilliseconds(totalTime)}ms (Shapes: {numShapes}, {Stopwatch.ToMilliseconds(shapesTime)}ms)");

                    return true;
                }

                NativeList<Shape> GetIntersectingShapes(int3 brickIndex, SDFScene scene)
                {
                    NativeList<Shape> intersectingShapes = new(Allocator.TempJob);

                    foreach (Shape shape in scene.Shapes)
                    {
                        // Get brick volume from shape.
                        shape.ComputeVolume(out float3 boundsPosition, out float3 boundsVolume);
                        GetBrickVolumeFromAABB(boundsPosition, boundsVolume, out int3 initialIndex, out int3 volume);

                        // Account for density data overflowing into adjacent bricks.
                        initialIndex -= 1;
                        volume += 2;

                        // If brick index is within the volume, add to the.
                        if (math.all(brickIndex >= initialIndex) && math.all(brickIndex < initialIndex + volume))
                            intersectingShapes.Add(shape);
                    }

                    return intersectingShapes;
                }

                public void DeallocateAll()
                {
                    if (bricks != null)
                    {
                        foreach (int3 brickIndex in bricks.Keys)
                        {
                            EnsureBrickDisposed(brickIndex);
                            bricks[brickIndex].state = 0;
                        }
                    }
                }

                public void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume)
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
                                
                                if (BrickInBounds(brickIndex) && !modifiedBricks.Contains(brickIndex))
                                    modifiedBricks.Add(brickIndex);
                            }
                        }
                    }
                }

                void GetBrickVolumeFromAABB(float3 boundsCentre, float3 boundsVolume, out int3 initialIndex, out int3 volume)
                {
                    ComputeIndices(boundsCentre, out _, out int3 brickIndex, out int3 localCellIndex);

                    float3 worldBrickSize = brickSize * levelScale * worldScale;

                    // Snap the volume to the brick grid and output the result.
                    volume = (int3)math.ceil(boundsVolume.xyz / worldBrickSize) + 1;

                    // Compute the central position of the volume.
                    int3 centreIndex = brickIndex;

                    // For even volumes, the centre must be offset by +1 when the volume's local centre within the brick is on the positive half.
                    int halfBrickSize = brickSize / 2;
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
                    positionWS *= 1.0f / (levelScale * worldScale);

                    // Output the global cell index of the position.
                    globalCellIndex = (int3)math.floor(positionWS);

                    // Output the brick index containing the position.
                    brickIndex = (int3)math.floor(positionWS / brickSize);

                    // Ouput the cells index within it's encompassing brick.
                    localCellIndex = globalCellIndex - (brickIndex * brickSize);
                }

                void EnsureBrickAllocated(int3 brickIndex)
                {
                    Brick brick = bricks[brickIndex];

                    if (!brick.IsAllocated)
                    {
                        brick.Allocate(brickSize + 3);
                        numBricksAllocated++;
                    }
                }

                void EnsureBrickDisposed(int3 brickIndex)
                {
                    Brick brick = bricks[brickIndex];

                    if (brick.IsAllocated)
                    {
                        brick.Dispose();
                        numBricksAllocated--;
                    }
                }

                int3 GetOriginIndex(float3 observerPosition)
                {
                    // See ClipmapDemo.cs for explanation of this function.
                    // Essentially, the brick index is calculated on the above grid level and then remapped to this grid level.
                    // This prevents bricks from ever partially overlapping, which cannot be meshed.

                    // Scale the observer position by the world scale.
                    observerPosition *= 1.0f / worldScale;

                    // Compute position on upper grid level.
                    float3 upperGridPosition = (observerPosition + (brickSize * levelScale)) / math.pow(2, level + 1) / brickSize;

                    // Floor position and multiply by 2 to restore index to this grid level.
                    return (int3)math.floor(upperGridPosition) * 2;
                }

                bool BrickInBounds(int3 brickIndex) => math.all(brickIndex < originIndex + halfMapSize) && math.all(brickIndex >= originIndex - halfMapSize);

                bool BrickOverlapsPreviousLevel(int3 brickIndex)
                {
                    if (level == 0)
                        return false;

                    float3 localBrickIndex = brickIndex - originIndex;

                    if (localBrickIndex.x < lowerGridOffset.x + quarterMapSize &&
                        localBrickIndex.x >= lowerGridOffset.x - quarterMapSize)
                    {
                        if (localBrickIndex.y < lowerGridOffset.y + quarterMapSize &&
                            localBrickIndex.y >= lowerGridOffset.y - quarterMapSize)
                        {
                            if (localBrickIndex.z < lowerGridOffset.z + quarterMapSize &&
                                localBrickIndex.z >= lowerGridOffset.z - quarterMapSize)
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }

                public float SampleDensityCache(int3 brickIndex, int3 cellIndex)
                {
                    // If brick is not loaded, return empty value (air).
                    if (!bricks.ContainsKey(brickIndex))
                        return EmptyDensityValue;

                    // Flatten cell position to 1d density index.
                    int densityIndex = (cellIndex.z * brickSize * brickSize) + (cellIndex.y * brickSize) + cellIndex.x;

                    // Sample the given brick at that index.
                    return bricks[brickIndex].density[densityIndex];
                }
            }

            readonly SparseBrickMap[] brickMapLevels;
            readonly DensityEvaluator densityEvaluator;

            readonly int numLevels;
            readonly int brickSize;

            public DensityCache(int numLevels, int mapLevelSize, int brickSize, float worldScale)
            {
                this.numLevels = numLevels;
                this.brickSize = brickSize;

                brickMapLevels = new SparseBrickMap[this.numLevels];

                for (int i = 0; i < this.numLevels; i++)
                    brickMapLevels[i] = new(mapLevelSize, brickSize, worldScale, i);

                densityEvaluator = new();
            }

            public void Allocate()
            {
                densityEvaluator.Allocate(brickSize);
            }

            public void Dispose()
            {
                for (int i = 0; i < numLevels; i++)
                    brickMapLevels[i].Dispose();

                densityEvaluator.Dispose();
            }

            public void Update(float3 observerPosition, SDFScene scene, ref RenderingData renderingData, ref TerrainDebugInfo debugInfo)
            {
                debugInfo.numBricksLoaded = 0;
                debugInfo.numBricksAllocated = 0;
                debugInfo.numBricksModified = 0;
                debugInfo.bricksEvaluationTime = 0.0;
                debugInfo.numDensityJOBs = 0;

                renderingData.brickmapData = new BrickmapRenderingData[numLevels];

                brickMapLevels[0].Update(observerPosition, 0, scene, densityEvaluator, ref renderingData.brickmapData[0], ref debugInfo);

                for (int i = 1; i < numLevels; i++)
                    brickMapLevels[i].Update(observerPosition, brickMapLevels[i - 1].LocalOriginIndex, scene, densityEvaluator, ref renderingData.brickmapData[i], ref debugInfo);
            }

            public void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume)
            {
                foreach (SparseBrickMap level in brickMapLevels)
                    level.MarkBoundsAsModified(boundsCentre, boundsVolume);
            }

            public void ClearDensity()
            {
                foreach (SparseBrickMap level in brickMapLevels)
                    level.DeallocateAll();
            }

            public float SampleDensityCache(int3 brickIndex, int3 cellIndex)
            {
                // TODO: Compute level to sample at.
                // Check each level for lowest in-bounds level.

                return brickMapLevels[0].SampleDensityCache(brickIndex, cellIndex);
            }
        }

        class SDFScene
        {
            readonly List<Shape> shapes = new();

            public List<Shape> Shapes => shapes;

            public int NumShapes => shapes.Count;

            public void AddShape(Shape shape) => shapes.Add(shape);

            public void RemoveShape(int index) => shapes.RemoveAt(index);

            public void ReplaceShape(int index, Shape shape) => shapes[index] = shape;

            public void Clear() => shapes.Clear();
        }
    }
}
