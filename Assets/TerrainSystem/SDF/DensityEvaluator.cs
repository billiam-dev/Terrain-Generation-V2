using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace TerrainSystem.SDF
{
    public class DensityEvaluator : IDisposable
    {
        NativeArray<float> m_DensityData;
        int m_DensityArraySize;

        NativeArray<float> m_TransitionDensityData;
        int m_TransitionDensityArraySize;

        NativeReference<bool> m_PositiveValueFound;
        NativeReference<bool> m_NegativeValueFound;

        const int k_InnerloopBatchCount = 32;

        // Debug info
        MeanTime m_AvgExecutionTime;
        double m_ExecutionTime;

        public double TotalExecutionTime => m_ExecutionTime;
        public double AvgExecutionTime => m_AvgExecutionTime.Avarage();

        public void Allocate(int brickSize)
        {
            int extendedBrickSize = brickSize + 3;

            m_DensityArraySize = extendedBrickSize * extendedBrickSize * extendedBrickSize;
            m_DensityData = new(m_DensityArraySize, Allocator.Persistent);

            int transitionSize = (extendedBrickSize * 2) - 1;
            m_TransitionDensityArraySize = transitionSize * transitionSize * 3;
            m_TransitionDensityData = new(m_TransitionDensityArraySize, Allocator.Persistent);

            m_PositiveValueFound = new(Allocator.Persistent);
            m_NegativeValueFound = new(Allocator.Persistent);

            m_AvgExecutionTime = new();
        }

        public void Dispose()
        {
            m_DensityData.Dispose();
            m_TransitionDensityData.Dispose();

            m_PositiveValueFound.Dispose();
            m_NegativeValueFound.Dispose();

            m_AvgExecutionTime = null;
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
            m_AvgExecutionTime.AddTime(m_ExecutionTime);

            bool isEmpty = m_PositiveValueFound.Value && !m_NegativeValueFound.Value;
            bool isFull = m_NegativeValueFound.Value && !m_PositiveValueFound.Value;

            return new DensityEvaluationResult(m_DensityData, isEmpty || isFull);
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
                doubleBrickSize = brickSize * 2,
                density = m_TransitionDensityData
            };

            Stopwatch.Start(ref m_ExecutionTime);

            job.ScheduleParallel(m_TransitionDensityArraySize, k_InnerloopBatchCount, default).Complete();
            
            Stopwatch.End(ref m_ExecutionTime);
            m_AvgExecutionTime.AddTime(m_ExecutionTime);

            return new DensityEvaluationResult(m_TransitionDensityData, false);
        }

        // Note: [FloatMode = FloatMode.Fast] is potentially sketchy for these Jobs.

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

                // Subtract one from all axis to offset sample position 1 cell into the bricks on the negative axis.
                // This is so we can compute accurate normal vectors in the meshing state.
                x -= 1;
                y -= 1;
                z -= 1;

                int3 localCellIndex = new(x, y, z);

                // Derrive world position from iteration index.
                int3 globalCellIndex = ((brickIndex * brickSize) + localCellIndex) * levelScale;
                float3 worldPosition = (float3)globalCellIndex * worldScale;

                // Sample the SDF.
                float newDensity = densitySampler.SampleWithIndices(worldPosition);

                // Store the new density.
                density[index] = newDensity;

                // Update positive / negative value flags.
                if (math.all(localCellIndex >= 0) && math.all(localCellIndex <= brickSize + 1))
                {
                    if (newDensity > 0.0f)
                        positiveValueFound.Value = true;
                    else
                        negativeValueFound.Value = true;
                }
            }
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct TransitionDensityJob : IJobFor
        {
            [ReadOnly] public DensitySampler densitySampler;
            [ReadOnly] public int3 brickIndex;
            [ReadOnly] public int brickSize;
            [ReadOnly] public int pointsPerAxis;
            [ReadOnly] public float worldScale;
            [ReadOnly] public int levelScale;
            [ReadOnly] public int transitionIndex;
            [ReadOnly] public int doubleBrickSize;

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

                // Subtract a full cell from the x and y positions, and a half cell from the z position.
                // The x and y axis need to be alligned with the core mesh, and the z is the standard -1 for normal vectors.
                x -= 2;
                y -= 2;
                z -= 1;

                // Re-align the z offsets with the larger grid to mirror the LOD of the core normals.
                z *= 2;

                // Derrive cell index from face index.
                int3 localCellIndex = FaceToCellIndex(x, y, z);

                // Coord range is double-precision compared to core evaluation jobs for this level.
                // Therefore we multiply the rest of the calculation by 2 to match.
                int3 globalCellIndex = ((brickIndex * brickSize * 2) + localCellIndex) * (levelScale / 2);
                float3 worldPosition = (float3)globalCellIndex * worldScale;

                // Store the new density.
                density[index] = densitySampler.SampleWithIndices(worldPosition);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly int3 FaceToCellIndex(int x, int y, int z)
            {
                return transitionIndex switch
                {
                    0 => new(doubleBrickSize - z, y, x), //  x
                    1 => new(z, x, y),                   // -x
                    2 => new(x, doubleBrickSize - z, y), //  y
                    3 => new(y, z, x),                   // -y
                    4 => new(y, x, doubleBrickSize - z), //  z
                    5 => new(x, y, z),                   // -z
                    _ => new(0, 0, 0)
                };
            }
        }
    }

    public readonly struct DensityEvaluationResult
    {
        public readonly NativeArray<float> density;
        public readonly bool isUniformState;

        public DensityEvaluationResult(NativeArray<float> density, bool isUniformState)
        {
            this.density = density;
            this.isUniformState = isUniformState;
        }
    }
}
