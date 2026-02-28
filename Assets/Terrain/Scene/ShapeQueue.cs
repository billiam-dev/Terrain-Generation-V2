using System.Collections.Generic;
using Unity.Mathematics;

namespace LevelGeneration.Terrain.Scene
{
    public class ShapeQueue
    {
        readonly List<Shape> shapes;
        readonly List<Volume> modifiedVolumes;

        public Shape[] Shapes => shapes.ToArray();

        public int Count => shapes.Count;

        public Volume[] ModifiedVolumes => modifiedVolumes.ToArray();

        bool isDirty;

        public bool IsDirty
        {
            get
            {
                return isDirty;
            }
            set
            {
                modifiedVolumes.Clear();
                isDirty = value;
            }
        }

        public ShapeQueue()
        {
            shapes = new();
            modifiedVolumes = new();
        }

        public bool AddShape(Shape shape)
        {
            if (shapes.Contains(shape))
                return false;

            shapes.Add(shape);
            modifiedVolumes.Add(shape.ComputeVolume());

            isDirty = true;

            return true;
        }

        public bool RemoveShape(Shape shape)
        {
            if (!shapes.Contains(shape))
                return false;

            shapes.Remove(shape);
            modifiedVolumes.Add(shape.ComputeVolume());

            isDirty = true;

            return true;
        }

        public void Clear()
        {
            if (shapes.Count == 0)
                return;

            shapes.Clear();
            isDirty = true;
        }
    }

    /// <summary>
    /// An object which represents a transformable SDF in a terrain scene.
    /// Can be added to a terrain and then moved or modifed.
    /// </summary>
    public class Shape
    {
        AffineTransform matrix;
        AffineTransform inverseMatrix;

        DistanceFunction distanceFunction;
        BlendMode blendMode;

        float3 dimentions;

        public AffineTransform Matrix
        {
            get
            {
                return matrix;
            }
            set
            {
                matrix = value;
                inverseMatrix = math.inverse(value);
                IsDirty = true;
            }
        }

        public AffineTransform InverseMatrix
        {
            get
            {
                return inverseMatrix;
            }
        }

        public DistanceFunction DistanceFunction
        {
            get
            {
                return distanceFunction;
            }
            set
            {
                distanceFunction = value;
                IsDirty = true;
            }
        }

        public BlendMode BlendMode
        {
            get
            {
                return blendMode;
            }
            set
            {
                blendMode = value;
                IsDirty = true;
            }
        }

        public float3 Dimentions
        {
            get
            {
                return dimentions;
            }
            set
            {
                dimentions = value;
                IsDirty = true;
            }
        }

        public bool IsDirty;

        // Multiplier for how much the smoothness value should extend the brick volume effected by this shape.
        // Larger values result in a larger volume allowing for smoothing over larger distances at the expense of speed.
        const float k_SmoothnessVolumeExtentConstant = 4.0f;

        // Constructors
        public Shape(float3 translation, quaternion rotation, float3 scale, DistanceFunction distanceFunction, BlendMode blendMode, float3 dimentions)
        {
            matrix = new(translation, rotation, scale);
            inverseMatrix = math.inverse(matrix);

            this.distanceFunction = distanceFunction;
            this.blendMode = blendMode;
            this.dimentions = dimentions;
        }

        public Shape(float3 translation, quaternion rotation, float3 scale, DistanceFunction distanceFunction, BlendMode blendMode, float dimention1, float dimention2, float dimention3)
            : this(translation, rotation, scale, distanceFunction, blendMode, new float3(dimention1, dimention2, dimention3))
        {

        }

        public Shape(float3 translation, quaternion rotation, float3 scale, DistanceFunction distanceFunction, BlendMode blendMode, float dimention1, float dimention2)
            : this(translation, rotation, scale, distanceFunction, blendMode, new float3(dimention1, dimention2, 0))
        {

        }

        public Shape(float3 translation, quaternion rotation, float3 scale, DistanceFunction distanceFunction, BlendMode blendMode, float dimention1)
            : this(translation, rotation, scale, distanceFunction, blendMode, new float3(dimention1, 0, 0))
        {

        }

        public Shape()
        {

        }

        /// <summary>
        /// Compute a world space AABB for the shape.
        /// </summary>
        public Volume ComputeVolume()
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
            }

            // Pad the volume to account for the smoothing factor around shapes.
            boundsVolume += ProceduralTerrain.Smoothness * k_SmoothnessVolumeExtentConstant;

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

            return new Volume(
                matrix.t,
                boundsVolume
            );
        }
    }

    public enum DistanceFunction
    {
        Sphere     = 0,
        SemiSphere = 1,
        Capsule    = 2,
        Torus      = 3,
        Cube       = 4
    }

    public enum BlendMode
    {
        Additive    = 0,
        Subtractive = 1
    }
}
