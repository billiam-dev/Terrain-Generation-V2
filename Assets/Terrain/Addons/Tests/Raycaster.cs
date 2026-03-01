using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain.Addons.Test
{
    public class Raycaster : MonoBehaviour
    {
        [SerializeField]
        ProceduralTerrain m_Terrain;

        [SerializeField, Range(0.0f, 10.0f)]
        float m_MinDistance = 0.1f;

        [SerializeField]
        Color m_Color = Color.red;

        [SerializeField]
        bool m_ShowSteps = false;

        void OnDrawGizmosSelected()
        {
            if (!m_Terrain)
                return;

            Color color = m_Color;

            float3 origin = transform.position;
            float3 dir = transform.forward;
            float radius = m_MinDistance;

            if (!m_ShowSteps)
            {
                RaymarchResult result = m_Terrain.FindSurface(origin, dir, radius);

                color.a = 1.0f;
                Gizmos.color = color;
                Gizmos.DrawLine(origin, result.position);

                color.a = m_Color.a;
                Gizmos.color = color;

                float3 position = result.position;
                float distance = result.distance;

                Handles.Label(position, distance.ToString());
                Gizmos.DrawSphere(position, distance);
            }
            else
            {
                List<RaymarchResult> steps = m_Terrain.FindSurfaceWithSteps(origin, dir, radius);

                color.a = 1.0f;
                Gizmos.color = color;
                Gizmos.DrawLine(origin, steps[^1].position);

                color.a = m_Color.a;
                Gizmos.color = color;
                for (int i = 0; i < steps.Count; i++)
                {
                    float3 position = steps[i].position;
                    float distance = steps[i].distance;

                    Handles.Label(position, distance.ToString());
                    Gizmos.DrawSphere(position, distance);
                }
            }
        }
    }
}
