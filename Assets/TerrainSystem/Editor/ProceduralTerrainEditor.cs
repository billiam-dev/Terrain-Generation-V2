using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace TerrainSystem
{
    [CustomEditor(typeof(ProceduralTerrain))]
    partial class ProceduralTerrainEditor : Editor
    {
        static class Styles
        {
            public static readonly GUIContent Material = EditorGUIUtility.TrTextContent("Material", "Apply a material to the terrain.");
            public static readonly GUIContent UseStaticOrigin = EditorGUIUtility.TrTextContent("Use Static Origin", "Debug option, use the terrain position as the observer position.");
            public static readonly GUIContent ShowDebugGUI = EditorGUIUtility.TrTextContent("Show Debug GUI", "Debug option, show debug information on screen.");
            public static readonly GUIContent DrawBrickmapBorders = EditorGUIUtility.TrTextContent("Brickmap Bounds");
            public static readonly GUIContent DrawBricks = EditorGUIUtility.TrTextContent("Bricks");
            public static readonly GUIContent DrawShapeVolumes = EditorGUIUtility.TrTextContent("Shape Volumes");
            public static readonly GUIContent EnableDensityTester = EditorGUIUtility.TrTextContent("Enable Density Tester");
            public static readonly GUIContent DensityTesterPosition = EditorGUIUtility.TrTextContent("Position");
            public static readonly GUIContent HighlightBrickmapLevels = EditorGUIUtility.TrTextContent("Highlight Brickmap Levels");
            public static readonly GUIContent HighlightTransitionMeshes = EditorGUIUtility.TrTextContent("Highlight Transition Meshes");
        }

        SerializedProperty m_Material;
        SerializedProperty m_UseStaticOrigin;
        SerializedProperty m_ShowDebugGUI;
        SerializedProperty m_DrawBrickmapBorders;
        SerializedProperty m_DrawBricks;
        SerializedProperty m_DrawShapeVolumes;
        SerializedProperty m_EnableDensityTester;
        SerializedProperty m_DensityTesterPosition;

        ProceduralTerrain m_Target;

        void OnEnable()
        {
            var o = new PropertyFetcher<ProceduralTerrain>(serializedObject);

            m_Material = o.Find(x => x.Material);
            m_UseStaticOrigin = o.Find(x => x.UseStaticOrigin);
            m_ShowDebugGUI = o.Find(x => x.ShowDebugGUI);
            m_DrawBrickmapBorders = o.Find(x => x.m_DrawBrickmapBorders);
            m_DrawBricks = o.Find(x => x.m_DrawBricks);
            m_DrawShapeVolumes = o.Find(x => x.m_DrawShapeVolumes);
            m_EnableDensityTester = o.Find(x => x.m_EnableDensityTester);
            m_DensityTesterPosition = o.Find(x => x.m_DensityTesterPosition);

            m_Target = (ProceduralTerrain)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            //
            // Runtime properties
            //
            EditorGUILayout.PropertyField(m_Material, Styles.Material);
            EditorGUILayout.PropertyField(m_UseStaticOrigin, Styles.UseStaticOrigin);
            EditorGUILayout.PropertyField(m_ShowDebugGUI, Styles.ShowDebugGUI);

            //
            // Static debug options
            //
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug Options", EditorStyles.boldLabel);

            // Highlight Brickmap Levels Toggle
            EditorGUI.BeginChangeCheck();
            bool highlightBrickmapLevels = EditorGUILayout.Toggle(Styles.HighlightBrickmapLevels, ProceduralTerrain.HighlightBrickmapLevels);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Toggle Highlight Brickmap Levels");
                ProceduralTerrain.HighlightBrickmapLevels = highlightBrickmapLevels;
                SceneView.RepaintAll();
            }

            // Highlight Transition Meshes Toggle
            EditorGUI.BeginChangeCheck();
            bool highlightTransitionMeshes = EditorGUILayout.Toggle(Styles.HighlightTransitionMeshes, ProceduralTerrain.HighlightTransitionMeshes);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Toggle Highlight Transition Meshes");
                ProceduralTerrain.HighlightTransitionMeshes = highlightTransitionMeshes;
                SceneView.RepaintAll();
            }
            
            //
            // Debug overlays
            //
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug Overlays (Editor only)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_DrawBrickmapBorders, Styles.DrawBrickmapBorders);
            EditorGUILayout.PropertyField(m_DrawBricks, Styles.DrawBricks);
            EditorGUILayout.PropertyField(m_DrawShapeVolumes, Styles.DrawShapeVolumes);
            
            // Density tester
            EditorGUILayout.PropertyField(m_EnableDensityTester, Styles.EnableDensityTester);
            if (m_EnableDensityTester.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_DensityTesterPosition, Styles.DensityTesterPosition);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        void OnSceneGUI()
        {
            if (m_EnableDensityTester.boolValue)
                DrawDensitySamplerHandle();
        }

        void DrawDensitySamplerHandle()
        {
            Vector3 position = m_Target.m_DensityTesterPosition;

            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Change Density Sampler Position");
                m_Target.m_DensityTesterPosition = newPosition;
            }
        }
    }
}
