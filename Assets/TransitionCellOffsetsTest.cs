#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

// Temp

public class TransitionCellOffsetsTest : MonoBehaviour
{
    public static readonly int3[][] TransitionCellOffsets =
    {
        new int3[] { new(2, 0, 0), new(2, 0, 1), new(2, 0, 2), new(2, 1, 0), new(2, 1, 1), new(2, 1, 2), new(2, 2, 0), new(2, 2, 1), new(2, 2, 2) },
        new int3[] { new(0, 0, 0), new(0, 0, 1), new(0, 0, 2), new(0, 1, 0), new(0, 1, 1), new(0, 1, 2), new(0, 2, 0), new(0, 2, 1), new(0, 2, 2) },
        new int3[] { new(0, 2, 0), new(0, 2, 1), new(0, 2, 2), new(1, 2, 0), new(1, 2, 1), new(1, 2, 2), new(2, 2, 0), new(2, 2, 1), new(2, 2, 2) },
        new int3[] { new(0, 0, 0), new(0, 0, 1), new(0, 0, 2), new(1, 0, 0), new(1, 0, 1), new(1, 0, 2), new(2, 0, 0), new(2, 0, 1), new(2, 0, 2) },
        new int3[] { new(0, 0, 2), new(1, 0, 2), new(2, 0, 2), new(0, 1, 2), new(1, 1, 2), new(2, 1, 2), new(0, 2, 2), new(1, 2, 2), new(2, 2, 2) },
        new int3[] { new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(0, 1, 0), new(1, 1, 0), new(2, 1, 0), new(0, 2, 0), new(1, 2, 0), new(2, 2, 0) }
    };

    void OnDrawGizmos()
    {
        for (int transitionIndex = 0; transitionIndex < 6; transitionIndex++)
            DrawIndices(transitionIndex, Color.red);
    }

    void DrawIndices(int transitionIndex, Color color)
    {
        float3 centre = new(transitionIndex * 8, 0, 0);

        color.a = 0.1f;
        Gizmos.color = color;
        Gizmos.DrawCube(centre + 1.0f, Vector3.one * 2.0f);

        color.a = 1.0f;
        Gizmos.color = color;

        for (int i = 0; i < 9; i++)
        {
            float3 pos = (float3)TransitionCellOffsets[transitionIndex][i] + centre;

            Gizmos.DrawSphere(pos, 0.05f);
            Handles.Label(pos + new float3(0, 0.1f, 0), i.ToString());
        }
    }
}
#endif
