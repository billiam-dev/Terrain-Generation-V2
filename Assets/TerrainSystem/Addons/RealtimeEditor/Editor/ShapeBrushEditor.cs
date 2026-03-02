using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

using TerrainSystem.Scene;

namespace TerrainSystem.Addons.RealtimeEditor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(ShapeBrush), true)]
    public class ShapeBrushEditor : Editor
    {
        SerializedProperty m_DistanceFunction;
        SerializedProperty m_BlendMode;
        SerializedProperty m_Smoothness;
        SerializedProperty m_Dimention1;
        SerializedProperty m_Dimention2;
        SerializedProperty m_Dimention3;

        GUIContent m_DistanceFunctionContent;
        GUIContent m_BlendModeContent;

        PrimitiveBoundsHandle m_BoundsHandle;

        ShapeBrush m_Target;

        void OnEnable()
        {
            m_DistanceFunction = serializedObject.FindProperty("m_DistanceFunction");
            m_BlendMode = serializedObject.FindProperty("m_BlendMode");
            m_Smoothness = serializedObject.FindProperty("m_Smoothness");
            m_Dimention1 = serializedObject.FindProperty("m_Dimention1");
            m_Dimention2 = serializedObject.FindProperty("m_Dimention2");
            m_Dimention3 = serializedObject.FindProperty("m_Dimention3");

            m_DistanceFunctionContent = new GUIContent("Shape");
            m_BlendModeContent = new GUIContent("Blend Mode");

            m_Target = (ShapeBrush)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_DistanceFunction, m_DistanceFunctionContent);
            EditorGUILayout.PropertyField(m_BlendMode, m_BlendModeContent);

            switch ((DistanceFunction)m_DistanceFunction.enumValueIndex)
            {
                case DistanceFunction.Sphere:
                    EditorGUILayout.PropertyField(m_Dimention1, new GUIContent("Radius"));
                    break;

                case DistanceFunction.SemiSphere:
                    EditorGUILayout.PropertyField(m_Dimention1, new GUIContent("Radius"));
                    m_Dimention2.floatValue = EditorGUILayout.Slider(new GUIContent("Slice"), m_Dimention2.floatValue, -1.0f, 1.0f);
                    break;

                case DistanceFunction.Capsule:
                    EditorGUILayout.PropertyField(m_Dimention1, new GUIContent("Height"));
                    EditorGUILayout.PropertyField(m_Dimention2, new GUIContent("Radius"));
                    break;

                case DistanceFunction.Torus:
                    EditorGUILayout.PropertyField(m_Dimention1, new GUIContent("Outer Radius"));
                    EditorGUILayout.PropertyField(m_Dimention2, new GUIContent("Inner Radius"));
                    break;

                case DistanceFunction.Cube:
                    EditorGUILayout.PropertyField(m_Dimention1, new GUIContent("Width"));
                    EditorGUILayout.PropertyField(m_Dimention2, new GUIContent("Height"));
                    EditorGUILayout.PropertyField(m_Dimention3, new GUIContent("Depth"));
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        void OnSceneGUI()
        {
            Handles.matrix = m_Target.transform.localToWorldMatrix;
            Handles.color = Color.green;

            EditorGUI.BeginChangeCheck();

            switch ((DistanceFunction)m_DistanceFunction.enumValueIndex)
            {
                case DistanceFunction.Sphere:
                    DrawSphereHandle();
                    break;

                case DistanceFunction.SemiSphere:
                    DrawSemiSphereHandle();
                    break;

                case DistanceFunction.Capsule:
                    DrawCapsuleHandle();
                    break;

                case DistanceFunction.Torus:
                    DrawTorusHandle();
                    break;

                case DistanceFunction.Cube:
                    DrawBoxHandle();
                    break;
            }
        }

        void DrawSphereHandle()
        {
            if (m_BoundsHandle is not SphereBoundsHandle)
                m_BoundsHandle = new SphereBoundsHandle();

            SphereBoundsHandle handle = (SphereBoundsHandle)m_BoundsHandle;

            handle.center = Vector3.zero;
            handle.radius = m_Dimention1.floatValue;
            handle.DrawHandle();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Edited Sphere");

                m_Dimention1.SetUnderlyingValue(handle.radius);
                m_Target.FlagPropertyChanged();
            }
        }

        void DrawSemiSphereHandle()
        {
            if (m_BoundsHandle is not SemiSphereBoundsHandle)
                m_BoundsHandle = new SemiSphereBoundsHandle();

            SemiSphereBoundsHandle handle = (SemiSphereBoundsHandle)m_BoundsHandle;

            handle.center = Vector3.zero;
            handle.radius = m_Dimention1.floatValue;
            handle.slice = m_Dimention2.floatValue;
            handle.DrawHandle();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Edited Semi-Sphere");

                m_Dimention1.SetUnderlyingValue(handle.radius);
                m_Target.FlagPropertyChanged();
            }
        }

        void DrawCapsuleHandle()
        {
            if (m_BoundsHandle is not CapsuleBoundsHandle)
                m_BoundsHandle = new CapsuleBoundsHandle();

            CapsuleBoundsHandle handle = (CapsuleBoundsHandle)m_BoundsHandle;

            handle.center = Vector3.zero;
            handle.height = (m_Dimention1.floatValue + m_Dimention2.floatValue) * 2.0f;
            handle.radius = m_Dimention2.floatValue;
            handle.DrawHandle();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Edited Capsule");

                m_Dimention1.SetUnderlyingValue((handle.height / 2.0f) - handle.radius);
                m_Dimention2.SetUnderlyingValue(handle.radius);
                m_Target.FlagPropertyChanged();
            }
        }

        void DrawTorusHandle()
        {
            if (m_BoundsHandle is not TorusBoundsHandle)
                m_BoundsHandle = new TorusBoundsHandle();

            TorusBoundsHandle handle = (TorusBoundsHandle)m_BoundsHandle;

            handle.center = Vector3.zero;
            handle.outerRadius = m_Dimention1.floatValue;
            handle.innerRadius = m_Dimention2.floatValue;
            handle.DrawHandle();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Edited Torus");

                m_Dimention1.SetUnderlyingValue(handle.outerRadius);
                m_Target.FlagPropertyChanged();
            }
        }

        void DrawBoxHandle()
        {
            if (m_BoundsHandle is not BoxBoundsHandle)
                m_BoundsHandle = new BoxBoundsHandle();

            BoxBoundsHandle handle = (BoxBoundsHandle)m_BoundsHandle;

            handle.center = Vector3.zero;
            handle.size = new Vector3(m_Dimention1.floatValue, m_Dimention2.floatValue, m_Dimention3.floatValue) * 2.0f;
            handle.DrawHandle();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Edited Cube");

                m_Dimention1.SetUnderlyingValue(handle.size.x / 2.0f);
                m_Dimention2.SetUnderlyingValue(handle.size.y / 2.0f);
                m_Dimention3.SetUnderlyingValue(handle.size.z / 2.0f);
                m_Target.FlagPropertyChanged();
            }
        }
    }
}
