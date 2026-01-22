using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        struct DebugInfo
        {
            // Brickmap constants
            public int brickSize;
            public int cellsPerBrick;
            public int brickmapLevelSize;

            // Runtime info
            public int shapeCount;
            public int numBricks;
            public int numBricksAllocated;
            public int recomputedBricks;
            public double recomputationTime;

            readonly double[] densityJobTimes;
            int densityJobTimeIdx;

            public double mapUpdateTime;

            const float k_SingleLineHeight = 20.0f;

            public DebugInfo(int i) // Useless parameter 'cause structs can't have empty constructors.
            {
                brickSize = 0;
                cellsPerBrick = 0;
                brickmapLevelSize = 0;

                shapeCount = 0;
                numBricks = 0;
                numBricksAllocated = 0;
                recomputedBricks = 0;
                recomputationTime = 0.0;

                densityJobTimes = new double[10];
                densityJobTimeIdx = 0;

                mapUpdateTime = 0.0;
            }

            public void AddJobTime(double time)
            {
                densityJobTimes[densityJobTimeIdx] = time;

                densityJobTimeIdx++;
                if (densityJobTimeIdx >= densityJobTimes.Length)
                    densityJobTimeIdx = 0;
            }

            readonly double AvarageDensityJobTime()
            {
                double t = 0;
                int count = 0;
                for (int i = 0; i < densityJobTimes.Length; i++)
                {
                    t += densityJobTimes[i];
                    if (t > 0.0)
                        count++;
                }

                t /= count;

                return t;
            }

            public readonly void DisplayGUI()
            {
                Rect rect = new(10.0f, 10.0f, 260.0f, k_SingleLineHeight);

                int brickMapMemoryUsageBytes = numBricksAllocated * cellsPerBrick * sizeof(float);

                // Constants
                GUI.Label(rect, $"Brick Size: {brickSize} (Total cells: {cellsPerBrick})");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Brickmap Level Size: {brickmapLevelSize}");
                rect.y += k_SingleLineHeight;

                // Runtime info
                GUI.Label(rect, $"Shapes: {shapeCount}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Total bricks: {numBricks}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Bricks allocated: {numBricksAllocated}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Approximate memory: {brickMapMemoryUsageBytes / 1000}kb");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Last recomputed bricks: {recomputedBricks}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Total recomputation time: {Stopwatch.ToMilliseconds(recomputationTime)}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Avarage density evaluation time: {Stopwatch.ToMilliseconds(AvarageDensityJobTime())}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Map update time: {Stopwatch.ToMilliseconds(mapUpdateTime)}ms");
            }
        }

        DebugInfo m_DebugInfo;

        void InitializeDebug()
        {
            m_DebugInfo = new(0)
            {
                brickSize = k_BrickSize,
                cellsPerBrick = k_CellsPerBrick,
                brickmapLevelSize = k_BrickmapLevelSize
            };
        }

        void OnGUI() => m_DebugInfo.DisplayGUI();

#if UNITY_EDITOR
        [SerializeField]
        bool m_DrawShapeVolumes;

        [SerializeField]
        bool m_DrawLoadedBricks;

        [SerializeField]
        bool m_DrawAllocatedBricks;

        [SerializeField]
        bool m_DrawBrickMapBorders;

        [SerializeField]
        bool m_DetachCamera;

        void OnDrawGizmos()
        {
            if (!isActiveAndEnabled)
                return;

            Gizmos.matrix = Matrix4x4.identity;

            if (m_DrawShapeVolumes) DrawShapeVolumes();
            if (m_DrawLoadedBricks) DrawLoadedBricks();
            if (m_DrawAllocatedBricks) DrawAllocatedBricks();
            if (m_DrawBrickMapBorders) DrawBrickMapBorders();
        }

        void DrawShapeVolumes()
        {
            HashSet<int3> bricksInShapeVolumes = new();

            foreach (Shape shape in m_Scene.Shapes)
            {
                shape.ComputeVolume(out float3 boundsPosition, out float3 boundsVolume);
                GetBrickVolumeFromAABB(boundsPosition, boundsVolume, out int3 initialIndex, out int3 volume);

                for (int x = 0; x < volume.x; x++)
                    for (int y = 0; y < volume.y; y++)
                        for (int z = 0; z < volume.z; z++)
                            bricksInShapeVolumes.Add(initialIndex + new int3(x, y, z));
            }

            Gizmos.color = new Color(1.0f, 0.1f, 0.0f, 0.1f);
            foreach (int3 brickIndex in bricksInShapeVolumes)
            {
                float3 brickSize = k_BrickSize * k_TerrainScale;
                float3 brickCorner = brickSize * brickIndex;
                float3 bricksCentre = brickCorner + (brickSize / 2.0f);

                Gizmos.DrawCube(bricksCentre, brickSize);
            }
        }

        void DrawBrickMapBorders()
        {
            float3 scaledCameraPos = GetObserverPosition() * (1.0f / k_TerrainScale);
            int3 originIndex = (int3)math.floor(scaledCameraPos / k_BrickSize);

            float3 brickmapLevelCentre = k_BrickSize * k_TerrainScale * (float3)originIndex;
            float3 brickMapLevelSize = k_BrickmapLevelSize * k_BrickSize * k_TerrainScale;

            Gizmos.color = new Color(1.0f, 1.0f, 1.0f, 0.5f);
            Gizmos.DrawWireCube(brickmapLevelCentre, brickMapLevelSize);
        }

        void DrawLoadedBricks()
        {
            Camera sceneCamera = SceneView.currentDrawingSceneView.camera;
            float viewingDistance;

            float3 worldBrickSize = k_BrickSize * k_TerrainScale;
            float3 brickCorner;
            float3 brickCentre;

            foreach (int3 brickIndex in m_BrickMap.GetLoadedBrickIndices())
            {
                brickCorner = worldBrickSize * brickIndex;
                brickCentre = brickCorner + (worldBrickSize / 2.0f);

                viewingDistance = math.length((float3)sceneCamera.transform.position - brickCentre);

                Color color = RandomColor(brickIndex);
                color.a = math.clamp(1.0f - (viewingDistance / 256.0f), 0.05f, 1.0f);

                Gizmos.color = color;
                Gizmos.DrawWireCube(brickCentre, worldBrickSize);
            }
        }

        void DrawAllocatedBricks()
        {
            Camera sceneCamera = SceneView.currentDrawingSceneView.camera;
            float viewingDistance;

            float3 worldBrickSize = k_BrickSize * k_TerrainScale;
            float3 brickCorner;
            float3 brickCentre;

            foreach (int3 brickIndex in m_BrickMap.GetAllocatedBrickIndices())
            {
                brickCorner = worldBrickSize * brickIndex;
                brickCentre = brickCorner + (worldBrickSize / 2.0f);

                viewingDistance = math.length((float3)sceneCamera.transform.position - brickCentre);

                Color color = RandomColor(brickIndex);
                color.a = math.clamp(1.0f - (viewingDistance / 256.0f), 0.05f, 1.0f);

                Gizmos.color = color;
                Gizmos.DrawWireCube(brickCentre, worldBrickSize);
            }
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
#endif
    }
}
