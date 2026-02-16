using Unity.Mathematics;

namespace LevelGeneration.Terrain
{
    /// <summary>
    /// Readonly shape struct which can be passed to jobs.
    /// </summary>
    public class Shape
    {
        public readonly AffineTransform matrix;
        public readonly AffineTransform inverseMatrix;

        public readonly DistanceFunction distanceFunction;
        public readonly BlendMode blendMode;
        public readonly float smoothness;

        public readonly float3 dimentions;

        public bool IsGlobal => (uint)distanceFunction >= 5;

        // Multiplier for how much the smoothness value should extend the brick volume effected by this shape.
        // Larger values result in a larger volume allowing for smoothing over larger distances at the expense of speed.
        const float k_SmoothnessVolumeExtentConstant = 4.0f;

        // Constructors
        public Shape(float3 translation, quaternion rotation, float3 scale, DistanceFunction distanceFunction, BlendMode blendMode, float smoothness, float3 dimentions)
        {
            matrix = new(translation, rotation, scale);
            inverseMatrix = math.inverse(matrix);

            this.distanceFunction = distanceFunction;
            this.blendMode = blendMode;
            this.smoothness = smoothness;
            this.dimentions = dimentions;
        }

        public Shape(float3 translation, quaternion rotation, float3 scale, DistanceFunction distanceFunction, BlendMode blendMode, float smoothness, float dimention1, float dimention2, float dimention3)
            : this(translation, rotation, scale, distanceFunction, blendMode, smoothness, new float3(dimention1, dimention2, dimention3))
        {

        }

        public Shape(float3 translation, quaternion rotation, float3 scale, DistanceFunction distanceFunction, BlendMode blendMode, float smoothness, float dimention1, float dimention2)
            : this(translation, rotation, scale, distanceFunction, blendMode, smoothness, new float3(dimention1, dimention2, 0))
        {

        }

        public Shape(float3 translation, quaternion rotation, float3 scale, DistanceFunction distanceFunction, BlendMode blendMode, float smoothness, float dimention1)
            : this(translation, rotation, scale, distanceFunction, blendMode, smoothness, new float3(dimention1, 0, 0))
        {

        }

        /// <summary>
        /// Compute a world space AABB for the shape.
        /// </summary>
        public void ComputeVolume(out float3 position, out float3 volume)
        {
            // Compute an accurate bounding volume for the shape in world space.
            float3 boundsVolume = 0;
            switch (distanceFunction)
            {
                case DistanceFunction.Sphere:
                case DistanceFunction.SemiSphere:
                    boundsVolume = dimentions.x * 2.0f; // diameter = radius * 2
                    break;

                case DistanceFunction.Capsule:
                    boundsVolume.y = (dimentions.x + dimentions.y) * 2.0f;
                    boundsVolume.xz = dimentions.y * 2.0f;
                    break;

                case DistanceFunction.Cube:
                    boundsVolume = dimentions * 2.0f;
                    break;

                case DistanceFunction.Surface:
                case DistanceFunction.Noise:
                    position = 0;
                    volume = 0;
                    return;
            }

            // Pad the volume to account for the smoothing factor around shapes.
            boundsVolume += smoothness * k_SmoothnessVolumeExtentConstant;

            // Translate the volume by my matrix.
            // Compute all eight corner points
            float3[] cornerPoints = new float3[8];
            cornerPoints[0] = new float3(boundsVolume.x, boundsVolume.y, boundsVolume.z);
            cornerPoints[1] = new float3(-boundsVolume.x, boundsVolume.y, boundsVolume.z);
            cornerPoints[2] = new float3(boundsVolume.x, -boundsVolume.y, boundsVolume.z);
            cornerPoints[3] = new float3(-boundsVolume.x, -boundsVolume.y, boundsVolume.z);
            cornerPoints[4] = new float3(boundsVolume.x, boundsVolume.y, -boundsVolume.z);
            cornerPoints[5] = new float3(-boundsVolume.x, boundsVolume.y, -boundsVolume.z);
            cornerPoints[6] = new float3(boundsVolume.x, -boundsVolume.y, -boundsVolume.z);
            cornerPoints[7] = new float3(-boundsVolume.x, -boundsVolume.y, -boundsVolume.z);

            for (int i = 0; i < 8; i++)
                cornerPoints[i] = math.mul(matrix, new float4(cornerPoints[i], 1.0f)).xyz;

            // Recompute the aabb volume using the transformed points.
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;

            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                minX = math.min(cornerPoints[i].x, minX);
                minY = math.min(cornerPoints[i].y, minY);
                minZ = math.min(cornerPoints[i].z, minZ);

                maxX = math.max(cornerPoints[i].x, maxX);
                maxY = math.max(cornerPoints[i].y, maxY);
                maxZ = math.max(cornerPoints[i].z, maxZ);
            }

            boundsVolume = new float3(
                (maxX - minX) / 2.0f,
                (maxY - minY) / 2.0f,
                (maxZ - minZ) / 2.0f
                );

            volume = boundsVolume;
            position = matrix.t;
        }
    }

    public enum DistanceFunction
    {
        Sphere     = 0,
        SemiSphere = 1,
        Capsule    = 2,
        Torus      = 3,
        Cube       = 4,
        Surface    = 5,
        Noise      = 6
    }

    public enum BlendMode
    {
        Additive    = 0,
        Subtractive = 1
    }
}
