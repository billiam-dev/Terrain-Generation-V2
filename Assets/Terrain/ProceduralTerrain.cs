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
        Scene m_Scene;
        SDFTerrain m_Terrain;
        TerrainRenderingData m_RenderingData;

        public float WorldCellSize => k_TerrainScale;                // Uniform size in world units of a single cell.
        public float WorldBrickSize => k_BrickSize * k_TerrainScale; // Uniform size in world units of a single brick.
        public TerrainRenderingData RenderingData => m_RenderingData;

        const float k_TerrainScale = 1.0f;    // The size of a single cell in world units, effectively controls the scale of the whole terrain.
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level.
        const int k_NumBrickMapLevels = 1;    // The number of brickmap levels, each doubling the grid size of the previous level.

        void OnEnable()
        {
            m_Scene.Allocate();

            m_Terrain = new(k_NumBrickMapLevels, k_BrickmapLevelSize, k_BrickSize, k_TerrainScale);
            m_RenderingData = new();
            
            InitializeDebug();

            m_DebugInfo.shapeCount = 0;

#if UNITY_EDITOR
            EditorApplication.update += UpdateDensityBrickmap;
#endif
        }

        void OnDisable()
        {
            m_Scene.Dispose();

            m_Terrain = null;
            m_RenderingData = null;

#if UNITY_EDITOR
            EditorApplication.update -= UpdateDensityBrickmap;
#endif
        }

#if !UNITY_EDITOR
        void Update()
        {
            UpdateTerrain();
        }
