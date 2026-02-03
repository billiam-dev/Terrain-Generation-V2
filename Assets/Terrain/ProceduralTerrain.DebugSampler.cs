#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        public bool EnableDensitySampler;
        public Vector3 DensitySamplerPosition;

        void DrawSamplerGizmo()
        {
            if (EnableDensitySampler) m_DensityCache.DrawDensitySampler(BrickmapDebugLevel, DensitySamplerPosition);
        }

        partial class DensityCache
        {
            partial class SparseBrickMap
            {
                public void DrawDensitySampler(Vector3 position)
                {
                    ComputeIndices(position, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex);
                    float density = SampleDensityCache(brickIndex, localCellIndex);

                    // Make label string.
                    string globalIndexStr = string.Format("({0}, {1}, {2})", globalCellIndex.x, globalCellIndex.y, globalCellIndex.z);
                    string brickIndexStr = string.Format("({0}, {1}, {2})", brickIndex.x, brickIndex.y, brickIndex.z);
                    string cellIndexStr = string.Format("({0}, {1}, {2})", localCellIndex.x, localCellIndex.y, localCellIndex.z);
                    string infoStr = $"Global Cell: {globalIndexStr}\nBrick: {brickIndexStr}\nLocal cell: {cellIndexStr}\nDensity: {density}";

                    // Draw label.
                    SceneView view = SceneView.currentDrawingSceneView;
                    Vector3 cameraDir = position - view.camera.transform.position;
                    Vector3 tangent = Vector3.Cross(Vector3.up, cameraDir).normalized;
                    Vector3 offset = tangent + (Vector3.up * 0.2f);
                    Handles.Label(position + offset, infoStr);

                    // Draw cell.
                    float worldCellSize = levelScale * worldScale;
                    float3 cellCorner = (float3)globalCellIndex * worldCellSize;
                    float3 cellCentre = cellCorner + (worldCellSize / 2.0f);

                    Gizmos.color = new Color(0.0f, 1.0f, 0.2f, 0.5f);
                    Gizmos.DrawCube(cellCentre, (float3)worldCellSize);

                    // Draw brick.
                    float worldBrickSize = brickSize * worldCellSize;
                    float3 brickCorner = brickIndex * (float3)worldBrickSize;
                    float3 brickCentre = brickCorner + (worldBrickSize / 2.0f);

                    Gizmos.color = new Color(1.0f, 1.0f, 1.0f, 0.5f);
                    Gizmos.DrawWireCube(brickCentre, (float3)worldBrickSize);

                    // Draw xyz indicators for better spacial reference.
                    float dx = cellCorner.x - brickCorner.x;
                    float dy = cellCorner.y - brickCorner.y;
                    float dz = cellCorner.z - brickCorner.z;

                    // x
                    Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.5f);
                    Gizmos.DrawLine(brickCorner + new float3(dx, dy, 0), brickCorner + new float3(dx, dy, worldBrickSize));

                    // y
                    Gizmos.color = new Color(0.0f, 1.0f, 0.0f, 0.5f);
                    Gizmos.DrawLine(brickCorner + new float3(0, dy, dz), brickCorner + new float3(worldBrickSize, dy, dz));

                    // z
                    Gizmos.color = new Color(0.0f, 0.0f, 1.0f, 0.5f);
                    Gizmos.DrawLine(brickCorner + new float3(dx, 0, dz), brickCorner + new float3(dx, worldBrickSize, dz));
                }
            }

            public void DrawDensitySampler(int brickmapLevel, Vector3 position)
            {
                brickMapLevels[brickmapLevel].DrawDensitySampler(position);
            }
        }
    }
}
#endif
