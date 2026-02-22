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

        MeshingTask m_CurrentTask;
        bool m_HasTask;

        const int k_InitialArrayCapacity = 61440; // cellsPerBrick (4096) x maxTrianglesPerCell (5) x verticesPerTriangle (3)

        const MeshUpdateFlags k_UpdateFlags =
              MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontResetBoneBounds
            | MeshUpdateFlags.DontValidateIndices;

        public void Allocate()
        {
            m_Vertices = new(k_InitialArrayCapacity, Allocator.Persistent);
            m_Indices = new(k_InitialArrayCapacity, Allocator.Persistent);
        }

        public void Dispose()
        {
            m_Vertices.Dispose();
            m_Indices.Dispose();
        }

        /// <summary>
        /// Schedule and immediately complete a meshing job.
        /// </summary>
        public void ScheduleAndCompleteTask(MeshingTask meshingTask)
        {
            ScheduleTask(meshingTask).Complete();
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
            m_HasTask = true;

            m_Vertices.Clear();
            m_Indices.Clear();
            m_VertexIndices = new(meshingTask.chunkSize * meshingTask.chunkSize * meshingTask.chunkSize, Allocator.TempJob);

            if (meshingTask.transitionIndex == -1)
            {
                CoreMeshingJob mesherJob = new()
                {
                    chunkIndex = meshingTask.chunkIndex,
                    chunkSize = meshingTask.chunkSize,
                    levelScale = meshingTask.levelScale,
                    worldScale = meshingTask.worldScale,
                    densityPtr = meshingTask.densityPtr,
                    vertices = m_Vertices,
                    indices = m_Indices,
                    vertexIndices = m_VertexIndices
                };

                return mesherJob.Schedule(default);
            }
            else
            {
                TransitionMeshingJob mesherJob = new()
                {
                    chunkIndex = meshingTask.chunkIndex,
                    chunkSize = meshingTask.chunkSize,
                    levelScale = meshingTask.levelScale,
                    worldScale = meshingTask.worldScale,
                    transitionIndex = meshingTask.transitionIndex,
                    densityPtr = meshingTask.densityPtr,
                    vertices = m_Vertices,
                    indices = m_Indices
                };

                return mesherJob.Schedule(default);
            }
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

            UpdateMeshData(m_CurrentTask.mesh);
            m_VertexIndices.Dispose();

            m_HasTask = false;
        }

        void UpdateMeshData(Mesh mesh)
        {
            // Early return if vertices or indices come back empty.
            // Note: this check actually does pass sometimes, though it shouldn't really...
            if (m_Vertices.Length < 2 || m_Indices.Length < 2)
            {
                mesh.Clear();
                return;
            }

            // Allocate temp arrays.
            NativeArray<Vertex> vertices = m_Vertices.ToArray(Allocator.Temp);
            NativeArray<ushort> indices = m_Indices.ToArray(Allocator.Temp);

            int numVertices = vertices.Length;
            int numIndices = indices.Length;

            // Update base mesh
            mesh.SetVertexBufferParams(numVertices, Vertex.Format);
            mesh.SetVertexBufferData(vertices, 0, 0, numVertices, 0, k_UpdateFlags);

            mesh.SetIndexBufferParams(numIndices, IndexFormat.UInt16);
            mesh.SetIndexBufferData(indices, 0, 0, numIndices, k_UpdateFlags);

            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new(0, numIndices, MeshTopology.Triangles), k_UpdateFlags);

            // Dispose temp arrays.
            vertices.Dispose();
            indices.Dispose();
        }
    }

    public readonly struct MeshingTask
    {
        public readonly Mesh mesh;
        public readonly int3 chunkIndex;
        public readonly int chunkSize;
        public readonly int levelScale;
        public readonly float worldScale;
        public readonly IntPtr densityPtr;
        public readonly int transitionIndex; // -1 for core tasks.

        public MeshingTask(Mesh mesh, int3 chunkIndex, int chunkSize, int levelScale, float worldScale, IntPtr densityPtr)
        {
            this.mesh = mesh;
            this.chunkIndex = chunkIndex;
            this.chunkSize = chunkSize;
            this.levelScale = levelScale;
            this.worldScale = worldScale;
            this.densityPtr = densityPtr;
            this.transitionIndex = -1;
        }

        public MeshingTask(Mesh mesh, int3 chunkIndex, int chunkSize, int levelScale, float worldScale, IntPtr densityPtr, int transitionIndex) : this(mesh, chunkIndex, chunkSize, levelScale, worldScale, densityPtr)
        {
            this.transitionIndex = transitionIndex;
        }

        public bool CanReplace(MeshingTask task) => chunkIndex.Equals(task.chunkIndex) && levelScale.Equals(task.levelScale) && transitionIndex.Equals(task.transitionIndex);
    }
}
