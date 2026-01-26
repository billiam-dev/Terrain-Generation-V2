using LevelGeneration.Terrain.Rendering;
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

        public float WorldCellSize => k_WorldScale;                // Uniform size in world units of a single cell.
        public float WorldBrickSize => k_BrickSize * k_WorldScale; // Uniform size in world units of a single brick.

        const float k_WorldScale = 1.0f;      // The size of a single cell in world units, effectively controls the scale of the whole terrain.
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level.
        const int k_NumBrickMapLevels = 2;    // The number of brickmap levels, each doubling the grid size of the previous level.

        void OnEnable()
        {
            Initialize();

#if UNITY_EDITOR
            EditorApplication.update += UpdateDensityBrickmap;
#endif
        }

        void OnDisable()
        {
            Dispose();

#if UNITY_EDITOR
            EditorApplication.update -= UpdateDensityBrickmap;
#endif
        }

#if !UNITY_EDITOR
        void Update()
        {
            UpdateDensityBrickmap();
        }
#endif

        void Initialize()
        {
            m_Scene = new();
            m_Terrain = new(k_NumBrickMapLevels, k_BrickmapLevelSize, k_BrickSize, k_WorldScale);
            m_RenderingData = new();

            InitializeDebugGUI();
        }

        void Dispose()
        {
            m_Scene = null;
            m_Terrain = null;
            m_RenderingData = null;
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

        /// <summary>
        /// Clear all shapes in the scene.
        /// </summary>
        public void ClearShapes()
        {
            m_Terrain?.ClearDensity();
            m_Scene?.Clear();
        }

        /// <summary>
        /// Sample the density cache at the given indices.
        /// </summary>
        public float SampleDensity(int3 brickIndex, int3 cellIndex)
        {
            return m_Terrain.SampleDensityCache(brickIndex, cellIndex);
        }

        void UpdateDensityBrickmap()
        {
            m_Terrain.Update(GetObserverPosition(), m_Scene, ref m_DebugInfo);
        }

        void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume)
        {
            m_Terrain.MarkBoundsAsModified(boundsCentre, boundsVolume);
        }

        float3 GetObserverPosition()
        {
            // TODO

#if UNITY_EDITOR
            if (DetachCamera)
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

        void OnGUI()
        {
            DisplayDebugGUI();
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!isActiveAndEnabled)
                return;

            DrawDebugGizmos();
            DrawSamplerGizmo();
        }
