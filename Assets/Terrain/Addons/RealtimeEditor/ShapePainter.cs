/*
using UnityEngine;

namespace LevelGeneration.Terrain.Addons.RealtimeEditor
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TerrainEditor))]
    public class ShapePainter : MonoBehaviour
    {
        ShapeBrush[] m_ShapeBrushes;
        int m_NumActiveBrushes;

        TerrainEditor m_Terrain;

        void OnEnable()
        {
            m_Terrain = GetComponent<TerrainEditor>();
        }

        void OnDisable()
        {
            if (m_Terrain != null)
                m_Terrain.Clear();

            m_ShapeBrushes = null;
            m_NumActiveBrushes = -1;
        }

        void Update()
        {
            m_ShapeBrushes = GetComponentsInChildren<ShapeBrush>(true);

            int numActiveBrushes = 0;
            foreach (ShapeBrush shapeBrush in m_ShapeBrushes)
            {
                if (shapeBrush.isActiveAndEnabled)
                    numActiveBrushes++;
            }

            // Build queue
            if (numActiveBrushes != m_NumActiveBrushes)
            {
                m_Terrain.Clear();
                foreach (ShapeBrush shapeBrush in m_ShapeBrushes)
                {
                    if (shapeBrush.isActiveAndEnabled)
                    {
                        m_Terrain.AddShape(shapeBrush.Shape);
                        shapeBrush.IsDirty = false;
                    }
                }

                m_NumActiveBrushes = numActiveBrushes;
            }

            // Modify Queue
            for (int i = 0; i < m_ShapeBrushes.Length; i++)
            {
                ShapeBrush shapeBrush = m_ShapeBrushes[i];

                if (shapeBrush.isActiveAndEnabled)
                {
                    if (shapeBrush.IsDirty)
                    {
                        m_Terrain.ReplaceShape(i, shapeBrush.Shape);
                        shapeBrush.IsDirty = false;
                    }
                }
            }
        }
    }
}
*/
