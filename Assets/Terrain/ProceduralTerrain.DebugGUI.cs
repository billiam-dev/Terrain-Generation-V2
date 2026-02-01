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

            // Density cache info
            public int shapeCount;
            public int numBricks;
            public int numBricksAllocated;
            public int recomputedBricks;
            public double recomputationTime;
            public double mapUpdateTime;

            public readonly MeanTime densityJobTimes;

            // Rendering info
            public readonly MeanTime meshingJobTimes;
            public int chunkRendererdThisFrame;
            public double frameTime;

            const float k_SingleLineHeight = 20.0f;

            public TerrainDebugInfo(int i) // Useless parameter 'cause structs can't have empty constructors.
            {
                brickSize = 0;
                cellsPerBrick = 0;
                brickmapLevelSize = 0;

                shapeCount = 0;
                numBricks = 0;
                numBricksAllocated = 0;
                recomputedBricks = 0;
                recomputationTime = 0.0;
                mapUpdateTime = 0.0;
                densityJobTimes = new MeanTime();

                chunkRendererdThisFrame = 0;
                meshingJobTimes = new MeanTime();
                frameTime = 0.0;
            }

            public readonly void DisplayGUI()
            {
                Rect rect = new(10.0f, 10.0f, 260.0f, k_SingleLineHeight);

                int brickMapMemoryUsageBytes = numBricksAllocated * cellsPerBrick * sizeof(float);

                // Constants
                GUI.Label(rect, $"Brick Size: {brickSize} (Total cells: {cellsPerBrick})");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Brickmap Level Size: {brickmapLevelSize}");
                rect.y += k_SingleLineHeight * 2.0f;

                // Density cache info
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
                GUI.Label(rect, $"Avarage density evaluation time: {Stopwatch.ToMilliseconds(densityJobTimes.Avarage())}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Map update time: {Stopwatch.ToMilliseconds(mapUpdateTime)}ms");
                rect.y += k_SingleLineHeight * 2.0f;

                // Rendering info
                GUI.Label(rect, $"Drawing chunks: {chunkRendererdThisFrame}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Avarage meshing time: {Stopwatch.ToMilliseconds(meshingJobTimes.Avarage())}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Rendering time: {Stopwatch.ToMilliseconds(frameTime)}ms");
            }
        }
    }
}
