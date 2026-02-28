using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace LevelGeneration.Terrain.Addons.RealtimeEditor
{
    [CustomEditor(typeof(TerrainEditor))]
    public class TerrainSceneEditor : Editor
    {
        // Properties
        SerializedProperty m_EnableSurface;
        SerializedProperty m_SurfaceNoiseAmplitude;
        SerializedProperty m_SurfaceNoiseFrequency;
        SerializedProperty m_SurfaceYPosition;
        SerializedProperty m_SurfaceNoiseSeed;

        SerializedProperty m_EnableGlobalNoise;
        SerializedProperty m_GlobalNoiseAmplitude;
        SerializedProperty m_GlobalNoiseFrequency;
        SerializedProperty m_GlobalNoiseSeed;

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

        TerrainEditor m_Target;

        void OnEnable()
        {
            var o = new PropertyFetcher<TerrainEditor>(serializedObject);

            m_EnableSurface = o.Find(x => x.EnableSurface);
            m_SurfaceNoiseAmplitude = o.Find(x => x.SurfaceNoiseAmplitude);
            m_SurfaceNoiseFrequency = o.Find(x => x.SurfaceNoiseFrequency);
            m_SurfaceYPosition = o.Find(x => x.SurfaceYPosition);
            m_SurfaceNoiseSeed = o.Find(x => x.SurfaceNoiseSeed);

            m_EnableGlobalNoise = o.Find(x => x.EnableGlobalNoise);
            m_GlobalNoiseAmplitude = o.Find(x => x.GlobalNoiseAmplitude);
            m_GlobalNoiseFrequency = o.Find(x => x.GlobalNoiseFrequency);
            m_GlobalNoiseSeed = o.Find(x => x.GlobalNoiseSeed);

            m_EnableSurfaceGUI = new GUIContent("Enable Surface");
            m_SurfaceNoiseAmplitudeGUI = new GUIContent("Amplitude");
            m_SurfaceNoiseFrequencyGUI = new GUIContent("Frequency");
            m_SurfaceYPositionGUI = new GUIContent("Y Level");
            m_SurfaceSeedGUI = new GUIContent("Seed");

            m_EnableGlobalNoiseGUI = new GUIContent("Enable Global Noise");
            m_GlobalNoiseAmplitudeGUI = new GUIContent("Amplitude");
            m_GlobalNoiseFrequencyGUI = new GUIContent("Frequency");
            m_GlobalNoiseSeedGUI = new GUIContent("Seed");

            m_Target = (TerrainEditor)target;
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
                EditorGUILayout.PropertyField(m_SurfaceNoiseSeed, m_SurfaceSeedGUI);
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

            if (GUILayout.Button("Randomize Seed"))
            {
                m_Target.RandomizeSeed();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
