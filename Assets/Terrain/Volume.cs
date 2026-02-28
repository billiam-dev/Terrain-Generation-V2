using Unity.Mathematics;

namespace LevelGeneration.Terrain
{
    public struct Volume
    {
        public float3 position;
        public float3 size;

        public Volume(float3 position, float3 size)
        {
            this.position = position;
            this.size = size;
        }
    }

    public struct IntVolume
    {
        public int3 coordinate;
        public int3 size;

        public IntVolume(int3 coordinate, int3 size)
        {
            this.coordinate = coordinate;
            this.size = size;
        }
    }
}
