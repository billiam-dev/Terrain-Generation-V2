namespace TerrainSystem.Scene
{
    public class BaseLayer : SDFLayer
    {
        float value;

        public float Value
        {
            get
            {
                return value;
            }
            set
            {
                this.value = value;
                isDirty = true;
            }
        }
    }
}
