namespace LevelGeneration.Terrain.Scene
{
    /// <summary>
    /// Contains all maintained objects that influence the density function underlying the terrain.
    /// </summary>
    public class SDFScene
    {
        public readonly ShapeQueue terrainShapes;
        public readonly NoiseLayer surfaceNoise;
        public readonly NoiseLayer globalNoise;

        public SDFScene()
        {
            terrainShapes = new();
            surfaceNoise = new();
            globalNoise = new();
        }

        public void Clear()
        {
            terrainShapes.Clear();
            surfaceNoise.Clear();
            globalNoise.Clear();
        }
    }
}
