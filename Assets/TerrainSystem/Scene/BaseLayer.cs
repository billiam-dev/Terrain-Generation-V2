namespace TerrainSystem.Scene
{
    public class BaseLayer
    {
        float value;

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
