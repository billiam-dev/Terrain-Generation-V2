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
            // Ordered in decending order for cache efficiency.

            public readonly AffineTransform inverseMatrix; // 64 bytes
            public readonly float3 dimentions;             // 12 bytes
            public readonly uint functionID;               // 4 bytes
            public readonly float smoothness;              // 4 bytes
            public readonly float minSign;                 // 4 bytes

            public DistanceFunctionData(AffineTransform inverseMatrix, float3 dimentions, uint functionID, float smoothness, bool isSubtractive)
            {
                this.inverseMatrix = inverseMatrix;
                this.dimentions = dimentions;
                this.functionID = functionID;
                this.smoothness = smoothness;
                minSign = isSubtractive ? -1 : 1;
            }
        }

        NativeArray<DistanceFunctionData> m_DistanceFunctions;
        int m_NumShapesAllocated;

        public void Allocate(List<Shape> shapes, Allocator allocator)
        {
            m_NumShapesAllocated = shapes.Count;
            m_DistanceFunctions = new(m_NumShapesAllocated, allocator);

            for (int i = 0; i < m_NumShapesAllocated; i++)
            {
                m_DistanceFunctions[i] = new DistanceFunctionData(
                    shapes[i].inverseMatrix,
                    shapes[i].dimentions,
                    (uint)shapes[i].distanceFunction,
                    shapes[i].smoothness * 6.0f, // Multiply smoothness by 6 as the first step to the cubic polynomial smooth min / max functions.
                    shapes[i].blendMode == BlendMode.Subtractive
                );
            }
        }

        public void Dispose()
        {
            m_DistanceFunctions.Dispose();
        }

        public readonly float Sample(float3 worldPosition, float initialDensity)
        {
            float result = initialDensity;

            // Apply distance functions.
            DistanceFunctionData sdf;
            float3 translatedPosition;
            float distance;

            for (int i = 0; i < m_NumShapesAllocated; i++)
            {
                sdf = m_DistanceFunctions[i];

                // Get a translated position using the shape's inverse matrix (worldToLocal).
                translatedPosition = math.mul(sdf.inverseMatrix.rs, worldPosition) + sdf.inverseMatrix.t;

                // Special case for noise to avoid min/max functions.
                if (sdf.functionID == 6) // TODO: remove noise as shape (as well as surface). Pass noise index, itterate through pre-noise shapes, then post-noise shapes to cut out this if statement.
                {
                    result += Noise(translatedPosition, sdf.dimentions.x, sdf.dimentions.y);
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
                    5 => Surface(translatedPosition, sdf.dimentions.x, sdf.dimentions.y), // TODO: integrate with terrain component, there can only be one surface. Replace initial density w/ this.
                    _ => 0
                };

                // Mix the old and new distance values using smooth min.
                result = SmoothMin(result * sdf.minSign, distance, sdf.smoothness) * sdf.minSign;

                // Note: this can have separate Kernal which avoids the expensive SmoothMin call SampleFast, far off terrain would benefit from this if the visual impact is acceptable.
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
        static float Surface(float3 pos, float frequency, float amplitude)
        {
            float4 noise;
            noise.x = SimplexNoise.Sample3D(frequency * pos) * amplitude;
            noise.y = SimplexNoise.Sample3D(2.0f * frequency * pos) * amplitude * 0.5f;
            noise.z = SimplexNoise.Sample3D(4.0f * frequency * pos) * amplitude * 0.25f;
            noise.w = SimplexNoise.Sample3D(8.0f * frequency * pos) * amplitude * 0.125f;
            return pos.y + math.csum(noise);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Noise(float3 pos, float frequency, float amplitude)
        {
            float3 noise;
            noise.x = SimplexNoise.Sample3D(frequency * pos) * amplitude;
            noise.y = SimplexNoise.Sample3D(2.0f * frequency * pos) * amplitude * 0.5f;
            noise.z = SimplexNoise.Sample3D(4.0f * frequency * pos) * amplitude * 0.25f;
            return math.csum(noise);
        }

        /*
         * Smoothing functions
         *
         * https://iquilezles.org/articles/smin/
         * Note: This is the best method I have found on Inigo Quilez's website for both speed and accuracy.
        */

        const float OneOverSix = 0.16666667f; // Cache the (1 / 6) calculation performed in SmoothMin & SmoothMax; division is slow.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float SmoothMin(float a, float b, float k)
        {
            float h = math.max(k - math.abs(a - b), 0.0f) / k;
            return math.min(a, b) - h * h * h * k * OneOverSix;
        }
    }
}
