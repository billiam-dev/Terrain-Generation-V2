using LevelGeneration.Terrain.Meshing;
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
        public Material Material;
        public bool ForceMainCamera;
        public bool UseStaticOrigin;
        public bool ColorBrickmapLevels;

        SDFScene m_Scene;
        Brickmap[] m_BrickmapLevels;
        MaterialPropertyBlock m_MaterialProperties;
        
        bool m_IsInitialized;

        static DensityEvaluator s_DensityEvaluator;
        static BatchChunkMesher s_Mesher;

        static MeanTime s_AvgDensityEvalTime;
        static MeanTime s_AvgMeshingTime;
        double m_TotalMeshingTime;
        int m_TotalMeshingTasks;
        double m_UpdateTime;
        double m_RenderTime;

        const float k_WorldScale = 1.0f;      // The size of a single cell in world units, effectively controls the scale of the whole terrain.
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level that can be converted into meshes and rendered.
        const int k_NumBrickmapLevels = 3;    // The number of brickmap levels, each doubling the grid size of the previous level.

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
            if (!isActiveAndEnabled)
                return;

            DrawDebugGizmos();
            DrawDensityTester();
        }
#endif

        void OnGUI()
        {
            DisplayDebugGUI();
        }

        public void Initialize()
        {
            m_Scene = new();

            m_BrickmapLevels = new Brickmap[k_NumBrickmapLevels];
            for (int i = 0; i < k_NumBrickmapLevels; i++)
                m_BrickmapLevels[i] = new(k_BrickmapLevelSize, k_BrickSize, i, k_WorldScale);

            m_MaterialProperties = new();

            s_DensityEvaluator ??= new();
            s_DensityEvaluator.Allocate(k_BrickSize);

            s_Mesher ??= new();
            s_Mesher.Allocate();

            s_AvgDensityEvalTime = new();
            s_AvgMeshingTime = new();

            m_IsInitialized = true;
        }

        public void Dispose()
        {
            foreach (Brickmap brickmap in m_BrickmapLevels)
                brickmap.Dispose();

            m_Scene = null;
            m_BrickmapLevels = null;
            m_MaterialProperties = null;

            s_DensityEvaluator.Dispose();
            s_Mesher.Dispose();

            s_AvgDensityEvalTime = null;
            s_AvgMeshingTime = null;

            m_IsInitialized = false;
        }

        void UpdateTerrain()
        {
            // Find observer camera.
#if UNITY_EDITOR
            Camera camera = Application.isPlaying || ForceMainCamera ? Camera.main : Camera.current;
#else
            Camera camera = Camera.main;
#endif

            if (!camera)
                return;

            Stopwatch.Start(ref m_UpdateTime);

            float3 observerPosition = UseStaticOrigin ? transform.position : camera.transform.position;

            // Update brickmap levels.
            m_BrickmapLevels[0].Update(camera, observerPosition, 0, m_Scene);
            for (int i = 1; i < k_NumBrickmapLevels; i++)
                m_BrickmapLevels[i].Update(camera, observerPosition, m_BrickmapLevels[i - 1].OriginIndex, m_Scene);

            // Execute meshing tasks queued this frame.
            int pendingTasks = s_Mesher.NumPendingTasks;
            if (pendingTasks > 0)
            {
                m_TotalMeshingTasks = pendingTasks;

                Stopwatch.Start(ref m_TotalMeshingTime);

                s_Mesher.ExecutePendingTasksContinuous();

                Stopwatch.End(ref m_TotalMeshingTime);
                s_AvgMeshingTime.AddTime(m_TotalMeshingTime / m_TotalMeshingTasks);
            }

            // Disable scene changed flag.
            m_Scene.IsDirty = false;

            Stopwatch.End(ref m_UpdateTime);
        }

        void RenderTerrain(ScriptableRenderContext context, Camera camera)
        {
            Stopwatch.Start(ref m_RenderTime);

            for (int i = 0; i < k_NumBrickmapLevels; i++)
            {
                m_MaterialProperties.SetColor("_ClipmapDebugColor", ColorBrickmapLevels ? k_BrickmapLevelDebugColors[i] : Color.white);
                m_BrickmapLevels[i].Render(camera, Material, m_MaterialProperties);
            }

            Stopwatch.End(ref m_RenderTime);
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

                return true;
            }

            return false;
        }

        void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume)
        {
            foreach (Brickmap brickmap in m_BrickmapLevels)
                brickmap.MarkBoundsAsModified(boundsCentre, boundsVolume);
        }

        /// <summary>
        /// Clear all shapes in the scene.
        /// </summary>
        public void ClearShapes()
        {
            if (!m_IsInitialized)
                return;

            foreach (Brickmap brickmap in m_BrickmapLevels)
                brickmap.Clear();

            m_Scene?.Clear();
        }

        /// <summary>
        /// Sample the density cache at the given indices.
        /// </summary>
        public float SampleDensity(float3 positionWS)
        {
            positionWS *= 1.0f / k_WorldScale;

            // TODO
            return EmptyDensityValue;
        }

        /// <summary>
        /// Raytraces the terrain to find the surface position.
        /// </summary>
        public float3 FindSurface(float3 origin, float3 direction)
        {
            // We can use the cached density values to speed this up, if a brick we are visiting is not allocated we can skip it immediatly.

            // Pog http://www.cse.yorku.ca/~amana/research/grid.pdf

            return 0.0f;
        }

        static void GetBrickVolumeFromAABB(int brickSize, float scale, float3 boundsCentre, float3 boundsVolume, out int3 initialIndex, out int3 volume)
        {
            ComputeIndices(brickSize, scale, boundsCentre, out _, out int3 brickIndex, out int3 localCellIndex);

            float3 worldBrickSize = brickSize * scale;

            // Snap the volume to the brick grid and output the result.
            volume = (int3)math.ceil(boundsVolume.xyz / worldBrickSize) + 1; // TODO: Remove +1 when possible. This is especially detremental on higher brickmap levels.

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

        static void ComputeIndices(int brickSize, float scale, float3 positionWS, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex)
        {
            // Scale position by inverse terrain scale.
            positionWS *= 1.0f / scale;

            // Output the global cell index of the position.
            globalCellIndex = (int3)math.floor(positionWS);

            // Output the brick index containing the position.
            brickIndex = (int3)math.floor(positionWS / brickSize);

            // Ouput the cells index within it's encompassing brick.
            localCellIndex = globalCellIndex - (brickIndex * brickSize);
        }

        partial class Brickmap : IDisposable
        {
            partial class Brick : IDisposable
            {
                readonly int3 index;
                readonly int size;
                readonly int levelScale;
                readonly float worldScale;

                readonly float3 worldPosition;
                readonly float3 worldSize;

                NativeArray<float> density;
                IntPtr densityPtr;

                readonly Mesh mesh;

                bool isUniformState;
                bool densityModified;
                bool remeshRequired;

                public Brick(int3 index, int size, int levelScale, float worldScale)
                {
                    this.index = index;
                    this.size = size;
                    this.levelScale = levelScale;
                    this.worldScale = worldScale;

                    worldSize = worldScale * levelScale * size;
                    worldPosition = worldSize * index;

                    mesh = new()
                    {
                        bounds = new(worldSize * 0.5f, worldSize)
                    };

                    isUniformState = true;
                    densityModified = false;
                    remeshRequired = false;
                }

                public void Dispose()
                {
                    if (density.IsCreated)
                        density.Dispose();
                }

                public void Update(Camera observerCamera, List<Shape> shapes)
                {
                    // Cannot have this check if terrain casts shadows.
                    //if (!InViewFrustum(observerCamera))
                    //    return;

                    // Can do something like this
                    if (levelScale > 1 && !InViewFrustum(observerCamera))
                        return;

                    if (densityModified)
                    {
                        EvaluateDensity(shapes);
                        densityModified = false;
                    }

                    if (remeshRequired)
                    {
                        if (isUniformState)
                        {
                            mesh.Clear();
                        }
                        else
                        {
                            s_Mesher.QueueRemeshTask(new MeshingTask(
                                mesh,
                                null,
                                index,
                                size,
                                levelScale,
                                worldScale,
                                densityPtr
                            ));
                        }

                        remeshRequired = false;
                    }
                }

                public void Render(Camera renderCamera, Material material, MaterialPropertyBlock mpb)
                {
                    // Cannot have this check if terrain casts shadows.
                    //if (!InViewFrustum(renderCamera))
                    //    return;

                    // Can do something like this
                    if (levelScale > 1 && !InViewFrustum(renderCamera))
                        return;

                    // TODO: I reckon this is garbage performance.
                    Graphics.DrawMesh(mesh, worldPosition, Quaternion.identity, material, 0, renderCamera, 0, mpb);
                }

                void EvaluateDensity(List<Shape> shapes)
                {
                    // If there are no intersecting shapes, this brick is of uniform state.
                    int numShapes = shapes.Count;

                    if (numShapes == 0)
                    {
                        if (!isUniformState)
                            remeshRequired = true;

                        isUniformState = true;
                        return;
                    }

                    // Execute density evaluation.
                    double t = 0.0;
                    Stopwatch.Start(ref t);

                    DensityEvaluationResult result = s_DensityEvaluator.Execute(FindIntersectingShapes(shapes), index, size, levelScale, worldScale);

                    Stopwatch.End(ref t);
                    s_AvgDensityEvalTime.AddTime(t);

                    // We do not need to remesh bricks that are already of uniform state.
                    if (isUniformState && result.isUniformState)
                        return;

                    isUniformState = result.isUniformState;

                    if (isUniformState)
                    {
                        // Dispose density data if necessary.
                        if (density.IsCreated)
                            density.Dispose();
                    }
                    else
                    {
                        // Allocate and copy density data.
                        if (!density.IsCreated)
                        {
                            int extendedSize = size + 3;
                            density = new(extendedSize * extendedSize * extendedSize, Allocator.Persistent);
                            
                            unsafe
                            {
                                densityPtr = new IntPtr(density.GetUnsafePtr());
                            }
                        }

                        density.CopyFrom(result.density);
                    }

                    remeshRequired = true;
                }

                List<Shape> FindIntersectingShapes(List<Shape> shapes)
                {
                    List<Shape> intersectingShapes = new();

                    foreach (Shape shape in shapes)
                    {
                        // Get brick volume from shape.
                        shape.ComputeVolume(out float3 boundsPosition, out float3 boundsVolume);
                        GetBrickVolumeFromAABB(size, levelScale * worldScale, boundsPosition, boundsVolume, out int3 initialIndex, out int3 volume);

                        // Account for density data overflowing into adjacent bricks.
                        initialIndex -= 1;
                        volume += 2;

                        // ^ Note: this causes jobs to be queued in bricks that we know won't be allocated TODO!!

                        // If brick index is within the volume, add to the.
                        if (math.all(index >= initialIndex) && math.all(index < initialIndex + volume))
                            intersectingShapes.Add(shape);
                    }

                    return intersectingShapes;
                }

                bool InViewFrustum(Camera camera)
                {
                    Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                    Bounds bounds = new(worldPosition + (worldSize / 2.0f), worldSize);
                    return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
                }

                public void MarkAsModified() => densityModified = true;

                public float SampleCache(int3 index)
                {
                    int extendedSize = size + 3;
                    return density[(index.z * extendedSize * extendedSize) + (index.y * extendedSize) + index.x];
                }

                public bool IntersecsSurface => !isUniformState;
            }

            readonly int brickmapSize;         // Number of bricks per axis contained in this brick map.
            readonly int brickSize;            // Number of points per axis contained in a single brick.
            readonly int levelIndex;           // The level index if this brick map. Higher levels work with larger bricks at greater distances from the view origin
            readonly float worldScale;         // The world scale of this brick map.

            readonly int halfBrickmapSize;     // Half the number of bricks per axis contained in this brick map.
            readonly int quarterBrickmapSize;  // One fourth the number of bricks per axis contained in this brick map.
            readonly int levelScale;           // The scale multiplier relative to the smallest brickmap level, derrived from the level index with the equation (2 ^ level).

            readonly Dictionary<int3, Brick> bricks;
            readonly List<Shape> shapes;

            int3 originIndex;                  // The global brick index in which this map currently originates.
            int3 lowerGridOffset;              // The local offset of the brickmap contained within this one.

            double updateTime;
            double majorUpdateTime;
            double renderTime;

            public int3 OriginIndex => originIndex;

            public Brickmap(int brickmapSize, int brickSize, int levelIndex, float worldScale)
            {
                this.brickmapSize = brickmapSize;
                this.brickSize = brickSize;
                this.levelIndex = levelIndex;
                this.worldScale = worldScale;

                halfBrickmapSize = brickmapSize / 2;
                quarterBrickmapSize = brickmapSize / 4;

                levelScale = 1 << levelIndex;

                bricks = new(brickmapSize * brickmapSize * brickmapSize);
                shapes = new();

                originIndex = int.MaxValue;
                lowerGridOffset = 0;

                s_AvgDensityEvalTime = new();
                s_AvgMeshingTime = new();
            }

            public void Dispose()
            {
                foreach (Brick brick in bricks.Values)
                    brick.Dispose();
            }

            public void Update(Camera observerCamera, float3 observerPosition, int3 lowerGridOriginIndex, SDFScene scene)
            {
                Stopwatch.Start(ref updateTime);

                // Calculate the brick index in which the observer is located (local within this brickmap level).
                int3 newOriginIndex = GetOriginIndex(observerPosition);
                int3 newLowerGridOffset = GetLowerGridOffset(lowerGridOriginIndex, newOriginIndex);

                bool originHasMoved = math.any(newOriginIndex != originIndex);
                bool lowerGridHasMoved = math.any(newLowerGridOffset != lowerGridOffset);

                bool isMajorUpdate = originHasMoved || lowerGridHasMoved;
                if (isMajorUpdate)
                    Stopwatch.Start(ref majorUpdateTime);

                // If the origin index is different this frame; update loaded bricks.
                if (originHasMoved || lowerGridHasMoved)
                {
                    // Update origin index.
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
                            bricks[brickIndex].Dispose();
                            bricks.Remove(brickIndex);
                        }
                    }

                    // Add in bounds entries (loop through intended entry indices).
                    for (int x = 0; x < brickmapSize; x++)
                    {
                        for (int y = 0; y < brickmapSize; y++)
                        {
                            for (int z = 0; z < brickmapSize; z++)
                            {
                                // Find the index position of this brick, using the origin index calculated from the observer position.
                                int3 brickIndex = originIndex + new int3(x, y, z) - halfBrickmapSize;

                                if (!bricks.ContainsKey(brickIndex) && !BrickOverlapsPreviousLevel(brickIndex))
                                {
                                    Brick brick = new(brickIndex, brickSize, levelScale, worldScale);
                                    brick.MarkAsModified();

                                    bricks.Add(brickIndex, brick);
                                }
                            }
                        }
                    }
                }

                // Update shapes intersecting this brickmap level.
                if (originHasMoved || scene.IsDirty)
                    FindIntersectingShapes(shapes, scene);

                // Update bricks.
                foreach (int3 brickIndex in bricks.Keys)
                    bricks[brickIndex].Update(observerCamera, shapes);

                if (isMajorUpdate)
                    Stopwatch.End(ref majorUpdateTime);

                Stopwatch.End(ref updateTime);
            }

            public void Render(Camera renderCamera, Material material, MaterialPropertyBlock mpb)
            {
                Stopwatch.Start(ref renderTime);

                foreach (Brick brick in bricks.Values)
                    brick.Render(renderCamera, material, mpb);

                Stopwatch.End(ref renderTime);
            }

            public void Clear()
            {
                shapes.Clear();

                foreach (Brick brick in bricks.Values)
                    brick.MarkAsModified();
            }

            public void MarkBoundsAsModified(float3 boundsCentre, float3 boundsVolume)
            {
                if (math.any(boundsVolume == 0))
                    return;

                GetBrickVolumeFromAABB(brickSize, levelScale * worldScale, boundsCentre, boundsVolume, out int3 initialIndex, out int3 volume);

                for (int x = 0; x < volume.x; x++)
                {
                    for (int y = 0; y < volume.y; y++)
                    {
                        for (int z = 0; z < volume.z; z++)
                        {
                            int3 brickIndex = initialIndex + new int3(x, y, z);

                            if (BrickInBounds(brickIndex) && !BrickOverlapsPreviousLevel(brickIndex))
                                bricks[brickIndex].MarkAsModified();
                        }
                    }
                }
            }

            void FindIntersectingShapes(List<Shape> shapes, SDFScene scene)
            {
                // TODO: a fun optimization here; a given brickmap level only needs to test shapes for intersection that passed the check for the prior brickmap level.
                // This sort of acts a binary chop for the shape data, cutting down on the other most expensive part of the updating loop.

                shapes.Clear();

                foreach (Shape shape in scene.Shapes)
                {
                    // Get brick volume from shape.
                    shape.ComputeVolume(out float3 boundsPosition, out float3 boundsVolume);
                    GetBrickVolumeFromAABB(brickSize, levelScale * worldScale, boundsPosition, boundsVolume, out int3 initialIndex, out int3 volume);

                    // Account for density data overflowing into adjacent bricks.
                    initialIndex -= 1;
                    volume += 2;

                    // AABB intersection logic.
                    int3 aMax = initialIndex + volume - 1;
                    int3 aMin = initialIndex;

                    int3 bMax = originIndex + halfBrickmapSize - 1;
                    int3 bMin = originIndex - halfBrickmapSize;

                    // If map volume overlaps with the shape volume.
                    if (math.any(aMax < bMin) || math.any(aMin > bMax))
                        continue;

                    shapes.Add(shape);
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
                float3 upperGridPosition = (observerPosition + (brickSize * levelScale)) / math.pow(2, levelIndex + 1) / brickSize;

                // Floor position and multiply by 2 to restore index to this grid level.
                return (int3)math.floor(upperGridPosition) * 2;
            }

            int3 GetLowerGridOffset(int3 lowerGridOriginIndex, int3 newOriginIndex)
            {
                if (levelIndex == 0)
                    return 0;
                
                return (lowerGridOriginIndex - newOriginIndex - newOriginIndex) / 2;
            }

            bool BrickInBounds(int3 brickIndex) => math.all(brickIndex < originIndex + halfBrickmapSize) && math.all(brickIndex >= originIndex - halfBrickmapSize);

            bool BrickOverlapsPreviousLevel(int3 brickIndex)
            {
                if (levelIndex == 0)
                    return false;

                float3 localBrickIndex = brickIndex - originIndex;

                if (localBrickIndex.x < lowerGridOffset.x + quarterBrickmapSize &&
                    localBrickIndex.x >= lowerGridOffset.x - quarterBrickmapSize)
                {
                    if (localBrickIndex.y < lowerGridOffset.y + quarterBrickmapSize &&
                        localBrickIndex.y >= lowerGridOffset.y - quarterBrickmapSize)
                    {
                        if (localBrickIndex.z < lowerGridOffset.z + quarterBrickmapSize &&
                            localBrickIndex.z >= lowerGridOffset.z - quarterBrickmapSize)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            public bool BrickIntersectsSurface(int3 brickIndex) => bricks.ContainsKey(brickIndex) && bricks[brickIndex].IntersecsSurface;

            public float SampleCache(int3 brickIndex, int3 cellIndex)
            {
                if (!bricks.ContainsKey(brickIndex))
                    return EmptyDensityValue;

                return bricks[brickIndex].SampleCache(cellIndex);
            }
        }

        class SDFScene
        {
            readonly List<Shape> shapes = new();

            public List<Shape> Shapes => shapes;

            public int NumShapes => shapes.Count;

            public bool IsDirty;

            public void AddShape(Shape shape)
            {
                shapes.Add(shape);
                IsDirty = true;
            }

            public void RemoveShape(int index)
            {
                shapes.RemoveAt(index);
                IsDirty = true;
            }

            public void ReplaceShape(int index, Shape shape)
            {
                shapes[index] = shape;
                IsDirty = true;
            }

            public void Clear()
            {
                shapes.Clear();
                IsDirty = true;
            }
        }
    }
}
