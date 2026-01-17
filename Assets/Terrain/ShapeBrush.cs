using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public class ShapeBrush : MonoBehaviour
    {
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

        public Shape Shape => new(
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

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // Early return if this object is selected, since the handle is drawn by this object's editor script.
            if (Selection.Contains(gameObject))
                return;

            // Else... draw gizmo.
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.1f, 0.8f, 0.1f, 0.5f);

            Shape shape = Shape;
            switch (shape.DistanceFunction)
            {
                case DistanceFunction.Sphere:
                    Gizmos.DrawWireSphere(Vector3.zero, shape.Dimention1);
                    break;

                case DistanceFunction.SemiSphere:
                    MoreGizmos.DrawWireSemiSphere(Vector3.zero, shape.Dimention1, shape.Dimention2);
                    break;

                case DistanceFunction.Capsule:
                    MoreGizmos.DrawWireCapsule(Vector3.zero, (shape.Dimention1 + shape.Dimention2) * 2.0f, shape.Dimention2);
                    break;

                case DistanceFunction.Torus:
                    MoreGizmos.DrawWireTorus(Vector3.zero, shape.Dimention1, shape.Dimention2);
                    break;

                case DistanceFunction.Cube:
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(shape.Dimention1, shape.Dimention2, shape.Dimention3) * 2.0f);
                    break;
            }
        }
#endif
    }
}
