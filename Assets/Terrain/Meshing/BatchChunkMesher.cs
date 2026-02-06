using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace LevelGeneration.Terrain.Meshing
{
    /// <summary>
    /// Provides a pool of chunk meshers so that meshing tasks can be ran in parallel.
    /// </summary>
    public class BatchChunkMesher : IDisposable
    {
        ChunkMesher[] m_MesherPool;    // The pool of individually allocated ChunkMeshers capable of running meshing JOBs independently.
        List<MeshingTask> m_TaskQueue; // The list of meshing tasks that have been queued by the user.

        public int NumPendingTasks => m_TaskQueue.Count;

        const int k_PoolSize = 16;

        public void Allocate()
        {
            m_MesherPool = new ChunkMesher[k_PoolSize];

            for (int i = 0; i < k_PoolSize; i++)
            {
                m_MesherPool[i] = new ChunkMesher();
                m_MesherPool[i].Allocate();
            }

            m_TaskQueue = new();
        }

        public void Dispose()
        {
            for (int i = 0; i < k_PoolSize; i++)
                m_MesherPool[i].Dispose();

            m_TaskQueue = null;
        }

        /// <summary>
        /// Queue a meshing task. These will be pending until ExecutePendingTasks is called.
        /// </summary>
        public void QueueRemeshTask(MeshingTask task)
        {
            // If there are already tasks in the queue, try to replace an existing task
            int numTasks = m_TaskQueue.Count;
            if (numTasks > 0)
            {
                for (int i = 0; i < numTasks; i++)
                {
                    if (m_TaskQueue[i].CanReplace(task))
                    {
                        m_TaskQueue[i] = task;
                        return;
                    }
                }
            }

            m_TaskQueue.Add(task);
        }

        /// <summary>
        /// Execute as many pending tasks as there are meshers in the pool.
        /// </summary>
        public void ExecutePendingTasks()
        {
            int numTasks = m_TaskQueue.Count;

            if (numTasks == 0)
                return;

            List<RemeshTask> remeshTasks = new();
            int mesherIndex = 0;

            // Create remesh tasks array.
            for (int i = numTasks - 1; i >= 0; i--)
            {
                ChunkMesher mesher = m_MesherPool[mesherIndex];
                MeshingTask task = m_TaskQueue[i];

                // Schedule remesh JOB.
                JobHandle jobHandle = mesher.ScheduleTask(task);

                // Save remesh task data for later.
                remeshTasks.Add(new RemeshTask(mesherIndex, jobHandle));

                // Remove task from queue.
                m_TaskQueue.Remove(m_TaskQueue[i]);

                // Once every mesher has been given a task, break from the loop.
                mesherIndex++;
                if (mesherIndex == k_PoolSize)
                    break;
            }

            // Schedule and complete JOBs.
            int numTasksQueued = remeshTasks.Count;
            NativeArray<JobHandle> jobs = new(numTasksQueued, Allocator.Temp);

            for (int i = 0; i < numTasksQueued; i++)
            {
                // Add job handle to wait list.
                jobs[i] = remeshTasks[i].jobHandle;
            }

            JobHandle.CompleteAll(jobs);

            jobs.Dispose();

            // Clean up active meshing JOBs.
            for (int i = 0; i < remeshTasks.Count; i++)
                m_MesherPool[remeshTasks[i].mesherIndex].CompleteTask();
        }

        /// <summary>
        /// Continually execute tasks in the queue until there are none left.
        /// This may result in frame drops if there are a large number of tasks in the queue.
        /// </summary>
        public void ExecutePendingTasksContinuous()
        {
            while (m_TaskQueue.Count > 0)
                ExecutePendingTasks();
        }

        readonly struct RemeshTask
        {
            public readonly int mesherIndex;
            public readonly JobHandle jobHandle;

            public RemeshTask(int mesherIndex, JobHandle jobHandle)
            {
                this.mesherIndex = mesherIndex;
                this.jobHandle = jobHandle;
            }
        }
    }
}
