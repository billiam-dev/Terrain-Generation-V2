using System.Collections.Generic;
using TerrainSystem.SDF;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainSystem
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        /// <summary>
        /// Sample the density cache at the given indices.
        /// </summary>
        public float SampleDensity(float3 positionWS)
        {
            // Scale position by the world scale.
            positionWS *= 1.0f / k_WorldScale;

            // Allocate a temporary density sampler.
            DensitySampler sampler = new();
            sampler.Allocate(m_Scene, Allocator.Temp);

            // Sample the SDF at the given position.
            float value = sampler.Sample(positionWS);

            // Dispose the sampler and return the result.
            sampler.Dispose();

            return value;
        }

        /// <summary>
        /// Raytraces the terrain to find the surface position.
        /// Returns a structure containing the hit position and its distance to the terrain.
        /// </summary>
        public RaymarchResult RaytraceSurface(float3 positionWS, float3 direction, float minDistance = 0.1f)
        {
            // Ensure direction is normalized.
            direction = math.normalize(direction);

            // Scale origin position by the world scale.
            positionWS *= 1.0f / k_WorldScale;

            // Allocate a temporary density sampler.
            DensitySampler sampler = new();
            sampler.Allocate(m_Scene, Allocator.Temp);

            // Step forward by the sampled distance value until we are acceptably close to the surface.
            float distance = sampler.Sample(positionWS);

            int step = 0;
            while (distance > minDistance)
            {
                if (step == k_MaxRaymarchSteps)
                {
                    sampler.Dispose();
                    return new RaymarchResult(positionWS, distance, false);
                }

                positionWS += direction * distance;
                distance = sampler.Sample(positionWS);
                step++;
            }

            // Dispose the temporary sampler.
            sampler.Dispose();

            // Re-scale and return the position.
            return new RaymarchResult(positionWS * k_WorldScale, distance, true);
        }

        /// <summary>
        /// Raytraces the terrain to find the surface position.
        /// Returns a list of visited positions and their distances to the terrain.
        /// </summary>
        public List<RaymarchResult> RaytraceSurfaceWithSteps(float3 positionWS, float3 direction, float minDistance = 0.1f)
        {
            // Ensure direction is normalized.
            direction = math.normalize(direction);

            // Scale origin position by the world scale.
            positionWS *= 1.0f / k_WorldScale;

            // Allocate a temporary density sampler.
            DensitySampler sampler = new();
            sampler.Allocate(m_Scene, Allocator.Temp);

            // Step forward by the sampled distance value until we are acceptably close to the surface.
            float distance = sampler.Sample(positionWS);

            List<RaymarchResult> positions = new()
            {
                new RaymarchResult(positionWS * k_WorldScale, distance, true) // Initial position.
            };

            int step = 0;
            while (distance > minDistance)
            {
                if (step == k_MaxRaymarchSteps)
                {
                    sampler.Dispose();
                    positions.Add(new RaymarchResult(positionWS * k_WorldScale, distance, false));
                    return positions;
                }

                positionWS += direction * distance;
                distance = sampler.Sample(positionWS);
                positions.Add(new RaymarchResult(positionWS * k_WorldScale, distance, true));
                step++;
            }

            // Dispose the temporary sampler.
            sampler.Dispose();

            // Re-scale and return the position.
            return positions;
        }
    }

    public readonly struct RaymarchResult
    {
        public readonly float3 position;
        public readonly float distance;
        public readonly bool hitSurface;

        public RaymarchResult(float3 position, float distance, bool hitSurface)
        {
            this.position = position;
            this.distance = distance;
            this.hitSurface = hitSurface;
        }
    }
}
