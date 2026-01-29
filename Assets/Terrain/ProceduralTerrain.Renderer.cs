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
            m_RenderingData = new(k_NumBrickMapLevels, k_BrickmapLevelSize);
            
            m_ChunkMesher = new ChunkMesher();

            m_Clipmaps = new ClipmapLevel[k_NumBrickMapLevels];
            for (int i = 0; i < k_NumBrickMapLevels; i++)
                m_Clipmaps[i] = new(k_BrickmapLevelSize, i);
        }

        void CleanupRendering()
        {
            m_RenderingData = null;
            m_ChunkMesher = null;
            m_Clipmaps = null;
        }

        void DrawClipmapLevels()
        {
            foreach (ClipmapLevel clipmap in m_Clipmaps)
                clipmap.Draw(Material, m_MaterialProperties, m_RenderingData, m_ChunkMesher, ref m_DebugInfo);
        }

        class ClipmapLevel
        {
            class Chunk
            {
                int3 chunkIndex;
                int level;

                float3 position;
                float3 size;
                
                Mesh mesh;
                Mesh[] transitionMeshes;

                bool meshUpToDate;

                public Chunk(int3 chunkIndex, int level)
                {
                    this.chunkIndex = chunkIndex;
                    this.level = level;

                    float chunkSize = k_BrickSize; // TODO: pass in k_BrickSize as chunkSize

                    position = (float3)chunkIndex * chunkSize;
                    size = chunkSize;
                    
                    mesh = new Mesh()
                    {
                        bounds = new Bounds(Vector3.zero, size)
                    };

                    meshUpToDate = false;
                }

                public void DrawMesh(Material material, MaterialPropertyBlock mpb, TerrainRenderingData renderingData, ChunkMesher mesher, ref TerrainDebugInfo debugInfo)
                {
                    Camera camera = renderingData.Camera;

                    if (!InViewFrustum(camera))
                        return;

                    if (renderingData.IsBrickPendingRemesh(level, chunkIndex))
                    {
                        renderingData.RemovePendingRemeshFlag(level, chunkIndex);
                        meshUpToDate = false;
                    }

                    if (!meshUpToDate)
                    {
                        // Make mesh
                        MeshingResult result = mesher.DoRemesh(mesh, transitionMeshes, renderingData, chunkIndex, level, k_TransitionCellPadding);
                        debugInfo.meshingJobTimes.AddTime(result.ExecutionTime);
                        meshUpToDate = true;
                    }

                    // Draw mesh
                    if (mesh.vertexCount > 2)
                        Graphics.DrawMesh(mesh, Matrix4x4.TRS(position, Quaternion.identity, Vector3.one), material, 0, camera, 0, mpb);
                }

                bool InViewFrustum(Camera camera)
                {
                    Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                    return GeometryUtility.TestPlanesAABB(frustumPlanes, new Bounds(position, size));
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
                int3[] allocatedBrickIndices = renderingData.GetAllocatedBricks(level);

                // Note this method of dictionary management is crap because we are already doing this in brickmap levels.
                // It may be smarter to simply send a list of chunks that were allocated and de-allocated per-frame.

                // Remove chunks that are no longer allocated.
                var existingChunks = chunks.Keys;
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
