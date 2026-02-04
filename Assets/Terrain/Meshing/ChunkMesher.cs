using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGeneration.Terrain.Meshing
{
    /// <summary>
    /// A helper object to handle a single meshing handler instance.
    /// Allocates and queues a single transvoxel meshing job at a time.
    /// </summary>
    public class ChunkMesher : IDisposable
    {
        NativeList<Vertex> m_Vertices;
        NativeList<ushort> m_Indices;
        NativeArray<ReuseCellVertexIndices> m_VertexIndices;

        // Splits the vertex and indices arrays at the start of each transition mesh.
        // x = vertices split, y = indices split.
        NativeArray<int2> m_MeshStartIndices;

        MeshingTask m_CurrentTask;
        bool m_HasTask;

        const int k_InitialArrayCapacity = 512;

        const MeshUpdateFlags k_UpdateFlags =
              MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontResetBoneBounds
            | MeshUpdateFlags.DontValidateIndices;

        public void Allocate()
        {
            m_Vertices = new(k_InitialArrayCapacity, Allocator.Persistent);
            m_Indices = new(k_InitialArrayCapacity, Allocator.Persistent);
            m_MeshStartIndices = new(6, Allocator.Persistent);
        }

        public void Dispose()
        {
            m_Vertices.Dispose();
            m_Indices.Dispose();
            m_MeshStartIndices.Dispose();
        }

        /// <summary>
        /// Schedule and immediately complete a meshing job.
        /// </summary>
        public void ScheduleAndCompleteTask(MeshingTask meshingTask)
        {
            JobHandle job = ScheduleTask(meshingTask);
            job.Complete();
            CompleteTask();
        }

        /// <summary>
        /// Schedule a meshing job.
        /// </summary>
        public JobHandle ScheduleTask(MeshingTask meshingTask)
        {
            if (m_HasTask)
            {
                Debug.LogWarning($"Could not schedule meshing task for chunk {meshingTask.chunkIndex}, mesher already active.");
                return new JobHandle();
            }

            m_CurrentTask = meshingTask;

            m_Vertices.Clear();
            m_Indices.Clear();
            m_VertexIndices = new(meshingTask.chunkSize * meshingTask.chunkSize * meshingTask.chunkSize, Allocator.TempJob);

            TransvoxelMesherJob mesherJob = new()
            {
                clipmapLevel = meshingTask.clipmapLevel,
                chunkIndex = meshingTask.chunkIndex,
                chunkSize = meshingTask.chunkSize,
                cellScale = meshingTask.worldScale,
                padding = meshingTask.transitionCellPadding,
                chunks = meshingTask.densitySampler,
                vertices = m_Vertices,
                indices = m_Indices,
                meshStartIndices = m_MeshStartIndices,
                vertexIndices = m_VertexIndices
            };

            m_HasTask = true;

            return mesherJob.Schedule(default);
        }

        /// <summary>
        /// Complete the current meshing job.
        /// </summary>
        public void CompleteTask()
        {
            if (!m_HasTask)
            {
                Debug.LogWarning($"Could not complete meshing task, no task active.");
                return;
            }

            UpdateMeshData(m_CurrentTask.mesh, m_CurrentTask.transitionMeshes);
            m_VertexIndices.Dispose();

            m_HasTask = false;
        }

        void UpdateMeshData(Mesh mesh, Mesh[] transitionMeshes)
        {
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

                return;
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
                return;

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
        }
    }

    public readonly struct MeshingTask
    {
        public readonly Mesh mesh;
        public readonly Mesh[] transitionMeshes;
        public readonly int3 chunkIndex;
        public readonly int chunkSize;
        public readonly float worldScale;
        public readonly int clipmapLevel;
        public readonly float transitionCellPadding;
        public readonly DensitySampler densitySampler;

        public MeshingTask(Mesh mesh, Mesh[] transitionMeshes, int3 chunkIndex, int chunkSize, float worldScale, int clipmapLevel, float transitionCellPadding, DensitySampler densitySampler)
        {
            this.mesh = mesh;
            this.transitionMeshes = transitionMeshes;
            this.chunkIndex = chunkIndex;
            this.chunkSize = chunkSize;
            this.worldScale = worldScale;
            this.clipmapLevel = clipmapLevel;
            this.transitionCellPadding = transitionCellPadding;
            this.densitySampler = densitySampler;
        }

        public bool CanReplace(MeshingTask task)
        {
            return chunkIndex.Equals(task.chunkIndex) && clipmapLevel.Equals(task.clipmapLevel);
        }
    }
}
