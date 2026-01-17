using Unity.Mathematics;

namespace LevelGeneration.Terrain
{
    public struct Shape
    {
        float3 position;
        quaternion rotation;
        float3 scale;

        DistanceFunction distanceFunction;
        BlendMode blendMode;
        float smoothness;

        float dimention1;
        float dimention2;
        float dimention3;

        AffineTransform matrix;
        AffineTransform inverseMatrix;

        public Shape(float3 position, quaternion rotation, float3 scale, DistanceFunction distanceFunction, BlendMode blendMode, float smoothness, float dimention1, float dimention2, float dimention3)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
            
            this.distanceFunction = distanceFunction;
            this.blendMode = blendMode;
            this.smoothness = smoothness;

            this.dimention1 = dimention1;
            this.dimention2 = dimention2;
            this.dimention3 = dimention3;

            matrix = new(position, rotation, scale);
            inverseMatrix = math.inverse(matrix);
        }

        public readonly AffineTransform Matrix => matrix;
        public readonly AffineTransform InverseMatrix => inverseMatrix;

        public readonly DistanceFunction DistanceFunction => distanceFunction;

        public readonly bool IsAdditive => blendMode == BlendMode.Additive;
        public readonly bool IsSubtractive => blendMode == BlendMode.Subtractive;
        public readonly float Smoothness => smoothness;

        public readonly float Dimention1 => dimention1;
        public readonly float Dimention2 => dimention2;
        public readonly float Dimention3 => dimention3;
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
