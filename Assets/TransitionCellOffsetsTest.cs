using Unity.Mathematics;
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
        Gizmos.color = Color.red;

        for (int transitionIndex = 0; transitionIndex < 6; transitionIndex++)
            DrawIndices(transitionIndex);
    }

    void DrawIndices(int transitionIndex)
    {
        for (int i = 0; i < 9; i++)
        {
            Gizmos.DrawSphere((float3)TransitionCellOffsets[transitionIndex][i] + new float3(transitionIndex * 8, 0, 0), 0.05f);
        }
    }
}
