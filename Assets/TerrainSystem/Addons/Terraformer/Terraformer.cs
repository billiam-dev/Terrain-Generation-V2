using Unity.Mathematics;
using UnityEngine;

namespace TerrainSystem.Addons.Terraformer
{
    public class Terraformer : MonoBehaviour
    {
        public ProceduralTerrain m_Terrain;

        public float m_InputDeadTime = 0.05f;

        const float k_BushSizeScrollSpeed = 75.0f;

        float lastInputTime;
        float brushRadius;

        void Start()
        {
            brushRadius = 1.0f;
        }

        void Update()
        {
            if (!m_Terrain || Time.realtimeSinceStartup < lastInputTime + m_InputDeadTime)
                return;

            // Radius
            if (Input.GetAxis("Mouse ScrollWheel") < 0.0f)
            {
                brushRadius += k_BushSizeScrollSpeed * Time.deltaTime;
                brushRadius = Mathf.Clamp(brushRadius, 1.0f, 32.0f);
            }

            if (Input.GetAxis("Mouse ScrollWheel") > 0.0f)
            {
                brushRadius -= k_BushSizeScrollSpeed * Time.deltaTime;
                brushRadius = Mathf.Clamp(brushRadius, 1.0f, 32.0f);
            }

            if (Input.GetMouseButton(0))
            {
                // Build

                RaymarchResult result = m_Terrain.RaytraceSurface(transform.position, transform.forward);
                if (result.hitSurface)
                {
                    m_Terrain.Terraform(new Scene.Shape(result.position, quaternion.identity, 1.0f, Scene.DistanceFunction.Sphere, Scene.BlendMode.Additive, brushRadius));
                    lastInputTime = Time.realtimeSinceStartup;
                }
            }

            if (Input.GetMouseButton(1))
            {
                // Mine

                RaymarchResult result = m_Terrain.RaytraceSurface(transform.position, transform.forward);
                if (result.hitSurface)
                {
                    m_Terrain.Terraform(new Scene.Shape(result.position, quaternion.identity, 1.0f, Scene.DistanceFunction.Sphere, Scene.BlendMode.Subtractive, brushRadius));
                    lastInputTime = Time.realtimeSinceStartup;
                }
            }
        }
    }
}
