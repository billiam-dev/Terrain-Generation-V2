using UnityEngine;

namespace TerrainSystem
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        const float k_SingleLineHeight = 20.0f;

        void DisplayDebugGUI()
        {
            Rect rect = new(10.0f, 10.0f, 260.0f, k_SingleLineHeight);

            int extendedBrickSize = k_BrickSize + 3;
            int cellsPerBrick = extendedBrickSize * extendedBrickSize * extendedBrickSize;

            int brickmapMemoryUsage = 0;
            for (int i = 0; i < k_NumBrickmapLevels; i++)
                brickmapMemoryUsage += m_BrickmapLevels[i].MemoryUsageBytes();

            // Scene info TODO
            //GUI.Label(rect, $"Shapes in scene: {m_Scene.NumShapes}");
            //rect.y += k_SingleLineHeight * 2.0f;

            // Brick constants
            GUI.Label(rect, $"Brick Size: {k_BrickSize} (+3) (Cells per brick: {cellsPerBrick})");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Brickmap Level Size: {k_BrickmapLevelSize}");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Num brickmap levels: {k_NumBrickmapLevels}");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"~Memory usage: {brickmapMemoryUsage / 1024 / 1024}MB");
            rect.y += k_SingleLineHeight * 2.0f;

            // Brickmaps update time
            GUI.Label(rect, $"Avg density eval time: {Stopwatch.ToMilliseconds(m_DensityEvaluator.AvgExecutionTime)}ms");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Completed: {m_Mesher.NumTasksCompleted} meshing tasks in {Stopwatch.ToMilliseconds(m_Mesher.TotalExecutionTime)}ms");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"   (avg: {Stopwatch.ToMilliseconds(m_Mesher.AvgExecutionTime)}ms)");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Total update time: {Stopwatch.ToMilliseconds(m_UpdateTime)}ms");
            rect.y += k_SingleLineHeight * 2.0f;

            // Brickmaps rendering time
            GUI.Label(rect, $"Total vertices: {s_VertexCount}");
            rect.y += k_SingleLineHeight;
            GUI.Label(rect, $"Total indices: {s_IndexCount}");
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
                GUI.Label(rect, $"   Num shapes: {intersectingCSGShapes.Count}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"   Loaded bricks: {bricks.Count}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"   Update time: {Stopwatch.ToMilliseconds(updateTime)}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"   Origin shift time: {Stopwatch.ToMilliseconds(originShiftTime)}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"   Render time: {Stopwatch.ToMilliseconds(renderTime)}ms");
                rect.y += k_SingleLineHeight * 2.0f;
            }
        }
    }
}
