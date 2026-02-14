using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        const float k_SingleLineHeight = 20.0f;

        void DisplayDebugGUI()
        {
            Rect rect = new(10.0f, 10.0f, 260.0f, k_SingleLineHeight);

            int bricksPerLevel = k_BrickmapLevelSize * k_BrickmapLevelSize * k_BrickmapLevelSize;
            int extendedBrickSize = k_BrickSize + 3;
            int cellsPerBrick = extendedBrickSize * extendedBrickSize * extendedBrickSize;

            // Scene info
            GUI.Label(rect, $"Shapes in scene: {m_Scene.NumShapes}");
            rect.y += k_SingleLineHeight * 2.0f;

            // Brick constants
            GUI.Label(rect, $"Brick Size: {k_BrickSize} (+3) (Cells per brick: {cellsPerBrick})");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Brickmap Level Size: {k_BrickmapLevelSize}");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Num brickmap levels: {k_NumBrickmapLevels}");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Max memory usage: {k_NumBrickmapLevels * bricksPerLevel * cellsPerBrick * sizeof(float) / 1024 / 1024}MB"); // TODO
            rect.y += k_SingleLineHeight * 2.0f;

            // Brickmaps update time
            GUI.Label(rect, $"Avg density eval time: {Stopwatch.ToMilliseconds(s_AvgDensityEvalTime.Avarage())}ms");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Completed: {m_TotalMeshingTasks} meshing tasks in {Stopwatch.ToMilliseconds(m_TotalMeshingTime)}ms");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"   (avg: {Stopwatch.ToMilliseconds(s_AvgMeshingTime.Avarage())}ms)");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Total update time: {Stopwatch.ToMilliseconds(m_UpdateTime)}ms");
            rect.y += k_SingleLineHeight * 2.0f;

            // Brickmaps rendering time
            GUI.Label(rect, $"Total vertices: {s_DrawingVertices}");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Total indices: {s_DrawingIndices}");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Total Render time: {Stopwatch.ToMilliseconds(m_RenderTime)}ms");
            rect.y += k_SingleLineHeight * 2.0f;

            // Individual brickmaps
            foreach (Brickmap brickmap in m_BrickmapLevels)
                brickmap.DisplayDebugGUI(ref rect);
        }

        partial class Brickmap
        {
            public void DisplayDebugGUI(ref Rect rect)
            {
                GUI.Label(rect, $"Level idx: {levelIndex}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"   Num shapes: {shapes.Count}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"   Loaded bricks: {bricks.Count}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"   Update time: {Stopwatch.ToMilliseconds(updateTime)}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"   Major update time: {Stopwatch.ToMilliseconds(majorUpdateTime)}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"   Render time: {Stopwatch.ToMilliseconds(renderTime)}ms");
                rect.y += k_SingleLineHeight * 2.0f;
            }
        }
    }
}
