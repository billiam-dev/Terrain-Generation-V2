using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

using LevelGeneration.Terrain.Scene;
using LevelGeneration.Terrain.SDF;
using LevelGeneration.Terrain.Meshing;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LevelGeneration.Terrain
{
    [DisallowMultipleComponent]
    public partial class ProceduralTerrain : MonoBehaviour
    {
        /// <summary>
        /// Apply a material to the terrain.
        /// </summary>
        [Tooltip("Apply a material to the terrain.")]
        public Material Material;

        /// <summary>
        /// Debug option, use the terrain position as the observer position.
        /// </summary>
        [Tooltip("Debug option, use the terrain position as the observer position.")]
        public bool UseStaticOrigin;

        /// <summary>
        /// Debug option, color each brickmap level differently.
        /// </summary>
        [Tooltip("Debug option, color each brickmap level differently.")]
        public static bool HighlightBrickmapLevels
        {
            get;
            set;
        }

        /// <summary>
        /// Debug option, highlight the brick transition meshes.
        /// </summary>
        [Tooltip("Debug option, highlight brick transition meshes.")]
        public static bool HighlightTransitionMeshes
        {
            get;
            set;
        }

        /// <summary>
        /// Set the camera through which the user is viewing the terrain.
        /// </summary>
        [Tooltip("Set the camera through which the user is viewing the terrain.")]
        public static Camera ObserverCamera
        {
            get;
            set;
        }

        static ProceduralTerrain s_Instance;

        Brickmap[] m_BrickmapLevels;
        MaterialPropertyBlock m_MaterialProperties;

        DensityEvaluator m_DensityEvaluator;
        BatchChunkMesher m_Mesher;

        bool m_Initialized;

        SDFScene m_Scene;

        // Debug info
        MeanTime m_AvgDensityEvalTime;
        MeanTime m_AvgMeshingTime;
        uint m_DrawingVertices;
        uint m_DrawingIndices;
        double m_TotalMeshingTime;
        int m_TotalMeshingTasks;
        double m_UpdateTime;
        double m_RenderTime;

        const float k_WorldScale = 1.0f;      // The size of a single cell in world units, effectively controls the scale of the whole terrain. TODO: this is broken now?
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level that can be converted into meshes and rendered.
        const int k_NumBrickmapLevels = 5;    // The number of brickmap levels, each doubling the grid size of the previous level.

        /// <summary>
        /// The amount of blending applied between terrain-forming shapes.
        /// Does not apply to the CSG shapes users apply by mining or building upon the terrain.
        /// </summary>
        public const float Smoothness = 6.0f;

#if !UNITY_EDITOR
        void Update()
        {
            if (!m_Initialized)
                return;

            UpdateTerrain();
        }
#endif

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!m_Initialized)
                return;

            DrawDebugGizmos();
            DrawDensityTester();
        }
