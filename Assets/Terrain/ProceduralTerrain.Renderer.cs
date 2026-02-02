using LevelGeneration.Terrain.Meshing;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        public Material Material;

#if UNITY_EDITOR
        public bool DisableRendering;
        public bool ColorClipmapLevels;

        readonly Color[] m_ClipmapDebugHighlights = new Color[]
        {
                new(1.0f, 0.2f, 0.0f),
                new(0.0f, 1.0f, 0.2f),
                new(0.2f, 0.0f, 1.0f),
                new(0.8f, 0.8f, 0.8f),
                new(0.4f, 0.4f, 0.4f),
                new(0.1f, 0.1f, 0.1f)
        };
#endif

        RenderingData m_RenderingData;
        BatchChunkMesher m_ChunkMesher;
        ClipmapLevel[] m_Clipmaps;

        MaterialPropertyBlock m_MaterialProperties;

        const float k_TransitionCellPadding = 0.5f;

        void InitializeRendering()
        {
            m_RenderingData.brickmapLevels = m_DensityCache.GetRenderingData();

            m_ChunkMesher = new();
            m_Clipmaps = new ClipmapLevel[k_NumBrickMapLevels];
            m_MaterialProperties = new();

            for (int i = 0; i < k_NumBrickMapLevels; i++)
                m_Clipmaps[i] = new(k_BrickmapLevelSize, i);

            m_ChunkMesher.Allocate();
        }

        void CleanupRendering()
        {
            m_ChunkMesher.Dispose();

            m_ChunkMesher = null;
            m_Clipmaps = null;
            m_MaterialProperties = null;
        }

        void UpdateClipmap()
        {
#if UNITY_EDITOR
            if (DisableRendering)
                return;
#endif

            foreach (ClipmapLevel clipmap in m_Clipmaps)
                clipmap.Update(m_RenderingData, m_ChunkMesher, ref m_DebugInfo);

            // TODO: mess
            double t = 0;
            Stopwatch.Start(ref t);

            m_ChunkMesher.ExecutePendingTasksContinuous();

            Stopwatch.End(ref t);

            m_DebugInfo.meshingJobTimes.AddTime(t);
        }

        void RenderTerrain(ScriptableRenderContext context, Camera camera)
        {
#if UNITY_EDITOR
            if (DisableRendering)
                return;
#endif

            m_DebugInfo.chunkRendererdThisFrame = 0;

            for (int i = 0; i < k_NumBrickMapLevels; i++)
            {
#if UNITY_EDITOR
                if (ColorClipmapLevels)
                    m_MaterialProperties.SetColor("_ClipmapDebugColor", m_ClipmapDebugHighlights[i]);
                else
#endif
                    m_MaterialProperties.SetColor("_ClipmapDebugColor", Color.white);

                m_Clipmaps[i].Render(camera, Material, m_MaterialProperties, ref m_DebugInfo);
            }
        }

        class ClipmapLevel
        {
            class Chunk
            {
                readonly int3 chunkIndex;
                readonly int level;

                readonly float3 worldPosition;
                readonly float3 worldSize;
                readonly Matrix4x4 matrix;
                
                readonly Mesh mesh;
                readonly Mesh[] transitionMeshes;

                bool meshUpToDate;

                public Chunk(int3 chunkIndex, int level)
                {
                    this.chunkIndex = chunkIndex;
                    this.level = level;

                    worldSize = k_BrickSize * k_WorldScale;
                    worldPosition = (float3)chunkIndex * worldSize;
                    
                    matrix = Matrix4x4.TRS(worldPosition, Quaternion.identity, Vector3.one);

                    mesh = new Mesh()
                    {
                        bounds = new Bounds(worldSize * 0.5f, worldSize)
                    };

                    meshUpToDate = false;
                }

                public void Update(RenderingData renderingData, BatchChunkMesher mesher, ref TerrainDebugInfo debugInfo)
                {
                    if (!InViewFrustum(renderingData.ObserverCamera))
                        return;

                    BrickmapRenderingData brickmapRenderingData = renderingData.brickmapLevels[level];

                    // Check if the density data within this chunk has changed and flag for remeshing if true.
                    if (brickmapRenderingData.modifiedBricks.Contains(chunkIndex))
                    {
                        brickmapRenderingData.modifiedBricks.Remove(chunkIndex);
                        meshUpToDate = false;
                    }

                    // Remesh if necessary.
                    if (!meshUpToDate)
                    {
                        mesher.QueueRemeshTask(
                            new MeshingTask(
                                mesh,
                                transitionMeshes,
                                chunkIndex,
                                k_BrickSize,
                                k_WorldScale,
                                level,
                                k_TransitionCellPadding,
                                brickmapRenderingData.densitySampler
                            )
                        );

                        meshUpToDate = true;
                    }
                }

                public void Render(Camera camera, Material material, MaterialPropertyBlock mpb, ref TerrainDebugInfo debugInfo)
                {
                    if (!InViewFrustum(camera))
                        return;

                    if (meshUpToDate && mesh.vertexCount > 2)
                    {
                        Graphics.DrawMesh(mesh, matrix, material, 0, camera, 0, mpb);
                        debugInfo.chunkRendererdThisFrame++;
                    }
                }

                bool InViewFrustum(Camera camera)
                {
                    Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                    return GeometryUtility.TestPlanesAABB(frustumPlanes, new Bounds(worldPosition + (worldSize * 0.5f), worldSize));
                }
            }

            readonly Dictionary<int3, Chunk> chunks;
            readonly int level;

            public ClipmapLevel(int mapSize, int level)
            {
                this.level = level;
                chunks = new(mapSize * mapSize * mapSize);
            }

            public void Update(RenderingData renderingData, BatchChunkMesher mesher, ref TerrainDebugInfo debugInfo)
            {
                DensitySampler densitySampler = renderingData.brickmapLevels[level].densitySampler;

                // Note this method of dictionary management is crap because we are already doing this in brickmap levels.
                // It may be smarter to simply send a list of chunks that were allocated and de-allocated per-frame.

                // Remove chunks that are no longer allocated.
                int3[] existingChunks = new int3[chunks.Keys.Count];
                chunks.Keys.CopyTo(existingChunks, 0);

                foreach (int3 chunkIndex in existingChunks)
                {
                    if (!densitySampler.BrickIsAllocated(chunkIndex))
                        chunks.Remove(chunkIndex);
                }

                // Add new allocated chunks.
                foreach (int3 chunkIndex in densitySampler.GetAllocatedBricks())
                {
                    if (!chunks.ContainsKey(chunkIndex))
                        chunks.Add(chunkIndex, new Chunk(chunkIndex, level));
                }

                // Update chunks.
                foreach (int3 chunkIndex in chunks.Keys)
                    chunks[chunkIndex].Update(renderingData, mesher, ref debugInfo);
            }

            public void Render(Camera camera, Material material, MaterialPropertyBlock mpb, ref TerrainDebugInfo debugInfo)
            {
                Stopwatch.Start(ref debugInfo.frameTime);

                foreach (int3 chunkIndex in chunks.Keys)
                    chunks[chunkIndex].Render(camera, material, mpb, ref debugInfo);

                Stopwatch.End(ref debugInfo.frameTime);
            }
        }

        struct RenderingData
        {
            public BrickmapRenderingData[] brickmapLevels;
            public Camera ObserverCamera;
        }

        struct BrickmapRenderingData : IDisposable
        {
            public HashSet<int3> modifiedBricks;
            public DensitySampler densitySampler;

            public void Allocate(int size)
            {
                modifiedBricks = new();
                densitySampler.Allocate(size);
            }

            public void Dispose()
            {
                modifiedBricks = null;
                densitySampler.Dispose();
            }
        }
    }

    /// <summary>
    /// Provides an interface for mesher jobs to read density data.
    /// Regions of density data (bricks) can be added to the hash map and sampled via a pointer to the original array.
    /// </summary>
    public struct DensitySampler : IDisposable
    {
        NativeHashMap<int3, int> bricks;

        [NativeDisableUnsafePtrRestriction]
        NativeHashMap<int3, IntPtr> densityPointers;

        public void Allocate(int size)
        {
            bricks = new(size * size * size, Allocator.Persistent);
            densityPointers = new(size * size * size, Allocator.Persistent);
        }

        public void Dispose()
        {
            bricks.Dispose();
            densityPointers.Dispose();
        }

        public void LoadBrick(int3 index) => bricks.Add(index, 0); // Default state 0 = empty.

        public void UnloadBrick(int3 index) => bricks.Remove(index);

        public void SetBrickState(int3 index, int state) => bricks[index] = state;

        public void AttatchDensityData(int3 index, IntPtr densityPointer) => densityPointers.Add(index, densityPointer);

        public void RemoveDensityData(int3 index) => densityPointers.Remove(index);

        public bool BrickIsAllocated(int3 index) => densityPointers.ContainsKey(index);

        public int3[] GetAllocatedBricks()
        {
            // TODO: This is possibly crap.

            NativeArray<int3> nativeIndices = densityPointers.GetKeyArray(Allocator.Temp);

            int3[] indices = nativeIndices.ToArray();

            nativeIndices.Dispose();

            return indices;
        }

        public unsafe readonly float Sample(int3 globalCellIndex, int brickSize)
        {
            int3 brickIndex = (int3)math.floor((double3)globalCellIndex / brickSize); // TODO: find a way to do this without the cast. Also note that casting to a float3 fuks everything up with precision errors.

            // Early return if the brick is not loaded.
            if (!bricks.ContainsKey(brickIndex))
                return ProceduralTerrain.k_EmptyDensityValue;

            // Early return if the brick is completely empty or full.
            int brickState = bricks[brickIndex];
            if (brickState != 2)
                return math.select(ProceduralTerrain.k_FullDensityValue, ProceduralTerrain.k_EmptyDensityValue, brickState == 0);

            int3 localCellIndex = globalCellIndex - (brickIndex * brickSize);
            int densityIndex = (localCellIndex.z * brickSize * brickSize) + (localCellIndex.y * brickSize) + localCellIndex.x;

            float* ptr = (float*)densityPointers[brickIndex];
            return *(ptr + densityIndex);
        }
    }
}
