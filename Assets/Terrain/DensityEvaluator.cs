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
        NativeArray<float> m_WorkingDensityData;
        NativeReference<bool> m_PositiveValueFound;
        NativeReference<bool> m_NegativeValueFound;

        int m_DensityDataSize;
        int m_DensityDataPoints;
        double m_ExecutionTime;

        const int k_InnerloopBatchCount = 32;

        public void Allocate(int brickSize)
        {
            m_DensityDataSize = brickSize + 3;
            m_DensityDataPoints = m_DensityDataSize * m_DensityDataSize * m_DensityDataSize;

            m_WorkingDensityData = new(m_DensityDataSize * m_DensityDataSize * m_DensityDataSize, Allocator.Persistent);

            m_PositiveValueFound = new(Allocator.Persistent);
            m_NegativeValueFound = new(Allocator.Persistent);
        }

        public void Dispose()
        {
            m_WorkingDensityData.Dispose();
            m_PositiveValueFound.Dispose();
            m_NegativeValueFound.Dispose();
        }

        public DensityEvaluationResult ExecuteJob(NativeArray<Shape> shapes, int3 brickIndex, int brickSize, float terrainScale, int levelScale)
        {
            m_PositiveValueFound.Value = false;
            m_NegativeValueFound.Value = false;

            DensityJob job = new()
            {
                shapes = shapes,
                initialValue = ProceduralTerrain.EmptyDensityValue,
                brickIndex = brickIndex,
                brickSize = brickSize,
                extendedBrickSize = m_DensityDataSize,
                terrainScale = terrainScale,
                levelScale = levelScale,
                density = m_WorkingDensityData,
                positiveValueFound = m_PositiveValueFound,
                negativeValueFound = m_NegativeValueFound
            };

            Stopwatch.Start(ref m_ExecutionTime);
            job.ScheduleParallel(m_DensityDataPoints, k_InnerloopBatchCount, default).Complete();
            Stopwatch.End(ref m_ExecutionTime);

            bool isEmpty = m_PositiveValueFound.Value && !m_NegativeValueFound.Value;
            bool isFull = m_NegativeValueFound.Value && !m_PositiveValueFound.Value;

            return new DensityEvaluationResult(m_WorkingDensityData, isEmpty, isFull, m_ExecutionTime);
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct DensityJob : IJobFor
        {
            [ReadOnly] public NativeArray<Shape> shapes;
            [ReadOnly] public float initialValue;
            [ReadOnly] public int3 brickIndex;
            [ReadOnly] public int brickSize;
            [ReadOnly] public int extendedBrickSize;
            [ReadOnly] public float terrainScale;
            [ReadOnly] public int levelScale;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<float> density;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> positiveValueFound;

            [NativeDisableParallelForRestriction]
            public NativeReference<bool> negativeValueFound;

            public void Execute(int index)
            {
                float newDensity = initialValue;

                // Unwrap the iteration index into a 3D index using the extended brick size.
                int x = index;

                int z = x / (extendedBrickSize * extendedBrickSize);
                x -= z * extendedBrickSize * extendedBrickSize;

                int y = x / extendedBrickSize;
                x -= y * extendedBrickSize;

                int3 itterationIndex = new(x, y, z);

                // Derrive world position from iteration index.
                float3 worldPosition = levelScale * terrainScale * (float3)((brickIndex * brickSize) + (itterationIndex - 1));

                // Apply shapes.
                float3 translatedPosition;
                float distance;

                foreach (Shape shape in shapes)
                {
                    // Get a translated position using the shape's inverse matrix (worldToLocal).
                    translatedPosition = FastMul(shape.inverseMatrix, worldPosition);

                    // Calculate the distance value using the SDF function of the current shape.
                    distance = shape.distanceFunction switch
                    {
                        DistanceFunction.Sphere => Sphere(translatedPosition, shape.dimention1),
                        DistanceFunction.SemiSphere => SemiSphere(translatedPosition, shape.dimention1, shape.dimention2),
                        DistanceFunction.Capsule => Capsule(translatedPosition, shape.dimention1, shape.dimention2),
                        DistanceFunction.Torus => Torus(translatedPosition, shape.dimention1, shape.dimention2),
                        DistanceFunction.Cube => Cube(translatedPosition, shape.dimention1, shape.dimention2, shape.dimention3),
                        _ => 0,
                    };

                    // Mix the old and new distance values using a smoothing function.
                    newDensity = math.select(
                        SmoothMin(newDensity, distance, shape.smoothnessConstant),
                        SmoothMax(newDensity, distance, shape.smoothnessConstant),
                        shape.blendMode == BlendMode.Subtractive
                        );
                }

                // If the cell is in bounds of the density array, update the density array.
                density[index] = newDensity;

                // Update positive / negative value flags, both in bounds and out of bounds values are considered in this check.
                if (math.all(itterationIndex > 0) && math.all(itterationIndex <= brickSize + 1))
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

            const float OneOverSix = 0.16666666666666666666666666666667f; // Cache the (1 / 6) calculation performed in SmoothMin & SmoothMax; division is slow.

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float SmoothMax(float a, float b, float k)
            {
                float h = math.max(k - math.abs(-a + b), 0.0f) / k;
                return -(math.min(-a, -b) - h * h * h * k * OneOverSix);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float SmoothMin(float a, float b, float k)
            {
                float h = math.max(k - math.abs(a - b), 0.0f) / k;
                return math.min(a, b) - h * h * h * k * OneOverSix;
            }

            /*
             * Fast multiply function, same as math.mul for AffineTransforms but without the w component.
            */

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float3 FastMul(AffineTransform a, float3 pos)
            {
                return math.mul(a.rs, pos) + a.t;
            }
        }
    }

    public readonly struct DensityEvaluationResult
    {
        enum State
        {
            Empty = 0,
            Full = 1,
            Partial = 2,
            Error = 3
        }

        readonly NativeArray<float> density;
        readonly State state;
        readonly double executionTime;

        public readonly NativeArray<float> Density => density;
        public readonly int DensityState => (int)state;
        public readonly double ExecutionTime => executionTime;

        public DensityEvaluationResult(NativeArray<float> density, bool isEmpty, bool isFull, double executionTime)
        {
            this.density = density;

            if (!isEmpty && !isFull)
                state = State.Partial;
            else if (isEmpty)
                state = State.Empty;
            else if (isFull)
                state = State.Full;
            else
                state = State.Error;

            this.executionTime = executionTime;
        }
    }
}
