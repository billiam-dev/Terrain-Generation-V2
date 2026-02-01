using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration.Terrain.Rendering
{
    public class TerrainRenderingData
    {
        public BrickMapRenderingData[] brickmapLevels;
        public Camera OriginCamera;
    }

    public class BrickMapRenderingData
    {
        readonly HashSet<int3> modifiedBricks;
        readonly DensitySampler densitySampler;

        public DensitySampler DensitySampler => densitySampler;

        public BrickMapRenderingData(int size)
        {
            modifiedBricks = new();
            densitySampler.Allocate(size);
        }

        ~BrickMapRenderingData()
        {
            densitySampler.Dispose();
        }

        public void RegisterBrick(int3 index, IntPtr densityPointer)
        {
            densitySampler.AddBrick(index, densityPointer);
        }

        public void DeregisterBrick(int3 index)
        {
            densitySampler.RemoveBrick(index);
            modifiedBricks.Remove(index);
        }

        public void FlagBrickPendingRemesh(int3 index)
        {
            if (!modifiedBricks.Contains(index))
                modifiedBricks.Add(index);
        }

        public bool IsBrickPendingRemesh(int3 index)
        {
            return modifiedBricks.Contains(index);
        }

        public void RemovePendingRemeshFlag(int3 index)
        {
            if (modifiedBricks.Contains(index))
                modifiedBricks.Remove(index);
        }
    }

    /// <summary>
    /// Provides an interface for mesher jobs to read density data.
    /// Regions of density data (bricks) can be added to the hash map and sampled via a pointer to the original array.
    /// </summary>
    public struct DensitySampler
    {
        [NativeDisableUnsafePtrRestriction]
        NativeHashMap<int3, IntPtr> bricks; // TODO: Pointer should point to DensityBrick. DensityBrick should have bool values for isFull / isEmpty. Currently, walls are created on the inside of large objects because of the lack of this distinction.

        public void Allocate(int size) => bricks = new(size * size * size, Allocator.Persistent);

        public void Dispose() => bricks.Dispose();

        public unsafe void AddBrick(int3 index, IntPtr densityPointer) => bricks.Add(index, densityPointer);

        public void RemoveBrick(int3 index) => bricks.Remove(index);

        public bool ContainsBrick(int3 index) => bricks.ContainsKey(index);

        public int3[] GetAllocatedBricks()
        {
            // TODO: This is possibly crap.

            NativeArray<int3> nativeIndices = bricks.GetKeyArray(Allocator.Temp);

            int3[] indices = nativeIndices.ToArray();

            nativeIndices.Dispose();

            return indices;
        }

        public unsafe readonly float Sample(int3 globalCellIndex, int brickSize)
        {
            int3 brickIndex = (int3)math.floor((double3)globalCellIndex / brickSize); // TODO: find a way to do this without the cast. Also note that casting to a float3 fuks everything up with precision errors.

            // The terrain surface should never hit this check, but edge cell cases must
            // still be handled by simply returning the base density value; completely air.
            if (!bricks.ContainsKey(brickIndex))
                return ProceduralTerrain.k_InitialDensityValue;

            int3 localCellIndex = globalCellIndex - (brickIndex * brickSize);

            int densityIndex = (localCellIndex.z * brickSize * brickSize) + (localCellIndex.y * brickSize) + localCellIndex.x;

            float* ptr = (float*)bricks[brickIndex];
            return *(ptr + densityIndex);
        }
    }
}
