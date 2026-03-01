namespace LevelGeneration.Terrain.Scene
{
    /// <summary>
    /// Contains all maintained objects that influence the density function underlying the terrain.
    /// </summary>
    public class SDFScene
    {
        // public bool terrainShapesEnabled; // TODO
        public readonly ShapeQueue terrainShapes;

        // public bool surfaceNoiseEnabled; // TODO
        public readonly NoiseLayer surfaceNoise;

        // public bool globalNoiseEnabled; // TODO
        public readonly NoiseLayer globalNoise;

        // An option to use a pre-computed density map rather than the terrain shapes
        // and noise layers before applying Constructive Solid Geometry shapes.
        public bool useDensityCache;
        public readonly DensityCache densityCache;

        public readonly ShapeQueue csgShapes;

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
