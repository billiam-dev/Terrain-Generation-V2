using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace TerrainSystem.Addons.RealtimeEditor
{
    [CustomEditor(typeof(TerrainEditor))]
    class TerrainEditorEditor : Editor
    {
        // Properties
        SerializedProperty m_InitialDensity;

        SerializedProperty m_EnableSurface;
        SerializedProperty m_SurfaceNoiseAmplitude;
        SerializedProperty m_SurfaceNoiseFrequency;
        SerializedProperty m_SurfaceNoiseSeed;

        SerializedProperty m_EnableGlobalNoise;
        SerializedProperty m_GlobalNoiseAmplitude;
        SerializedProperty m_GlobalNoiseFrequency;
        SerializedProperty m_GlobalNoiseSeed;

        SerializedProperty m_EnableTerrainShapes;

        // GUI Contents
        GUIContent m_InitialDensityGUI;

        GUIContent m_EnableSurfaceGUI;
        GUIContent m_SurfaceNoiseAmplitudeGUI;
        GUIContent m_SurfaceNoiseFrequencyGUI;
        GUIContent m_SurfaceSeedGUI;

        GUIContent m_EnableGlobalNoiseGUI;
        GUIContent m_GlobalNoiseAmplitudeGUI;
        GUIContent m_GlobalNoiseFrequencyGUI;
        GUIContent m_GlobalNoiseSeedGUI;
        
        GUIContent m_EnableTerrainShapesGUI;

        TerrainEditor m_Target;

        void OnEnable()
        {
            var o = new PropertyFetcher<TerrainEditor>(serializedObject);

            m_InitialDensity = o.Find(x => x.InitialDensity);

            m_EnableSurface = o.Find(x => x.EnableSurface);
            m_SurfaceNoiseAmplitude = o.Find(x => x.SurfaceNoiseAmplitude);
            m_SurfaceNoiseFrequency = o.Find(x => x.SurfaceNoiseFrequency);
            m_SurfaceNoiseSeed = o.Find(x => x.SurfaceNoiseSeed);

            m_EnableGlobalNoise = o.Find(x => x.EnableGlobalNoise);
            m_GlobalNoiseAmplitude = o.Find(x => x.GlobalNoiseAmplitude);
            m_GlobalNoiseFrequency = o.Find(x => x.GlobalNoiseFrequency);
            m_GlobalNoiseSeed = o.Find(x => x.GlobalNoiseSeed);

            m_EnableTerrainShapes = o.Find(x => x.EnableTerrainShapes);

            m_InitialDensityGUI = new GUIContent("Initial Value");

            m_EnableSurfaceGUI = new GUIContent("Enable Surface");
            m_SurfaceNoiseAmplitudeGUI = new GUIContent("Amplitude");
            m_SurfaceNoiseFrequencyGUI = new GUIContent("Frequency");
            m_SurfaceSeedGUI = new GUIContent("Seed");

            m_EnableGlobalNoiseGUI = new GUIContent("Enable Global Noise");
            m_GlobalNoiseAmplitudeGUI = new GUIContent("Amplitude");
            m_GlobalNoiseFrequencyGUI = new GUIContent("Frequency");
            m_GlobalNoiseSeedGUI = new GUIContent("Seed");

            m_EnableTerrainShapesGUI = new GUIContent("Enable Terrain Shapes");

            m_Target = (TerrainEditor)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_InitialDensity, m_InitialDensityGUI);

            EditorGUILayout.PropertyField(m_EnableSurface, m_EnableSurfaceGUI);
            if (m_EnableSurface.boolValue)
            {
                EditorGUILayout.BeginVertical("Box");
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(m_SurfaceNoiseAmplitude, m_SurfaceNoiseAmplitudeGUI);
                EditorGUILayout.PropertyField(m_SurfaceNoiseFrequency, m_SurfaceNoiseFrequencyGUI);
                EditorGUILayout.PropertyField(m_SurfaceNoiseSeed, m_SurfaceSeedGUI);
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.PropertyField(m_EnableGlobalNoise, m_EnableGlobalNoiseGUI);
            if (m_EnableGlobalNoise.boolValue)
            {
                EditorGUILayout.BeginVertical("Box");
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(m_GlobalNoiseAmplitude, m_GlobalNoiseAmplitudeGUI);
                EditorGUILayout.PropertyField(m_GlobalNoiseFrequency, m_GlobalNoiseFrequencyGUI);
                EditorGUILayout.PropertyField(m_GlobalNoiseSeed, m_GlobalNoiseSeedGUI);
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Randomize Seed"))
            {
                m_Target.RandomizeSeed();
            }

            EditorGUILayout.PropertyField(m_EnableTerrainShapes, m_EnableTerrainShapesGUI);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
