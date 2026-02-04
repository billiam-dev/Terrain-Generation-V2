using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        TerrainDebugInfo m_DebugInfo;

        void InitializeDebugGUI()
        {
            m_DebugInfo.brickSize = k_BrickSize;
            m_DebugInfo.brickmapLevelSize = k_BrickmapLevelSize;
            m_DebugInfo.numBrickmapLevels = k_NumBrickMapLevels;

            m_DebugInfo.densityJobTimes ??= new();
            m_DebugInfo.meshingJobTimes ??= new();
        }

        void DisplayDebugGUI()
        {
            m_DebugInfo.DisplayGUI();
        }

        struct TerrainDebugInfo
        {
            public int brickSize;
            public int brickmapLevelSize;
            public int numBrickmapLevels;

            public int shapeCount;

            public int numBricksLoaded;
            public int numBricksAllocated;
            public MeanTime densityJobTimes;
            public double brickmapUpdateTime;

            public int numChunks;
            public int chunkRendererdThisFrame;
            public MeanTime meshingJobTimes;
            public double clipmapUpdateTime;
            public double clipmapRenderingTime;

            const float k_SingleLineHeight = 20.0f;

            public readonly void DisplayGUI()
            {
                Rect rect = new(10.0f, 10.0f, 260.0f, k_SingleLineHeight);

                int cellsPerBrick = brickSize * brickSize * brickSize;
                int brickMapMemoryUsageBytes = numBricksAllocated * cellsPerBrick * sizeof(float);

                // Constants
                GUI.Label(rect, $"Brick Size: {brickSize} (Cells per brick: {cellsPerBrick})");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Brickmap Level Size: {brickmapLevelSize}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Num brickmap levels: {numBrickmapLevels}");
                rect.y += k_SingleLineHeight * 2.0f;

                // Scene info
                GUI.Label(rect, $"Shapes in scene: {shapeCount}");
                rect.y += k_SingleLineHeight * 2.0f;

                // Density cache info
                GUI.Label(rect, $"Total loaded bricks: {numBricksLoaded}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Total allocated bricks: {numBricksAllocated} ({brickMapMemoryUsageBytes / 1024}kb)");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Avg density JOB time: {Stopwatch.ToMilliseconds(densityJobTimes.Avarage())}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Brickmap update time: {Stopwatch.ToMilliseconds(brickmapUpdateTime)}ms");
                rect.y += k_SingleLineHeight * 2.0f;

                // Rendering info
                GUI.Label(rect, $"Total chunks: {numChunks}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Drawing chunks: {chunkRendererdThisFrame}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Avg mesher JOB time: {Stopwatch.ToMilliseconds(meshingJobTimes.Avarage())}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Clipmap update time: {Stopwatch.ToMilliseconds(clipmapUpdateTime)}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Rendering time: {Stopwatch.ToMilliseconds(clipmapRenderingTime)}ms");
                rect.y += k_SingleLineHeight * 2.0f;

                GUI.Label(rect, $"Total frame time: {Stopwatch.ToMilliseconds(brickmapUpdateTime + clipmapUpdateTime + clipmapRenderingTime)}ms");
            }
        }
    }
}
