using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace LevelGeneration.Terrain.Rendering
{
    public class TerrainRenderingData : IDisposable
    {
        /// <summary>
        /// Provides an interface for mesher jobs to read density data.
        /// Regions of density data (bricks) can be added to the hash map and sampled via a pointer to the original array.
        /// </summary>
        public struct DensitySampler // Pass me to mesher!
        {
            [NativeDisableUnsafePtrRestriction]
            NativeHashMap<int3, IntPtr> bricks;

            public void Allocate(int totalChunks) => bricks = new(totalChunks, Allocator.Persistent);

            public void Dispose() => bricks.Dispose();

            public unsafe void AddDensityBrick(int3 index, NativeArray<float> density) => bricks.Add(index, new IntPtr(density.GetUnsafePtr()));

            public void RemoveDensityBrick(int3 index) => bricks.Remove(index);

            public bool ContainsBrick(int3 index) => bricks.ContainsKey(index);

            public unsafe readonly float Sample(int3 globalCellIndex, int brickSize)
            {
                int3 brickIndex = (int3)math.floor((float3)globalCellIndex / brickSize);
                int3 localCellIndex = globalCellIndex - (brickIndex * brickSize);

                int densityIndex = (localCellIndex.z * brickSize * brickSize) + (localCellIndex.y * brickSize) + localCellIndex.x;

                float* ptr = (float*)bricks[brickIndex];
                return *(ptr + densityIndex);
            }
        }

        DensitySampler densitySampler;
        NativeHashSet<int3> modifiedBricks;

        public DensitySampler Density => densitySampler;

        public void Allocate(int totalChunks)
        {
            densitySampler.Allocate(totalChunks);
            modifiedBricks = new(64, Allocator.Persistent);
        }

        public void Dispose()
        {
            densitySampler.Dispose();
            modifiedBricks.Dispose();
        }

        public void RegisterBrick(int3 index, NativeArray<float> density)
        {
            densitySampler.AddDensityBrick(index, density);
        }

        public void DeregisterBrick(int3 index)
        {
            densitySampler.RemoveDensityBrick(index);
            modifiedBricks.Remove(index);
        }

        // TODO: name these functions better!
        public void FlagBrickPendingRemesh(int3 index)
        {
            modifiedBricks.Add(index);
        }

        public bool BrickPendingRemesh(int3 index)
        {
            return modifiedBricks.Contains(index);
        }

        public void ClearPendingRemesh()
        {
            modifiedBricks.Clear();
        }
    }
}
