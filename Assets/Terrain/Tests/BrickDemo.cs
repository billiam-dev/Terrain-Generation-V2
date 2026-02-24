#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration.Terrain.Tests
{
    public class BrickDemo : MonoBehaviour
    {
        [SerializeField, Range(4, 16)]
        int m_BrickSize = 16;

        [SerializeField, Range(0, 3)]
        int m_Level = 1;

        [SerializeField]
        bool m_ShowTransitionDemo = false;

        [SerializeField, Range(0, 5)]
        int m_TransitionIndex = 0;

        readonly Color CoreCellColor = new(0.8f, 0.2f, 0.0f, 1.0f);
        readonly Color ExtendedCellColor = new(0.0f, 0.8f, 0.2f, 1.0f);
        readonly Color TransitionCellColor = new(0.2f, 0.0f, 0.8f, 0.5f);

        void OnDrawGizmos()
        {
            // Per axis; add one to complete cells on the maximum extent, then one either side for normal sampling.
            int corePointsPerAxis = m_BrickSize + 1 + 2;

            int levelScale = 1 << m_Level;

            DrawCore(corePointsPerAxis, levelScale);

            if (m_ShowTransitionDemo && m_Level > 0)
                DrawTransition(corePointsPerAxis, levelScale);
        }

        void DrawCore(int corePointsPerAxis, int levelScale)
        {
            for (int x = 0; x < corePointsPerAxis; x++)
            {
                for (int y = 0; y < corePointsPerAxis; y++)
                {
                    for (int z = 0; z < corePointsPerAxis; z++)
                    {
                        int3 localCellCoord = new(x, y, z);
                        localCellCoord -= 1; // Subtract one from all axis to fetch normals from the minimal edges.

                        int3 globalCellCoord = localCellCoord * levelScale;

                        Gizmos.color = math.all(localCellCoord >= 0) && math.all(localCellCoord < m_BrickSize) ? CoreCellColor : ExtendedCellColor;
                        Gizmos.DrawSphere((float3)globalCellCoord, levelScale * 0.05f);
                    }
                }
            }
        }

        void DrawTransition(int corePointsPerAxis, int levelScale)
        {
            Gizmos.color = TransitionCellColor;

            int transitionPointsPerAxis = (corePointsPerAxis * 2) - 1;

            for (int x = 0; x < transitionPointsPerAxis; x++)
            {
                for (int y = 0; y < transitionPointsPerAxis; y++)
                {
                    for (int z = 0; z < 3; z++)
                    {
                        int3 faceCoord = new(x, y, z);

                        // Subtract 2 (one full cell at this resolution) from the x and y axis.
                        faceCoord.x -= 2;
                        faceCoord.y -= 2;

                        // Subtract 1 (one full cell at half resolution) from the z axis to fetch one point from +z and one from -z. This is for normal vectors.
                        faceCoord.z -= 1;

                        int3 localCellCoord = FaceToCellIndex(faceCoord);
                        int3 globalCellCoord = localCellCoord * (levelScale / 2);

                        Gizmos.DrawSphere((float3)globalCellCoord, levelScale * 0.05f);
                    }
                }
            }
        }

        int3 FaceToCellIndex(int3 faceIndex)
        {
            int scaledBrickSize = m_BrickSize * 2;

            return m_TransitionIndex switch
            {
                0 => new(scaledBrickSize - faceIndex.z, faceIndex.y, faceIndex.x), //  x
                1 => new(faceIndex.z, faceIndex.x, faceIndex.y),                   // -x
                2 => new(faceIndex.x, scaledBrickSize - faceIndex.z, faceIndex.y), //  y
                3 => new(faceIndex.y, faceIndex.z, faceIndex.x),                   // -y
                4 => new(faceIndex.y, faceIndex.x, scaledBrickSize - faceIndex.z), //  z
                5 => new(faceIndex.x, faceIndex.y, faceIndex.z),                   // -z
                _ => new(0, 0, 0)
            };
        }
    }
}
#endif
