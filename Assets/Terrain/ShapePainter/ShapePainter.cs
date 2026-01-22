using UnityEngine;

namespace LevelGeneration.Terrain.ShapePainter
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProceduralTerrain))]
    public class ShapePainter : MonoBehaviour
    {
        ShapeBrush[] m_ShapeBrushes;
        int m_TotalShapeBrushes;

        ProceduralTerrain m_Terrain;

        void OnEnable()
        {
            m_Terrain = GetComponent<ProceduralTerrain>();

            m_ShapeBrushes = GetComponentsInChildren<ShapeBrush>(true);
            m_TotalShapeBrushes = m_ShapeBrushes.Length;

            foreach (ShapeBrush shapeBrush in m_ShapeBrushes)
            {
                m_Terrain.AddShape(shapeBrush.Shape);
                shapeBrush.IsDirty = false;
            }
        }

        void OnDisable()
        {
            if (m_Terrain != null)
                m_Terrain.ClearShapes();

            m_ShapeBrushes = null;
            m_TotalShapeBrushes = -1;
        }

        void Update()
        {
            for (int i = 0; i < m_ShapeBrushes.Length; i++)
            {
                ShapeBrush shapeBrush = m_ShapeBrushes[i];

                if (shapeBrush.IsDirty)
                {
                    m_Terrain.ReplaceShape(i, shapeBrush.Shape);
                    shapeBrush.IsDirty = false;
                }
            }
        }
    }
}
