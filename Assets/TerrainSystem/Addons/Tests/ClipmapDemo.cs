#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace TerrainSystem.Addons.Tests
{
    /// <summary>
    /// Demonstration object. Visualy shows how clipmap levels dynamically shift to a given origin.
    /// </summary>
    public class ClipmapDemo : MonoBehaviour
    {
        [SerializeField, Range(1, k_MaxClipmapLevels)]
        int m_NumClipmapLevels = 1;

        const int k_ChunkSize = 16;       // Voxels per axis contained within a single chunk.
        const int k_ClipmapSize = 8;      // Chunks per axis contained within each clipmap level.
        const int k_MaxClipmapLevels = 6; // Total number of clipmap levels.

        readonly Color[] m_LevelColors = new Color[k_MaxClipmapLevels]
        {
            new(1.0f, 0.2f, 0.0f),
            new(0.0f, 1.0f, 0.2f),
            new(0.2f, 0.0f, 1.0f),
            new(0.8f, 0.8f, 0.8f),
            new(0.4f, 0.4f, 0.4f),
            new(0.1f, 0.1f, 0.1f)
        };

        int3[] m_ClipmapLevelOrigins;

        void OnDrawGizmos()
        {
            if (m_ClipmapLevelOrigins == null || m_ClipmapLevelOrigins.Length != k_MaxClipmapLevels)
                m_ClipmapLevelOrigins = new int3[k_MaxClipmapLevels];

            Gizmos.matrix = Matrix4x4.identity;

            string debugText = "Expected grid offsets:\n";

            for (int i = 0; i < m_NumClipmapLevels; i++)
            {
                debugText += string.Format("{0}: ", i);
                DrawClipmapLevel(i, m_LevelColors[i], ref debugText);
            }

            Handles.Label(transform.position, debugText);
        }

        void DrawClipmapLevel(int clipmapLevelIndex, Color color, ref string debugText)
        {
            // Calculate size of chunk.
            float3 chunkSize = k_ChunkSize * math.pow(2, clipmapLevelIndex);
            chunkSize.z = 0; // Make 2D for demo.

            // Compute the centre chunk index at this clipmap level scale from which to build the remaining chunks.

            /* To explain the maths here, to find the position on our grid level we would do:
             * 
             * float3 halfChunkSize = chunkSize / 2.0f;
             * float3 scaledOriginPosition = ((float3)transform.position + halfChunkSize) / math.pow(2, level);
             * 
             * However, this creates overlaps with higher grid levels, so we calculate it's position on the upper
             * grid level and then multiply it by 2 in the next line to restore it to the correct grid level.
            */

            float3 scaledOriginPosition = ((float3)transform.position + chunkSize) / math.pow(2, clipmapLevelIndex + 1);
            int3 originChunkIndex = (int3)math.floor(scaledOriginPosition / k_ChunkSize) * 2;

            /*
             * This section sets up the skipping of large chunks rendering over small chunks.
             * 
             * In this 2D example, there are 9 positions a lower grid can be in relation to it's encompassing grid.
             * From each case, we must algorithmically decide which chunks to skip in the encompassing grid.
             * 
             * These cases can be represented by an public offset in each axis, with potential values -1, 0 and 1.
             * This covers all 9 public position cases.
             * 
             * We can then use each axis in relation with our offset index to skip the proper chunks.
            */

            m_ClipmapLevelOrigins[clipmapLevelIndex] = originChunkIndex;

            int3 lowerGridOffset = 0;
            if (clipmapLevelIndex > 0)
            {
                lowerGridOffset = m_ClipmapLevelOrigins[clipmapLevelIndex - 1] - originChunkIndex - originChunkIndex;
                lowerGridOffset /= 2; // Not exactly sure why I have to divide by 2 but it works.
            }

            debugText += string.Format("({0}, {1})\n", lowerGridOffset.x, lowerGridOffset.y);

            int3 offsetIndex;
            int3 chunkIndex;
            float3 chunkCorner;
            float3 chunkCentre;

            int halfClipmapSize = k_ClipmapSize / 2;
            int quarterClipmapSize = k_ClipmapSize / 4;

            for (int x = 0; x < k_ClipmapSize; x++)
            {
                for (int y = 0; y < k_ClipmapSize; y++)
                {
                    offsetIndex = new int3(x, y, 0) - halfClipmapSize;

                    // Skip chunks based on lowerGridpublicOffset, chunk is already being rendered by a higher LOD clipmap level.
                    // Arrived at this set of comparisons by determining what cells should be rendered and then flipping the logic for early continue.
                    if (clipmapLevelIndex > 0)
                    {
                        if (offsetIndex.x < lowerGridOffset.x + quarterClipmapSize &&
                            offsetIndex.x >= lowerGridOffset.x - quarterClipmapSize)
                        {
                            if (offsetIndex.y < lowerGridOffset.y + quarterClipmapSize &&
                                offsetIndex.y >= lowerGridOffset.y - quarterClipmapSize)
                            {
                                continue;
                            }
                        }
                    }

                    chunkIndex = originChunkIndex + offsetIndex;

                    chunkCorner = chunkIndex * chunkSize;
                    chunkCentre = chunkCorner + (chunkSize / 2.0f);

                    color.a = 1.0f;
                    Gizmos.color = color;
                    Gizmos.DrawWireCube(chunkCentre, chunkSize);

                    color.a = 0.2f;
                    Gizmos.color = color;
                    Gizmos.DrawCube(chunkCentre, chunkSize);
                }
            }
        }
    }
}
#endif
