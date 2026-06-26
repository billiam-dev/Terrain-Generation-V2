namespace TerrainSystem.Scene
{
    // TODO
    class BVH
    {
        class Node
        {
            public int[] indices;
            public Volume volume;

            public Node child1;
            public Node child2;

            public void Expand(Volume volume)
            {

            }
        }

        public void Construct(Volume[] volumes)
        {

        }

        public int[] Traverse()
        {
            return new int[0];
        }
    }
}
