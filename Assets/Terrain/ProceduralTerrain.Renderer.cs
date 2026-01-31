using LevelGeneration.Terrain.Rendering;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        public Material Material;

        TerrainRenderingData m_RenderingData;
        ChunkMesher m_ChunkMesher;
        ClipmapLevel[] m_Clipmaps;

        MaterialPropertyBlock m_MaterialProperties;

        const float k_TransitionCellPadding = 0.5f;

        void InitializeRendering()
        {
            m_RenderingData = new()
            {
                brickmapLevels = m_DensityCache.GetRenderingData()
            };
            
            m_ChunkMesher = new ChunkMesher();

            m_Clipmaps = new ClipmapLevel[k_NumBrickMapLevels];
            for (int i = 0; i < k_NumBrickMapLevels; i++)
                m_Clipmaps[i] = new(k_BrickmapLevelSize, i);

            m_MaterialProperties = new();
        }

        void CleanupRendering()
        {
            m_RenderingData = null;
            m_ChunkMesher = null;
            m_Clipmaps = null;
        }

        void DrawClipmapLevels()
        {
            m_DebugInfo.chunkRendererdThisFrame = 0;

            foreach (ClipmapLevel clipmap in m_Clipmaps)
                clipmap.Draw(Material, m_MaterialProperties, m_RenderingData, m_ChunkMesher, ref m_DebugInfo);
        }

        class ClipmapLevel
        {
            class Chunk
            {
                readonly int3 chunkIndex;
                readonly int level;

                readonly float3 position;
                readonly float3 size;
                
                readonly Mesh mesh;
                readonly Mesh[] transitionMeshes;

                bool meshUpToDate;

                public Chunk(int3 chunkIndex, int level)
                {
                    this.chunkIndex = chunkIndex;
                    this.level = level;

                    float chunkSize = k_BrickSize * k_WorldScale; // TODO: pass in k_BrickSize as chunkSize

                    position = (float3)chunkIndex * chunkSize;
                    size = chunkSize;
                    
                    mesh = new Mesh()
                    {
                        bounds = new Bounds(size * 0.5f, size)
                    };

                    meshUpToDate = false;
                }

                public void DrawMesh(Material material, MaterialPropertyBlock mpb, TerrainRenderingData renderingData, ChunkMesher mesher, ref TerrainDebugInfo debugInfo)
                {
                    Camera camera = renderingData.Camera;

                    if (!InViewFrustum(camera))
                        return;

                    BrickMapRenderingData brickMapRendering = renderingData.brickmapLevels[level];

                    if (brickMapRendering.IsBrickPendingRemesh(chunkIndex))
                    {
                        brickMapRendering.RemovePendingRemeshFlag(chunkIndex);
                        meshUpToDate = false;
                    }

                    if (!meshUpToDate)
                    {
                        MeshingResult result = mesher.DoRemesh(mesh, transitionMeshes, renderingData, chunkIndex, level, k_TransitionCellPadding);
                        debugInfo.meshingJobTimes.AddTime(result.ExecutionTime);
                        meshUpToDate = true;

                        debugInfo.chunkRendererdThisFrame++;
                    }

                    if (mesh.vertexCount > 2)
                        Graphics.DrawMesh(mesh, Matrix4x4.TRS(position, Quaternion.identity, Vector3.one), material, 0, camera, 0, mpb);
                }

                bool InViewFrustum(Camera camera)
                {
                    Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                    return GeometryUtility.TestPlanesAABB(frustumPlanes, new Bounds(position + (size * 0.5f), size));
                }
            }

            readonly Dictionary<int3, Chunk> chunks;
            readonly int mapSize;
            readonly int level;

            public ClipmapLevel(int mapSize, int level)
            {
                this.mapSize = mapSize;
                this.level = level;

                chunks = new(mapSize * mapSize * mapSize);
            }

            public void Draw(Material material, MaterialPropertyBlock mpb, TerrainRenderingData renderingData, ChunkMesher mesher, ref TerrainDebugInfo debugInfo)
            {
                int3[] allocatedBrickIndices = renderingData.brickmapLevels[level].AllocatedBricks;

                // Note this method of dictionary management is crap because we are already doing this in brickmap levels.
                // It may be smarter to simply send a list of chunks that were allocated and de-allocated per-frame.

                // Remove chunks that are no longer allocated.
                int3[] existingChunks = new int3[chunks.Keys.Count];
                chunks.Keys.CopyTo(existingChunks, 0);

                foreach (int3 chunkIndex in existingChunks)
                {
                    if (!allocatedBrickIndices.Contains(chunkIndex))
                        chunks.Remove(chunkIndex);
                }

                // Add new allocated chunks.
                foreach (int3 chunkIndex in allocatedBrickIndices)
                {
                    if (!chunks.ContainsKey(chunkIndex))
                        chunks.Add(chunkIndex, new Chunk(chunkIndex, level));
                }

                // Draw chunks
                foreach (int3 chunkIndex in chunks.Keys)
                {
                    chunks[chunkIndex].DrawMesh(material, mpb, renderingData, mesher, ref debugInfo);
                }
            }
        }
    }
}
