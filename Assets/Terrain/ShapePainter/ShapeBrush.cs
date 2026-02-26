using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain.ShapePainter
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

        public Shape Shape => new(
            transform.position,
            transform.rotation,
            transform.lossyScale,

            m_DistanceFunction,
            m_BlendMode,

            m_Dimention1,
            m_Dimention2,
            m_Dimention3
            );

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

            Shape.ComputeVolume(out float3 position, out float3 volume);

            Gizmos.color = new(0, 1, 0, 0.1f);
            Gizmos.DrawWireCube(position, volume);

            Gizmos.color = new(0, 1, 0, 0.05f);
            Gizmos.DrawCube(position, volume);
        }
#endif
    }
}
