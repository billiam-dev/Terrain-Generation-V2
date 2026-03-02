using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace TerrainSystem.Addons.Test
{
    public class SurfaceSampler : MonoBehaviour
    {
        [SerializeField]
        ProceduralTerrain m_Terrain;

        [SerializeField]
        Color m_Color = Color.red;
        void OnDrawGizmosSelected()
        {
            if (!m_Terrain)
                return;

            float3 origin = transform.position;
            float density = m_Terrain.SampleDensity(origin);

            Gizmos.color = m_Color;
            Handles.Label(origin, density.ToString());
            Gizmos.DrawSphere(origin, density);
        }
    }
}
