using System;
using System.Collections.Generic;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using TerrainSystem.SDF;

namespace TerrainSystem.Scene
{
    public class DensityCache : SDFLayer, IDisposable
    {
        // The number of regional points per axis.
        const int RegionSize = 64;

        readonly string DataPath = Path.Combine(Application.streamingAssetsPath, "TerrainRegions");

        Dictionary<int3, Region> regions;

        public void Allocate()
        {
            regions = new();
        }

        public void Dispose()
        {
            foreach (Region region in regions.Values)
                region.Dispose();

            regions = null;
        }

        public void LoadRegion(int3 index)
        {
            if (regions.ContainsKey(index))
            {
                Debug.LogWarning(string.Format("Failed to load region {0}, region already loaded.", index));
                return;
            }

            Region region = new();
            region.Allocate(index, RegionSize);
            regions.Add(index, region);
        }

        public void UnloadRegion(int3 index)
        {
            if (!regions.ContainsKey(index))
            {
                Debug.LogWarning(string.Format("Failed to unload region {0}, region not loaded.", index));
                return;
            }

            regions[index].Dispose();
            regions.Remove(index);
        }

        public void Clear()
        {
            foreach (Region region in regions.Values)
                region.Dispose();

            regions.Clear();
        }

        class Region : IDisposable
        {
            NativeArray<float> densityData;
            int3 index;
            int size;

            public void Allocate(int3 regionIndex, int size)
            {
                densityData = new(RegionSize * RegionSize * RegionSize, Allocator.Persistent);
                index = regionIndex;
                this.size = size;
            }

            public void Dispose()
            {
                densityData.Dispose();
            }

            public void Evaluate(SDFScene scene)
            {
                DensitySampler sampler = new();
                sampler.Allocate(scene, Allocator.TempJob);

                EvaluateRegionJob evaluateRegionJob = new()
                {
                    densitySampler = sampler,
                    regionSize = size,
                    regionIndex = index,
                    density = densityData
                };

                evaluateRegionJob.Schedule(RegionSize * RegionSize * RegionSize, default).Complete();

                sampler.Dispose();
            }

            public void LoadFromDisc()
            {

            }

            public void SaveToDisc()
            {

            }
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
    struct EvaluateRegionJob : IJobFor
    {
        [ReadOnly] public DensitySampler densitySampler;
        [ReadOnly] public int regionSize;
        [ReadOnly] public int3 regionIndex;
        
        [WriteOnly] public NativeArray<float> density;
        
        public void Execute(int index)
        {
            // Unwrap the iteration index into a 3D index using the extended brick size.
            int z = index / (regionSize * regionSize);
            int y = (index / regionSize) % regionSize;
            int x = index % regionSize;

            int3 localCellIndex = new(x, y, z);

            // Derrive world position from iteration index.
            int3 globalCellIndex = (regionIndex * regionSize) + localCellIndex;

            // Sample the SDF.
            float newDensity = densitySampler.SampleNoCSG((float3)globalCellIndex);

            // Store the new density.
            density[index] = newDensity;
        }
    }
}
