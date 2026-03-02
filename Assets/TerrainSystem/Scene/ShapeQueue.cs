using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainSystem.Scene
{
    /// <summary>
    /// For an SDF scene, an ordered list of shapes applied to the distance field.
    /// </summary>
    public class ShapeQueue
    {
        readonly List<Shape> shapes;            // Shapes to be applied in order to the terrain.
        readonly List<Volume> modifiedVolumes;  // A list of AABB which have been modified. Should be used to selectively recompute cached density values, then cleared.

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
            {
                Debug.LogWarning("Could add shape to shape queue, shape already in queue.");
                return false;
            }

            shapes.Add(shape);
            shape.OnModified += ShapeModified;

            modifiedVolumes.Add(shape.Volume);

            isDirty = true;

            return true;
        }

        public bool RemoveShape(Shape shape)
        {
            if (!shapes.Contains(shape))
            {
                Debug.LogWarning("Could not remove shape from shape queue, shape not in queue.");
                return false;
            }

            shape.OnModified -= ShapeModified;
            shapes.Remove(shape);

            modifiedVolumes.Add(shape.Volume);

            isDirty = true;

            return true;
        }

        void ShapeModified(Volume previousVolume, Volume newVolume)
        {
            modifiedVolumes.Add(previousVolume);
            modifiedVolumes.Add(newVolume);

            isDirty = true;
        }

        public void ClearModifiedVolumes()
        {
            modifiedVolumes.Clear();
        }

        public void Clear()
        {
            if (shapes.Count == 0)
                return;

            foreach (Shape shape in shapes)
                modifiedVolumes.Add(shape.Volume);
            
            shapes.Clear();

            isDirty = true;
        }

        public int[] GetIntersectingShapes(float3 positionWS)
        {
            List<int> intersectingShapeIndices = new();
            int numShapes = shapes.Count;

            Volume volume;
            float3 position;
            float3 halfSize;

            for (int i = 0; i < numShapes; i++)
            {
                volume = shapes[i].Volume;
                position = volume.position;
                halfSize = volume.size * 0.5f;

                if (math.any(positionWS > position + halfSize) || math.any(positionWS < position - halfSize))
                    continue;

                intersectingShapeIndices.Add(i);
            }

            return intersectingShapeIndices.ToArray();
        }
    }

    /// <summary>
    /// A single shape within an SDF scene.
    /// </summary>
    public class Shape
    {
        AffineTransform matrix;
        AffineTransform inverseMatrix;

        DistanceFunction distanceFunction;
        BlendMode blendMode;

        float3 dimentions;

        Volume volume;

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

                Volume previousVolume = volume;
                volume = ComputeVolume();
                OnModified?.Invoke(previousVolume, volume);
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

                Volume previousVolume = volume;
                volume = ComputeVolume();
                OnModified?.Invoke(previousVolume, volume);
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

                Volume previousVolume = volume;
                volume = ComputeVolume();
                OnModified?.Invoke(previousVolume, volume);
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

                Volume previousVolume = volume;
                volume = ComputeVolume();
                OnModified?.Invoke(previousVolume, volume);
            }
        }

        public Volume Volume
        {
            get
            {
                return volume;
            }
        }

        // Event which is fired whenever properties for this shape are modified.
        // Contains old and new volume parameters for areas which will need to be updated to reflect the property change.
        public Action<Volume, Volume> OnModified;

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

            volume = ComputeVolume();
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

        /// <summary>
        /// Computes a world space AABB for this shape, in world space.
        /// </summary>
        Volume ComputeVolume()
        {
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
            // TODO: Add bool IsSmooth to this shape. CSG shapes do not run through the SmoothMin function so therefore the bounds does not need to be extended.
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
