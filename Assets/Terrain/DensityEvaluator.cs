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

        public DensityEvaluationResult ExecuteJob(NativeList<Shape> shapes, int3 brickIndex, int brickSize, int brickMapLevel, float terrainScale)
        {
            positiveValueFound.Value = false;
            negativeValueFound.Value = false;

            DensityJob job = new()
            {
                shapes = shapes,
                initialValue = 32.0f,
                brickIndex = brickIndex,
                brickSize = brickSize,
                stepSize = (int)math.pow(2, brickMapLevel),
                terrainScale = terrainScale,
                density = workingDensityData,
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
            [ReadOnly] public NativeList<Shape> shapes;
            [ReadOnly] public float initialValue;
            [ReadOnly] public int3 brickIndex;
            [ReadOnly] public int brickSize;
            [ReadOnly] public int stepSize;
            [ReadOnly] public float terrainScale;

            [NativeDisableParallelForRestriction]
            public NativeArray<float> density;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> positiveValueFound;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> negativeValueFound;

            public void Execute(int index)
            {
                // Reset density to initial value.
                density[index] = initialValue;

                // Derrive world position from iteration index.
                int x = index;

                int z = x / (brickSize * brickSize);
                x -= z * brickSize * brickSize;

                int y = x / brickSize;
                x -= y * brickSize;

                int3 globalCellIndex = (brickIndex * brickSize) + (new int3(x, y, z) * stepSize);
                float3 worldPosition = (float3)globalCellIndex * terrainScale;

                // Apply shapes.
                foreach (Shape shape in shapes)
                {
                    // Get a translated position using the shape's inverse matrix (worldToLocal).
                    float3 translatedPosition = FastMul(shape.InverseMatrix, worldPosition);

                    // Calculate the distance value using the SDF function of the current shape.
                    float distance = shape.DistanceFunction switch
                    {
                        DistanceFunction.Sphere => Sphere(translatedPosition, shape.Dimention1),
                        DistanceFunction.SemiSphere => SemiSphere(translatedPosition, shape.Dimention1, shape.Dimention2),
                        DistanceFunction.Capsule => Capsule(translatedPosition, shape.Dimention1, shape.Dimention2),
                        DistanceFunction.Torus => Torus(translatedPosition, shape.Dimention1, shape.Dimention2),
                        DistanceFunction.Cube => Cube(translatedPosition, shape.Dimention1, shape.Dimention2, shape.Dimention3),
                        _ => 0,
                    };

                    // Mix the old and new distance values using a smoothing function.
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

            /*
             * Signed Distance Functions (Shapes)
             *
             * Note: Spheres are the easiest shape to calculate and will create the most optimised result.
             * https://iquilezles.org/articles/distfunctions/
            */

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Sphere(float3 centre, float radius)
            {
                return math.length(centre) - radius;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float SemiSphere(float3 centre, float radius, float h)
            {
                float w = math.sqrt(radius * radius - h * h);

                float2 q = new(math.length(centre.xz), centre.y);
                float s = math.max((h - radius) * q.x * q.x + w * w * (h + radius - 2.0f * q.y), h * q.x - w * q.y);
                return (s < 0.0) ? math.length(q) - radius :
                       (q.x < w) ? h - q.y :
                       math.length(q - new float2(w, h));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Capsule(float3 centre, float height, float radius)
            {
                float3 dir = new(0, height, 0);

                float3 pa = centre - dir;
                float3 ba = -dir - dir;
                float h = math.clamp(math.dot(pa, ba) / math.dot(ba, ba), 0.0f, 1.0f);
                return math.length(pa - ba * h) - radius;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Torus(float3 centre, float outerRadius, float innerRadius)
            {
                float2 q = new(math.length(centre.xz) - outerRadius, centre.y);
                return math.length(q) - innerRadius;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Cube(float3 centre, float width, float height, float depth)
            {
                float3 q = math.abs(centre) - new float3(width, height, depth);
                return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);
            }

            /*
             * Smoothing functions
             *
             * https://iquilezles.org/articles/smin/
             * Note: This is the best method I have found on Inigo Quilez's website for both speed and accuracy.
            */

            // TODO: Multiply the smoothness by 6 outside of the loop!

            const float OneOverSix = 0.16666666666666666666666666666667f; // Cache the (1 / 6) calculation performed in SmoothMin & SmoothMax; division is slow.

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float SmoothMax(float a, float b, float k)
            {
                k *= 6.0f;
                float h = math.max(k - math.abs(-a + b), 0.0f) / k;
                return -(math.min(-a, -b) - h * h * h * k * OneOverSix);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float SmoothMin(float a, float b, float k)
            {
                k *= 6.0f;
                float h = math.max(k - math.abs(a - b), 0.0f) / k;
                return math.min(a, b) - h * h * h * k * OneOverSix;
            }

            /*
             * Fast multiply function, same as math.mul for AffineTransforms but without the w component.
            */

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float3 FastMul(AffineTransform a, float3 pos)
            {
                return math.mul(a.rs, pos.xyz) + a.t;
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
