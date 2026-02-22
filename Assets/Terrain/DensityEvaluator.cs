using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LevelGeneration.Terrain
{
    public class DensityEvaluator : IDisposable
    {
        NativeArray<float> m_DensityData;
        int m_DensityArraySize;

        NativeArray<float> m_TransitionDensityData;
        int m_TransitionDensityArraySize;

        NativeReference<bool> m_PositiveValueFound;
        NativeReference<bool> m_NegativeValueFound;
        
        double m_ExecutionTime;

        const int k_InnerloopBatchCount = 32;

        public void Allocate(int brickSize)
        {
            int extendedBrickSize = brickSize + 3;

            m_DensityArraySize = extendedBrickSize * extendedBrickSize * extendedBrickSize;
            m_DensityData = new(m_DensityArraySize, Allocator.Persistent);

            int transitionSize = (extendedBrickSize * 2) - 1;
            m_TransitionDensityArraySize = transitionSize * transitionSize;
            m_TransitionDensityData = new(m_TransitionDensityArraySize, Allocator.Persistent);

            m_PositiveValueFound = new(Allocator.Persistent);
            m_NegativeValueFound = new(Allocator.Persistent);
        }

        public void Dispose()
        {
            m_DensityData.Dispose();
            m_TransitionDensityData.Dispose();

            m_PositiveValueFound.Dispose();
            m_NegativeValueFound.Dispose();
        }

        public DensityEvaluationResult ComputeCore(DensitySampler sampler, int3 brickIndex, int brickSize, int levelScale, float worldScale)
        {
            m_PositiveValueFound.Value = false;
            m_NegativeValueFound.Value = false;

            int extendedBrickSize = brickSize + 3;

            DensityJob job = new()
            {
                densitySampler = sampler,
                brickIndex = brickIndex,
                brickSize = brickSize,
                pointsPerAxis = extendedBrickSize,
                levelScale = levelScale,
                worldScale = worldScale,
                density = m_DensityData,
                positiveValueFound = m_PositiveValueFound,
                negativeValueFound = m_NegativeValueFound
            };

            Stopwatch.Start(ref m_ExecutionTime);
            job.ScheduleParallel(m_DensityArraySize, k_InnerloopBatchCount, default).Complete();
            Stopwatch.End(ref m_ExecutionTime);

            bool isEmpty = m_PositiveValueFound.Value && !m_NegativeValueFound.Value;
            bool isFull = m_NegativeValueFound.Value && !m_PositiveValueFound.Value;

            return new DensityEvaluationResult(m_DensityData, isEmpty || isFull, m_ExecutionTime);
        }

        public DensityEvaluationResult ComputeTransition(DensitySampler sampler, int3 brickIndex, int brickSize, int levelScale, float worldScale, int transitionIndex)
        {
            int extendedBrickSize = brickSize + 3;

            TransitionDensityJob job = new()
            {
                densitySampler = sampler,
                brickIndex = brickIndex,
                brickSize = brickSize,
                pointsPerAxis = (extendedBrickSize * 2) - 1,
                levelScale = levelScale,
                worldScale = worldScale,
                transitionIndex = transitionIndex,
                density = m_TransitionDensityData
            };

            Stopwatch.Start(ref m_ExecutionTime);
            job.ScheduleParallel(m_TransitionDensityArraySize, k_InnerloopBatchCount, default).Complete();
            Stopwatch.End(ref m_ExecutionTime);

            return new DensityEvaluationResult(m_TransitionDensityData, false, m_ExecutionTime);
        }

        /*
         * Note: cannot use [FloatMode = FloatMode.Fast] for these Jobs.
         * Doing so quickly introduces large errors when moving away from the world origin.
         * 
         * Note: I could possibly re-introduce this setting by localising the shapes around the brickIndex.
         * So, brickIndex is always treated as the world centre. However, this may still introduce small gaps in the terrain.
         * TODO: test this!
        */

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct DensityJob : IJobFor
        {
            [ReadOnly] public DensitySampler densitySampler;
            [ReadOnly] public int3 brickIndex;
            [ReadOnly] public int brickSize;
            [ReadOnly] public int pointsPerAxis;
            [ReadOnly] public float worldScale;
            [ReadOnly] public int levelScale;

            [WriteOnly] public NativeArray<float> density;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> positiveValueFound;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> negativeValueFound;

            public void Execute(int index)
            {
                // Unwrap the iteration index into a 3D index using the extended brick size.
                int z = index / (pointsPerAxis * pointsPerAxis);
                int y = (index / pointsPerAxis) % pointsPerAxis;
                int x = index % pointsPerAxis;
                int3 localCellIndex = new(x, y, z);

                // Derrive world position from iteration index.
                int3 globalCellIndex = ((brickIndex * brickSize) + localCellIndex - 1) * levelScale;
                float3 worldPosition = (float3)globalCellIndex * worldScale;

                // Sample the SDF.
                float newDensity = densitySampler.Sample(worldPosition);

                // Store the new density.
                density[index] = newDensity;

                // Update positive / negative value flags.
                if (math.all(localCellIndex > 0) && math.all(localCellIndex <= brickSize + 2))
                {
                    if (newDensity > 0.0f)
                        positiveValueFound.Value = true;
                    else
                        negativeValueFound.Value = true;
                }
            }
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct TransitionDensityJob : IJobFor
        {
            [ReadOnly] public DensitySampler densitySampler;
            [ReadOnly] public int3 brickIndex;
            [ReadOnly] public int brickSize;
            [ReadOnly] public int pointsPerAxis;
            [ReadOnly] public float worldScale;
            [ReadOnly] public int levelScale;
            [ReadOnly] public int transitionIndex;

            [WriteOnly] public NativeArray<float> density;

            public void Execute(int index)
            {
                // Unwrap the iteration index into a 3D index using the extended brick size.
                
                // Ranges:
                // x = 0 -> pointsPerAxis (-1)
                // y = 0 -> pointsPerAxis (-1)
                // z = 0 -> 3 (-1)

                int z = index / (pointsPerAxis * pointsPerAxis);
                int y = (index / pointsPerAxis) % pointsPerAxis;
                int x = index % pointsPerAxis;

                // Derrive cell index from face index.
                int3 localCellIndex = FaceToCellIndex(new int3(x, y, z));

                // Coord range is double-precision compared to core evaluation jobs for this level.
                // Therefore we multiply the rest of the calculation by 2 to match.
                int3 globalCellIndex = ((brickIndex * brickSize * 2) + localCellIndex) * levelScale / 2;
                float3 worldPosition = (float3)globalCellIndex * worldScale;

                // Store the new density.
                density[index] = densitySampler.Sample(worldPosition);

                // ^ TODO: init tranition density with core density values to avoid duplicate computation.
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly int3 FaceToCellIndex(int3 index)
            {
                int scaledBrickSize = brickSize * levelScale;

                return transitionIndex switch
                {
                    0 => new(scaledBrickSize - index.z, index.y, index.x), //  x
                    1 => new(index.z, index.x, index.y),                   // -x
                    2 => new(index.x, scaledBrickSize - index.z, index.y), //  y
                    3 => new(index.y, index.z, index.x),                   // -y
                    4 => new(index.y, index.x, scaledBrickSize - index.z), //  z
                    5 => new(index.x, index.y, index.z),                   // -z
                    _ => new(0, 0, 0)
                };
            }
        }
    }

    public readonly struct DensityEvaluationResult
    {
        public readonly NativeArray<float> density;
        public readonly bool isUniformState;
        public readonly double executionTime;

        public DensityEvaluationResult(NativeArray<float> density, bool isUniformState, double executionTime)
        {
            this.density = density;
            this.isUniformState = isUniformState;
            this.executionTime = executionTime;
        }
    }
}