#endif

        void OnGUI()
        {
            if (!m_Initialized)
                return;

            DisplayDebugGUI();
        }

        void TryInitializeAsInstance()
        {
            if (m_Initialized)
                return;

            if (s_Instance != null)
            {
                Debug.LogWarning($"Could not initialize {name}, only one ProceduralTerrain may exist in the scene.");
                return;
            }

            s_Instance = this;

            Initialize();

            RenderPipelineManager.beginCameraRendering += RenderTerrain;
#if UNITY_EDITOR
            EditorApplication.update += UpdateTerrain;
#endif
        }

        void TryDisposeInstance()
        {
            if (!m_Initialized)
                return;

            s_Instance = null;

            RenderPipelineManager.beginCameraRendering -= RenderTerrain;
#if UNITY_EDITOR
            EditorApplication.update -= UpdateTerrain;
#endif

            Dispose();
        }

        void Initialize()
        {
            m_BrickmapLevels = new Brickmap[k_NumBrickmapLevels];
            m_MaterialProperties = new();

            m_DensityEvaluator = new();
            m_Mesher = new();

            m_AvgDensityEvalTime = new();
            m_AvgMeshingTime = new();

            m_DensityEvaluator.Allocate(k_BrickSize);
            m_Mesher.Allocate();

            for (int i = 0; i < k_NumBrickmapLevels; i++)
                m_BrickmapLevels[i] = new(k_BrickmapLevelSize, k_BrickSize, i, k_WorldScale);

            m_Initialized = true;
        }

        void Dispose()
        {
            foreach (Brickmap brickmap in m_BrickmapLevels)
                brickmap.Dispose();

            m_DensityEvaluator.Dispose();
            m_Mesher.Dispose();

            m_BrickmapLevels = null;
            m_MaterialProperties = null;

            m_DensityEvaluator = null;
            m_Mesher = null;

            m_AvgDensityEvalTime = null;
            m_AvgMeshingTime = null;

            m_Initialized = false;
        }

        void UpdateTerrain()
        {
            if (m_Scene == null)
                return;

#if UNITY_EDITOR
            Camera camera = Application.isPlaying ? ObserverCamera : Camera.current;
#else
            Camera camera = s_ObserverCamera;
#endif

            if (!camera)
                return;

            Stopwatch.Start(ref m_UpdateTime);

            // Evaluate scene changes.
            if (m_Scene.surfaceNoise.IsDirty || m_Scene.globalNoise.IsDirty)
            {
                foreach (Brickmap brickmap in m_BrickmapLevels)
                    brickmap.MarkAllAsModified();
            }

            if (m_Scene.terrainShapes.IsDirty)
            {
                foreach (Volume volume in m_Scene.terrainShapes.ModifiedVolumes)
                {
                    foreach (Brickmap brickmap in m_BrickmapLevels)
                        brickmap.MarkBoundsAsModified(volume);
                }
            }

            // Update brickmap levels.
            float3 observerPosition = UseStaticOrigin ? transform.position : camera.transform.position;

            // TODO: I would really like not to have to pass the scene this deep.
            // Then the IsDirty flags can be disabled above and we have better encapsulation.

            m_BrickmapLevels[0].Update(camera, observerPosition, 0, m_Scene);
            for (int i = 1; i < k_NumBrickmapLevels; i++)
                m_BrickmapLevels[i].Update(camera, observerPosition, m_BrickmapLevels[i - 1].OriginIndex, m_Scene);

            // Execute meshing tasks queued this frame.
            int pendingTasks = m_Mesher.NumPendingTasks;
            if (pendingTasks > 0)
            {
                m_TotalMeshingTasks = pendingTasks;

                Stopwatch.Start(ref m_TotalMeshingTime);

                m_Mesher.ExecutePendingTasksContinuous();

                Stopwatch.End(ref m_TotalMeshingTime);
                m_AvgMeshingTime.AddTime(m_TotalMeshingTime / m_TotalMeshingTasks);
            }

            m_Scene.surfaceNoise.IsDirty = false;
            m_Scene.globalNoise.IsDirty = false;
            m_Scene.terrainShapes.IsDirty = false;

            Stopwatch.End(ref m_UpdateTime);
        }

        void RenderTerrain(ScriptableRenderContext context, Camera camera)
        {
            if (m_Scene == null)
                return;

            m_DrawingVertices = 0;
            m_DrawingIndices = 0;

            Stopwatch.Start(ref m_RenderTime);

            for (int i = 0; i < k_NumBrickmapLevels; i++)
                m_BrickmapLevels[i].Render(camera, Material, m_MaterialProperties);

            Stopwatch.End(ref m_RenderTime);
        }

        /// <summary>
        /// Load a SDF scene.
        /// The terrain will automatically respond to changes in the scene if it is flagged as dirty.
        /// </summary>
        public void LoadScene(SDFScene scene)
        {
            TryInitializeAsInstance();

            if (s_Instance == this)
                m_Scene = scene;
        }

        /// <summary>
        /// Unload the current SDF scene.
        /// </summary>
        public void UnloadScene()
        {
            TryDisposeInstance();

            if (s_Instance == this)
                m_Scene = null;
        }

        /// <summary>
        /// Sample the density cache at the given indices.
        /// </summary>
        public float SampleDensity(float3 positionWS)
        {
            // Scale position by world scale.
            positionWS *= 1.0f / k_WorldScale;

            // Allocate a temporary density sampler.
            DensitySampler sampler = new();
            sampler.Allocate(m_Scene.terrainShapes.Shapes, m_Scene.surfaceNoise, m_Scene.globalNoise, Allocator.Temp);

            // Sample the SDF at the given position.
            float value = sampler.Sample(positionWS);

            // Dispose the sampler and return the result.
            sampler.Dispose();

            return value;
        }

        /// <summary>
        /// Raytraces the terrain to find the surface position.
        /// </summary>
        public float3 FindSurface(float3 positionWS, float3 direction, float minDistance = 0.1f)
        {
            // Ensure direction is normalized.
            direction = math.normalize(direction);

            // Scale origin position by world scale.
            positionWS *= 1.0f / k_WorldScale;

            // Allocate a temporary density sampler.
            DensitySampler sampler = new();
            sampler.Allocate(m_Scene.terrainShapes.Shapes, m_Scene.surfaceNoise, m_Scene.globalNoise, Allocator.Temp);

            // Step forward by the sampled distance value until we are acceptably close to the surface.
            float distance = sampler.Sample(positionWS);
            while (distance > minDistance)
            {
                positionWS += direction * distance;
                distance = sampler.Sample(positionWS);
            }

            // Dispose the sampler.
            sampler.Dispose();

            // Re-scale and return the position.
            return positionWS * k_WorldScale;
        }

        /// <summary>
        /// Converts a world space aabb to a set of iterable brick indices.
        /// Returns the initial index and the size of the volume in each axis.
        /// </summary>
        static IntVolume GetBrickVolumeFromAABB(int brickSize, float scale, Volume bounds)
        {
            ComputeIndices(brickSize, scale, bounds.position, out _, out int3 brickIndex, out int3 localCellIndex);

            float3 worldBrickSize = brickSize * scale;

            // Snap the volume to the brick grid and output the result.
            int3 volume = (int3)math.ceil(bounds.size / worldBrickSize) + 1; // TODO: Remove +1 when possible. This is especially detremental on higher brickmap levels.

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
            int3 initialIndex = centreIndex - (volume / 2);

            return new IntVolume(
                initialIndex,
                volume
                );
        }

        /// <summary>
        /// Takes a world space position and returns the correlating global cell index, brick index and local cell index (to the bricks origin).
        /// </summary>
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
                class DensityCache : IDisposable
                {
                    NativeArray<float> coreDensity;
                    IntPtr coreDensityPtr;

                    /*
                     * Since this terrain system uses a clipmap system, only one of
                     * the six brick faces can be adjacent to a higher LOD brick at a time.
                     * Therefore the density cache only needs one array for transition values,
                     * which can be recomputed to whichever side it is needed.
                     * 
                     * Note that this is not the case in other chunking systems, such as an octree.
                    */

                    NativeArray<float> transitionDensity;
                    IntPtr transitionDensityPtr;

                    bool isAllocated;

                    public bool IsAllocated => isAllocated;

                    public unsafe IntPtr CoreDensityPointer => coreDensityPtr;

                    public unsafe IntPtr TransitionDensityPointer => transitionDensityPtr;

                    public unsafe void Allocate(int brickSize, bool allocateTransition)
                    {
                        int extendedSize = brickSize + 3;

                        // Allocate core density field.
                        if (!coreDensity.IsCreated)
                        {
                            coreDensity = new(extendedSize * extendedSize * extendedSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                            coreDensityPtr = new(coreDensity.GetUnsafeReadOnlyPtr());
                        }

                        // Allocate transition density field.
                        if (allocateTransition && !transitionDensity.IsCreated)
                        {
                            int transitionSize = (extendedSize * 2) - 1;
                            int totalTransitionPoints = transitionSize * transitionSize * 3;

                            transitionDensity = new(totalTransitionPoints, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                            transitionDensityPtr = new(transitionDensity.GetUnsafeReadOnlyPtr());
                        }

                        isAllocated = true;
                    }

                    public void Dispose()
                    {
                        if (coreDensity.IsCreated)
                            coreDensity.Dispose();

                        if (transitionDensity.IsCreated)
                            transitionDensity.Dispose();

                        isAllocated = false;
                    }

                    public void CopyCoreDensity(NativeArray<float> density)
                    {
                        if (coreDensity.IsCreated)
                            coreDensity.CopyFrom(density);
                    }

                    public void CopyTransitionDensity(NativeArray<float> density)
                    {
                        if (transitionDensity.IsCreated)
                            transitionDensity.CopyFrom(density);
                    }

                    public float Sample(int3 index, int size)
                    {
                        int extendedSize = size + 3;
                        return coreDensity[(index.z * extendedSize * extendedSize) + (index.y * extendedSize) + index.x];
                    }

                    public int MemoryUsageBytes()
                    {
                        int bytes = 0;

                        // Core density
                        if (coreDensity.IsCreated)
                            bytes += coreDensity.Length * sizeof(float);

                        // Transition density
                        if (transitionDensity.IsCreated)
                            bytes += transitionDensity.Length * sizeof(float);

                        // Density pointers
                        bytes += IntPtr.Size * 2;

                        // isAllocated bool
                        bytes += 1;

                        return bytes;
                    }
                }

                class BrickRenderer : IDisposable
                {
                    Mesh coreMesh;
                    Mesh transitionMesh;

                    bool isAllocated;

                    public bool IsAllocated => isAllocated;

                    public void Allocate(float worldSize, bool allocateTransition)
                    {
                        // Create mesh bounds (local).
                        Bounds bounds = new(Vector3.zero, Vector3.one * worldSize);

                        // Allocate core mesh.
                        coreMesh = new()
                        {
                            bounds = bounds
                        };

                        // Allocate transition mesh.
                        if (allocateTransition)
                        {
                            transitionMesh = new()
                            {
                                bounds = bounds
                            };
                        }

                        isAllocated = true;
                    }

                    public void Dispose()
                    {
                        if (coreMesh != null)
                            DestroyImmediate(coreMesh);

                        if (transitionMesh != null)
                            DestroyImmediate(transitionMesh);

                        isAllocated = false;
                    }

                    public void RemeshCore(int3 index, int size, int levelScale, float worldScale, IntPtr densityPtr)
                    {
                        s_Instance.m_Mesher.QueueRemeshTask(new MeshingTask(
                            coreMesh,
                            index,
                            size,
                            levelScale,
                            worldScale,
                            densityPtr
                        ));
                    }

                    public void RemeshTransition(int3 index, int size, int levelScale, float worldScale, IntPtr densityPtr, int transitionIndex)
                    {
                        s_Instance.m_Mesher.QueueRemeshTask(new MeshingTask(
                            transitionMesh,
                            index,
                            size,
                            levelScale,
                            worldScale,
                            densityPtr,
                            transitionIndex
                        ));
                    }

                    public void Draw(float3 position, Material material, MaterialPropertyBlock mpb, Camera camera, byte neighborLOD)
                    {
                        // Set neighbor LOD data for this brick.
                        mpb.SetInt(ShaderIDs.PackedNeighborLOD, neighborLOD);
                        
                        // Draw core mesh.
                        mpb.SetColor(ShaderIDs.TransitionDebugColor, Color.white);
                        DrawMesh(coreMesh, position, material, mpb, camera);

                        // Draw transition meshes.
                        if (neighborLOD != 0 && transitionMesh != null)    
                        {
                            if (HighlightTransitionMeshes)
                                mpb.SetColor(ShaderIDs.TransitionDebugColor, Color.red);

                            DrawMesh(transitionMesh, position, material, mpb, camera);
                        }
                    }

                    void DrawMesh(Mesh mesh, float3 position, Material material, MaterialPropertyBlock mpb, Camera camera)
                    {
                        // TODO: When in play mode; use GameObject with MeshRenderer and MeshCollider components.

                        Graphics.DrawMesh(mesh, position, Quaternion.identity, material, 0, camera, 0, mpb);

                        s_Instance.m_DrawingVertices += (uint)mesh.vertexCount;
                        s_Instance.m_DrawingIndices += mesh.GetIndexCount(0);
                    }

                    public int MemoryUsageBytes()
                    {
                        int bytes = 0;

                        // Core mesh
                        if (coreMesh != null)
                        {
                            bytes += coreMesh.vertexCount * Vertex.SizeBytes;
                            bytes += (int)coreMesh.GetIndexCount(0) * 2; // Index format is UInt16; 16 bits or 2 bytes.
                        }

                        // Transition mesh
                        if (transitionMesh != null)
                        {
                            bytes += transitionMesh.vertexCount * Vertex.SizeBytes;
                            bytes += (int)transitionMesh.GetIndexCount(0) * 2;
                        }

                        // isAllocated bool
                        bytes += 1;

                        return bytes;
                    }
                }

                readonly int3 index;                // Brick index within its brickmap level.
                readonly int size;                  // The base points per axis of this density brick.
                readonly int levelScale;            // The scale of this brick based on the brickmap level index.

                readonly float3 worldPosition;      // In-world centre of this brick.
                readonly float worldSize;           // In-world size of this brick.
                readonly float worldScale;          // The in-world scale of a single cell, constant.

                readonly DensityCache densityCache; // Native arrays of cached density values.
                readonly BrickRenderer renderer;    // Meshes for this brick.

                DensitySampler densitySampler;      // Density sampler struct for density JOBs, allocated when needed.

                bool isUniformState;                // Whether this brick's density cache is uniform (all > 0 or all < 0).
                bool densityModified;               // Flag to signal the density field underlying this brick has been modified.
                bool transitionUpdateQueued;        // Whether the tranision mesh needs to be updated next time this brick is selected for rendering.
                byte neighborLOD;                   // The packed LOD information of this brick.

                public Brick(int3 index, int size, int levelScale, float worldScale)
                {
                    this.index = index;
                    this.size = size;
                    this.levelScale = levelScale;

                    this.worldScale = worldScale;
                    worldSize = worldScale * levelScale * size;
                    worldPosition = worldSize * (float3)index + (worldSize * 0.5f);

                    densityCache = new();
                    renderer = new();

                    densitySampler = new();

                    isUniformState = true;
                    densityModified = false;
                    neighborLOD = 0x0000_0000;
                }

                public void Dispose()
                {
                    if (densityCache.IsAllocated)
                        densityCache.Dispose();

                    if (renderer.IsAllocated)
                        renderer.Dispose();
                }

                public void Update(Camera observerCamera, List<Shape> shapes, SDFScene scene)
                {
                    // We can skip meshing far away bricks that are not in the view frustum under the assumption that their shadows are not needed.
                    if (levelScale > 1 && !InViewFrustum(observerCamera))
                        return;

                    // Queue transition update if density has been modified.
                    if (densityModified && levelScale > 1)
                        transitionUpdateQueued = true;

                    // Allocate a density sampler in advance for core or transition density JOBs.
                    if (densityModified || transitionUpdateQueued)
                    {
                        // TODO: possibly run FindIntersectingShapes(shapes) as JOBs in parallel, this is quite slow.
                        densitySampler.Allocate(FindIntersectingShapes(shapes), scene.surfaceNoise, scene.globalNoise, Allocator.TempJob); // TODO: allocate in ProceduralTerrain and pass around, perhaps with an indices array for shapes?
                    }

                    //
                    // Evaluate core.
                    //
                    if (densityModified)
                    {
                        /* 
                         * Special case for bricks with large distances:
                         * 
                         * Take a distance sample at the brick's centre.
                         * If the distance to the nearest surface is greater than twice the brick size,
                         * skip evaluating the full density function for this brick.
                        */

                        float centreSample = densitySampler.Sample(worldPosition);
                        float densityFence = (worldSize * 2.0f) + 1.0f; // Add 1 to catch literal edge cases.

                        if (centreSample > densityFence || centreSample < -densityFence)
                        {
                            // If we are not already uniform, dispose this object.
                            if (!isUniformState)
                                Dispose();

                            isUniformState = true;
                        }
                        else
                        {
                            // Evaluate the core density function.
                            double t = 0.0;
                            Stopwatch.Start(ref t);
                            DensityEvaluationResult result = s_Instance.m_DensityEvaluator.ComputeCore(densitySampler, index, size, levelScale, worldScale);
                            Stopwatch.End(ref t);
                            s_Instance.m_AvgDensityEvalTime.AddTime(t);

                            // If the result is not uniform, continue to recompute the mesh.
                            // Else, if this brick is not already uniform, ensure it is disposed.

                            if (!result.isUniformState)
                            {
                                // Ensure core density is allocated and copy density result.
                                if (!densityCache.IsAllocated)
                                    densityCache.Allocate(size, levelScale > 1);

                                densityCache.CopyCoreDensity(result.density);

                                // Ensure renderer is allocated and schedule core remeshing task.
                                if (!renderer.IsAllocated)
                                    renderer.Allocate(worldSize, levelScale > 1);

                                renderer.RemeshCore(index, size, levelScale, worldScale, densityCache.CoreDensityPointer);
                            }
                            else if (!isUniformState)
                            {
                                Dispose();
                            }

                            // Set new uniformity status.
                            isUniformState = result.isUniformState;
                        }

                        // Disable densityModified flag.
                        densityModified = false;
                    }

                    //
                    // Evaluate transitions.
                    //
                    if (transitionUpdateQueued)
                    {
                        // Skip evaluating transition for this brick if it is of uniform state.
                        if (!isUniformState)
                        {
                            for (int i = 0; i < 6; i++)
                            {
                                if ((neighborLOD & (1 << i)) != 0)
                                {
                                    // Evaluate transition density with selected transition index and copy the result.
                                    DensityEvaluationResult result = s_Instance.m_DensityEvaluator.ComputeTransition(densitySampler, index, size, levelScale, worldScale, i);
                                    densityCache.CopyTransitionDensity(result.density);

                                    // Remesh transition mesh with selected transition index.
                                    renderer.RemeshTransition(index, size, levelScale, worldScale, densityCache.TransitionDensityPointer, i);

                                    break;
                                }
                            }
                        }

                        transitionUpdateQueued = false;
                    }

                    // Dispose density sampler.
                    if (densitySampler.IsAllocated)
                        densitySampler.Dispose();
                }

                public void UpdateNeighborLOD(byte neighborLOD)
                {
                    if (this.neighborLOD != neighborLOD)
                    {
                        this.neighborLOD = neighborLOD;
                        transitionUpdateQueued = true;
                    }
                }

                public void Render(Camera renderCamera, Material material, MaterialPropertyBlock mpb)
                {
                    // We can skip rendering if this is of uniform state.
                    if (isUniformState)
                        return;

                    // We can skip rendering far away bricks that are not in the view frustum under the assumption that their shadows are not needed.
                    if (levelScale > 1 && !InViewFrustum(renderCamera))
                        return;

                    renderer.Draw(worldPosition, material, mpb, renderCamera, neighborLOD);
                }

                Shape[] FindIntersectingShapes(List<Shape> shapes)
                {
                    List<Shape> intersectingShapes = new();

                    foreach (Shape shape in shapes)
                    {
                        // Get brick volume from shape.
                        IntVolume indices = GetBrickVolumeFromAABB(size, levelScale * worldScale, shape.ComputeVolume());

                        // Account for density data overflowing into adjacent bricks by extending the brick volume by 1 on each side.
                        indices.coordinate -= 1;
                        indices.size += 2;

                        // If brick index is within the volume, add to the list.
                        if (math.all(index >= indices.coordinate) && math.all(index < indices.coordinate + indices.size))
                            intersectingShapes.Add(shape);
                    }

                    return intersectingShapes.ToArray();
                }

                bool InViewFrustum(Camera camera)
                {
                    Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                    Bounds bounds = new(worldPosition, Vector3.one * worldSize);
                    return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
                }

                public void MarkAsModified() => densityModified = true;

                public float SampleCache(int3 index) => densityCache.Sample(index, size);

                public bool IntersectingSurface => !isUniformState;

                public int MemoryUsageBytes()
                {
                    int bytes = 0;

                    bytes += sizeof(int) * 3;   // index
                    bytes += sizeof(int);       // size
                    bytes += sizeof(int);       // levelScale

                    bytes += sizeof(float) * 3; // worldPosition
                    bytes += sizeof(float);     // worldSize
                    bytes += sizeof(float);     // worldScale

                    bytes += densityCache.MemoryUsageBytes();   // densityCache
                    bytes += renderer.MemoryUsageBytes();       // renderer

                    bytes += sizeof(bool);      // isUniformState
                    bytes += sizeof(bool);      // densityModified
                    bytes += sizeof(bool);      // transitionUpdateQueued
                    bytes += sizeof(byte);      // neighborLOD

                    return bytes;
                }
            }

            readonly int brickmapSize;         // Number of bricks per axis contained in this brick map.
            readonly int brickSize;            // Number of points per axis contained in a single brick.
            readonly int levelIndex;           // The level index if this brick map. Higher levels work with larger bricks at greater distances from the view origin
            readonly float worldScale;         // The world scale of this brick map.

            readonly int halfBrickmapSize;     // Half the number of bricks per axis contained in this brick map.
            readonly int quarterBrickmapSize;  // One fourth the number of bricks per axis contained in this brick map.
            readonly int levelScale;           // The scale multiplier relative to the smallest brickmap level, derrived from the level index with the equation (2 ^ level).

            readonly Dictionary<int3, Brick> bricks; // Or should I call it the Bricktionary?
            readonly List<Shape> shapes;

            int3 originIndex;                  // The global brick index in which this map currently originates.
            int3 lowerGridOffset;              // The local offset of the brickmap contained within this one.

            //bool isUpdating; // TODO: use this flag to spread density & meshing jobs over multiple frames. Do not allow the origin of higher brickmaps to shift until the lower one has finished updating.

            double updateTime;
            double majorUpdateTime;
            double renderTime;

            public int3 OriginIndex => originIndex;

            public List<Shape> IntersectingShapes => shapes; // TODO: only check intersection against previous brickmap level shapes.

            readonly int3[] NeighborOffsets =
            {
                new(1, 0, 0),  //  x
                new(-1, 0, 0), // -x
                new(0, 1, 0),  //  y
                new(0, -1, 0), // -y
                new(0, 0, 1),  //  z
                new(0, 0, -1)  // -z
            };

            readonly Color[] DebugColors = new Color[]
            {
                new(1.0f, 0.2f, 0.0f, 1.0f),
                new(0.0f, 1.0f, 0.2f, 0.8f),
                new(0.2f, 0.0f, 1.0f, 0.6f),
                new(0.8f, 0.8f, 0.8f, 0.4f),
                new(0.4f, 0.4f, 0.4f, 0.2f),
                new(0.1f, 0.1f, 0.1f, 0.1f)
            };

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

                s_Instance.m_AvgDensityEvalTime = new();
                s_Instance.m_AvgMeshingTime = new();
            }

            public void Dispose()
            {
                foreach (Brick brick in bricks.Values)
                    brick.Dispose();

                bricks.Clear();
                shapes.Clear();
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

                    //
                    // Remove out of bounds entries (loop through existing entries).
                    //

                    int3[] bricksCopy = new int3[bricks.Keys.Count];
                    bricks.Keys.CopyTo(bricksCopy, 0);

                    foreach (int3 brickIndex in bricksCopy)
                    {
                        if (!BrickInBounds(brickIndex) || BrickOverlapsPreviousLevel(brickIndex))
                        {
                            bricks[brickIndex].Dispose();
                            bricks.Remove(brickIndex);
                        }
                    }
                    
                    //
                    // Add in bounds entries (loop through intended entry indices).
                    //

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

                    //
                    // Update neighbor LOD data.
                    //

                    if (levelIndex > 0)
                    {
                        foreach (int3 brickIndex in bricks.Keys)
                            bricks[brickIndex].UpdateNeighborLOD(PackNeighborLOD(brickIndex));
                    }
                }

                // Update shapes intersecting this brickmap level.
                if (originHasMoved || scene.terrainShapes.IsDirty)
                    FindIntersectingShapes(scene);

                // Update all bricks.
                foreach (Brick brick in bricks.Values)
                    brick.Update(observerCamera, shapes, scene);

                if (isMajorUpdate)
                    Stopwatch.End(ref majorUpdateTime);

                Stopwatch.End(ref updateTime);
            }

            public void Render(Camera renderCamera, Material material, MaterialPropertyBlock mpb)
            {
                Stopwatch.Start(ref renderTime);

                mpb.SetColor(ShaderIDs.ClipmapDebugColor, HighlightBrickmapLevels ? DebugColors[levelIndex] : Color.white);

                foreach (Brick brick in bricks.Values)
                    brick.Render(renderCamera, material, mpb);

                Stopwatch.End(ref renderTime);
            }

            public void MarkAllAsModified()
            {
                foreach (Brick brick in bricks.Values)
                    brick.MarkAsModified();
            }

            public void MarkBoundsAsModified(Volume bounds)
            {
                if (math.any(bounds.size == 0))
                    return;

                IntVolume indices = GetBrickVolumeFromAABB(brickSize, levelScale * worldScale, bounds);
                int3 size = indices.size;
                int3 initialIndex = indices.coordinate;

                for (int x = 0; x < size.x; x++)
                {
                    for (int y = 0; y < size.y; y++)
                    {
                        for (int z = 0; z < size.z; z++)
                        {
                            int3 brickIndex = initialIndex + new int3(x, y, z);
                            
                            if (BrickInBounds(brickIndex) && !BrickOverlapsPreviousLevel(brickIndex))
                                bricks[brickIndex].MarkAsModified();
                        }
                    }
                }
            }

            void FindIntersectingShapes(SDFScene scene)
            {
                // TODO: a fun optimization here; a given brickmap level only needs to test shapes for intersection that passed the check for the prior brickmap level.
                // This sort of acts a binary chop for the shape data, cutting down on the other most expensive part of the updating loop.

                shapes.Clear();

                foreach (Shape shape in scene.terrainShapes.Shapes)
                {
                    // Get brick volume from shape.
                    IntVolume bounds = GetBrickVolumeFromAABB(brickSize, levelScale * worldScale, shape.ComputeVolume());

                    // Account for density data overflowing into adjacent bricks.
                    bounds.coordinate -= 1;
                    bounds.size += 2;

                    // AABB intersection logic.
                    int3 aMax = bounds.coordinate + bounds.size - 1;
                    int3 aMin = bounds.coordinate;

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

            byte PackNeighborLOD(int3 brickIndex)
            {
                byte neighborLOD = 0;

                for (int i = 0; i < 6; i++)
                {
                    if (BrickOverlapsPreviousLevel(brickIndex + NeighborOffsets[i]))
                        neighborLOD |= (byte)(1 << i);
                }

                return neighborLOD;
            }

            public bool BrickIntersectsSurface(int3 brickIndex)
            {
                return bricks.ContainsKey(brickIndex) && bricks[brickIndex].IntersectingSurface;
            }

            public float SampleCache(int3 brickIndex, int3 cellIndex)
            {
                if (!bricks.ContainsKey(brickIndex))
                    return 32.0f;

                return bricks[brickIndex].SampleCache(cellIndex);
            }

            public int MemoryUsageBytes()
            {
                int bytes = 0;

                bytes += sizeof(int);        // brickmapSize
                bytes += sizeof(int);        // brickSize
                bytes += sizeof(int);        // levelIndex
                bytes += sizeof(float);      // worldScale

                bytes += sizeof(int);        // halfBrickmapSize
                bytes += sizeof(int);        // quarterBrickmapSize
                bytes += sizeof(int);        // levelScale

                // bricks
                foreach (Brick brick in bricks.Values)
                    bytes += brick.MemoryUsageBytes();

                bytes += sizeof(int) * 3;    // originIndex
                bytes += sizeof(int) * 3;    // lowerGridOffset

                bytes += sizeof(double);     // updateTime
                bytes += sizeof(double);     // majorUpdateTime
                bytes += sizeof(double);     // renderTime

                return bytes;
            }
        }
    }
}
