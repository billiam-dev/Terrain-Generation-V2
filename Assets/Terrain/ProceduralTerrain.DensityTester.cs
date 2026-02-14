#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    public partial class ProceduralTerrain : MonoBehaviour
    {
        public bool m_EnableDensityTester;
        public Vector3 m_DensityTesterPosition;

        void DrawDensityTester()
        {
            if (m_EnableDensityTester) DrawDensitySampler(m_DensityTesterPosition);
        }

        public void DrawDensitySampler(Vector3 position)
        {
            ComputeIndices(k_BrickSize, k_WorldScale, position, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex);

            double t = 0.0;
            Stopwatch.Start(ref t);
            float density = SampleDensity(position, false);
            Stopwatch.End(ref t);

            // Make label string.
            string globalIndexStr = string.Format("({0}, {1}, {2})", globalCellIndex.x, globalCellIndex.y, globalCellIndex.z);
            string brickIndexStr = string.Format("({0}, {1}, {2})", brickIndex.x, brickIndex.y, brickIndex.z);
            string cellIndexStr = string.Format("({0}, {1}, {2})", localCellIndex.x, localCellIndex.y, localCellIndex.z);
            string sampleTime = string.Format("{0}ms", Stopwatch.ToMilliseconds(t));
            string infoStr = $"Global Cell: {globalIndexStr}\nBrick: {brickIndexStr}\nLocal cell: {cellIndexStr}\nDensity: {density}\n t: {sampleTime}";

            // Draw label.
            SceneView view = SceneView.currentDrawingSceneView;
            Vector3 cameraDir = position - view.camera.transform.position;
            Vector3 tangent = Vector3.Cross(Vector3.up, cameraDir).normalized;
            Vector3 offset = tangent + (Vector3.up * 0.2f);
            Handles.Label(position + offset, infoStr);

            // Draw cell.
            float3 worldCellSize = 1.0f * k_WorldScale;
            float3 cellCorner = (float3)globalCellIndex * worldCellSize;
            float3 cellCentre = cellCorner + (worldCellSize / 2.0f);

            Gizmos.color = new Color(0.0f, 1.0f, 0.2f, 0.5f);
            Gizmos.DrawCube(cellCentre, worldCellSize);

            // Draw brick.
            float3 worldBrickSize = k_BrickSize * worldCellSize;
            float3 brickCorner = brickIndex * worldBrickSize;
            float3 brickCentre = brickCorner + (worldBrickSize / 2.0f);

            Gizmos.color = new Color(1.0f, 1.0f, 1.0f, 0.5f);
            Gizmos.DrawWireCube(brickCentre, worldBrickSize);

            // Draw xyz indicators for better spacial reference.
            float dx = cellCorner.x - brickCorner.x;
            float dy = cellCorner.y - brickCorner.y;
            float dz = cellCorner.z - brickCorner.z;

            // x
            Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.5f);
            Gizmos.DrawLine(brickCorner + new float3(dx, dy, 0), brickCorner + new float3(dx, dy, worldBrickSize.z));

            // y
            Gizmos.color = new Color(0.0f, 1.0f, 0.0f, 0.5f);
            Gizmos.DrawLine(brickCorner + new float3(0, dy, dz), brickCorner + new float3(worldBrickSize.x, dy, dz));

            // z
            Gizmos.color = new Color(0.0f, 0.0f, 1.0f, 0.5f);
            Gizmos.DrawLine(brickCorner + new float3(dx, 0, dz), brickCorner + new float3(dx, worldBrickSize.y, dz));
        }
    }
}
#endif
