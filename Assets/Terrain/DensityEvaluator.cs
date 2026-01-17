using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LevelGeneration.Terrain
{
    public struct DensityEvaluator : IDisposable
    {
        NativeArray<float> workingDensityData;
        NativeReference<bool> positiveValueFound;
        NativeReference<bool> negativeValueFound;
        int cellsPerBrick;

        double executionTime;

        const int k_InnerloopBatchCount = 32;

        public void Allocate(int cellsPerBrick)
        {
            this.cellsPerBrick = cellsPerBrick;
            workingDensityData = new(cellsPerBrick, Allocator.Persistent);
            positiveValueFound = new(Allocator.Persistent);
            negativeValueFound = new(Allocator.Persistent);
        }

        public void Dispose()
        {
            workingDensityData.Dispose();
            positiveValueFound.Dispose();
            negativeValueFound.Dispose();
        }

        public DensityEvaluationResult ExecuteJob(NativeList<Shape> shapes, int3 brickIndex, int brickSize, float terrainScale)
        {
            positiveValueFound.Value = false;
            negativeValueFound.Value = false;

            DensityJob job = new()
            {
                density = workingDensityData,
                shapes = shapes,
                initialValue = 32.0f,
                brickIndex = brickIndex,
                brickSize = brickSize,
                terrainScale = terrainScale,
                positiveValueFound = positiveValueFound,
                negativeValueFound = negativeValueFound
            };

            Stopwatch.Start(ref executionTime);
            job.ScheduleParallel(cellsPerBrick, k_InnerloopBatchCount, default).Complete();
            Stopwatch.End(ref executionTime);

            bool isEmpty = positiveValueFound.Value && !negativeValueFound.Value;
            bool isFull = negativeValueFound.Value && !positiveValueFound.Value;

            return new DensityEvaluationResult(workingDensityData, !isEmpty && !isFull, executionTime);
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct DensityJob : IJobFor
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<float> density;

            [ReadOnly] public NativeList<Shape> shapes;
            [ReadOnly] public float initialValue;
            [ReadOnly] public int3 brickIndex;
            [ReadOnly] public int brickSize;
            [ReadOnly] public float terrainScale;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> positiveValueFound;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> negativeValueFound;

            public void Execute(int index)
            {
                // Reset density to initial value.
                density[index] = initialValue;

                // Derrive 3D position from 1D index.
                int x = index;

                int z = x / (brickSize * brickSize);
                x -= z * brickSize * brickSize;

                int y = x / brickSize;
                x -= y * brickSize;

                // Apply shapes.
                int3 globalCellIndex = (brickIndex * brickSize) + new int3(x, y, z);
                float4 globelCellPos = new((float3)globalCellIndex * terrainScale, 1.0f);
                foreach (Shape shape in shapes)
                {
                    // Compute position translated by inverse shape matrix.
                    float3 translatedPosition = math.mul(shape.InverseMatrix, globelCellPos).xyz;

                    // Compute shape distance value and apply to density field.
                    float distance = 0;
                    switch (shape.DistanceFunction)
                    {
                        case DistanceFunction.Sphere:
                            distance = Sphere(translatedPosition, shape.Dimention1);
                            break;

                        case DistanceFunction.Cube:
                            distance = Cube(translatedPosition, shape.Dimention1, shape.Dimention2, shape.Dimention3);
                            break;
                    }

                    density[index] = math.select(
                        SmoothMin(density[index], distance, shape.Smoothness),
                        SmoothMax(density[index], distance, shape.Smoothness),
                        shape.IsSubtractive
                        );
                }

                // Update positive / negative value flags.
                if (density[index] > 0)
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

            //
            // Shapes - https://iquilezles.org/articles/distfunctions/
            //
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Sphere(float3 centre, float radius)
            {
                return math.length(centre) - radius;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Cube(float3 centre, float width, float height, float depth)
            {
                float3 size = new(width, height, depth);
                float3 q = math.abs(centre) - size;
                return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);
            }

            //
            // Smoothing functions - https://iquilezles.org/articles/smin/
            // Note: This is the best method I have found on Inigo Quilez's website for both speed and accuracy.
            //
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float SmoothMax(float a, float b, float k)
            {
                k *= 6.0f;
                float h = math.max(k - math.abs(-a + b), 0.0f) / k;
                return -(math.min(-a, -b) - h * h * h * k * (1.0f / 6.0f));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float SmoothMin(float a, float b, float k)
            {
                k *= 6.0f;
                float h = math.max(k - math.abs(a - b), 0.0f) / k;
                return math.min(a, b) - h * h * h * k * (1.0f / 6.0f);
            }
        }
    }

    public readonly struct DensityEvaluationResult
    {
        readonly NativeArray<float> density;
        readonly bool intersectsSurface;
        readonly double executionTime;

        public readonly NativeArray<float> Density => density;
        public readonly bool IntersectsSurface => intersectsSurface;
        public readonly double ExecutionTime => executionTime;

        public DensityEvaluationResult(NativeArray<float> density, bool intersectsSurface, double executionTime)
        {
            this.density = density;
            this.intersectsSurface = intersectsSurface;
            this.executionTime = executionTime;
        }
    }
}
