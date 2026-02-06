using LevelGeneration.Terrain.Meshing;
using System;
using System.Collections.Generic;
using Unity.Collections;
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

        BatchChunkMesher m_Mesher;
        ClipmapLevel[] m_Clipmaps;

        MaterialPropertyBlock m_MaterialProperties;

        const float k_TransitionCellPadding = 0.5f;

        void InitializeRendering()
        {
            m_Mesher = new();
            m_Clipmaps = new ClipmapLevel[k_NumBrickmapLevels];
            m_MaterialProperties = new();

            for (int i = 0; i < k_NumBrickmapLevels; i++)
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

        void UpdateClipmap(RenderingData renderingData)
        {
#if UNITY_EDITOR
            if (DisableRendering)
                return;
#endif

            m_DebugInfo.numChunks = 0;

            // Early return if there is no camera.
            if (!renderingData.observerCamera)
                return;

            // Update clipmap levels.
            foreach (ClipmapLevel clipmap in m_Clipmaps)
                clipmap.Update(renderingData, m_Mesher, ref m_DebugInfo);

            m_DebugInfo.numRemeshTasks = m_Mesher.NumPendingTasks;

            double meshingTaskTime = 0.0;

            // Execute meshing tasks queued this frame.
            Stopwatch.Start(ref meshingTaskTime);
            //m_Mesher.ExecutePendingTasksContinuous();
            m_Mesher.ExecutePendingTasks();
            Stopwatch.End(ref meshingTaskTime);

            m_DebugInfo.remeshTaskTime = meshingTaskTime;
        }

        void RenderTerrain(ScriptableRenderContext context, Camera camera)
        {
#if UNITY_EDITOR
            if (DisableRendering)
                return;
#endif

            m_DebugInfo.numChunkRendererd = 0;

            Stopwatch.Start(ref m_DebugInfo.clipmapRenderingTime);

            for (int i = 0; i < k_NumBrickmapLevels; i++)
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
                    // Note: meshing can be skipped or delayed for unviewed chunks where collision is unnecessary.
                    if (level > 0 && !InViewFrustum(renderingData.observerCamera))
                        return;

                    BrickmapRenderingData brickmapRenderingData = renderingData.brickmapData[level];

                    // Check if the density data within this chunk has changed and flag for remeshing if true.
                    if (brickmapRenderingData.modifiedBricks.Contains(chunkIndex))
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
                                brickmapRenderingData.allocatedBricks[chunkIndex].densityPtr
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
                        debugInfo.numChunkRendererd++;
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
                    if (!BrickInBounds(chunkIndex, origin, size) || !ShouldMeshBrick(brickmapRenderingData, chunkIndex))
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

                            if (!ShouldMeshBrick(brickmapRenderingData, chunkIndex))
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

            bool ShouldMeshBrick(BrickmapRenderingData brickmapRenderingData, int3 index) => brickmapRenderingData.allocatedBricks.ContainsKey(index) && brickmapRenderingData.allocatedBricks[index].state == 2;

        }

        // TODO: remove rendering data all together? It takes ages to fill the data, an effect which could be achieved by just passing in the brickmap levels.

        struct RenderingData
        {
            public BrickmapRenderingData[] brickmapData;
            public Camera observerCamera;
        }

        struct BrickmapRenderingData
        {
            // All loaded bricks.
            public Dictionary<int3, BrickRenderingData> allocatedBricks;

            // Bricks modified this frame.
            public HashSet<int3> modifiedBricks;

            // Origin and size of the intended clipmap level.
            public int3 originIndex;
            public int3 size;
        }

        struct BrickRenderingData
        {
            public IntPtr densityPtr;
            public int state;
        }
    }
}