#endif

        partial class SDFTerrain // ?? Purpose of object is to convert scene shapes into cached density values via several layers of sparsly allocated brick maps. TODO: move to different file.
        {
            partial class DensityBrickMap // SparseBrickMap?
            {
                unsafe class DensityBrick // DataBrick?
                {
                    NativeArray<float> density;

                    internal bool IsAllocated => density.IsCreated;

                    internal void Allocate(int size) => density = new(size * size * size, Allocator.Persistent);

                    internal void Dispose() => density.Dispose();

                    internal void CopyDenstiy(NativeArray<float> density) => this.density.CopyFrom(density);

                    internal float Sample(int i) => density[i];

                    internal void* GetUnsafePtr() => density.GetUnsafePtr();
                }

                /*
                 * Note: Dictionary<int3, DensityBrick> should be revised to look like this:
                 * void*[] map;
                 * List<DistanceBrick> bricks;
                 *
                 * However, the map would have to be managed very carefully when the player moves around the scene.
                 * The benefits are fixed memory usage for the pointer map and extremely fast lookup.
                */

                readonly Dictionary<int3, DensityBrick> bricks;
                readonly HashSet<int3> modifiedBricks;

                readonly int mapSize;      // Number of bricks per axis contained in this brick map.
                readonly int brickSize;    // Number of cells per axis contained in a single brick.
                readonly int level;        // The level index if this brick map. Higher levels work with larger bricks at greater distances from the view origin.
                readonly float worldScale; // The world scale of this brick map.

                readonly int halfMapSize;
                readonly int sizeMultiplier;

                int numBricksAllocated;
                int3 lastOriginIndex;

                /// <summary>
                /// How many bricks are in the brick map.
                /// </summary>
                public int NumLoadedBricks => bricks.Count;

                /// <summary>
                /// How many bricks have had their density arrays allocated. This is an indication of how much memory the brick map is using.
                /// </summary>
                public int NumBricksAllocated => numBricksAllocated;

                internal DensityBrickMap(int mapSize, int brickSize, int level, float worldScale)
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
                    modifiedBricks = new();

                    numBricksAllocated = 0;
                    lastOriginIndex = int.MaxValue;
                }

                ~DensityBrickMap()
                {
                    foreach (int3 brickIndex in bricks.Keys)
                        EnsureBrickDisposed(brickIndex);
                }

                internal void Update(float3 observerPosition, Scene scene, DensityEvaluator densityEvaluator, ref TerrainDebugInfo debugInfo)
                {
                    Stopwatch.Start(ref debugInfo.mapUpdateTime);

                    // Calculate which brick index the observer is located at this brickmap level.
                    int3 originIndex = GetOriginIndex(observerPosition);

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
                                        EvaluateDensity(brickIndex, scene, worldScale, densityEvaluator, ref debugInfo);
                                    }
                                }
                            }
                        }

                        lastOriginIndex = originIndex;
                    }

                    // Check recompute queue.
                    if (modifiedBricks.Count > 0)
                    {
                        Stopwatch.Start(ref debugInfo.recomputationTime);

                        int3[] recomputeQueue = new int3[modifiedBricks.Count];
                        modifiedBricks.CopyTo(recomputeQueue);

                        foreach (int3 brickIndex in recomputeQueue)
                        {
                            EvaluateDensity(brickIndex, scene, worldScale, densityEvaluator, ref debugInfo);
                            modifiedBricks.Remove(brickIndex);
                        }

                        Stopwatch.End(ref debugInfo.recomputationTime);

                        debugInfo.recomputedBricks = recomputeQueue.Length;
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

                internal void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume)
                {
                    if (math.all(boundsVolume == 0))
                        return;

                    GetBrickVolumeFromAABB(boundsCentre, boundsVolume, out int3 initialIndex, out int3 volume);

                    for (int x = 0; x < volume.x; x++)
                        for (int y = 0; y < volume.y; y++)
                            for (int z = 0; z < volume.z; z++)
                                modifiedBricks.Add(initialIndex + new int3(x, y, z));
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

                void EvaluateDensity(int3 brickIndex, Scene scene, float terrainScale, DensityEvaluator densityEvaluator, ref TerrainDebugInfo debugInfo)
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

                bool BrickInBounds(int3 brickIndex, int3 originIndex) => math.all(brickIndex < originIndex + halfMapSize) && math.all(brickIndex > originIndex - halfMapSize);
            }

            readonly DensityBrickMap[] brickMapLevels;
            readonly DensityEvaluator densityEvaluator;

            internal SDFTerrain(int numLevels, int mapLevelSize, int brickSize, float worldScale)
            {
                brickMapLevels = new DensityBrickMap[numLevels];

                for (int i = 0; i < numLevels; i++)
                    brickMapLevels[i] = new(mapLevelSize, brickSize, i, worldScale);

                densityEvaluator = new();
                densityEvaluator.Allocate(brickSize * brickSize * brickSize);
            }

            ~SDFTerrain()
            {
                densityEvaluator.Dispose();
            }

            internal void Update(float3 observerPosition, Scene scene, ref TerrainDebugInfo debugInfo)
            {
                foreach (DensityBrickMap brickMap in brickMapLevels)
                    brickMap.Update(observerPosition, scene, densityEvaluator, ref debugInfo);
            }

            internal void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume)
            {
                foreach (DensityBrickMap level in brickMapLevels)
                    level.MarkBoundsAsModified(boundsCentre, boundsVolume);
            }

            internal void ClearDensity()
            {
                foreach (DensityBrickMap level in brickMapLevels)
                    level.ClearDensity();
            }

            internal float SampleDensityCache(int3 brickIndex, int3 cellIndex)
            {
                // TODO: Compute level to sample at.
                return brickMapLevels[0].SampleDensityCache(brickIndex, cellIndex);
            }
        }

        class Scene
        {
            NativeList<Shape> shapes;

            public NativeList<Shape> Shapes => shapes;
            public int NumShapes => shapes.Length;

            public Scene()
            {
                shapes = new(Allocator.Persistent);
            }

            ~Scene()
            {
                shapes.Dispose();
            }

            public void AddShape(Shape shape) => shapes.Add(shape);

            public void RemoveShape(int index) => shapes.RemoveAt(index);

            public void ReplaceShape(int index, Shape shape) => shapes[index] = shape;

            public void Clear() => shapes.Clear();
        }
    }
}
