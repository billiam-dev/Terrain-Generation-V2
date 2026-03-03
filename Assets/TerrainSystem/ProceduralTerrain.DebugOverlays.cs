#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

using TerrainSystem.Scene;

namespace TerrainSystem
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
                    m_BrickmapLevels[i].DrawBounds();
            }

            if (m_DrawBricks)
            {
                for (int i = 0; i < k_NumBrickmapLevels; i++)
                    m_BrickmapLevels[i].DrawBricks();
            }

            if (m_DrawShapeVolumes)
            {
                for (int i = 0; i < k_NumBrickmapLevels; i++)
                    m_BrickmapLevels[i].DrawShapeVolumes(m_Scene);
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
                    color += coreUpdateQueued ? Color.red : RandomPastelColor(index);
                    color.a = isUniformState ? 0.005f : 0.2f;

                    Gizmos.color = color;
                    Gizmos.DrawWireCube(worldPosition, Vector3.one * worldSize);

                    // Draw colored panels on transition faces.
                    // Un-comment if needed.

                    /*
                    if (levelScale == 1 || densityModified || isUniformState)
                        return;

                    for (int i = 0; i < 6; i++)
                    {
                        if ((neighborLOD & (1 << i)) != 0)
                            DrawTransition(i);
                    }
                    */
                }

                void DrawTransition(int transitionIndex)
                {
                    float halfWorldSize = worldSize * 0.5f;
                    float width = 0.5f;

                    float3 transitionFaceOffset = transitionIndex switch
                    {
                        0 => new(halfWorldSize, 0, 0),  //  x
                        1 => new(-halfWorldSize, 0, 0), // -x
                        2 => new(0, halfWorldSize, 0),  //  y
                        3 => new(0, -halfWorldSize, 0), // -y
                        4 => new(0, 0, halfWorldSize),  //  z
                        5 => new(0, 0, -halfWorldSize), // -z
                        _ => new(0, 0, 0)
                    };

                    float3 transitionSize = transitionIndex switch
                    {
                        0 => new(width, halfWorldSize, halfWorldSize), //  x
                        1 => new(width, halfWorldSize, halfWorldSize), // -x
                        2 => new(halfWorldSize, width, halfWorldSize), //  y
                        3 => new(halfWorldSize, width, halfWorldSize), // -y
                        4 => new(halfWorldSize, halfWorldSize, width), //  z
                        5 => new(halfWorldSize, halfWorldSize, width), // -z
                        _ => new(0, 0, 0)
                    };

                    Color color = transitionIndex switch
                    {
                        0 => new(1.0f, 0.0f, 0.0f), //  x
                        1 => new(0.8f, 0.2f, 0.0f), // -x
                        2 => new(0.0f, 1.0f, 0.0f), //  y
                        3 => new(0.0f, 0.8f, 0.2f), // -y
                        4 => new(0.0f, 0.0f, 1.0f), //  z
                        5 => new(0.2f, 0.0f, 0.8f), // -z
                        _ => new(0, 0, 0)
                    };

                    color.a = 0.5f;

                    Gizmos.color = color;
                    Gizmos.DrawCube(worldPosition + transitionFaceOffset, transitionSize);
                }
            }

            public void DrawShapeVolumes(SDFScene scene)
            {
                HashSet<int3> bricksInShapeVolumes = new();

                foreach (int shapeIndex in intersectingCSGShapes)
                {
                    IntVolume brickVolume = GetBrickVolumeFromAABB(brickSize, levelScale * worldScale, scene.terrainShapes.Shapes[shapeIndex].Volume);
                    int3 initialIndex = brickVolume.coordinate;
                    int3 size = brickVolume.size;

                    for (int x = 0; x < size.x; x++)
                        for (int y = 0; y < size.y; y++)
                            for (int z = 0; z < size.z; z++)
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

            public void DrawBounds()
            {
                float3 worldBrickSize = brickSize * levelScale * worldScale;

                float3 brickmapLevelCentre = worldBrickSize * originIndex;
                float3 brickMapLevelSize = brickmapSize * worldBrickSize;

                Gizmos.color = DebugColors[levelIndex];
                Gizmos.DrawWireCube(brickmapLevelCentre, brickMapLevelSize);
            }

            public void DrawBricks()
            {
                foreach (Brick brick in bricks.Values)
                    brick.Draw(DebugColors[levelIndex]);
            }
        }
    }
}
#endif
