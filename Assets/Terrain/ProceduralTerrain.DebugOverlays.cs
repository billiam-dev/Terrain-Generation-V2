#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        [Range(1, k_NumBrickMapLevels)]
        public int BrickmapDebugLevel = 1;

        public bool EnableShapeVolumes;
        public bool EnableLoadedBricks;
        public bool EnableAllocatedBricks;
        public bool EnableBrickMapBorders;

        public bool DetachCamera;

        void DrawDebugGizmos()
        {
            Gizmos.matrix = Matrix4x4.identity;

            if (EnableShapeVolumes) m_Terrain.DrawShapeVolumeIndices(BrickmapDebugLevel - 1, m_Scene);
            if (EnableLoadedBricks) m_Terrain.DrawLoadedBricks();
            if (EnableAllocatedBricks) m_Terrain.DrawAllocatedBricks();
            if (EnableBrickMapBorders) m_Terrain.DrawMapLevelBounds(GetObserverPosition());
        }

        static Color RandomColor(int3 position)
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

        partial class SDFTerrain
        {
            partial class DensityBrickMap
            {
                internal void DrawShapeVolumeIndices(Scene scene)
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
                        float3 worldBrickSize = brickSize * sizeMultiplier * worldScale;

                        float3 brickCorner = worldBrickSize * brickIndex;
                        float3 bricksCentre = brickCorner + (worldBrickSize / 2.0f);

                        Gizmos.DrawCube(bricksCentre, worldBrickSize);
                    }
                }

                internal void DrawBounds(float3 observerPosition, Color color)
                {
                    int3 originIndex = GetOriginIndex(observerPosition);

                    float3 worldBrickSize = brickSize * sizeMultiplier * worldScale;

                    float3 brickmapLevelCentre = worldBrickSize * originIndex;
                    float3 brickMapLevelSize = mapSize * worldBrickSize;

                    Gizmos.color = color;
                    Gizmos.DrawWireCube(brickmapLevelCentre, brickMapLevelSize);
                }

                internal void DrawLoadedBricks(Camera camera)
                {
                    int3[] loadedBricks = new int3[bricks.Count];
                    bricks.Keys.CopyTo(loadedBricks, 0);

                    foreach (int3 brickIndex in loadedBricks)
                        DrawBrick(brickIndex, camera);
                }

                internal void DrawAllocatedBricks(Camera camera)
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
                        DrawBrick(brickIndex, camera);
                }

                void DrawBrick(int3 brickIndex, Camera camera)
                {
                    float3 worldBrickSize = brickSize * sizeMultiplier * worldScale;

                    float3 brickCorner = worldBrickSize * brickIndex;
                    float3 brickCentre = brickCorner + (worldBrickSize / 2.0f);

                    float viewingDistance = math.length((float3)camera.transform.position - brickCentre);

                    Color color = RandomColor(brickIndex);
                    color.a = math.clamp(1.0f - (viewingDistance / 256.0f), 0.05f, 1.0f);

                    Gizmos.color = color;
                    Gizmos.DrawWireCube(brickCentre, worldBrickSize);
                }
            }

            internal void DrawShapeVolumeIndices(int levelIndex, Scene scene)
            {
                brickMapLevels[levelIndex].DrawShapeVolumeIndices(scene);
            }

            internal void DrawMapLevelBounds(float3 observerPosition)
            {
                Color color = new(1.0f, 1.0f, 1.0f, 0.5f);
                foreach (DensityBrickMap brickMap in brickMapLevels)
                    brickMap.DrawBounds(observerPosition, color);
            }

            internal void DrawLoadedBricks()
            {
                Camera sceneCamera = SceneView.currentDrawingSceneView.camera;
                foreach (DensityBrickMap brickMap in brickMapLevels)
                    brickMap.DrawLoadedBricks(sceneCamera);
            }

            internal void DrawAllocatedBricks()
            {
                Camera sceneCamera = SceneView.currentDrawingSceneView.camera;
                foreach (DensityBrickMap brickMap in brickMapLevels)
                    brickMap.DrawAllocatedBricks(sceneCamera);
            }

            internal void Debug_GetBrickVolumeFromAABB(int level, float3 boundsPosition, float3 boundsVolume, out int3 initialIndex, out int3 volume)
            {
                brickMapLevels[level].GetBrickVolumeFromAABB(boundsPosition, boundsVolume, out initialIndex, out volume);
            }
        }
    }
}
#endif
