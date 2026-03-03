using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace TerrainSystem.Addons.Test
{
    public class TerrainSampler : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField]
        ProceduralTerrain m_Terrain;

        static readonly Color NearColor = new(0.05f, 0.1f, 1.0f);
        static readonly Color FarColor = new(1.0f, 0.1f, 0.05f);

        const float ColorDensityScale = 0.01f;

        void OnDrawGizmosSelected()
        {
            if (!m_Terrain)
                return;

            float3 origin = transform.position;
            float density = m_Terrain.SampleDensity(origin);

            float t = Mathf.Clamp01(density * ColorDensityScale);
            Gizmos.color = Color.Lerp(NearColor, FarColor, t);

            Handles.Label(origin, density.ToString());
            Gizmos.DrawWireSphere(origin, density);
        }
#endif
    }
}
