using System;
using Unity.Collections;

namespace TerrainSystem.SDF
{
    // So this becomes readonly struct DensityCacheData, in DensitySampler like DistanceFuncData & NoiseData.
    public struct DensityCache : IDisposable
    {
        /// <summary>
        /// The size of a single region in bricks per axis.
        /// Optimally, all necessary density data can be found in four regions.
        /// </summary>
        const int RegionSizeBricks = 10;

        int sizeX;
        int sizeY;
        int sizeZ;

        NativeArray<CacheRegion> regions;

        public void Allocate()
        {

        }

        public void Dispose()
        {
            foreach (CacheRegion region in regions)
                regions.Dispose();

            regions.Dispose();
        }

        struct CacheRegion : IDisposable
        {
            NativeArray<float> densityData;

            public void Allocate()
            {
                densityData = new(0, Allocator.Persistent);
            }

            public void Dispose()
            {
                densityData.Dispose();
            }

            public void SaveToDisc()
            {

            }

            public void LoadFromDisc()
            {

            }

            public readonly float Sample(int x, int y, int z)
            {
                return 0.0f;
            }
        }
    }
}