#endif

        /// <summary>
        /// Add a shape to the terrain, returns an index which acts as a handle to that shape.
        /// </summary>
        public int AddShape(Shape shape)
        {
            shape.ComputeVolume(out float3 position, out float3 volume);
            MarkBoundsAsModified(position, volume);

            m_DebugInfo.shapeCount = m_Scene.NumShapes;

            return m_Scene.AddShape(shape);
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

        /// <summary>
        /// Clear all shapes in the scene.
        /// </summary>
        public void ClearShapes()
        {
            m_Terrain?.Clear();
            m_Scene.Clear();
        }

        void UpdateDensityBrickmap() => m_Terrain.Update(GetObserverPosition(), m_Scene, ref m_DebugInfo);

        void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume) => m_Terrain.MarkBoundsAsModified(boundsCentre, boundsVolume);

        // TODO: Remove ComputeIndices from ProceduralTerrain, moved to BrickMap

        /// <summary>
        /// Takes a 3D position in world space out outputs its indices within the terrain.
        /// Public for debugging purposes.
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
            return SampleDensity(brickIndex, localCellIndex);
        }

        /// <summary>
        /// Sample the density cache at the given indices.
        /// </summary>
        public float SampleDensity(int3 brickIndex, int3 cellIndex)
        {
            return m_Terrain.SampleDensityCache(brickIndex, cellIndex); // TODO: work out which level to sample (interpolate between closest samples for levels > 0)
        }

        float3 GetObserverPosition()
        {
#if UNITY_EDITOR
            if (m_DetachCamera)
                return transform.position;
#endif

            Camera camera =
            #if UNITY_EDITOR
                Camera.current;
#else
                Camera.main;
#endif

            if (!camera)
                return 0;

            return camera.transform.position;
        }

        partial class SDFTerrain
        {
            class DensityBrickMap
            {
                /*
                 * Note: this should be revised to look like this:
                 * void*[] map;
                 * List<DistanceBrick> bricks;
                 *
                 * However, the map would have to be managed very carefully when the player moves around the scene.
                 * The benefits are fixed memory usage for the pointer map and extremely fast lookup.
                */

                unsafe class DensityBrick
                {
                    NativeArray<float> density;

                    public bool IsAllocated => density.IsCreated;

                    public void Allocate(int size) => density = new(size * size * size, Allocator.Persistent);

                    public void Dispose() => density.Dispose();

                    public void CopyDenstiy(NativeArray<float> density) => this.density.CopyFrom(density);

                    public float Sample(int i) => density[i];

                    public void* GetUnsafePtr() => density.GetUnsafePtr();
                }

                readonly Dictionary<int3, DensityBrick> bricks;
                readonly HashSet<int3> modifiedBricks; // TODO: this is here now!

                readonly int mapSize;      // Number of bricks per axis contained in this brick map.
                readonly int halfMapSize;  // Number of bricks per axis either side of 0, 0, 0.
                readonly int brickSize;    // Number of cells per axis contained in a single brick.
                readonly int level;

                int numBricksAllocated;
                int3 lastOriginIndex;

                /// <summary>
                /// How many bricks are in the brick map.
                /// </summary>
                public int NumBricks => bricks.Count;

                /// <summary>
                /// How many bricks have had their density arrays allocated. This is an indication of how much memory the brick map is using.
                /// </summary>
                public int NumBricksAllocated => numBricksAllocated;

                /// <summary>
                /// The scale that the base brick size is multiplied by at this brick level.
                /// </summary>
                public int SizeMultiplier => (int)math.pow(2, level);

                public DensityBrickMap(int mapSize, int brickSize, int level)
                {
                    this.mapSize = mapSize;
                    this.halfMapSize = mapSize / 2;
                    this.brickSize = brickSize;
                    this.level = level;

                    // Allocate brick map with a capacity of (mapSize ^ 3).
                    bricks = new(mapSize * mapSize * mapSize);
                    
                    modifiedBricks = new();

                    // Initialize brick allocation counter to 0.
                    numBricksAllocated = 0;

                    // Initialize last origin post to out of bounds value so the first UpdatMap will pass.
                    lastOriginIndex = int.MaxValue;
                }

                ~DensityBrickMap()
                {
                    // Ensure all bricks in brick map are disposed before cleaning up to prevent memory leaks.
                    foreach (int3 brickIndex in bricks.Keys)
                        EnsureBrickDisposed(brickIndex);
                }

                /// <summary>
                /// Update the declared bricks based on the position of the observer.
                /// </summary>
                public void Update(float3 scaledObserverPosition, Scene scene, float terrainScale, DensityEvaluator densityEvaluator, ref DebugInfo debugInfo)
                {
                    // Calculate which brick index the observer is located at this brickmap level.
                    int3 originIndex = (int3)math.floor(scaledObserverPosition / (brickSize * math.pow(2, level)));

                    if (math.all(originIndex != lastOriginIndex))
                    {
                        // Remove out of bounds entries (loop through existing entries).
                        int3[] loadedBricks = new int3[bricks.Keys.Count];
                        bricks.Keys.CopyTo(loadedBricks, 0);

                        foreach (int3 brickIndex in loadedBricks)
                        {
                            if (!BrickInBounds(brickIndex, originIndex))
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
                                    int3 brickIndex = originIndex + new int3(x, y, z) - halfMapSize;

                                    if (!bricks.ContainsKey(brickIndex))
                                    {
                                        bricks.Add(brickIndex, new DensityBrick());
                                        EvaluateDensity(brickIndex, scene, terrainScale, densityEvaluator, ref debugInfo);
                                    }
                                }
                            }
                        }

                        lastOriginIndex = originIndex;
                    }

                    // Check recompute queue.
                    if (modifiedBricks.Count > 0)
                    {
                        int3[] recomputeQueue = new int3[modifiedBricks.Count];
                        modifiedBricks.CopyTo(recomputeQueue);

                        foreach (int3 brickIndex in recomputeQueue)
                        {
                            EvaluateDensity(brickIndex, scene, k_TerrainScale, densityEvaluator, ref debugInfo);
                            modifiedBricks.Remove(brickIndex);
                        }
                    }
                }

                /// <summary>
                /// Evaluate the density function of a given brick.
                /// </summary>
                public void EvaluateDensity(int3 brickIndex, Scene scene, float terrainScale, DensityEvaluator densityEvaluator, ref DebugInfo debugInfo)
                {
                    // Skip bricks which have not been loaded.
                    if (!bricks.ContainsKey(brickIndex))
                        return;

                    // TODO: only execute job with intersecting shapes.

                    DensityEvaluationResult result = densityEvaluator.ExecuteJob(scene.Shapes, brickIndex, brickSize, level, terrainScale);
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

                /// <summary>
                /// Clear (deallocate) all density bricks, perform when removing all shapes from the scene to quickly reset the brickmap.
                /// </summary>
                public void ClearDensity()
                {
                    if (bricks != null)
                    {
                        foreach (int3 brickIndex in bricks.Keys)
                            EnsureBrickDisposed(brickIndex);
                    }
                }

                public void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume)
                {
                    if (math.all(boundsVolume == 0))
                        return;

                    GetBrickVolumeFromAABB(boundsCentre, boundsVolume, out int3 initialIndex, out int3 volume);

                    for (int x = 0; x < volume.x; x++)
                        for (int y = 0; y < volume.y; y++)
                            for (int z = 0; z < volume.z; z++)
                                modifiedBricks.Add(initialIndex + new int3(x, y, z));
                }

                public void GetBrickVolumeFromAABB(float3 boundsCentre, float3 boundsVolume, out int3 initialIndex, out int3 volume)
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

                void ComputeIndices(float3 positionWS, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex)
                {
                    // TODO: Remove constants, add scaling math.pow(2, level)

                    // Scale position by inverse terrain scale.
                    positionWS *= 1.0f / k_TerrainScale;

                    // Output the global cell index of the position.
                    globalCellIndex = (int3)math.floor(positionWS);

                    // Output the brick index containing the position.
                    brickIndex = (int3)math.floor(positionWS / k_BrickSize);

                    // Ouput the cells index within it's encompassing brick.
                    localCellIndex = globalCellIndex - (brickIndex * k_BrickSize);
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
                public float Sample(int3 brickIndex, int3 cellIndex)
                {
                    if (!bricks.ContainsKey(brickIndex))
                        return 0.0f;

                    // Flatten cell position to 1d density index.
                    int densityIndex = (cellIndex.z * brickSize * brickSize) + (cellIndex.y * brickSize) + cellIndex.x;

                    return bricks[brickIndex].Sample(densityIndex);
                }

                /// <summary>
                /// Returns an array of all brick indices that are loaded.
                /// </summary>
                /// <returns></returns>
                public int3[] GetLoadedBrickIndices()
                {
                    int3[] loadedBricks = new int3[bricks.Count];
                    bricks.Keys.CopyTo(loadedBricks, 0);

                    return loadedBricks;
                }

                /// <summary>
                /// Returns a list of brick indices that have been allocated.
                /// </summary>
                public int3[] GetAllocatedBrickIndices()
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

                bool BrickInBounds(int3 brickIndex, int3 originIndex) => math.all(brickIndex < originIndex + halfMapSize) && math.all(brickIndex > originIndex - halfMapSize);
            }

            readonly DensityBrickMap[] brickMapLevels;
            readonly int numLevels;
            readonly int mapLevelSize;
            readonly int brickSize;
            readonly float scale;

            readonly DensityEvaluator densityEvaluator;

            public SDFTerrain(int numLevels, int mapLevelSize, int brickSize, float scale)
            {
                this.numLevels = numLevels;
                this.mapLevelSize = mapLevelSize;
                this.brickSize = brickSize;
                this.scale = scale;

                brickMapLevels = new DensityBrickMap[numLevels];

                for (int i = 0; i < numLevels; i++)
                    brickMapLevels[i] = new(mapLevelSize, brickSize, i);

                densityEvaluator = new();
                densityEvaluator.Allocate(brickSize * brickSize * brickSize);
            }

            ~SDFTerrain()
            {
                densityEvaluator.Dispose();
            }

            public void Update(float3 observerPosition, Scene scene, ref DebugInfo debugInfo)
            {
                Stopwatch.Start(ref debugInfo.mapUpdateTime);

                // Scale the observer position by the inverse terrain scale.
                float3 scaledObserverPosition = observerPosition * (1.0f / scale);

                // Offset camera pos by half the brick size in world units so it uses the halfway point within bricks to determine when to shift the terrain origin.
                scaledObserverPosition += brickSize * scale / 2.0f;

                // Update all brick map levels.
                foreach (DensityBrickMap brickMap in brickMapLevels)
                    brickMap.Update(scaledObserverPosition, scene, scale, densityEvaluator, ref debugInfo);

                Stopwatch.End(ref debugInfo.mapUpdateTime);

                // TODO
                //debugInfo.recomputedBricks = recomputeQueue.Length;
                //debugInfo.numBricks = m_Brickmap[0].NumBricks;
                //debugInfo.numBricksAllocated = m_Brickmap[0].NumBricksAllocated;
            }

            public void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume)
            {
                foreach (DensityBrickMap level in brickMapLevels)
                    level.MarkBoundsAsModified(boundsCentre, boundsVolume);
            }

            public void Clear()
            {
                foreach (DensityBrickMap level in brickMapLevels)
                    level.ClearDensity();
            }

            public float SampleDensityCache(int3 brickIndex, int3 cellIndex)
            {
                // TODO: Compute level to sample at.
                return brickMapLevels[0].Sample(brickIndex, cellIndex);
            }

#if UNITY_EDITOR
            public void GetBrickVolumeFromAABB(int level, float3 boundsPosition, float3 boundsVolume, out int3 initialIndex, out int3 volume) => brickMapLevels[level].GetBrickVolumeFromAABB(boundsPosition, boundsVolume, out initialIndex, out volume);
#endif
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

            public int AddShape(Shape shape)
            {
                shapes.Add(shape);
                return shapes.Length;
            }

            public void RemoveShape(int index)
            {
                shapes.RemoveAt(index);
            }

            public void ReplaceShape(int index, Shape shape)
            {
                shapes[index] = shape;
            }

            public void Clear()
            {
                if (shapes.IsCreated)
                    shapes.Clear();
            }
        }
    }
}
