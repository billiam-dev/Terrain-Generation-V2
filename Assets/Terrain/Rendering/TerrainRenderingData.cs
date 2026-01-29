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
        class BrickMapRenderingData
        {
            readonly HashSet<int3> modifiedBricks;
            readonly DensitySampler densitySampler;

            internal DensitySampler DensitySampler => densitySampler;

            internal int3[] AllocatedBricks => densitySampler.GetIndices();

            internal BrickMapRenderingData(int size)
            {
                modifiedBricks = new();
                densitySampler.Allocate(size);
            }

            ~BrickMapRenderingData()
            {
                densitySampler.Dispose();
            }

            internal void RegisterBrick(int3 index, IntPtr densityPointer)
            {
                densitySampler.AddBrick(index, densityPointer);
            }

            internal void DeregisterBrick(int3 index)
            {
                modifiedBricks.Remove(index);
                densitySampler.RemoveBrick(index);
            }

            internal void FlagBrickPendingRemesh(int3 index)
            {
                if (!modifiedBricks.Contains(index))
                    modifiedBricks.Add(index);
            }

            internal bool IsBrickPendingRemesh(int3 index)
            {
                return modifiedBricks.Contains(index);
            }

            internal void RemovePendingRemeshFlag(int3 index)
            {
                if (modifiedBricks.Contains(index))
                    modifiedBricks.Remove(index);
            }
        }

        readonly BrickMapRenderingData[] brickmapRenderingDatas;
        public Camera Camera;

        public TerrainRenderingData(int numLevels, int brickmapLevelSize)
        {
            brickmapRenderingDatas = new BrickMapRenderingData[numLevels];

            for (int i = 0; i < numLevels; i++)
                brickmapRenderingDatas[i] = new(brickmapLevelSize);
        }

        public void RegisterBrick(int brickmapLevel, int3 index, IntPtr densityPointer) => brickmapRenderingDatas[brickmapLevel].RegisterBrick(index, densityPointer);

        public void DeregisterBrick(int brickmapLevel, int3 index) => brickmapRenderingDatas[brickmapLevel].DeregisterBrick(index);

        public void FlagBrickPendingRemesh(int brickmapLevel, int3 index) => brickmapRenderingDatas[brickmapLevel].FlagBrickPendingRemesh(index);

        public bool IsBrickPendingRemesh(int brickmapLevel, int3 index) => brickmapRenderingDatas[brickmapLevel].IsBrickPendingRemesh(index);

        public void RemovePendingRemeshFlag(int brickmapLevel, int3 index) => brickmapRenderingDatas[brickmapLevel].RemovePendingRemeshFlag(index);

        public int3[] GetAllocatedBricks(int brickmapLevel) => brickmapRenderingDatas[brickmapLevel].AllocatedBricks;

        public DensitySampler GetDensitySampler(int brickmapLevel) => brickmapRenderingDatas[brickmapLevel].DensitySampler;
    }

    /// <summary>
    /// Provides an interface for mesher jobs to read density data.
    /// Regions of density data (bricks) can be added to the hash map and sampled via a pointer to the original array.
    /// </summary>
    public struct DensitySampler
    {
        [NativeDisableUnsafePtrRestriction]
        NativeHashMap<int3, IntPtr> bricks;

        public void Allocate(int size)
        {
            bricks = new(size * size * size, Allocator.Persistent);
        }

        public void Dispose()
        {
            bricks.Dispose();
        }

        public unsafe void AddBrick(int3 index, IntPtr densityPointer)
        {
            if (!bricks.ContainsKey(index))
                bricks.Add(index, densityPointer);
        }

        public void RemoveBrick(int3 index)
        {
            if (bricks.ContainsKey(index))
                bricks.Remove(index);
        }

        public bool ContainsBrick(int3 index)
        {
            return bricks.ContainsKey(index);
        }

        public int3[] GetIndices()
        {
            // TODO: This is possibly crap.

            NativeArray<int3> nativeIndices = bricks.GetKeyArray(Allocator.Temp);

            int3[] indices = nativeIndices.ToArray();

            nativeIndices.Dispose();

            return indices;
        }

        public unsafe readonly float Sample(int3 globalCellIndex, int brickSize)
        {
            int3 brickIndex = (int3)math.floor((float3)globalCellIndex / brickSize);

            if (!bricks.ContainsKey(brickIndex))
                return 32.0f;

            int3 localCellIndex = globalCellIndex - (brickIndex * brickSize);

            int densityIndex = (localCellIndex.z * brickSize * brickSize) + (localCellIndex.y * brickSize) + localCellIndex.x;

            float* ptr = (float*)bricks[brickIndex];
            return *(ptr + densityIndex);
        }
    }
}
