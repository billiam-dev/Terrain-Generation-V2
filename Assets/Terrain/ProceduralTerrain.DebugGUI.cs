using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        TerrainDebugInfo m_DebugInfo;

        void InitializeDebugGUI()
        {
            m_DebugInfo = new()
            {
                brickSize = k_BrickSize,
                cellsPerBrick = k_BrickSize * k_BrickSize * k_BrickSize,
                brickmapLevelSize = k_BrickmapLevelSize,
                densityJobTimes = new(),
                meshingJobTimes = new()
            };
        }

        void DisplayDebugGUI()
        {
            m_DebugInfo.DisplayGUI();
        }

        struct TerrainDebugInfo
        {
            public int brickSize;
            public int cellsPerBrick;
            public int brickmapLevelSize;

            public int shapeCount;

            public int numBricksLoaded;
            public int numBricksAllocated;
            public int recomputedBricks;
            public double recomputationTime;
            public MeanTime densityJobTimes;
            public double brickmapUpdateTime;

            public MeanTime meshingJobTimes;
            public int chunkRendererdThisFrame;
            public double frameTime;

            const float k_SingleLineHeight = 20.0f;

            public readonly void DisplayGUI()
            {
                Rect rect = new(10.0f, 10.0f, 260.0f, k_SingleLineHeight);

                int brickMapMemoryUsageBytes = numBricksAllocated * cellsPerBrick * sizeof(float);

                // Constants
                GUI.Label(rect, $"Brick Size: {brickSize} (Cells per brick: {cellsPerBrick})");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Brickmap Level Size: {brickmapLevelSize}");
                rect.y += k_SingleLineHeight * 2.0f;

                // Scene info
                GUI.Label(rect, $"Shapes in scene: {shapeCount}");
                rect.y += k_SingleLineHeight * 2.0f;

                // Density cache info
                GUI.Label(rect, $"Loaded bricks: {numBricksLoaded}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Allocated bricks: {numBricksAllocated} ({brickMapMemoryUsageBytes / 1024}kb)");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Last modified bricks: {recomputedBricks}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Total recomputation time: {Stopwatch.ToMilliseconds(recomputationTime)}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Avg density JOB time: {Stopwatch.ToMilliseconds(densityJobTimes.Avarage())}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Brickmap update time: {Stopwatch.ToMilliseconds(brickmapUpdateTime)}ms");
                rect.y += k_SingleLineHeight * 2.0f;

                // Rendering info
                GUI.Label(rect, $"Drawing chunks: {chunkRendererdThisFrame}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Avg mesher JOB time: {Stopwatch.ToMilliseconds(meshingJobTimes.Avarage())}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Rendering time: {Stopwatch.ToMilliseconds(frameTime)}ms");
            }
        }
    }
}
