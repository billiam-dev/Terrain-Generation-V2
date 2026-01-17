#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public static class MoreGizmos
    {
        public static void DrawWireSemiSphere(Vector3 centre, float radius, float slice)
        {
            Handles.color = Gizmos.color;
            Handles.matrix = Gizmos.matrix;

            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            float r = radius;
            float hs = slice * radius;
            float rs = Mathf.Sqrt((radius * radius) - (hs * hs));

            // Outer bounds
            Handles.DrawWireArc(centre, up, right, 360f, r);
            Handles.DrawWireArc(centre, forward, right, 360f, r);
            Handles.DrawWireArc(centre, right, forward, 360f, r);

            // Slice
            Handles.DrawWireArc(centre + (up * hs), up, right, 360f, rs);
        }

        /// <summary>
        /// Draws a wireframe capsule with center, height and radius in the Scene view.
        /// </summary>
        public static void DrawWireCapsule(Vector3 centre, float height, float radius)
        {
            Handles.color = Gizmos.color;
            Handles.matrix = Gizmos.matrix;

            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            float r = radius;
            float h = height;

            Vector3 top = centre + up * (h * 0.5f - r);
            Vector3 bottom = centre - up * (h * 0.5f - r);

            // Height ring 1
            Handles.DrawWireArc(top, forward, right, 180f, r);
            Handles.DrawWireArc(bottom, forward, right, -180f, r);
            Handles.DrawLine(top + right * r, bottom + right * r);
            Handles.DrawLine(top - right * r, bottom - right * r);

            // Height ring 2
            Handles.DrawWireArc(top, right, forward, -180f, r);
            Handles.DrawWireArc(bottom, right, forward, 180f, r);
            Handles.DrawLine(top + forward * r, bottom + forward * r);
            Handles.DrawLine(top - forward * r, bottom - forward * r);

            // Top / bottom radius rings
            Handles.DrawWireArc(top, up, forward, 360f, r);
            Handles.DrawWireArc(bottom, up, forward, -360f, r);
        }

        /// <summary>
        /// Draws a wireframe torus with center, outerRadius and innerRadius in the Scene view.
        /// </summary>
        public static void DrawWireTorus(Vector3 centre, float outerRadius, float innerRadius)
        {
            Handles.color = Gizmos.color;
            Handles.matrix = Gizmos.matrix;

            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            float or = outerRadius;
            float ir = innerRadius;

            // Outer rings
            Handles.DrawWireArc(centre + (forward * or), right, forward, 360f, ir);
            Handles.DrawWireArc(centre - (forward * or), right, forward, 360f, ir);
            Handles.DrawWireArc(centre + (right * or), forward, right, 360f, ir);
            Handles.DrawWireArc(centre - (right * or), forward, right, 360f, ir);

            // Top / bottom rings
            Handles.DrawWireArc(centre + (up * ir), up, right, 360f, or);
            Handles.DrawWireArc(centre - (up * ir), up, right, 360f, or);

            // Side rings
            Handles.DrawWireArc(centre, up, right, 360f, or + ir);
            Handles.DrawWireArc(centre, up, right, 360f, or - ir);
        }
    }
}
#endif
