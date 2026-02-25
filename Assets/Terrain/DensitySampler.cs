using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

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

        NoiseSettings m_SurfaceSettings;
        NoiseSettings m_GlobalNoiseSettings;

        NativeArray<DistanceFunctionData> m_DistanceFunctions;
        int m_NumShapesAllocated;

        bool m_IsAllocated;

        public readonly bool IsAllocated => m_IsAllocated;

        public void Allocate(List<Shape> shapes, NoiseSettings surface, NoiseSettings globalNoise, Allocator allocator)
        {
            if (m_IsAllocated)
            {
                Debug.LogWarning("Could not allocate density sampler, already allocated!");
                return;
            }

            m_NumShapesAllocated = shapes.Count;
            m_DistanceFunctions = new(m_NumShapesAllocated, allocator);

            m_SurfaceSettings = surface;
            m_GlobalNoiseSettings = globalNoise;

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

            m_IsAllocated = true;
        }

        public void Dispose()
        {
            if (!m_IsAllocated)
            {
                Debug.LogWarning("Could not dispose density sampler, already disposed!");
                return;
            }

            m_DistanceFunctions.Dispose();
            m_IsAllocated = false;
        }

        /* 
         * Note that this function is hyper-optimized for it's in-game use case, that is with a terrain surface and single noise layer.
         * The surface and noise components have been removed as Shape Brushes to avoid having to add extra cases for them in the main loop.
         * -> Burst should be optimized for branchless execution.
        */

        public readonly float Sample(float3 worldPosition)
        {
            float result = Surface(worldPosition + m_SurfaceSettings.offset, m_SurfaceSettings.frequency, m_SurfaceSettings.amplitude);

            DistanceFunctionData sdf;
            float3 translatedPosition;
            float distance;

            // Apply pre-noise shapes.
            for (int i = 0; i < m_NumShapesAllocated; i++)
            {
                sdf = m_DistanceFunctions[i];

                // Get a translated position using the shape's inverse matrix (worldToLocal).
                translatedPosition = math.mul(sdf.inverseMatrix.rs, worldPosition) + sdf.inverseMatrix.t;

                // Calculate the distance value using the SDF function of the current shape.
                distance = sdf.functionID switch
                {
                    0 => Sphere(translatedPosition, sdf.dimentions.x),
                    1 => SemiSphere(translatedPosition, sdf.dimentions.x, sdf.dimentions.y),
                    2 => Capsule(translatedPosition, sdf.dimentions.x, sdf.dimentions.y),
                    3 => Torus(translatedPosition, sdf.dimentions.x, sdf.dimentions.y),
                    4 => Cube(translatedPosition, sdf.dimentions.x, sdf.dimentions.y, sdf.dimentions.z),
                    _ => 0
                };

                // Mix the old and new distance values using smooth min.
                result = SmoothMin(result * sdf.minSign, distance, sdf.smoothness) * sdf.minSign;
            }

            // Apply noise.
            result += Noise(worldPosition + m_GlobalNoiseSettings.offset, m_GlobalNoiseSettings.frequency, m_GlobalNoiseSettings.amplitude);

            // Apply post-noise shapes (TODO).
            
            // Note: in this section we ditch the smooth-min for maximum speed.
            // The user can apply many more edits than the world generator, which focuses on minimal large shapes.
            // See constructive solid geometry.

            // Note: for maximum speed, I could introduce a caching system which saves the initial state of the world to avoid the previous step.
            // This cuts out the expensive smooth min and noise steps, which still allowing for dynamic user shapes.

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
            float2 samplePos = pos.xz;

            float4 noise;
            noise.x = SimplexNoise.Sample2D(frequency * samplePos) * amplitude;
            noise.y = SimplexNoise.Sample2D(1.7634f * frequency * (samplePos + 4099.0f)) * amplitude * 0.5f;
            noise.z = SimplexNoise.Sample2D(5.2453f * frequency * (samplePos + 5851.0f)) * amplitude * 0.25f;
            noise.w = SimplexNoise.Sample2D(7.6346f * frequency * (samplePos + 7549.0f)) * amplitude * 0.125f;
            return pos.y + math.csum(noise);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Noise(float3 pos, float frequency, float amplitude)
        {
            float3 noise;
            noise.x = SimplexNoise.Sample3D(frequency * pos) * amplitude;
            noise.y = SimplexNoise.Sample3D(2.2251f * frequency * (pos + 2521.0f)) * amplitude * 0.5f;
            noise.z = SimplexNoise.Sample3D(3.5362f * frequency * (pos + 3673.0f)) * amplitude * 0.25f;
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
