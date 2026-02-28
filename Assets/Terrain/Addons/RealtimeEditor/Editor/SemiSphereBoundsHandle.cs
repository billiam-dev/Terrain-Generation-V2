using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace LevelGeneration.Terrain.Addons.RealtimeEditor
{
    public class SemiSphereBoundsHandle : PrimitiveBoundsHandle
    {
        /// <summary>
        /// Returns or specifies the radius of the semi-sphere bounding volume.
        /// </summary>
        public float radius
        {
            get
            {
                Vector3 size = GetSize();
                float num = 0f;
                for (int i = 0; i < 3; i++)
                {
                    if (IsAxisEnabled(i))
                    {
                        num = Mathf.Max(num, Mathf.Abs(size[i]));
                    }
                }

                return num * 0.5f;
            }
            set
            {
                SetSize(2f * value * Vector3.one);
            }
        }

        /// <summary>
        /// Returns or specifies the height that the semi-sphere bounding handle is cut, between -1 and 1.
        /// </summary>
        public float slice
        {
            get;
            set;
        }

        /// <summary>
        /// Create a new instance of the SemiSphereBoundsHandle class.
        /// </summary>
        public SemiSphereBoundsHandle()
        {
            axes = Axes.X | Axes.Y | Axes.Z;
        }

        /// <summary>
        /// Draw a wireframe semi-shere for this instance.
        /// </summary>
        protected override void DrawWireframe()
        {
            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            float r = radius;
            float hs = slice * radius;
            float rs = Mathf.Sqrt((radius * radius) - (hs * hs));

            // Outer bounds
            Handles.DrawWireArc(center, up, right, 360f, r);
            Handles.DrawWireArc(center, forward, right, 360f, r);
            Handles.DrawWireArc(center, right, forward, 360f, r);

            // Slice
            Handles.DrawWireArc(center + (up * hs), up, right, 360f, rs);
        }

        protected override Bounds OnHandleChanged(HandleDirection handle, Bounds boundsOnClick, Bounds newBounds)
        {
            Vector3 max = newBounds.max;
            Vector3 min = newBounds.min;
            int num = 0;
            switch (handle)
            {
                case HandleDirection.PositiveY:
                case HandleDirection.NegativeY:
                    num = 1;
                    break;
                case HandleDirection.PositiveZ:
                case HandleDirection.NegativeZ:
                    num = 2;
                    break;
            }

            float num2 = 0.5f * (max[num] - min[num]);
            for (int i = 0; i < 3; i++)
            {
                if (i != num)
                {
                    min[i] = center[i] - num2;
                    max[i] = center[i] + num2;
                }
            }

            return new Bounds((max + min) * 0.5f, max - min);
        }
    }
}
