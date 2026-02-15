using System;
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
            m_TransitionDensityArraySize = brickSize * brickSize * 6;

            m_DensityData = new(m_DensityArraySize, Allocator.Persistent);
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

        public DensityEvaluationResult ComputeBrick(DensitySampler sampler, int3 brickIndex, int brickSize, int levelScale, float worldScale)
        {
            m_PositiveValueFound.Value = false;
            m_NegativeValueFound.Value = false;

            DensityJob job = new()
            {
                densitySampler = sampler,
                initialDensity = ProceduralTerrain.EmptyDensityValue,
                brickIndex = brickIndex,
                brickSize = brickSize,
                extendedBrickSize = brickSize + 3,
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

        public DensityEvaluationResult ComputeBrickTransitions(DensitySampler sampler, int3 brickIndex, int brickSize, int levelScale, float worldScale)
        {
            TransitionDensityJob job = new()
            {
                densitySampler = sampler,
                initialDensity = ProceduralTerrain.EmptyDensityValue,
                brickIndex = brickIndex,
                brickSize = brickSize,
                levelScale = levelScale,
                worldScale = worldScale,
                density = m_DensityData
            };

            Stopwatch.Start(ref m_ExecutionTime);
            job.ScheduleParallel(m_TransitionDensityArraySize, k_InnerloopBatchCount, default).Complete();
            Stopwatch.End(ref m_ExecutionTime);

            return new DensityEvaluationResult(m_TransitionDensityData, false, m_ExecutionTime);
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct DensityJob : IJobFor
        {
            [ReadOnly] public DensitySampler densitySampler;
            [ReadOnly] public float initialDensity;
            [ReadOnly] public int3 brickIndex;
            [ReadOnly] public int brickSize;
            [ReadOnly] public int extendedBrickSize;
            [ReadOnly] public float worldScale;
            [ReadOnly] public int levelScale;

            [WriteOnly]
            public NativeArray<float> density;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> positiveValueFound;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> negativeValueFound;

            public void Execute(int index)
            {
                // Unwrap the iteration index into a 3D index using the extended brick size.
                int3 coord = 0;

                coord.x = index;

                coord.z = coord.x / (extendedBrickSize * extendedBrickSize);
                coord.x -= coord.z * extendedBrickSize * extendedBrickSize;

                coord.y = coord.x / extendedBrickSize;
                coord.x -= coord.y * extendedBrickSize;

                // Derrive world position from iteration index.
                float3 worldPosition = levelScale * worldScale * (float3)((brickIndex * brickSize) + (coord - 1));

                // Sample the SDF.
                float newDensity = densitySampler.Sample(worldPosition, initialDensity, levelScale > 1);

                // Store the new density.
                density[index] = newDensity;

                // Update positive / negative value flags.
                if (math.all(coord > 0) && math.all(coord <= brickSize + 2))
                {
                    if (newDensity > 0)
                    {
                        if (positiveValueFound.Value == false)
                            positiveValueFound.Value = true;
                    }
                    else
                    {
                        if (negativeValueFound.Value == false)
                            negativeValueFound.Value = true;
                    }
                }
            }
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct TransitionDensityJob : IJobFor
        {
            [ReadOnly] public DensitySampler densitySampler;
            [ReadOnly] public float initialDensity;
            [ReadOnly] public int3 brickIndex;
            [ReadOnly] public int brickSize;
            [ReadOnly] public float worldScale;
            [ReadOnly] public int levelScale;

            [WriteOnly]
            public NativeArray<float> density; // Size: (brickSize + 3 - 1) * 6

            public void Execute(int index)
            {
                int3 coord = 0;

                coord.x = index;

                coord.z = coord.x / (brickSize * brickSize);
                coord.x -= coord.z * brickSize * brickSize;

                coord.y = coord.x / brickSize;
                coord.x -= coord.y * brickSize;

                // Order: (x, -x, y, -y, z, -z)
                int transitionIndex = coord.z;
                coord = transitionIndex switch
                {
                    0 => new(brickSize - 1, coord.x, coord.y),
                    1 => new(0, coord.x, coord.y),
                    2 => new(coord.x, brickSize - 1, coord.y),
                    3 => new(coord.x, 0, coord.y),
                    4 => new(coord.x, coord.y, brickSize - 1),
                    5 => new(coord.x, coord.y, 0),
                    _ => new(0, 0, 0)
                };

                // Derrive world position from iteration index.
                float3 worldPosition = levelScale * worldScale * (float3)((brickIndex * brickSize) + (coord - 1));

                // Store the new density.
                density[index] = densitySampler.Sample(worldPosition, initialDensity, levelScale > 1);
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
