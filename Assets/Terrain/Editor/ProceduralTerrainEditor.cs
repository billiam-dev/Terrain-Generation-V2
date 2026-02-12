using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    [CustomEditor(typeof(ProceduralTerrain))]
    public partial class ProceduralTerrainEditor : Editor
    {
        // Properties
        SerializedProperty m_Material;
        SerializedProperty m_ForceMainCamera;
        SerializedProperty m_UseStaticOrigin;
        
        SerializedProperty m_DrawBrickmapBorders;
        SerializedProperty m_DrawBricks;
        SerializedProperty m_DrawShapeVolumes;

        SerializedProperty m_EnableDensityTester;
        SerializedProperty m_DensityTesterPosition;

        // GUI Contents
        GUIContent m_MaterialGUI;
        GUIContent m_ForceMainCameraGUI;
        GUIContent m_UseStaticOriginGUI;
        
        GUIContent m_DrawBrickmapBordersGUI;
        GUIContent m_DrawBricksGUI;
        GUIContent m_DrawShapeVolumesGUI;

        GUIContent m_EnableDensityTesterGUI;
        GUIContent m_DensityTesterPositionGUI;

        ProceduralTerrain m_Target;

        void OnEnable()
        {
            var o = new PropertyFetcher<ProceduralTerrain>(serializedObject);

            m_Material = o.Find(x => x.Material);
            m_ForceMainCamera = o.Find(x => x.ForceMainCamera);
            m_UseStaticOrigin = o.Find(x => x.UseStaticOrigin);

            m_DrawBrickmapBorders = o.Find(x => x.m_DrawBrickmapBorders);
            m_DrawBricks = o.Find(x => x.m_DrawBricks);
            m_DrawShapeVolumes = o.Find(x => x.m_DrawShapeVolumes);

            m_EnableDensityTester = o.Find(x => x.m_EnableDensityTester);
            m_DensityTesterPosition = o.Find(x => x.m_DensityTesterPosition);

            m_MaterialGUI = new GUIContent("Material");
            m_ForceMainCameraGUI = new GUIContent("Force Use Main Camera");
            m_UseStaticOriginGUI = new GUIContent("Use Static Origin");

            m_DrawBrickmapBordersGUI = new GUIContent("Brickmap Bounds");
            m_DrawBricksGUI = new GUIContent("Bricks");
            m_DrawShapeVolumesGUI = new GUIContent("Shape Volumes");

            m_EnableDensityTesterGUI = new GUIContent("Enable Density Tester");
            m_DensityTesterPositionGUI = new GUIContent("Position");

            m_Target = (ProceduralTerrain)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_Material, m_MaterialGUI);
            EditorGUILayout.PropertyField(m_ForceMainCamera, m_ForceMainCameraGUI);
            EditorGUILayout.PropertyField(m_UseStaticOrigin, m_UseStaticOriginGUI);

            EditorGUILayout.LabelField("Debug Options (Editor only)", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_DrawBrickmapBorders, m_DrawBrickmapBordersGUI);
            EditorGUILayout.PropertyField(m_DrawBricks, m_DrawBricksGUI);
            EditorGUILayout.PropertyField(m_DrawShapeVolumes, m_DrawShapeVolumesGUI);
            
            EditorGUILayout.PropertyField(m_EnableDensityTester, m_EnableDensityTesterGUI);
            if (m_EnableDensityTester.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_DensityTesterPosition, m_DensityTesterPositionGUI);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        void OnSceneGUI()
        {
            if (m_EnableDensityTester.boolValue) DrawDensitySamplerHandle();
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
