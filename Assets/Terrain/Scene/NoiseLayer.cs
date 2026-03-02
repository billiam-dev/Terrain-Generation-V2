using Unity.Mathematics;

namespace LevelGeneration.Terrain.Scene
{
    /// <summary>
    /// For an SDF scene, a noise layer applied to the distance field.
    /// </summary>
    public class NoiseLayer
    {
        float3 offset;
        float amplitude;
        float frequency;
        int seed;

        bool isDirty;

        public bool IsDirty
        {
            get
            {
                return isDirty;
            }
            set
            {
                isDirty = value;
            }
        }

        bool isEnabled;

        public bool IsEnabled
        {
            get
            {
                return isEnabled;
            }
            set
            {
                isEnabled = value;
                isDirty = true;
            }
        }

        public float3 Offset
        {
            get
            {
                return offset;
            }
            set
            {
                offset = value;
                isDirty = true;
            }
        }

        public float Amplitude
        {
            get
            {
                return amplitude;
            }
            set
            {
                amplitude = value;
                isDirty = true;
            }
        }

        public float Frequency
        {
            get
            {
                return frequency;
            }
            set
            {
                frequency = value;
                isDirty = true;
            }
        }

        public int Seed
        {
            get
            {
                return seed;
            }
            set
            {
                seed = value;
                isDirty = true;
            }
        }

        public void Clear()
        {
            offset = 0;
            amplitude = 0;
            frequency = 0;
            seed = 0;

            isDirty = true;
        }
    }
}
