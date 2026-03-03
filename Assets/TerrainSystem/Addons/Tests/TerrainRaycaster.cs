using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TerrainSystem.Addons.Test
{
    public class TerrainRaycaster : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField]
        ProceduralTerrain m_Terrain;

        [SerializeField, Range(0.0f, 10.0f)]
        float m_MinDistance = 0.1f;

        [SerializeField]
        bool m_ShowSteps = false;

        // This website is great for colors: https://www.colorhexa.com/
        static readonly Color SuccessColor = new(0.2f, 0.2f, 1.0f);
        static readonly Color FailueColor = new(1.0f, 0.2f, 0.2f);

        void OnDrawGizmosSelected()
        {
            if (!m_Terrain)
                return;

            float3 origin = transform.position;
            float3 dir = transform.forward;
            float radius = m_MinDistance;

            if (!m_ShowSteps)
            {
                RaymarchResult result = m_Terrain.RaytraceSurface(origin, dir, radius);

                if (result.hitSurface)
                {
                    Gizmos.color = SuccessColor;

                    Gizmos.DrawLine(origin, result.position);

                    float3 position = result.position;
                    float distance = result.distance;

                    Handles.Label(position, distance.ToString());
                    Gizmos.DrawWireSphere(position, distance);
                }
                else
                {
                    Gizmos.color = SuccessColor;
                    Gizmos.DrawLine(origin, origin + (dir * 1000.0f));
                }
            }
            else
            {
                List<RaymarchResult> steps = m_Terrain.RaytraceSurfaceWithSteps(origin, dir, radius);

                Gizmos.color = steps[^1].hitSurface ? SuccessColor : FailueColor;

                Gizmos.DrawLine(origin, steps[^1].position);

                for (int i = 0; i < steps.Count; i++)
                {
                    if (!steps[i].hitSurface)
                        break;

                    float3 position = steps[i].position;
                    float distance = steps[i].distance;

                    Handles.Label(position, distance.ToString());
                    Gizmos.DrawWireSphere(position, distance);
                }
            }
        }
#endif
    }
}
