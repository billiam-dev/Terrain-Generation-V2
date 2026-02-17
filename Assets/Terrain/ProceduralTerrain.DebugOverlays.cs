#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        public bool m_DrawBrickmapBorders;
        public bool m_DrawBricks;
        public bool m_DrawShapeVolumes;

        void DrawDebugGizmos()
        {
            Gizmos.matrix = Matrix4x4.identity;

            if (m_DrawBrickmapBorders)
            {
                for (int i = 0; i < k_NumBrickmapLevels; i++)
                    m_BrickmapLevels[i].DrawBounds(k_BrickmapLevelDebugColors[i]);
            }

            if (m_DrawBricks)
            {
                for (int i = 0; i < k_NumBrickmapLevels; i++)
                    m_BrickmapLevels[i].DrawBricks(k_BrickmapLevelDebugColors[i]);
            }

            if (m_DrawShapeVolumes)
            {
                for (int i = 0; i < k_NumBrickmapLevels; i++)
                    m_BrickmapLevels[i].DrawShapeVolumes();
            }
        }

        static Color RandomPastelColor(int3 position)
        {
            System.Random random = new(position.GetHashCode());

            // Fill rgb channels with random values.
            int r = random.Next(256);
            int g = random.Next(256);
            int b = random.Next(256);

            // Mix with off-white for a pleasing pastel effect.
            r = (r + 200) / 2;
            g = (g + 200) / 2;
            b = (b + 200) / 2;

            // Convert range (0, 256) -> (0.0f, 1.0f) and return.
            return new Color(
                r / 256.0f,
                g / 256.0f,
                b / 256.0f,
                1.0f);
        }

        partial class Brickmap
        {
            partial class Brick
            {
                public void Draw(Color color)
                {
                    color += densityModified ? Color.red : RandomPastelColor(index);
                    color.a = isUniformState ? 0.005f : 0.2f;

                    Gizmos.color = color;
                    Gizmos.DrawWireCube(worldPosition, Vector3.one * worldSize);
                }
            }

            public void DrawShapeVolumes()
            {
                HashSet<int3> bricksInShapeVolumes = new();

                foreach (Shape shape in shapes)
                {
                    shape.ComputeVolume(out float3 boundsPosition, out float3 boundsVolume);
                    GetBrickVolumeFromAABB(brickSize, levelScale * worldScale, boundsPosition, boundsVolume, out int3 initialIndex, out int3 volume);

                    for (int x = 0; x < volume.x; x++)
                        for (int y = 0; y < volume.y; y++)
                            for (int z = 0; z < volume.z; z++)
                                bricksInShapeVolumes.Add(initialIndex + new int3(x, y, z));
                }

                Gizmos.color = new Color(1.0f, 0.1f, 0.0f, 0.1f);
                foreach (int3 brickIndex in bricksInShapeVolumes)
                {
                    float3 worldBrickSize = brickSize * levelScale * worldScale;

                    float3 brickCorner = worldBrickSize * brickIndex;
                    float3 bricksCentre = brickCorner + (worldBrickSize / 2.0f);

                    Gizmos.DrawCube(bricksCentre, worldBrickSize);
                }
            }

            public void DrawBounds(Color color)
            {
                float3 worldBrickSize = brickSize * levelScale * worldScale;

                float3 brickmapLevelCentre = worldBrickSize * originIndex;
                float3 brickMapLevelSize = brickmapSize * worldBrickSize;

                Gizmos.color = color;
                Gizmos.DrawWireCube(brickmapLevelCentre, brickMapLevelSize);
            }

            public void DrawBricks(Color color)
            {
                foreach (Brick brick in bricks.Values)
                    brick.Draw(color);
            }
        }
    }
}
#endif
