using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        TerrainDebugInfo m_DebugInfo;
        const float k_SingleLineHeight = 20.0f;

        void InitializeDebugGUI()
        {
            m_DebugInfo = new();

            m_DebugInfo.brickSize = k_BrickSize;
            m_DebugInfo.brickmapLevelSize = k_BrickmapLevelSize;
            m_DebugInfo.numBrickmapLevels = k_NumBrickmapLevels;

            m_DebugInfo.brickampLevels = new BrickampLevelInfo[k_NumBrickmapLevels];
            for (int i = 0; i < k_NumBrickmapLevels; i++)
            {
                m_DebugInfo.brickampLevels[i] = new();

                m_DebugInfo.brickampLevels[i].level = i;
                m_DebugInfo.brickampLevels[i].densityJobTimes ??= new();
            }
        }

        void DisplayDebugGUI()
        {
            Rect rect = new(10.0f, 10.0f, 260.0f, k_SingleLineHeight);
            m_DebugInfo.DisplayGUI(ref rect);
        }

        class TerrainDebugInfo
        {
            public int brickSize;
            public int brickmapLevelSize;
            public int numBrickmapLevels;

            public int numShapesInScene;

            public BrickampLevelInfo[] brickampLevels;

            public int numChunks;
            public int numChunkRendererd;
            public int numRemeshTasks;
            public double remeshTaskTime;
            public double clipmapUpdateTime;
            public double clipmapRenderingTime;

            public void DisplayGUI(ref Rect rect)
            {
                int extBrickSize = brickSize + 3;
                int cellsPerBrick = extBrickSize * extBrickSize * extBrickSize;

                int totalBricksLoaded = 0;
                int totalBricksAllocated = 0;
                double totalBrickmapUpdateTime = 0.0;

                foreach (BrickampLevelInfo brickmapLevel in brickampLevels)
                {
                    totalBricksLoaded += brickmapLevel.numBricksLoaded;
                    totalBricksAllocated += brickmapLevel.numBricksAllocated;
                    totalBrickmapUpdateTime += brickmapLevel.updateTime;
                }

                int totalBrickmapMemoryUsage = totalBricksAllocated * cellsPerBrick * sizeof(float); // (bytes)
                double totalFrameTime = totalBrickmapUpdateTime + clipmapUpdateTime + clipmapRenderingTime;
                int fps = (int)math.floor(1.0 / totalFrameTime);

                // Scene info
                GUI.Label(rect, $"Shapes in scene: {numShapesInScene}");
                rect.y += k_SingleLineHeight * 2.0f;

                // Density cache info
                GUI.Label(rect, $"Brick Size: {brickSize} (+3) (Cells per brick: {cellsPerBrick})");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Brickmap Level Size: {brickmapLevelSize}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Num brickmap levels: {numBrickmapLevels}");
                rect.y += k_SingleLineHeight * 2.0f;

                foreach (BrickampLevelInfo brickmapLevel in brickampLevels)
                    brickmapLevel.DisplayGUI(ref rect);
                rect.y += k_SingleLineHeight * 2.0f;

                GUI.Label(rect, $"Total loaded bricks: {totalBricksLoaded}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Total allocated bricks: {totalBricksAllocated} ({totalBrickmapMemoryUsage / 1024}kb)");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Total brickmap update time: {Stopwatch.ToMilliseconds(totalBrickmapUpdateTime)}ms");
                rect.y += k_SingleLineHeight * 2.0f;

                // Rendering info
                GUI.Label(rect, $"Total chunks: {numChunks}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Drawing chunks: {numChunkRendererd}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Num remesh tasks: {numRemeshTasks} ({Stopwatch.ToMilliseconds(remeshTaskTime)}ms)");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Clipmap update time: {Stopwatch.ToMilliseconds(clipmapUpdateTime)}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Rendering time: {Stopwatch.ToMilliseconds(clipmapRenderingTime)}ms");
                rect.y += k_SingleLineHeight * 2.0f;

                GUI.Label(rect, $"Total frame time: {Stopwatch.ToMilliseconds(totalFrameTime)}ms ({fps}fps)");
            }
        }

        class BrickampLevelInfo
        {
            public int level;

            public int numShapes;

            public int numBricksLoaded;
            public int numBricksAllocated;
            
            public int numBricksModified;
            public int numDensityJobs;
            public double evaluationTime;
            public MeanTime densityJobTimes;
            
            public double updateTime;

            public void DisplayGUI(ref Rect rect)
            {
                GUI.Label(rect, $"Level: {level}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Num shapes: {numShapes}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Total loaded bricks: {numBricksLoaded} ({numBricksAllocated} allocated)");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Total recomputed bricks: {numBricksModified} ({Stopwatch.ToMilliseconds(evaluationTime)}ms)");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Avg density JOB time: {Stopwatch.ToMilliseconds(densityJobTimes.Avarage())}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Brickmap update time: {Stopwatch.ToMilliseconds(updateTime)}ms");
            }
        }
    }
}
