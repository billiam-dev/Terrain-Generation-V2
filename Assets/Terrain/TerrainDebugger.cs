#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public class TerrainDebugger : MonoBehaviour
    {
        [SerializeField]
        ProceduralTerrain m_Terrain;

        void OnDrawGizmos()
        {
            if (!m_Terrain)
            {
                Handles.Label(transform.position, "No terrain assigned.");
                return;
            }

            if (!m_Terrain.isActiveAndEnabled)
            {
                Handles.Label(transform.position, "Terrain is innactive.");
                return;
            }

            float3 position = transform.position;

            m_Terrain.ComputeIndices(position, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex);
            float density = m_Terrain.SampleDensity(brickIndex, localCellIndex);

            // Make label string.
            string globalIndexStr = string.Format("({0}, {1}, {2})", globalCellIndex.x, globalCellIndex.y, globalCellIndex.z);
            string brickIndexStr = string.Format("({0}, {1}, {2})", brickIndex.x, brickIndex.y, brickIndex.z);
            string cellIndexStr = string.Format("({0}, {1}, {2})", localCellIndex.x, localCellIndex.y, localCellIndex.z);
            string infoStr = $"Global Cell: {globalIndexStr}\nChunk: {brickIndexStr}\nLocal cell: {cellIndexStr}\nDensity: {density}";

            // Draw label.
            SceneView view = SceneView.currentDrawingSceneView;
            Vector3 cameraDir = transform.position - view.camera.transform.position;
            Vector3 tangent = Vector3.Cross(Vector3.up, cameraDir).normalized;
            Vector3 offset = tangent + (Vector3.up * 0.2f);
            Handles.Label(transform.position + offset, infoStr);

            // Draw cell.
            float cellSize = m_Terrain.CellSize;
            float3 cellCorner = (float3)globalCellIndex * cellSize;
            float3 cellCentre = cellCorner + (cellSize / 2.0f);

            Gizmos.color = new Color(0.0f, 1.0f, 0.2f, 0.5f);
            Gizmos.DrawCube(cellCentre, (float3)cellSize);

            // Draw brick.
            float brickSize = m_Terrain.BrickSize;
            float3 brickCorner = brickIndex * (float3)brickSize;
            float3 brickCentre = brickCorner + (brickSize / 2.0f);

            Gizmos.color = new Color(1.0f, 1.0f, 1.0f, 0.5f);
            Gizmos.DrawWireCube(brickCentre, (float3)brickSize);

            // Draw xyz indicators for better spacial reference.
            float dx = cellCorner.x - brickCorner.x;
            float dy = cellCorner.y - brickCorner.y;
            float dz = cellCorner.z - brickCorner.z;

            // x
            Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.5f);
            Gizmos.DrawLine(brickCorner + new float3(dx, dy, 0), brickCorner + new float3(dx, dy, brickSize));

            // y
            Gizmos.color = new Color(0.0f, 1.0f, 0.0f, 0.5f);
            Gizmos.DrawLine(brickCorner + new float3(0, dy, dz), brickCorner + new float3(brickSize, dy, dz));

            // z
            Gizmos.color = new Color(0.0f, 0.0f, 1.0f, 0.5f);
            Gizmos.DrawLine(brickCorner + new float3(dx, 0, dz), brickCorner + new float3(dx, brickSize, dz));
        }
    }
}
#endif
