using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    [CustomEditor(typeof(ProceduralTerrain))]
    public partial class ProceduralTerrainEditor : Editor
    {
        // Properties
        SerializedProperty m_EnableSurface;
        SerializedProperty m_SurfaceNoiseAmplitude;
        SerializedProperty m_SurfaceNoiseFrequency;
        SerializedProperty m_SurfaceYPosition;
        SerializedProperty m_SurfaceSeed;

        SerializedProperty m_EnableGlobalNoise;
        SerializedProperty m_GlobalNoiseAmplitude;
        SerializedProperty m_GlobalNoiseFrequency;
        SerializedProperty m_GlobalNoiseSeed;

        SerializedProperty m_Material;
        SerializedProperty m_UseStaticOrigin;
        SerializedProperty m_ColorBrickmapLevels;
        SerializedProperty m_DrawBrickmapBorders;
        SerializedProperty m_DrawBricks;
        SerializedProperty m_DrawShapeVolumes;
        SerializedProperty m_EnableDensityTester;
        SerializedProperty m_DensityTesterPosition;

        // GUI Contents
        GUIContent m_EnableSurfaceGUI;
        GUIContent m_SurfaceNoiseAmplitudeGUI;
        GUIContent m_SurfaceNoiseFrequencyGUI;
        GUIContent m_SurfaceYPositionGUI;
        GUIContent m_SurfaceSeedGUI;

        GUIContent m_EnableGlobalNoiseGUI;
        GUIContent m_GlobalNoiseAmplitudeGUI;
        GUIContent m_GlobalNoiseFrequencyGUI;
        GUIContent m_GlobalNoiseSeedGUI;

        GUIContent m_MaterialGUI;
        GUIContent m_UseStaticOriginGUI;
        GUIContent m_ColorBrickmapLevelsGUI;
        GUIContent m_DrawBrickmapBordersGUI;
        GUIContent m_DrawBricksGUI;
        GUIContent m_DrawShapeVolumesGUI;
        GUIContent m_EnableDensityTesterGUI;
        GUIContent m_DensityTesterPositionGUI;

        ProceduralTerrain m_Target;

        void OnEnable()
        {
            var o = new PropertyFetcher<ProceduralTerrain>(serializedObject);

            m_EnableSurface = o.Find(x => x.EnableSurface);
            m_SurfaceNoiseAmplitude = o.Find(x => x.SurfaceNoiseAmplitude);
            m_SurfaceNoiseFrequency = o.Find(x => x.SurfaceNoiseFrequency);
            m_SurfaceYPosition = o.Find(x => x.SurfaceYPosition);
            m_SurfaceSeed = o.Find(x => x.SurfaceSeed);

            m_EnableGlobalNoise = o.Find(x => x.EnableGlobalNoise);
            m_GlobalNoiseAmplitude = o.Find(x => x.GlobalNoiseAmplitude);
            m_GlobalNoiseFrequency = o.Find(x => x.GlobalNoiseFrequency);
            m_GlobalNoiseSeed = o.Find(x => x.GlobalNoiseSeed);

            m_Material = o.Find(x => x.Material);
            m_UseStaticOrigin = o.Find(x => x.UseStaticOrigin);
            m_ColorBrickmapLevels = o.Find(x => x.ColorBrickmapLevels);
            m_DrawBrickmapBorders = o.Find(x => x.m_DrawBrickmapBorders);
            m_DrawBricks = o.Find(x => x.m_DrawBricks);
            m_DrawShapeVolumes = o.Find(x => x.m_DrawShapeVolumes);
            m_EnableDensityTester = o.Find(x => x.m_EnableDensityTester);
            m_DensityTesterPosition = o.Find(x => x.m_DensityTesterPosition);

            m_EnableSurfaceGUI = new GUIContent("Enable Surface");
            m_SurfaceNoiseAmplitudeGUI = new GUIContent("Amplitude");
            m_SurfaceNoiseFrequencyGUI = new GUIContent("Frequency");
            m_SurfaceYPositionGUI = new GUIContent("Y Level");
            m_SurfaceSeedGUI = new GUIContent("Seed");

            m_EnableGlobalNoiseGUI = new GUIContent("Enable Global Noise");
            m_GlobalNoiseAmplitudeGUI = new GUIContent("Amplitude");
            m_GlobalNoiseFrequencyGUI = new GUIContent("Frequency");
            m_GlobalNoiseSeedGUI = new GUIContent("Seed");

            m_MaterialGUI = new GUIContent("Material");
            m_UseStaticOriginGUI = new GUIContent("Use Static Origin");
            m_ColorBrickmapLevelsGUI = new GUIContent("Highlight Brickmap Levels");
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

            EditorGUILayout.PropertyField(m_EnableSurface, m_EnableSurfaceGUI);
            if (m_EnableSurface.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_SurfaceNoiseAmplitude, m_SurfaceNoiseAmplitudeGUI);
                EditorGUILayout.PropertyField(m_SurfaceNoiseFrequency, m_SurfaceNoiseFrequencyGUI);
                EditorGUILayout.PropertyField(m_SurfaceYPosition, m_SurfaceYPositionGUI);
                EditorGUILayout.PropertyField(m_SurfaceSeed, m_SurfaceSeedGUI);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(m_EnableGlobalNoise, m_EnableGlobalNoiseGUI);
            if (m_EnableGlobalNoise.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_GlobalNoiseAmplitude, m_GlobalNoiseAmplitudeGUI);
                EditorGUILayout.PropertyField(m_GlobalNoiseFrequency, m_GlobalNoiseFrequencyGUI);
                EditorGUILayout.PropertyField(m_GlobalNoiseSeed, m_GlobalNoiseSeedGUI);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(m_Material, m_MaterialGUI);

            EditorGUILayout.LabelField("Debug Options", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_UseStaticOrigin, m_UseStaticOriginGUI);
            EditorGUILayout.PropertyField(m_ColorBrickmapLevels, m_ColorBrickmapLevelsGUI);

            EditorGUILayout.LabelField("Debug Overlays (Editor only)", EditorStyles.boldLabel);
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
