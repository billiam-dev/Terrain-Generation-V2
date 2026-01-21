using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    [DisallowMultipleComponent]
    public class ShapeBrush : MonoBehaviour
    {
        public DistanceFunction DistanceFunction
        {
            get
            {
                return m_DistanceFunction;
            }
            set
            {
                if (value != m_DistanceFunction)
                {
                    m_DistanceFunction = value;
                    IsDirty = true;
                }
            }
        }

        public BlendMode BlendMode
        {
            get
            {
                return m_BlendMode;
            }
            set
            {
                if (value != m_BlendMode)
                {
                    BlendMode = value;
                    IsDirty = true;
                }
            }
        }

        public float Smoothness
        {
            get
            {
                return m_Smoothness;
            }
            set
            {
                if (value != Smoothness)
                {
                    Smoothness = value;
                    IsDirty = true;
                }
            }
        }

        public float Dimention1
        {
            get
            {
                return m_Dimention1;
            }
            set
            {
                if (value != m_Dimention1)
                {
                    m_Dimention1 = value;
                    IsDirty = true;
                }
            }
        }

        public float Dimention2
        {
            get
            {
                return m_Dimention2;
            }
            set
            {
                if (value != m_Dimention2)
                {
                    m_Dimention2 = value;
                    IsDirty = true;
                }
            }
        }

        public float Dimention3
        {
            get
            {
                return m_Dimention3;
            }
            set
            {
                if (value != m_Dimention3)
                {
                    m_Dimention3 = value;
                    IsDirty = true;
                }
            }
        }

        [SerializeField]
        DistanceFunction m_DistanceFunction = DistanceFunction.Sphere;

        [SerializeField]
        BlendMode m_BlendMode = BlendMode.Additive;

        [SerializeField, Range(0.1f, 10.0f)]
        float m_Smoothness = 0.2f;

        [SerializeField, Min(0.0f)]
        float m_Dimention1 = 4.0f;

        [SerializeField, Min(0.0f)]
        float m_Dimention2 = 4.0f;

        [SerializeField, Min(0.0f)]
        float m_Dimention3 = 4.0f;

        bool m_IsDirty;

        public bool IsDirty
        {
            get
            {
                return m_IsDirty || transform.hasChanged;
            }
            set
            {
                transform.hasChanged = value;
                m_IsDirty = value;
            }
        }

        Shape m_Shape;

        public Shape EvaluateNewShape(out float3 oldPosition, out float3 oldVolume, out float3 newPosition, out float3 newVolume)
        {
            Shape oldShape = m_Shape;
            Shape newShape = new(
            transform.position,
            transform.rotation,
            transform.lossyScale,

            m_DistanceFunction,
            m_BlendMode,
            m_Smoothness,

            m_Dimention1,
            m_Dimention2,
            m_Dimention3
            );

            // Apply old and new shape volumes to update brick array.
            if (!oldShape.IsNull)
            {
                oldPosition = oldShape.Matrix.t;
                oldVolume = oldShape.ComputeVolume();
            }
            else
            {
                oldPosition = 0;
                oldVolume = 0;
            }

            newPosition = newShape.Matrix.t;
            newVolume = newShape.ComputeVolume();

            // Update current shape.
            m_Shape = newShape;

            // Return the new shape.
            return m_Shape;
        }

        public Shape EvaluateCurrentShape(out float3 position, out float3 volume)
        {
            position = m_Shape.Matrix.t;
            volume = m_Shape.ComputeVolume();

            return m_Shape;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            m_IsDirty = true;

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

            float3 position = m_Shape.Matrix.t;
            float3 volume = m_Shape.ComputeVolume();

            Gizmos.color = new(0, 1, 0, 0.1f);
            Gizmos.DrawWireCube(position, volume);

            Gizmos.color = new(0, 1, 0, 0.05f);
            Gizmos.DrawCube(position, volume);
        }
#endif
    }
}
