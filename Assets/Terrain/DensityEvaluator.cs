using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace LevelGeneration.Terrain
{
    public class DensityEvaluator : IDisposable
    {
        NativeArray<float> m_DensityData;
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

            m_DensityData = new(m_DensityDataSize * m_DensityDataSize * m_DensityDataSize, Allocator.Persistent);

            m_PositiveValueFound = new(Allocator.Persistent);
            m_NegativeValueFound = new(Allocator.Persistent);
        }

        public void Dispose()
        {
            m_DensityData.Dispose();
            m_PositiveValueFound.Dispose();
            m_NegativeValueFound.Dispose();
        }

        public DensityEvaluationResult Execute(List<Shape> shapes, int3 brickIndex, int brickSize, int levelScale, float worldScale)
        {
            int numShapes = shapes.Count;
            NativeArray<SDF> distanceFunctions = new(numShapes, Allocator.TempJob);

            for (int i = 0; i < numShapes; i++)
            {
                distanceFunctions[i] = new SDF(
                    shapes[i].inverseMatrix,
                    (uint)shapes[i].distanceFunction,
                    shapes[i].blendMode == BlendMode.Subtractive,
                    shapes[i].smoothness * 6.0f, // Multiply smoothness by 6 as the first step to the cubic polynomial smooth min / max functions.
                    new float3(shapes[i].dimention1, shapes[i].dimention2, shapes[i].dimention3)
                );
            }

            m_PositiveValueFound.Value = false;
            m_NegativeValueFound.Value = false;

            DensityJob job = new()
            {
                distanceFunctions = distanceFunctions,
                initialValue = ProceduralTerrain.EmptyDensityValue,
                brickIndex = brickIndex,
                brickSize = brickSize,
                extendedBrickSize = m_DensityDataSize,
                levelScale = levelScale,
                worldScale = worldScale,
                density = m_DensityData,
                positiveValueFound = m_PositiveValueFound,
                negativeValueFound = m_NegativeValueFound
            };

            Stopwatch.Start(ref m_ExecutionTime);
            job.ScheduleParallel(m_DensityDataPoints, k_InnerloopBatchCount, default).Complete();
            Stopwatch.End(ref m_ExecutionTime);

            distanceFunctions.Dispose();

            bool isEmpty = m_PositiveValueFound.Value && !m_NegativeValueFound.Value;
            bool isFull = m_NegativeValueFound.Value && !m_PositiveValueFound.Value;

            return new DensityEvaluationResult(m_DensityData, isEmpty || isFull, m_ExecutionTime);
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct DensityJob : IJobFor
        {
            [ReadOnly] public NativeArray<SDF> distanceFunctions;
            [ReadOnly] public float initialValue;
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
                int x = index;

                int z = x / (extendedBrickSize * extendedBrickSize);
                x -= z * extendedBrickSize * extendedBrickSize;

                int y = x / extendedBrickSize;
                x -= y * extendedBrickSize;

                int3 coord = new(x, y, z);

                // Derrive world position from iteration index.
                float3 worldPosition = levelScale * worldScale * (float3)((brickIndex * brickSize) + (coord - 1));

                // Apply distance functions.
                float newDensity = initialValue;
                
                float3 translatedPosition;
                float distance;

                foreach (SDF sdf in distanceFunctions)
                {
                    // Get a translated position using the shape's inverse matrix (worldToLocal).
                    translatedPosition = math.mul(sdf.inverseMatrix.rs, worldPosition) + sdf.inverseMatrix.t;

                    // Special case for noise.
                    if (sdf.functionID == 6)
                    {
                        newDensity += Noise(translatedPosition, sdf.dimentions.x, sdf.dimentions.y);
                        continue;
                    }

                    // Calculate the distance value using the SDF function of the current shape.
                    distance = sdf.functionID switch
                    {
                        0 => Sphere(translatedPosition, sdf.dimentions.x),
                        1 => SemiSphere(translatedPosition, sdf.dimentions.x, sdf.dimentions.y),
                        2 => Capsule(translatedPosition, sdf.dimentions.x, sdf.dimentions.y),
                        3 => Torus(translatedPosition, sdf.dimentions.x, sdf.dimentions.y),
                        4 => Cube(translatedPosition, sdf.dimentions.x, sdf.dimentions.y, sdf.dimentions.z),
                        5 => Surface(translatedPosition),
                        _ => 0
                    };

                    // Mix the old and new distance values using a smoothing function.
                    // Optional optimization here; only use expensive smooth min/max functions for the highest LOD. TODO: Convert into separate kernals to avoid branch.
                    if (levelScale > 1)
                    {
                        newDensity = math.select(
                            math.min(newDensity, distance),
                            -math.min(-newDensity, distance),
                            sdf.isSubtractive
                            );
                    }
                    else
                    {
                        newDensity = math.select(
                            SmoothMin(newDensity, distance, sdf.smoothness),
                            SmoothMax(newDensity, distance, sdf.smoothness),
                            sdf.isSubtractive
                            );
                    }
                }

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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Surface(float3 pos)
            {
                return pos.y;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Noise(float3 pos, float frequency, float amplitude)
            {
                float n = 0.0f;
                for (int i = 0; i < 3; i++)
                {
                    n += SimplexNoise.snoise(pos * frequency) * amplitude;
                    frequency *= 0.5f;
                    amplitude *= 0.5f;
                }

                return n;
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
                return -(math.min(-a, b) - h * h * h * k * OneOverSix);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float SmoothMin(float a, float b, float k)
            {
                float h = math.max(k - math.abs(a - b), 0.0f) / k;
                return math.min(a, b) - h * h * h * k * OneOverSix;
            }
        }

        readonly struct SDF
        {
            public readonly AffineTransform inverseMatrix;
            public readonly uint functionID;
            public readonly bool isSubtractive;
            public readonly float smoothness;
            public readonly float3 dimentions;

            public SDF(AffineTransform inverseMatrix, uint functionID, bool isSubtractive, float smoothness, float3 dimentions)
            {
                this.inverseMatrix = inverseMatrix;
                this.functionID = functionID;
                this.isSubtractive = isSubtractive;
                this.smoothness = smoothness;
                this.dimentions = dimentions;
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
