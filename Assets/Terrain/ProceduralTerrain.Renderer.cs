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

        readonly Color[] m_ClipmapMeshDebugColors = new Color[]
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
        BatchChunkMesher m_Mesher;
        ClipmapLevel[] m_Clipmaps;

        MaterialPropertyBlock m_MaterialProperties;

        const float k_TransitionCellPadding = 0.5f;

        void InitializeRendering()
        {
            m_Mesher = new();
            m_Clipmaps = new ClipmapLevel[k_NumBrickMapLevels];
            m_MaterialProperties = new();

            for (int i = 0; i < k_NumBrickMapLevels; i++)
                m_Clipmaps[i] = new(i);

            m_Mesher.Allocate();
        }

        void CleanupRendering()
        {
            m_Mesher.Dispose();

            m_Mesher = null;
            m_Clipmaps = null;
            m_MaterialProperties = null;
        }

        void UpdateClipmap()
        {
#if UNITY_EDITOR
            if (DisableRendering)
                return;
#endif

            m_RenderingData.brickmapData = m_DensityCache.GetRenderingData();

            m_DebugInfo.numChunks = 0;

            foreach (ClipmapLevel clipmap in m_Clipmaps)
                clipmap.Update(m_RenderingData, m_Mesher, ref m_DebugInfo);

            double t = 0.0;

            Stopwatch.Start(ref t);
            m_Mesher.ExecutePendingTasksContinuous();
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

            Stopwatch.Start(ref m_DebugInfo.clipmapRenderingTime);

            for (int i = 0; i < k_NumBrickMapLevels; i++)
            {
#if UNITY_EDITOR
                m_MaterialProperties.SetColor("_ClipmapDebugColor", ColorClipmapLevels ? m_ClipmapMeshDebugColors[i] : Color.white);
#else
                m_MaterialProperties.SetColor("_ClipmapDebugColor", Color.white);
#endif

                m_Clipmaps[i].Render(camera, Material, m_MaterialProperties, ref m_DebugInfo);
            }

            Stopwatch.End(ref m_DebugInfo.clipmapRenderingTime);
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

                    worldSize = k_BrickSize * k_WorldScale * math.pow(2, level);
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

                    BrickmapRenderingData brickmapRenderingData = renderingData.brickmapData[level];

                    // Check if the density data within this chunk has changed and flag for remeshing if true.
                    if (brickmapRenderingData.CheckAndRemovePendingRemesh(chunkIndex))
                        meshUpToDate = false;

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

            public ClipmapLevel(int level)
            {
                this.level = level;
                chunks = new();
            }

            public void Update(RenderingData renderingData, BatchChunkMesher mesher, ref TerrainDebugInfo debugInfo)
            {
                BrickmapRenderingData brickmapRenderingData = renderingData.brickmapData[level];
                int3 origin = brickmapRenderingData.originIndex;
                int3 size = brickmapRenderingData.size;

                // Copy currently loaded chunk indices.
                int3[] chunksCopy = new int3[chunks.Keys.Count];
                chunks.Keys.CopyTo(chunksCopy, 0);

                // Remove chunks that are no longer allocated.
                foreach (int3 chunkIndex in chunksCopy)
                {
                    if (!BrickInBounds(chunkIndex, origin, size) || !brickmapRenderingData.densitySampler.IsAllocated(chunkIndex))
                        chunks.Remove(chunkIndex);
                }

                // Add new chunk and do updates.
                for (int x = 0; x < size.x; x++)
                {
                    for (int y = 0; y < size.y; y++)
                    {
                        for (int z = 0; z < size.z; z++)
                        {
                            int3 chunkIndex = origin + new int3(x, y, z) - (size / 2);

                            if (!brickmapRenderingData.densitySampler.IsAllocated(chunkIndex))
                                continue;

                            if (!chunks.ContainsKey(chunkIndex))
                                chunks.Add(chunkIndex, new Chunk(chunkIndex, level));

                            chunks[chunkIndex].Update(renderingData, mesher, ref debugInfo);
                        }
                    }
                }

                debugInfo.numChunks += chunks.Count;
            }

            public void Render(Camera camera, Material material, MaterialPropertyBlock mpb, ref TerrainDebugInfo debugInfo)
            {
                foreach (int3 chunkIndex in chunks.Keys)
                    chunks[chunkIndex].Render(camera, material, mpb, ref debugInfo);
            }

            bool BrickInBounds(int3 brickIndex, int3 originIndex, int3 mapSize)
            {
                int3 halfMapSize = mapSize / 2;
                return math.all(brickIndex < originIndex + halfMapSize) && math.all(brickIndex >= originIndex - halfMapSize);
            }
        }

        struct RenderingData
        {
            public BrickmapRenderingData[] brickmapData;
            public Camera ObserverCamera;
        }

        struct BrickmapRenderingData : IDisposable
        {
            // Allows the mesher to sample all density levels at this brickmap level via pointers to cached density arrays.
            public DensitySampler densitySampler;

            // The origin and size of the intended clipmap level.
            public int3 originIndex;
            public int3 size;

            // Tells the mesher which bricks have been modified and need to be remeshed.
            HashSet<int3> modifiedBricks;

            public void Allocate(int mapSize)
            {
                densitySampler.Allocate(mapSize);
                modifiedBricks = new();
            }

            public void Dispose()
            {
                densitySampler.Dispose();
                modifiedBricks = null;
            }

            public readonly void MarkBrickAsModified(int3 index)
            {
                modifiedBricks.Add(index);
            }

            public readonly bool CheckAndRemovePendingRemesh(int3 index)
            {
                if (modifiedBricks.Contains(index))
                {
                    modifiedBricks.Remove(index);
                    return true;
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Provides an interface for mesher jobs to read density data.
    /// Regions of density data (bricks) can be added to the hash map and sampled via a pointer to the original array.
    /// </summary>
    public struct DensitySampler : IDisposable
    {
        // Contains the state of loaded bricks. 0 = empty, 1 = full, 2 = partial.
        NativeHashMap<int3, int> brickStates;

        // Contains pointers allocated density data, only found when the brick state = 2.
        [NativeDisableUnsafePtrRestriction]
        NativeHashMap<int3, IntPtr> densityPointers;

        // TODO: ^ convert to single hash map of structs. OR better yet use 0 & 1 pointer to mean empty / full?

        public void Allocate(int size)
        {
            brickStates = new(size * size * size, Allocator.Persistent);
            densityPointers = new(size * size * size, Allocator.Persistent);
        }

        public void Dispose()
        {
            brickStates.Dispose();
            densityPointers.Dispose();
        }

        public void AddBrick(int3 index) => brickStates.Add(index, 0);

        public void RemoveBrick(int3 index) => brickStates.Remove(index);

        public void SetBrickState(int3 index, int state) => brickStates[index] = state;

        public void AddDensityPtr(int3 index, IntPtr densityPointer) => densityPointers.Add(index, densityPointer);

        public void RemoveDensityPtr(int3 index) => densityPointers.Remove(index);

        public bool IsAllocated(int3 index) => brickStates.ContainsKey(index) && brickStates[index] == 2;

        public unsafe readonly float Sample(int3 globalCellIndex, int brickSize)
        {
            // TODO: sample lower brickmap levels if data does not exist for this one.

            int3 brickIndex = (int3)math.floor((double3)globalCellIndex / brickSize); // TODO: find a way to do this without the cast (faster). Also note that casting to a float3 fuks everything up with precision errors.

            // Early return if the brick is completely empty or full.
            int brickState = brickStates[brickIndex];
            if (brickState != 2)
                return math.select(ProceduralTerrain.k_FullDensityValue, ProceduralTerrain.k_EmptyDensityValue, brickState == 0);

            int3 localCellIndex = globalCellIndex - (brickIndex * brickSize);
            int densityIndex = (localCellIndex.z * brickSize * brickSize) + (localCellIndex.y * brickSize) + localCellIndex.x;

            float* ptr = (float*)densityPointers[brickIndex];
            return *(ptr + densityIndex);
        }
    }
}
