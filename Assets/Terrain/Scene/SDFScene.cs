namespace LevelGeneration.Terrain.Scene
{
    /// <summary>
    /// Contains all maintained objects that influence the density function underlying the terrain.
    /// </summary>
    public class SDFScene
    {
        public readonly ShapeQueue terrainShapes;
        public readonly ShapeQueue csgShapes;
        public readonly NoiseLayer surfaceNoise;
        public readonly NoiseLayer globalNoise;

        public SDFScene()
        {
            terrainShapes = new();
            csgShapes = new();
            surfaceNoise = new();
            globalNoise = new();
        }

        public void Clear()
        {
            terrainShapes.Clear();
            csgShapes.Clear();
            surfaceNoise.Clear();
            globalNoise.Clear();
        }
    }
}
