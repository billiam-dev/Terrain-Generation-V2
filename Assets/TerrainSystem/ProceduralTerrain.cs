using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

using TerrainSystem.Scene;
using TerrainSystem.SDF;
using TerrainSystem.Meshing;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TerrainSystem
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public partial class ProceduralTerrain : MonoBehaviour
    {
        // TODO: Use BVH for shapes for quicker intersection querying?

        /// <summary>
        /// Apply a material to the terrain.
        /// </summary>
        public Material Material;

        /// <summary>
        /// Debug option, use the terrain position as the observer position.
        /// </summary>
        public bool UseStaticOrigin;

        /// <summary>
        /// Debug option, show debug information on screen.
        /// </summary>
        public bool ShowDebugGUI;

        /// <summary>
        /// Debug option, color each brickmap level differently.
        /// </summary>
        public static bool HighlightBrickmapLevels
        {
            get;
            set;
        }

        /// <summary>
        /// Debug option, highlight the brick transition meshes.
        /// </summary>
        public static bool HighlightTransitionMeshes
        {
            get;
            set;
        }

        /// <summary>
        /// Set the camera through which the user is viewing the terrain.
        /// </summary>
        public static Camera ObserverCamera
        {
            get;
            set;
        }

        SDFScene m_Scene;
        Brickmap[] m_BrickmapLevels;
        MaterialPropertyBlock m_MaterialProperties;

        DensityEvaluator m_DensityEvaluator;
        BatchChunkMesher m_Mesher;

        float3 m_ObserverPosition;
        float m_MinOriginUpdateDelta;
        bool m_Initialized;

        // Debug info
        static uint s_VertexCount;
        static uint s_IndexCount;
        double m_UpdateTime;
        double m_RenderTime;

        const float k_WorldScale = 1.0f;    // The size of a single cell in world units, effectively controls the scale of the whole terrain. TODO: this is broken now?
        const int k_BrickSize = 16;         // The number of cells per axis contained in a single brick.
        const int k_BrickmapLevelSize = 8;  // The number of bricks per axis of a single brickmap level that can be converted into meshes and rendered.
        const int k_NumBrickmapLevels = 5;  // The number of brickmap levels, each doubling the grid size of the previous level.

        // The amount of blending applied between terrain-forming shapes.
        public const float Smoothness = 6.0f;

        void OnEnable()
        {
            if (m_Initialized)
                return;

            Initialize();

            RenderPipelineManager.beginCameraRendering += RenderTerrain;
#if UNITY_EDITOR
            EditorApplication.update += UpdateTerrain;
#endif
        }

        void OnDisable()
        {
            if (!m_Initialized)
                return;

            RenderPipelineManager.beginCameraRendering -= RenderTerrain;
#if UNITY_EDITOR
            EditorApplication.update -= UpdateTerrain;
#endif

            Dispose();
        }

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
            if (!m_Initialized || !ShowDebugGUI)
                return;

            DisplayDebugGUI();
        }

        void Initialize()
        {
            m_BrickmapLevels = new Brickmap[k_NumBrickmapLevels];
            m_MaterialProperties = new();

            m_DensityEvaluator = new();
            m_Mesher = new();

            m_DensityEvaluator.Allocate(k_BrickSize);
            m_Mesher.Allocate();

            m_MinOriginUpdateDelta = k_BrickSize * k_WorldScale;

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

            m_Initialized = false;
        }

        void UpdateTerrain()
        {
            if (m_Scene == null)
                return;

#if UNITY_EDITOR
            Camera camera = Application.isPlaying ? ObserverCamera : Camera.current;
#else
            Camera camera = ObserverCamera;
#endif

            if (!camera)
                return;

            Stopwatch.Start(ref m_UpdateTime);

            // Evaluate scene changes.
            if (m_Scene.baseLayer.IsDirty || m_Scene.surfaceNoise.IsDirty || m_Scene.globalNoise.IsDirty)
            {
                foreach (Brickmap brickmap in m_BrickmapLevels)
                    brickmap.MarkAllAsModified();
            }

            if (m_Scene.terrainShapes.IsDirty)
            {
                foreach (Volume volume in m_Scene.terrainShapes.ModifiedVolumes)
                {
                    foreach (Brickmap brickmap in m_BrickmapLevels)
                        brickmap.MarkVolumeAsModified(volume);
                }

                m_Scene.terrainShapes.ClearModifiedVolumes();
            }

            if (m_Scene.terraformShapes.IsDirty)
            {
                foreach (Volume volume in m_Scene.terraformShapes.ModifiedVolumes)
                {
                    foreach (Brickmap brickmap in m_BrickmapLevels)
                        brickmap.MarkVolumeAsModified(volume);
                }

                m_Scene.terraformShapes.ClearModifiedVolumes();
            }

            // Update brickmaps origin.
            float3 observerPosition = UseStaticOrigin ? transform.position : camera.transform.position;
            if (math.length(observerPosition - m_ObserverPosition) > m_MinOriginUpdateDelta)
            {
                m_ObserverPosition = observerPosition;

                m_BrickmapLevels[0].UpdateOrigin(observerPosition, 0);
                Brickmap previousBrickmap = m_BrickmapLevels[0];

                for (int i = 1; i < k_NumBrickmapLevels; i++)
                {
                    m_BrickmapLevels[i].UpdateOrigin(observerPosition, previousBrickmap.OriginIndex);
                    previousBrickmap = m_BrickmapLevels[i];
                }
            }

            // Update brickmaps density.
            for (int i = 0; i < k_NumBrickmapLevels; i++)
                m_BrickmapLevels[i].UpdateBricks(camera, m_Scene, m_DensityEvaluator, m_Mesher);

            // Execute meshing tasks queued this frame.
            // Note: the continuous method completes all the meshing tasks within the frame, meaning the terrain updates faster.
            // To optimize for performance, use the regular ExecutePendingTasks(), which spaces the work over multiple frames.
            if (m_Mesher.NumPendingTasks > 0)
                m_Mesher.ExecutePendingTasksContinuous();

            // Disable dirty flags in scene, terrain has been updated to reflect the changes.
            m_Scene.baseLayer.IsDirty = false;
            m_Scene.surfaceNoise.IsDirty = false;
            m_Scene.globalNoise.IsDirty = false;
            m_Scene.terrainShapes.IsDirty = false;
            m_Scene.terraformShapes.IsDirty = false;

            Stopwatch.End(ref m_UpdateTime);
        }

        void RenderTerrain(ScriptableRenderContext context, Camera camera)
        {
            if (m_Scene == null)
                return;

            s_VertexCount = 0;
            s_IndexCount = 0;

            Stopwatch.Start(ref m_RenderTime);

            for (int i = 0; i < k_NumBrickmapLevels; i++)
                m_BrickmapLevels[i].Render(camera, Material, m_MaterialProperties);

            Stopwatch.End(ref m_RenderTime);
        }

        /// <summary>
        /// Load an SDF scene.
        /// The terrain will automatically respond to changes in the scene if it is flagged as dirty.
        /// </summary>
        public void LoadScene(SDFScene scene)
        {
            if (scene == null)
            {
                Debug.LogWarning("Failed to load null scene.");
                return;
            }

            m_ObserverPosition = float.MaxValue;
            m_Scene = scene;
        }

        /// <summary>
        /// Unload the current SDF scene.
        /// </summary>
        public void UnloadScene()
        {
            if (m_Scene == null)
            {
                Debug.LogWarning("Failed to unload scene, no scene loaded.");
                return;
            }

            m_Scene = null;
        }

        /// <summary>
        /// Terraform the terrain with a given shape.
        /// </summary>
        public void Terraform(Shape shape)
        {
            if (m_Scene == null)
            {
                Debug.LogWarning("Cannot terraform terrain if no scene is loaded!");
                return;
            }

            m_Scene.terraformShapes.AddShape(shape);
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

                    public void RemeshCore(int3 index, int size, int levelScale, float worldScale, IntPtr densityPtr, BatchChunkMesher mesher)
                    {
                        mesher.QueueRemeshTask(new MeshingTask(
                            coreMesh,
                            index,
                            size,
                            levelScale,
                            worldScale,
                            densityPtr
                        ));
                    }

                    public void RemeshTransition(int3 index, int size, int levelScale, float worldScale, IntPtr densityPtr, int transitionIndex, BatchChunkMesher mesher)
                    {
                        mesher.QueueRemeshTask(new MeshingTask(
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
                        // TODO: For builds: use MeshRenderer and MeshCollider components, or a custom render pass.

                        Graphics.DrawMesh(mesh, position, Quaternion.identity, material, 0, camera, 0, mpb);

                        s_VertexCount += (uint)mesh.vertexCount;
                        s_IndexCount += mesh.GetIndexCount(0);
                    }

                    public int MemoryUsageBytes()
                    {
                        int bytes = 0;

                        // Core mesh
                        if (coreMesh != null)
                        {
                            bytes += coreMesh.vertexCount * Vertex.SizeBytes;
                            bytes += (int)coreMesh.GetIndexCount(0) * 2; // Index format is UInt16 (2 bytes)
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

                readonly DensityCache densityCache; // Provides functions to evaluate and maintain the density field underlying this brick.
                readonly BrickRenderer renderer;    // Provides functions to remesh and render this brick.

                DensitySampler densitySampler;      // Density sampler struct for density JOBs, allocated when needed.

                bool isUniformState;                // Whether this brick is of uniform density or not (all > 0 or all < 0).
                bool coreUpdateQueued;              // Flag to signal required re-evaluate the core density of this brick.
                bool transitionUpdateQueued;        // Flag to signal required re-evaluate the transition density of this brick.
                byte neighborLOD;                   // LOD information about the six neighboring bricks, packed into a single byte.

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
                    coreUpdateQueued = false;
                    transitionUpdateQueued = false;
                    neighborLOD = 0x0000_0000;
                }

                public void Dispose()
                {
                    if (densityCache.IsAllocated)
                        densityCache.Dispose();

                    if (renderer.IsAllocated)
                        renderer.Dispose();
                }

                public void Update(Camera observerCamera, SDFScene scene, List<int> filteredShapeIndices, DensityEvaluator densityEvaluator, BatchChunkMesher mesher)
                {
                    // We can skip meshing far away bricks that are not in the view frustum under the assumption that their shadows are not needed.
                    if (levelScale > 1 && !InViewFrustum(observerCamera))
                        return;

                    // Allocate a density sampler in advance for core or transition density JOBs.
                    if (coreUpdateQueued || transitionUpdateQueued)
                    {                        
                        densitySampler.Allocate(scene, Allocator.TempJob); // TODO: allocate in ProceduralTerrain and pass around.

                        int[] intersectingTerrainShapeIndices = FilterIntersectingShapes(scene.terraformShapes, filteredShapeIndices);
                        densitySampler.FilterTerraformShapes(intersectingTerrainShapeIndices);
                    }

                    //
                    // Evaluate core.
                    //
                    if (coreUpdateQueued)
                    {
                        /* 
                         * Special case for bricks with large distances:
                         * 
                         * Take a distance sample at the brick's centre.
                         * If the distance to the nearest surface is greater than twice the brick size,
                         * skip evaluating the full density function for this brick.
                        */

                        float centreSample = densitySampler.SampleWithIndices(worldPosition);
                        float densityFence = (worldSize * 2.0f) + 1.0f; // Add 1 to catch literal edge cases.

                        if (centreSample > densityFence || centreSample < -densityFence)
                        {
                            // If we are not already uniform, dispose density and renderer data.
                            if (!isUniformState)
                                Dispose();

                            isUniformState = true;
                        }
                        else
                        {
                            // Evaluate the core density function.
                            DensityEvaluationResult result = densityEvaluator.ComputeCore(densitySampler, index, size, levelScale, worldScale);

                            // If the result is not uniform, continue to recompute the mesh.
                            // Else, if this brick is not already uniform, ensure it is disposed.

                            if (!result.isUniformState)
                            {
                                bool allocateTransition = levelScale > 1;

                                // Ensure core density is allocated and copy density result.
                                if (!densityCache.IsAllocated)
                                    densityCache.Allocate(size, allocateTransition);

                                densityCache.CopyCoreDensity(result.density);

                                // Ensure renderer is allocated and schedule core remeshing task.
                                if (!renderer.IsAllocated)
                                    renderer.Allocate(worldSize, allocateTransition);

                                renderer.RemeshCore(index, size, levelScale, worldScale, densityCache.CoreDensityPointer, mesher);
                            }
                            else
                            {
                                // If we are not already uniform, dispose density and renderer data.
                                if (!isUniformState)
                                    Dispose();
                            }

                            // Set new uniformity status.
                            isUniformState = result.isUniformState;
                        }

                        coreUpdateQueued = false;
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
                                    DensityEvaluationResult result = densityEvaluator.ComputeTransition(densitySampler, index, size, levelScale, worldScale, i);
                                    densityCache.CopyTransitionDensity(result.density);

                                    // Remesh transition mesh with selected transition index.
                                    renderer.RemeshTransition(index, size, levelScale, worldScale, densityCache.TransitionDensityPointer, i, mesher);

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
                    // If the new neighbor LOD data is different, update it and queue a transition update.
                    if (this.neighborLOD != neighborLOD)
                    {
                        this.neighborLOD = neighborLOD;
                        transitionUpdateQueued = true;
                    }
                }

                public void Render(Camera renderCamera, Material material, MaterialPropertyBlock mpb)
                {
                    // We can skip rendering far away bricks that are not in the view frustum under the assumption that their shadows are not needed.
                    if (levelScale > 1 && !InViewFrustum(renderCamera))
                        return;

                    // We can skip rendering if this brick is of uniform state.
                    if (!isUniformState)
                        renderer.Draw(worldPosition, material, mpb, renderCamera, neighborLOD);
                }

                public void MarkAsModified()
                {
                    coreUpdateQueued = true;

                    if (levelScale > 1)
                        transitionUpdateQueued = true;
                }

                int[] FilterIntersectingShapes(ShapeQueue shapeQueue, List<int> checkIndices)
                {
                    Shape[] shapes = shapeQueue.Shapes;
                    List<int> returnIndices = new();

                    foreach (int index in checkIndices)
                    {
                        // Get brick volume from shape.
                        IntVolume brickVolume = GetBrickVolumeFromAABB(size, levelScale * worldScale, shapes[index].Volume);

                        // Account for density data overflowing into adjacent bricks by extending the brick volume by 1 on each side.
                        brickVolume.coordinate -= 1;
                        brickVolume.size += 2;

                        // If this brick is within the volume, add the shape index to the list.
                        if (math.all(this.index >= brickVolume.coordinate) &&
                            math.all(this.index < brickVolume.coordinate + brickVolume.size))
                            returnIndices.Add(index);
                    }

                    return returnIndices.ToArray();
                }

                bool InViewFrustum(Camera camera)
                {
                    Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                    Bounds bounds = new(worldPosition, Vector3.one * worldSize);
                    return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
                }

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

            readonly Dictionary<int3, Brick> bricks;    // Map of loaded bricks, centred around the given observer. Should be called the Bricktionary.
            readonly List<int> intersectingShapes;      // List of indices into the terraform shape queue, filtered by intersection with this brickmap level.

            int3 originIndex;                  // The global brick index in which this map currently originates.
            int3 lowerGridOffset;              // The local offset of the brickmap contained within this one.
            bool originHasShifted; // TEMP

            //bool isUpdating; // TODO: use this flag to spread density & meshing jobs over multiple frames. Do not allow the origin of higher brickmaps to shift until the lower one has finished updating.

            double updateTime;
            double originShiftTime;
            double renderTime;

            public int3 OriginIndex => originIndex;

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
                intersectingShapes = new();

                originIndex = int.MaxValue;
                lowerGridOffset = 0;
            }

            public void Dispose()
            {
                foreach (Brick brick in bricks.Values)
                    brick.Dispose();

                bricks.Clear();
                intersectingShapes.Clear();
            }

            public void UpdateOrigin(float3 observerPosition, int3 lowerGridOriginIndex)
            {
                // Calculate the brick index in which the observer is located (local within this brickmap level).
                int3 newOriginIndex = GetOriginIndex(observerPosition);
                int3 newLowerGridOffset = GetLowerGridOffset(lowerGridOriginIndex, newOriginIndex);

                bool changeDetected = math.any(newOriginIndex != originIndex) ||
                                      math.any(newLowerGridOffset != lowerGridOffset);

                // If the origin index is different this frame; update loaded bricks.
                if (changeDetected)
                {
                    Stopwatch.Start(ref originShiftTime);

                    // Update grid indices.
                    originIndex = newOriginIndex;
                    lowerGridOffset = newLowerGridOffset;

                    originHasShifted = true;

                    // Remove out of bounds entries (loop through existing entries).
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

                    // Update neighbor LOD data.
                    if (levelIndex > 0)
                    {
                        foreach (int3 brickIndex in bricks.Keys)
                            bricks[brickIndex].UpdateNeighborLOD(PackNeighborLOD(brickIndex));
                    }

                    Stopwatch.End(ref originShiftTime);
                }
            }

            public void UpdateBricks(Camera observerCamera, SDFScene scene, DensityEvaluator densityEvaluator, BatchChunkMesher mesher)
            {
                Stopwatch.Start(ref updateTime);

                // Update shapes intersecting this brickmap level.
                if (originHasShifted || scene.terraformShapes.IsDirty)
                {
                    FilterIntersectingShapes(scene.terraformShapes, intersectingShapes);
                    originHasShifted = false;
                }

                // Update bricks.
                foreach (Brick brick in bricks.Values)
                    brick.Update(observerCamera, scene, intersectingShapes, densityEvaluator, mesher);

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

            public void MarkVolumeAsModified(Volume volume)
            {
                if (math.any(volume.size == 0))
                    return;

                IntVolume brickVolume = GetBrickVolumeFromAABB(brickSize, levelScale * worldScale, volume);
                int3 initialIndex = brickVolume.coordinate;
                int3 size = brickVolume.size;

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

            void FilterIntersectingShapes(ShapeQueue shapeQueue, List<int> indices)
            {
                indices.Clear();

                Shape[] shapes = shapeQueue.Shapes;
                for (int i = 0; i < shapes.Length; i++)
                {
                    // Get brick volume from shape.
                    IntVolume brickVolume = GetBrickVolumeFromAABB(brickSize, levelScale * worldScale, shapes[i].Volume);

                    // Account for density data overflowing into adjacent bricks.
                    brickVolume.coordinate -= 1;
                    brickVolume.size += 2;

                    // AABB intersection logic.
                    int3 aMax = brickVolume.coordinate + brickVolume.size - 1;
                    int3 aMin = brickVolume.coordinate;

                    int3 bMax = originIndex + halfBrickmapSize - 1;
                    int3 bMin = originIndex - halfBrickmapSize;

                    // If map volume overlaps with the shape volume, skip this shape.
                    if (math.any(aMax < bMin) || math.any(aMin > bMax))
                        continue;

                    // TODO: also filter shapes if overlapping previous level

                    // Else, add the shape index to the list.
                    indices.Add(i);
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
