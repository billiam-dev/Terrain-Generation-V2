using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

using LevelGeneration.Terrain.Scene;
using System;

namespace LevelGeneration.Terrain.Addons.RealtimeEditor
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public class ShapeBrush : MonoBehaviour
    {
        [SerializeField]
        DistanceFunction m_DistanceFunction = DistanceFunction.Sphere;

        [SerializeField]
        BlendMode m_BlendMode = BlendMode.Additive;

        [SerializeField, Min(0.0f)]
        float m_Dimention1 = 4.0f;

        [SerializeField, Min(0.0f)]
        float m_Dimention2 = 4.0f;

        [SerializeField, Min(0.0f)]
        float m_Dimention3 = 4.0f;

        bool m_PropertyChanged; // Flag set by OnValidate or visual editor to notify this object that it has been changed.

        Shape m_Shape;

        public Shape Shape
        {
            get
            {
                m_Shape ??= new(
                    transform.position,
                    transform.rotation,
                    transform.lossyScale,
                    m_DistanceFunction,
                    m_BlendMode,
                    m_Dimention1,
                    m_Dimention2,
                    m_Dimention3
                    );

                return m_Shape;
            }
        }

        public Action<ShapeBrush> OnDisabled;

        void OnEnable()
        {
            m_Shape ??= new(
                transform.position,
                transform.rotation,
                transform.lossyScale,
                m_DistanceFunction,
                m_BlendMode,
                m_Dimention1,
                m_Dimention2,
                m_Dimention3
                );
        }

        void OnDisable()
        {
            OnDisabled?.Invoke(this);
        }

        void Update()
        {
            UpdateUnderlyingShape();
        }

        void UpdateUnderlyingShape()
        {
            // Evaluate transform.
            if (transform.hasChanged)
            {
                AffineTransform matrix = new(transform.position, transform.rotation, transform.lossyScale);
                if (!m_Shape.Matrix.Equals(matrix))
                    m_Shape.Matrix = matrix;

                transform.hasChanged = false;
            }

            // Evaluate distance function properties.
            if (m_PropertyChanged)
            {
                if (!m_Shape.DistanceFunction.Equals(m_DistanceFunction))
                    m_Shape.DistanceFunction = m_DistanceFunction;

                if (!m_Shape.BlendMode.Equals(m_BlendMode))
                    m_Shape.BlendMode = m_BlendMode;

                float3 dims = new(m_Dimention1, m_Dimention2, m_Dimention3);
                if (!m_Shape.Dimentions.Equals(dims))
                    m_Shape.Dimentions = dims;

                m_PropertyChanged = false;
            }
        }

        public void FlagPropertyChanged()
        {
            m_PropertyChanged = true;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            m_PropertyChanged = true;

            switch (m_DistanceFunction)
            {
                case DistanceFunction.Sphere:
                case DistanceFunction.Capsule:
                case DistanceFunction.Torus:
                case DistanceFunction.Cube:
                    m_Dimention1 = math.max(m_Dimention1, 0);
                    m_Dimention2 = math.max(m_Dimention2, 0);
                    m_Dimention3 = math.max(m_Dimention3, 0);
                    break;

                case DistanceFunction.SemiSphere:
                    m_Dimention1 = math.max(m_Dimention1, 0);
                    m_Dimention2 = math.clamp(m_Dimention2, -1.0f, 1.0f);
                    break;
            }
        }

        void OnDrawGizmos()
        {
            // Early return if this object is selected, since the handle is drawn by this object's editor script.
            if (Selection.Contains(gameObject))
                return;
            
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.1f, 0.8f, 0.1f, 0.5f);

            switch (m_DistanceFunction)
            {
                case DistanceFunction.Sphere:
                    Gizmos.DrawWireSphere(Vector3.zero, m_Dimention1);
                    break;

                case DistanceFunction.SemiSphere:
                    MoreGizmos.DrawWireSemiSphere(Vector3.zero, m_Dimention1, m_Dimention2);
                    break;

                case DistanceFunction.Capsule:
                    MoreGizmos.DrawWireCapsule(Vector3.zero, (m_Dimention1 + m_Dimention2) * 2.0f, m_Dimention2);
                    break;

                case DistanceFunction.Torus:
                    MoreGizmos.DrawWireTorus(Vector3.zero, m_Dimention1, m_Dimention2);
                    break;

                case DistanceFunction.Cube:
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(m_Dimention1, m_Dimention2, m_Dimention3) * 2.0f);
                    break;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.identity;

            Volume shapeVolume = m_Shape.ComputeVolume();

            Gizmos.color = new(0, 1, 0, 0.1f);
            Gizmos.DrawWireCube(shapeVolume.position, shapeVolume.size);

            Gizmos.color = new(0, 1, 0, 0.05f);
            Gizmos.DrawCube(shapeVolume.position, shapeVolume.size);
        }
#endif
    }
}
