using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        TerrainDebugInfo m_DebugInfo;

        void InitializeDebugGUI()
        {
            m_DebugInfo = new(0)
            {
                brickSize = k_BrickSize,
                cellsPerBrick = k_BrickSize * k_BrickSize * k_BrickSize,
                brickmapLevelSize = k_BrickmapLevelSize,
                shapeCount = 0
            };
        }

        void DisplayDebugGUI()
        {
            m_DebugInfo.DisplayGUI();
        }

        struct TerrainDebugInfo
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

            internal TerrainDebugInfo(int i) // Useless parameter 'cause structs can't have empty constructors.
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

            internal void AddJobTime(double time)
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

                return t / count;
            }

            internal readonly void DisplayGUI()
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
                GUI.Label(rect, $"Approximate memory: {brickMapMemoryUsageBytes / 1024}kb");
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
    }
}
