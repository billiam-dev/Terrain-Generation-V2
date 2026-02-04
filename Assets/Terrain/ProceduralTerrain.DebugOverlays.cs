#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        [Range(-1, k_NumBrickmapLevels - 1)]
        public int BrickmapDebugLevel;

        public bool EnableLoadedBricks;
        public bool EnableAllocatedBricks;
        public bool DrawEdgeBricks;
        public bool EnableShapeVolumes;

        public bool EnableBrickMapBorders;
        public bool DetachCamera;

        void DrawDebugGizmos()
        {
            Gizmos.matrix = Matrix4x4.identity;

            if (EnableShapeVolumes) m_DensityCache.DrawShapeVolumeIndices(BrickmapDebugLevel, m_Scene);
            if (EnableLoadedBricks) m_DensityCache.DrawLoadedBricks(BrickmapDebugLevel, DrawEdgeBricks);
            if (EnableAllocatedBricks) m_DensityCache.DrawAllocatedBricks(BrickmapDebugLevel, DrawEdgeBricks);
            if (EnableBrickMapBorders) m_DensityCache.DrawMapLevelsBounds(BrickmapDebugLevel);
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

        partial class DensityCache
        {
            partial class SparseBrickMap
            {
                public void DrawShapeVolumeIndices(SDFScene scene)
                {
                    HashSet<int3> bricksInShapeVolumes = new();

                    foreach (Shape shape in scene.Shapes)
                    {
                        shape.ComputeVolume(out float3 boundsPosition, out float3 boundsVolume);
                        GetBrickVolumeFromAABB( boundsPosition, boundsVolume, out int3 initialIndex, out int3 volume);

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
                    float3 brickMapLevelSize = (mapSize - 2) * worldBrickSize;

                    Gizmos.color = color;
                    Gizmos.DrawWireCube(brickmapLevelCentre, brickMapLevelSize);
                }

                public void DrawLoadedBricks(Camera camera, Color color, bool indludeEdges)
                {
                    int3[] loadedBricks = new int3[bricks.Count];
                    bricks.Keys.CopyTo(loadedBricks, 0);

                    foreach (int3 brickIndex in loadedBricks)
                    {
                        bool isEdgeBrick = BrickOnEdge(brickIndex);

                        if (!isEdgeBrick || (isEdgeBrick && indludeEdges))
                            DrawBrick(brickIndex, camera, color, 0.05f);
                    }
                        
                }

                public void DrawAllocatedBricks(Camera camera, Color color, bool indludeEdges)
                {
                    int3[] allocatedBricks = new int3[numBricksAllocated];
                    int i = 0;

                    foreach (int3 brickIndex in bricks.Keys)
                    {
                        if (bricks[brickIndex].IsAllocated)
                        {
                            allocatedBricks[i] = brickIndex;
                            i++;
                        }
                    }

                    foreach (int3 brickIndex in allocatedBricks)
                    {
                        bool isEdgeBrick = BrickOnEdge(brickIndex);

                        if (!isEdgeBrick || (isEdgeBrick && indludeEdges))
                            DrawBrick(brickIndex, camera, color, 1.0f);
                    }
                }

                void DrawBrick(int3 brickIndex, Camera camera, Color color, float alphaMultiplier)
                {
                    float3 worldBrickSize = brickSize * levelScale * worldScale;

                    float3 brickCorner = worldBrickSize * brickIndex;
                    float3 brickCentre = brickCorner + (worldBrickSize / 2.0f);

                    float viewingDistance = math.length((float3)camera.transform.position - brickCentre);

                    color += RandomPastelColor(brickIndex);
                    color.a = math.clamp(1.0f - (viewingDistance / 256.0f), 0.05f, 1.0f) * alphaMultiplier;

                    Gizmos.color = color;
                    Gizmos.DrawWireCube(brickCentre, worldBrickSize);
                }
            }

            readonly Color[] k_BrickmapLevelDebugColors = new Color[]
            {
                new(1.0f, 0.2f, 0.0f, 1.0f),
                new(0.0f, 1.0f, 0.2f, 0.8f),
                new(0.2f, 0.0f, 1.0f, 0.6f),
                new(0.8f, 0.8f, 0.8f, 0.4f),
                new(0.4f, 0.4f, 0.4f, 0.2f),
                new(0.1f, 0.1f, 0.1f, 0.1f)
            };

            public void DrawShapeVolumeIndices(int levelIndex, SDFScene scene)
            {
                if (levelIndex == -1)
                {
                    for (int i = 0; i < brickMapLevels.Length; i++)
                        brickMapLevels[i].DrawShapeVolumeIndices(scene);
                }
                else
                {
                    brickMapLevels[levelIndex].DrawShapeVolumeIndices(scene);
                }
            }

            public void DrawMapLevelsBounds(int levelIndex)
            {
                if (levelIndex == -1)
                {
                    for (int i = 0; i < brickMapLevels.Length; i++)
                        brickMapLevels[i].DrawBounds(k_BrickmapLevelDebugColors[i]);
                }
                else
                {
                    brickMapLevels[levelIndex].DrawBounds(k_BrickmapLevelDebugColors[levelIndex]);
                }
            }

            public void DrawLoadedBricks(int levelIndex, bool includeEdges)
            {
                Camera sceneCamera = SceneView.currentDrawingSceneView.camera;

                if (levelIndex == -1)
                {
                    for (int i = 0; i < brickMapLevels.Length; i++)
                        brickMapLevels[i].DrawLoadedBricks(sceneCamera, k_BrickmapLevelDebugColors[i], includeEdges);
                }
                else
                {
                    brickMapLevels[levelIndex].DrawLoadedBricks(sceneCamera, k_BrickmapLevelDebugColors[levelIndex], includeEdges);
                }
            }

            public void DrawAllocatedBricks(int levelIndex, bool includeEdges)
            {
                Camera sceneCamera = SceneView.currentDrawingSceneView.camera;

                if (levelIndex == -1)
                {
                    for (int i = 0; i < brickMapLevels.Length; i++)
                        brickMapLevels[i].DrawAllocatedBricks(sceneCamera, k_BrickmapLevelDebugColors[i], includeEdges);
                }
                else
                {
                    brickMapLevels[levelIndex].DrawAllocatedBricks(sceneCamera, k_BrickmapLevelDebugColors[levelIndex], includeEdges);
                }
            }
        }
    }
}
#endif
