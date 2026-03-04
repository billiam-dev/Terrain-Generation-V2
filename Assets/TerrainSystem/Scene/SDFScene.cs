namespace TerrainSystem.Scene
{
    /// <summary>
    /// Contains all maintained objects that influence the density function underlying the terrain.
    /// </summary>
    public class SDFScene
    {
        public readonly BaseLayer baseLayer;
        public readonly ShapeQueue terrainShapes;
        public readonly NoiseLayer surfaceNoise;
        public readonly NoiseLayer globalNoise;

        // An option to use a pre-computed density map rather than the terrain shapes
        // and noise layers before applying terraforming shapes.
        public bool useDensityCache;
        public readonly DensityCache densityCache;

        public readonly ShapeQueue terraformShapes;

        public SDFScene()
        {
            baseLayer = new();
            terrainShapes = new();
            surfaceNoise = new();
            globalNoise = new();
            terraformShapes = new();
        }

        public void Clear()
        {
            baseLayer.Value = 0;
            terrainShapes.Clear();
            surfaceNoise.Clear();
            globalNoise.Clear();
            terraformShapes.Clear();
        }
    }

    public abstract class SDFLayer
    {
        protected bool isDirty;

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
    }
}
