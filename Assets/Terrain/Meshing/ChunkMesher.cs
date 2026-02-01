using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGeneration.Terrain.Meshing
{
    public class ChunkMesher
    {
        NativeList<Vertex> m_Vertices;
        NativeList<ushort> m_Indices;
        NativeArray<ReuseCellVertexIndices> m_VertexIndices;

        // Splits the vertex and indices arrays at the start of each transition mesh.
        // x = vertices split, y = indices split.
        NativeArray<int2> m_MeshStartIndices;

        double executionTime;

        const int k_InitialArrayCapacity = 512;

        const MeshUpdateFlags k_UpdateFlags =
              MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontResetBoneBounds
            | MeshUpdateFlags.DontValidateIndices;

        public ChunkMesher()
        {
            m_Vertices = new(k_InitialArrayCapacity, Allocator.Persistent);
            m_Indices = new(k_InitialArrayCapacity, Allocator.Persistent);
            m_MeshStartIndices = new(6, Allocator.Persistent);
        }

        ~ChunkMesher()
        {
            m_Vertices.Dispose();
            m_Indices.Dispose();
            m_MeshStartIndices.Dispose();
        }

        public MeshingResult DoRemesh(Mesh mesh, Mesh[] transitionMeshes, DensitySampler densitySampler, int3 chunkIndex, int chunkSize, float worldScale, int clipmapLevel, float transitionCellPadding)
        {
            m_Vertices.Clear();
            m_Indices.Clear();
            m_VertexIndices = new(chunkSize * chunkSize * chunkSize, Allocator.TempJob);

            TransvoxelMesherJob mesherJob = new()
            {
                clipmapLevel = clipmapLevel,
                chunkIndex = chunkIndex,
                chunks = densitySampler,
                chunkSize = chunkSize,
                cellScale = worldScale,
                padding = transitionCellPadding,
                vertices = m_Vertices,
                indices = m_Indices,
                meshStartIndices = m_MeshStartIndices,
                vertexIndices = m_VertexIndices
            };

            Stopwatch.Start(ref executionTime);

            mesherJob.Schedule(default).Complete();

            Stopwatch.End(ref executionTime);

            m_VertexIndices.Dispose();

            NativeArray<Vertex> vertices = m_Vertices.ToArray(Allocator.Temp);
            NativeArray<ushort> indices = m_Indices.ToArray(Allocator.Temp);

            // Early return if vertices & indices come back empty.
            if (vertices.Length < 2 || indices.Length < 2)
            {
                mesh.Clear();

                if (transitionMeshes != null)
                {
                    for (int i = 0; i < 6; i++)
                        transitionMeshes[i].Clear();
                }

                return new MeshingResult(vertices.Length, indices.Length, executionTime);
            }

            SubMeshDescriptor subMeshDescriptor = new(0, 0, MeshTopology.Triangles);

            int numVertices;
            int numIndices;

            int verticesStart;
            int indicesStart;

            int verticesEnd;
            int indicesEnd;

            // Update base mesh
            if (transitionMeshes != null)
            {
                numVertices = m_MeshStartIndices[0].x;
                numIndices = m_MeshStartIndices[0].y;
            }
            else
            {
                numVertices = vertices.Length;
                numIndices = indices.Length;
            }

            mesh.SetVertexBufferParams(numVertices, Vertex.Format);
            mesh.SetVertexBufferData(vertices, 0, 0, numVertices, 0, k_UpdateFlags);

            mesh.SetIndexBufferParams(numIndices, IndexFormat.UInt16);
            mesh.SetIndexBufferData(indices, 0, 0, numIndices, k_UpdateFlags);

            subMeshDescriptor.indexCount = numIndices;
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, subMeshDescriptor, k_UpdateFlags);

            // Update transition meshes
            if (transitionMeshes == null)
                return new MeshingResult(vertices.Length, indices.Length, executionTime);

            for (int i = 0; i < 6; i++)
            {
                verticesStart = m_MeshStartIndices[i].x;
                indicesStart = m_MeshStartIndices[i].y;

                if (i < 5)
                {
                    verticesEnd = m_MeshStartIndices[i + 1].x;
                    indicesEnd = m_MeshStartIndices[i + 1].y;
                }
                else
                {
                    verticesEnd = vertices.Length;
                    indicesEnd = indices.Length;
                }

                numVertices = verticesEnd - verticesStart;
                numIndices = indicesEnd - indicesStart;

                if (numVertices < 2 || numIndices < 2)
                {
                    transitionMeshes[i].Clear();
                    continue;
                }

                transitionMeshes[i].SetVertexBufferParams(numVertices, Vertex.Format);
                transitionMeshes[i].SetVertexBufferData(vertices, verticesStart, 0, numVertices, 0, k_UpdateFlags);

                transitionMeshes[i].SetIndexBufferParams(numIndices, IndexFormat.UInt16);
                transitionMeshes[i].SetIndexBufferData(indices, indicesStart, 0, numIndices, k_UpdateFlags);

                subMeshDescriptor.indexCount = numIndices;
                transitionMeshes[i].subMeshCount = 1;
                transitionMeshes[i].SetSubMesh(0, subMeshDescriptor, k_UpdateFlags);
            }

            return new MeshingResult(vertices.Length, indices.Length, executionTime);
        }
    }

    public readonly struct MeshingResult
    {
        readonly int vertexCount;
        readonly int indexCount;
        readonly double executionTime;

        public readonly int VertexCount => vertexCount;
        public readonly int IndexCount => indexCount;
        public readonly double ExecutionTime => executionTime;

        public MeshingResult(int vertexCount, int indexCount, double executionTime)
        {
            this.vertexCount = vertexCount;
            this.indexCount = indexCount;
            this.executionTime = executionTime;
        }
    }
}
