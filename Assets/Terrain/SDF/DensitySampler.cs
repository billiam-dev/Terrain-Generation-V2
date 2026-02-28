using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using LevelGeneration.Terrain.Scene;

namespace LevelGeneration.Terrain.SDF
{
    public struct DensitySampler : IDisposable
    {
        /*
         * Internal SDF data structs.
         * Non-nullable equivalents to SDF scene density effectors.
        */
        readonly struct DistanceFunctionData
        {
            public readonly AffineTransform inverseMatrix; // 64 bytes
            public readonly float3 dimentions;             // 12 bytes
            public readonly uint functionID;               // 4 bytes
            public readonly float minSign;                 // 4 bytes

            public DistanceFunctionData(AffineTransform inverseMatrix, float3 dimentions, uint functionID, bool isSubtractive)
            {
                this.inverseMatrix = inverseMatrix;
                this.dimentions = dimentions;
                this.functionID = functionID;
                minSign = isSubtractive ? -1 : 1;
            }
        }

        readonly struct NoiseData
        {
            public readonly float3 offset;   // 12 bytes
            public readonly float amplitude; // 4 byes
            public readonly float frequency; // 4 bytes
            public readonly int seed;        // 4 bytes

            public NoiseData(float3 offset, float amplitude, float frequency, int seed)
            {
                this.offset = offset;
                this.amplitude = amplitude;
                this.frequency = frequency;
                this.seed = seed;
            }
        }

        NativeArray<DistanceFunctionData> m_DistanceFunctions;
        int m_NumDistanceFunctions;

        NoiseData m_SurfaceNoise;
        NoiseData m_GlobalNoise;

        bool m_IsAllocated;

        public readonly bool IsAllocated => m_IsAllocated;

        public void Allocate(Shape[] terrainShapes, NoiseLayer surfaceNoise, NoiseLayer globalNoise, Allocator allocator)
        {
            if (m_IsAllocated)
            {
                Debug.LogWarning("Could not allocate density sampler, already allocated!");
                return;
            }

            // Terrain shapes.
            m_NumDistanceFunctions = terrainShapes.Length;
            m_DistanceFunctions = new(m_NumDistanceFunctions, allocator);

            for (int i = 0; i < m_NumDistanceFunctions; i++)
            {
                m_DistanceFunctions[i] = new DistanceFunctionData(
                    terrainShapes[i].InverseMatrix,
                    terrainShapes[i].Dimentions,
                    (uint)terrainShapes[i].DistanceFunction,
                    terrainShapes[i].BlendMode == BlendMode.Subtractive
                );
            }

            // TODO: Runtime shapes.

            // Surface noise.
            m_SurfaceNoise = new NoiseData(
                surfaceNoise.Offset,
                surfaceNoise.Amplitude,
                surfaceNoise.Frequency,
                surfaceNoise.Seed);

            // Global noise.
            m_GlobalNoise = new NoiseData(
                globalNoise.Offset,
                globalNoise.Amplitude,
                globalNoise.Frequency,
                globalNoise.Seed);

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
         * Sample function variants.
         * These are selected by density JOB kernals, rather than in the loop to optimise for branch-less execution.
        */

        public readonly float Sample(float3 worldPosition)
        {
            float density = 0.0f;

            // Initialize with surface noise.
            density = SampleSurface(worldPosition, density);

            // Apply terrain forming shapes before the noise step.
            density = SampleShapeQueueSmooth(worldPosition, density);

            // Apply global noise.
            density = Sample3DNoise(worldPosition, density);

            // Apply in-game user shapes (CSG).
            //density = SampleShapeQueueHard(worldPosition, density);
            // ^TODO

            return density;
        }

        // TODO
        public readonly float SampleWithCache(float3 worldPosition)
        {
            // Convert to 8 cell indices.
            // Interpolate between values.

            return 0.0f;
        }

        /*
         * Sampler functions.
         * The density field can be created by combining several sampling methods such as shapes, noise or cached data.
        */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float SampleSurface(float3 worldPosition, float density)
        {
            // TODO: seed
            return density + Surface(worldPosition + m_SurfaceNoise.offset, m_SurfaceNoise.frequency, m_SurfaceNoise.amplitude);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float Sample3DNoise(float3 worldPosition, float density)
        {
            // TODO: seed
            return density + Noise(worldPosition + m_GlobalNoise.offset, m_GlobalNoise.frequency, m_GlobalNoise.amplitude);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float SampleShapeQueueSmooth(float3 worldPosition, float density)
        {
            DistanceFunctionData sdf;
            float3 translatedPosition;
            float distance;

            // Apply pre-noise shapes.
            for (int i = 0; i < m_NumDistanceFunctions; i++)
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
                density = SmoothMin(density * sdf.minSign, distance, ProceduralTerrain.Smoothness) * sdf.minSign;
            }

            return density;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float SampleShapeQueueHard(float3 worldPosition, float density)
        {
            DistanceFunctionData sdf;
            float3 translatedPosition;
            float distance;

            // Apply pre-noise shapes.
            for (int i = 0; i < m_NumDistanceFunctions; i++)
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
                density = math.min(density * sdf.minSign, distance) * sdf.minSign;
            }

            return density;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float SampleDensityCache(float3 worldPosition, float density)
        {
            return density;
        }

        /*
         * Signed Distance Functions (Shapes)
         *
         * Note: Spheres are the easiest shape to calculate and thus create the most optimised result.
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
