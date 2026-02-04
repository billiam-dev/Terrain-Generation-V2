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
        SDFScene m_Scene;
        DensityCache m_DensityCache;

        Camera m_ObserverCamera;
        float3 m_ObserverPosition;

        const float k_WorldScale = 1.0f;      // The size of a single cell in world units, effectively controls the scale of the whole terrain.
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level that can be converted into meshes and rendered.
        const int k_NumBrickMapLevels = 3;    // The number of brickmap levels, each doubling the grid size of the previous level.

        public static readonly float k_EmptyDensityValue = 32.0f;
        public static readonly float k_FullDensityValue = -32.0f;

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
            m_DensityCache = new(k_NumBrickMapLevels, k_BrickmapLevelSize, k_BrickSize, k_WorldScale);

            m_Scene.Allocate();
            m_DensityCache.Allocate();

            InitializeRendering();
            InitializeDebugGUI();
        }

        public void Dispose()
        {
            m_Scene.Dispose();
            m_DensityCache.Dispose();

            m_Scene = null;
            m_DensityCache = null;

            CleanupRendering();
        }

        void UpdateTerrain()
        {
            // Assign the camera from which the terrain is originated.
#if UNITY_EDITOR
            if (Application.isPlaying)
                m_ObserverCamera = Camera.main;
            else
                m_ObserverCamera = Camera.current;
#else
            m_OriginCamera = Camera.main;
#endif

            if (!m_ObserverCamera)
                return;

            m_RenderingData.ObserverCamera = m_ObserverCamera;

            // Update the observer position.
#if UNITY_EDITOR
            m_ObserverPosition = DetachCamera ? transform.position : m_ObserverCamera.transform.position;
#else
            m_ObserverPosition = m_ObserverCamera.transform.position;
#endif

            // Update the density cache based on the observer position and shape updates.
            Stopwatch.Start(ref m_DebugInfo.brickmapUpdateTime);
            m_DensityCache.Update(m_ObserverPosition, m_Scene, ref m_DebugInfo);
            Stopwatch.End(ref m_DebugInfo.brickmapUpdateTime);

            // Update the clipmap meshes using the density cache.
            Stopwatch.Start(ref m_DebugInfo.clipmapUpdateTime);
            UpdateClipmap();
            Stopwatch.End(ref m_DebugInfo.clipmapUpdateTime);
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

        void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume) => m_DensityCache.MarkBoundsAsModified(boundsCentre, boundsVolume);

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
        public float SampleDensity(int3 brickIndex, int3 cellIndex) => m_DensityCache.SampleDensityCache(brickIndex, cellIndex);

        partial class DensityCache : IDisposable
        {
            partial class SparseBrickMap : IDisposable
            {
                class Brick
                {
                    NativeArray<float> density;

                    // 0 = Empty
                    // 1 = Full
                    // 2 = Partial (Intersects surface)
                    int state;

                    public bool IsAllocated => density.IsCreated;

                    public void Allocate(int size) => density = new(size * size * size, Allocator.Persistent);

                    public void Dispose() => density.Dispose();

                    public void CopyDenstiy(NativeArray<float> density) => this.density.CopyFrom(density);

                    public void SetState(int state) => this.state = state;

                    public int GetState() => this.state;

                    unsafe public IntPtr GetUnsafePtr() => new(density.GetUnsafePtr());

                    public float Sample(int index)
                    {
                        if (state == 0)
                            return k_EmptyDensityValue;
                        else if (state == 1)
                            return k_FullDensityValue;

                        return density[index];
                    }
                }

                readonly int mapSize;              // Number of bricks per axis contained in this brick map.
                readonly int brickSize;            // Number of points per axis contained in a single brick.
                readonly float worldScale;         // The world scale of this brick map.
                readonly int level;                // The level index if this brick map. Higher levels work with larger bricks at greater distances from the view origin
                
                readonly int halfMapSize;          // Half the number of bricks per axis contained in this brick map.
                readonly int levelScale;           // The scale multiplier relative to the smallest brickmap level, derrived from the level index with the equation (2 ^ level).

                readonly Dictionary<int3, Brick> bricks;
                readonly List<int3> modifiedBricks;
                BrickmapRenderingData renderingData;

                int numBricksAllocated;            // How many bricks are density allocated.
                int3 originIndex;                  // The global brick index in which this map currently originates.
                int3 lowerGridOffset;              // The local offset of the brickmap contained within this one.

                public BrickmapRenderingData RenderingData => renderingData;

                public int3 LocalOriginIndex => originIndex;

                public SparseBrickMap(int mapSize, int brickSize, float worldScale, int level)
                {
                    // Add 2 to account for one brick on the outside of each level. These bricks are required for meshing.
                    this.mapSize = mapSize + 2;

                    this.brickSize = brickSize;
                    this.worldScale = worldScale;
                    this.level = level;

                    halfMapSize = this.mapSize / 2;

                    levelScale = (int)math.pow(2, level);

                    bricks = new(this.mapSize * this.mapSize * this.mapSize);
                    modifiedBricks = new();

                    renderingData = new();
                    renderingData.Allocate(this.mapSize);

                    numBricksAllocated = 0;
                    originIndex = int.MaxValue;

                    renderingData.size = mapSize;
                }

                public void Dispose()
                {
                    foreach (int3 brickIndex in bricks.Keys)
                        EnsureBrickDisposed(brickIndex);

                    renderingData.Dispose();
                }

                public void Update(float3 observerPosition, int3 lowerGridOriginIndex, SDFScene scene, DensityEvaluator densityEvaluator, ref TerrainDebugInfo debugInfo)
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

                        renderingData.originIndex = originIndex;

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

                                // Unload brick in rendering data and remove from modified bricks if necessary.
                                renderingData.densitySampler.RemoveBrick(brickIndex);
                                renderingData.MarkBrickAsModified(brickIndex);
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

                                        // Load brick into rendering data.
                                        renderingData.densitySampler.AddBrick(brickIndex);
                                    }
                                }
                            }
                        }
                    }

                    // Re-evaluate density of all modified bricks and clear the list.
                    if (modifiedBricks.Count > 0)
                    {
                        foreach (int3 brickIndex in modifiedBricks)
                        {
                            if (bricks.ContainsKey(brickIndex))
                            {
                                // Re-evaluate density.
                                bool changeDetected = EvaluateDensity(brickIndex, scene, densityEvaluator, ref debugInfo);

                                // Inform the renderer that the density data at this brick has changed.
                                if (changeDetected)
                                    renderingData.MarkBrickAsModified(brickIndex);
                            }
                        }

                        modifiedBricks.Clear();
                    }

                    debugInfo.numBricksLoaded += bricks.Count;
                    debugInfo.numBricksAllocated += numBricksAllocated;
                }

                bool EvaluateDensity(int3 brickIndex, SDFScene scene, DensityEvaluator densityEvaluator, ref TerrainDebugInfo debugInfo)
                {
                    // TODO: only execute job with intersecting shapes!!

                    DensityEvaluationResult result = densityEvaluator.ExecuteJob(scene.Shapes, brickIndex, brickSize, worldScale, levelScale);
                    debugInfo.densityJobTimes.AddTime(result.ExecutionTime);

                    Brick brick = bricks[brickIndex];

                    int currentState = brick.GetState();
                    int newState = result.DensityState;

                    // If the new state is equal to the old state and the state != partial, return false.
                    if (newState == currentState && currentState != 2)
                        return false;

                    brick.SetState(newState);
                    renderingData.densitySampler.SetBrickState(brickIndex, newState);

                    if (result.IntersectsSurface)
                    {
                        EnsureBrickAllocated(brickIndex);
                        brick.CopyDenstiy(result.Density);
                    }
                    else
                    {
                        EnsureBrickDisposed(brickIndex);
                    }

                    return true;
                }

                public void DeallocateAll()
                {
                    if (bricks != null)
                    {
                        foreach (int3 brickIndex in bricks.Keys)
                            EnsureBrickDisposed(brickIndex);
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

                                if (!modifiedBricks.Contains(brickIndex))
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
                        brick.Allocate(brickSize);
                        numBricksAllocated++;
                        
                        renderingData.densitySampler.AddDensityPtr(brickIndex, brick.GetUnsafePtr());
                    }
                }

                void EnsureBrickDisposed(int3 brickIndex)
                {
                    Brick brick = bricks[brickIndex];

                    if (brick.IsAllocated)
                    {
                        brick.Dispose();
                        numBricksAllocated--;

                        renderingData.densitySampler.RemoveDensityPtr(brickIndex);
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

                bool BrickOnEdge(int3 brickIndex) => math.any(brickIndex == originIndex + halfMapSize - 1) || math.any(brickIndex == originIndex - halfMapSize);

                bool BrickOverlapsPreviousLevel(int3 brickIndex)
                {
                    if (level == 0)
                        return false;

                    float3 localBrickIndex = brickIndex - originIndex;
                    int overlapExtent = (mapSize - 2) / 4;

                    if (localBrickIndex.x < lowerGridOffset.x + overlapExtent &&
                        localBrickIndex.x >= lowerGridOffset.x - overlapExtent)
                    {
                        if (localBrickIndex.y < lowerGridOffset.y + overlapExtent &&
                            localBrickIndex.y >= lowerGridOffset.y - overlapExtent)
                        {
                            if (localBrickIndex.z < lowerGridOffset.z + overlapExtent &&
                                brickIndex.z >= lowerGridOffset.z - overlapExtent)
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
                        return k_EmptyDensityValue;

                    // Flatten cell position to 1d density index.
                    int densityIndex = (cellIndex.z * brickSize * brickSize) + (cellIndex.y * brickSize) + cellIndex.x;

                    // Sample the given brick at that index.
                    return bricks[brickIndex].Sample(densityIndex);
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

            public void Update(float3 observerPosition, SDFScene scene, ref TerrainDebugInfo debugInfo)
            {
                debugInfo.numBricksLoaded = 0;
                debugInfo.numBricksAllocated = 0;

                brickMapLevels[0].Update(observerPosition, 0, scene, densityEvaluator, ref debugInfo);

                for (int i = 1; i < numLevels; i++)
                    brickMapLevels[i].Update(observerPosition, brickMapLevels[i - 1].LocalOriginIndex, scene, densityEvaluator, ref debugInfo);
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

            public BrickmapRenderingData[] GetRenderingData()
            {
                BrickmapRenderingData[] brickMapRenderingDatas = new BrickmapRenderingData[numLevels];
                for (int i = 0; i < numLevels; i++)
                    brickMapRenderingDatas[i] = brickMapLevels[i].RenderingData;

                return brickMapRenderingDatas;
            }
        }

        class SDFScene : IDisposable
        {
            NativeList<Shape> shapes;

            public NativeList<Shape> Shapes => shapes;

            public int NumShapes => shapes.Length;

            public void Allocate() => shapes = new(Allocator.Persistent);

            public void Dispose() => shapes.Dispose();

            public void AddShape(Shape shape) => shapes.Add(shape);

            public void RemoveShape(int index) => shapes.RemoveAt(index);

            public void ReplaceShape(int index, Shape shape) => shapes[index] = shape;

            public void Clear() => shapes.Clear();
        }
    }
}
