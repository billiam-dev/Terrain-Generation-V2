using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace LevelGeneration.Terrain
{
    public struct DensitySampler : IDisposable
    {
        readonly struct DistanceFunctionData
        {
            public readonly AffineTransform inverseMatrix;
            public readonly byte functionID;
            public readonly bool isSubtractive;
            public readonly float smoothness;
            public readonly float3 dimentions;

            public DistanceFunctionData(AffineTransform inverseMatrix, byte functionID, bool isSubtractive, float smoothness, float3 dimentions)
            {
                this.inverseMatrix = inverseMatrix;
                this.functionID = functionID;
                this.isSubtractive = isSubtractive;
                this.smoothness = smoothness;
                this.dimentions = dimentions;
            }
        }

        NativeArray<DistanceFunctionData> m_DistanceFunctions;

        public void Allocate(List<Shape> shapes, Allocator allocator)
        {
            int numShapes = shapes.Count;
            m_DistanceFunctions = new(numShapes, allocator);

            for (int i = 0; i < numShapes; i++)
            {
                m_DistanceFunctions[i] = new DistanceFunctionData(
                    shapes[i].inverseMatrix,
                    (byte)shapes[i].distanceFunction,
                    shapes[i].blendMode == BlendMode.Subtractive,
                    shapes[i].smoothness * 6.0f, // Multiply smoothness by 6 as the first step to the cubic polynomial smooth min / max functions.
                    new float3(shapes[i].dimention1, shapes[i].dimention2, shapes[i].dimention3)
                );
            }
        }

        public void Dispose()
        {
            m_DistanceFunctions.Dispose();
        }

        public readonly float Sample(float3 worldPosition, float initialDensity, bool fast)
        {
            float result = initialDensity;

            // Apply distance functions.
            float3 translatedPosition;
            float distance;

            for (int i = 0; i < m_DistanceFunctions.Length; i++)
            {
                DistanceFunctionData sdf = m_DistanceFunctions[i];

                // Get a translated position using the shape's inverse matrix (worldToLocal).
                translatedPosition = math.mul(sdf.inverseMatrix.rs, worldPosition) + sdf.inverseMatrix.t;

                // Special case for noise to avoid min/max functions.
                if (sdf.functionID == 6)
                {
                    result += Noise(translatedPosition, sdf.dimentions.x, sdf.dimentions.y, 3);
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
                    5 => Surface(translatedPosition, sdf.dimentions.x, sdf.dimentions.y, 5), // TODO: integrate with terrain component, there can only be one surface. Replace initial density w/ this.
                    _ => 0
                };

                // Mix the old and new distance values using a smoothing function.
                // Optional optimization here; only use expensive smooth min/max functions for the highest LOD.
                
                // TODO: Convert into separate kernals to avoid branch!!
                
                result = math.select(
                    math.select(
                        SmoothMin(result, distance, sdf.smoothness),
                        SmoothMax(result, distance, sdf.smoothness),
                        sdf.isSubtractive),
                    math.select(
                        math.min(result, distance),
                        -math.min(-result, distance),
                        sdf.isSubtractive),
                    fast
                );
            }

            return result;
        }

        /*
         * Signed Distance Functions (Shapes)
         *
         * Note: Spheres are the easiest shape to calculate and will create the most optimised result.
         * https://iquilezles.org/articles/distfunctions/
        */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Sphere(float3 centre, float radius)
        {
            return math.length(centre) - radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float SemiSphere(float3 centre, float radius, float h)
        {
            float w = math.sqrt(radius * radius - h * h);

            float2 q = new(math.length(centre.xz), centre.y);
            float s = math.max((h - radius) * q.x * q.x + w * w * (h + radius - 2.0f * q.y), h * q.x - w * q.y);
            return (s < 0.0) ? math.length(q) - radius :
                   (q.x < w) ? h - q.y :
                   math.length(q - new float2(w, h));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Capsule(float3 centre, float height, float radius)
        {
            float3 dir = new(0, height, 0);

            float3 pa = centre - dir;
            float3 ba = -dir - dir;
            float h = math.clamp(math.dot(pa, ba) / math.dot(ba, ba), 0.0f, 1.0f);
            return math.length(pa - ba * h) - radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Torus(float3 centre, float outerRadius, float innerRadius)
        {
            float2 q = new(math.length(centre.xz) - outerRadius, centre.y);
            return math.length(q) - innerRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Cube(float3 centre, float width, float height, float depth)
        {
            float3 q = math.abs(centre) - new float3(width, height, depth);
            return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Surface(float3 pos, float frequency, float amplitude, int octaves)
        {
            return pos.y + Noise(new float3(pos.x, 0, pos.z), frequency, amplitude, octaves);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Noise(float3 pos, float frequency, float amplitude, int octaves)
        {
            float value = 0.0f;
            for (int i = 0; i < octaves; i++)
            {
                value += SimplexNoise.Sample(pos * frequency) * amplitude;
                frequency *= 2.0f;
                amplitude *= 0.5f;
            }

            return value;
        }

        /*
         * Smoothing functions
         *
         * https://iquilezles.org/articles/smin/
         * Note: This is the best method I have found on Inigo Quilez's website for both speed and accuracy.
        */

        const float OneOverSix = 0.16666666666666666666666666666667f; // Cache the (1 / 6) calculation performed in SmoothMin & SmoothMax; division is slow.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float SmoothMax(float a, float b, float k)
        {
            float h = math.max(k - math.abs(-a + b), 0.0f) / k;
            return -(math.min(-a, b) - h * h * h * k * OneOverSix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float SmoothMin(float a, float b, float k)
        {
            float h = math.max(k - math.abs(a - b), 0.0f) / k;
            return math.min(a, b) - h * h * h * k * OneOverSix;
        }
    }
}
