using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    [CustomEditor(typeof(ProceduralTerrain))]
    public partial class ProceduralTerrainEditor : Editor
    {
        SerializedProperty m_Material;

        SerializedProperty m_BrickmapDebugLevel;
        SerializedProperty m_EnableShapeVolumes;
        SerializedProperty m_EnableLoadedBricks;
        SerializedProperty m_EnableAllocatedBricks;
        SerializedProperty m_EnableBrickMapBorders;
        SerializedProperty m_DetachCamera;
        SerializedProperty m_EnableDensitySampler;
        SerializedProperty m_DensitySamplerPosition;

        SerializedProperty m_DisableRendering;
        SerializedProperty m_ColorClipmapLevels;

        GUIContent m_MaterialGUI;
        
        GUIContent m_BrickmapDebugLevelGUI;
        GUIContent m_EnableShapeVolumesGUI;
        GUIContent m_EnableLoadedBricksGUI;
        GUIContent m_EnableAllocatedBricksGUI;
        GUIContent m_EnableBrickMapBordersGUI;
        GUIContent m_DetachCameraFromOriginGUI;
        GUIContent m_EnableDensitySamplerGUI;
        GUIContent m_DensitySamplerPositionGUI;

        GUIContent m_DisableRenderingGUI;
        GUIContent m_ColorClipmapLevelsGUI;

        ProceduralTerrain m_Target;

        void OnEnable()
        {
            var o = new PropertyFetcher<ProceduralTerrain>(serializedObject);

            m_Material = o.Find(x => x.Material);
            
            m_BrickmapDebugLevel = o.Find(x => x.BrickmapDebugLevel);
            m_EnableShapeVolumes = o.Find(x => x.EnableShapeVolumes);
            m_EnableLoadedBricks = o.Find(x => x.EnableLoadedBricks);
            m_EnableAllocatedBricks = o.Find(x => x.EnableAllocatedBricks);
            m_EnableBrickMapBorders = o.Find(x => x.EnableBrickMapBorders);
            m_DetachCamera = o.Find(x => x.DetachCamera);
            m_EnableDensitySampler = o.Find(x => x.EnableDensitySampler);
            m_DensitySamplerPosition = o.Find(x => x.DensitySamplerPosition);

            m_DisableRendering = o.Find(x => x.DisableRendering);
            m_ColorClipmapLevels = o.Find(x => x.ColorClipmapLevels);

            m_MaterialGUI = new GUIContent("Material");

            m_BrickmapDebugLevelGUI = new GUIContent("Brickmap Level");
            m_EnableShapeVolumesGUI = new GUIContent("Shape Volumes");
            m_EnableLoadedBricksGUI = new GUIContent("Loaded Bricks");
            m_EnableAllocatedBricksGUI = new GUIContent("Allocated Bricks");
            m_EnableBrickMapBordersGUI = new GUIContent("Brick Map Bounds");
            m_DetachCameraFromOriginGUI = new GUIContent("Detatch Camera");
            m_EnableDensitySamplerGUI = new GUIContent("Enable");
            m_DensitySamplerPositionGUI = new GUIContent("Position");

            m_DisableRenderingGUI = new GUIContent("Disable Rendering");
            m_ColorClipmapLevelsGUI = new GUIContent("Highlight Clipmap Levels");

            m_Target = (ProceduralTerrain)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_Material, m_MaterialGUI);

            EditorGUILayout.LabelField("Brickmap Debug", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(m_BrickmapDebugLevel, m_BrickmapDebugLevelGUI);

            EditorGUILayout.PropertyField(m_EnableShapeVolumes, m_EnableShapeVolumesGUI);
            EditorGUILayout.PropertyField(m_EnableLoadedBricks, m_EnableLoadedBricksGUI);
            EditorGUILayout.PropertyField(m_EnableAllocatedBricks, m_EnableAllocatedBricksGUI);
            EditorGUILayout.PropertyField(m_EnableBrickMapBorders, m_EnableBrickMapBordersGUI);
            EditorGUILayout.PropertyField(m_DetachCamera, m_DetachCameraFromOriginGUI);

            EditorGUILayout.LabelField("Density Sampler", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_EnableDensitySampler, m_EnableDensitySamplerGUI);
            if (m_EnableDensitySampler.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_DensitySamplerPosition, m_DensitySamplerPositionGUI);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.LabelField("Rendering Debug", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_DisableRendering, m_DisableRenderingGUI);
            EditorGUILayout.PropertyField(m_ColorClipmapLevels, m_ColorClipmapLevelsGUI);

            serializedObject.ApplyModifiedProperties();
        }

        void OnSceneGUI()
        {
            if (m_EnableDensitySampler.boolValue) DrawDensitySamplerHandle();
        }

        void DrawDensitySamplerHandle()
        {
            Vector3 position = m_Target.DensitySamplerPosition;

            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Change Density Sampler Position");
                m_Target.DensitySamplerPosition = newPosition;
            }
        }
    }
}
