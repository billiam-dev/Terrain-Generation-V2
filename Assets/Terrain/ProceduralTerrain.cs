using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public class ProceduralTerrain : MonoBehaviour
    {
        Scene m_Scene;
        DensityBrickMap m_BrickMap; // Note: eventually this can be converted to BrickMapLevels, for multiple LODs stretching into the horizen.
        DensityEvaluator m_DensityEvaluator;

        ShapeBrush[] m_ShapeBrushes;
        HashSet<int3> m_ModifiedBricks; // Note: this might be better off inside DensityBrickMap.

        DebugInfo m_DebugInfo;

        public float WorldCellSize => k_TerrainScale;                // Uniform size in world units of a single cell.
        public float WorldBrickSize => k_BrickSize * k_TerrainScale; // Uniform size in world units of a single brick.

        const float k_TerrainScale = 1.0f;    // The size of a single cell in world units, effectively controls the scale of the whole terrain.
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_CellsPerBrick = 4096;     // The total number of cells contained in a single brick (brickSize ^ 3).
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level.

        void OnEnable()
        {
            m_Scene.Allocate();
            m_BrickMap.Allocate(k_BrickmapLevelSize, k_BrickSize);
            m_DensityEvaluator.Allocate(k_CellsPerBrick);
            m_ModifiedBricks = new();

            m_DebugInfo = new()
            {
                brickSize = k_BrickSize,
                cellsPerBrick = k_CellsPerBrick,
                clipmapLevelSize = k_BrickmapLevelSize
            };

            // Build initial shape queue.
            m_ShapeBrushes = GetComponentsInChildren<ShapeBrush>(false);
            foreach (ShapeBrush shapeBrush in m_ShapeBrushes)
            {
                m_Scene.AddShape(shapeBrush.EvaluateNewShape(out float3 oldPosition, out float3 oldVolume, out float3 newPosition, out float3 newVolume));
                MarkModifiedBricksFromAABB(oldPosition, oldVolume);
                MarkModifiedBricksFromAABB(newPosition, newVolume);
                shapeBrush.IsDirty = false;
            }

            m_DebugInfo.shapeCount = m_Scene.NumShapes;
        }

        void OnDisable()
        {
            m_Scene.Dispose();
            m_BrickMap.Dispose();
            m_DensityEvaluator.Dispose();
            m_ModifiedBricks = null;
            m_DebugInfo = null;
        }

        void Update()
        {
            UpdateScene();
            UpdateBrickMap();
        }

        void UpdateScene()
        {
            for (int i = 0; i < m_ShapeBrushes.Length; i++)
            {
                ShapeBrush shapeBrush = m_ShapeBrushes[i];

                if (shapeBrush.IsDirty)
                {
                    m_Scene.EditShape(i, shapeBrush.EvaluateNewShape(out float3 oldPosition, out float3 oldVolume, out float3 newPosition, out float3 newVolume));
                    MarkModifiedBricksFromAABB(oldPosition, oldVolume);
                    MarkModifiedBricksFromAABB(newPosition, newVolume);
                    shapeBrush.IsDirty = false;
                }
            }
        }

        /// <summary>
        /// Register bricks within a world AABB for recomputation.
        /// </summary>
        void MarkModifiedBricksFromAABB(float3 boundsCentre, float3 boundsVolume)
        {
            if (math.all(boundsVolume == 0))
                return;

            GetBrickVolumeFromAABB(boundsCentre, boundsVolume, out int3 initialIndex, out int3 volume);

            for (int x = 0; x < volume.x; x++)
                for (int y = 0; y < volume.y; y++)
                    for (int z = 0; z < volume.z; z++)
                        m_ModifiedBricks.Add(initialIndex + new int3(x, y, z));
        }

        /// <summary>
        /// Get a set if itterable brick indices from a world AABB.
        /// </summary>
        void GetBrickVolumeFromAABB(float3 boundsCentre, float3 boundsVolume, out int3 initialIndex, out int3 volume)
        {
            ComputeIndices(boundsCentre, out _, out int3 brickIndex, out int3 localCellIndex);

            // Scale volume by inverse terrain scale.
            boundsVolume *= 1.0f / k_TerrainScale;

            // Snap the volume to the brick grid and output the result.
            volume = (int3)math.ceil(boundsVolume.xyz / k_BrickSize) + 1;

            // Compute the central position of the volume.
            int3 centreIndex = brickIndex;

            // For even volumes, the centre must be offset by +1 when the volume's local centre within the brick is on the positive half.
            int halfBrickSize = k_BrickSize / 2;
            if (volume.x % 2 == 0)
            {
                if (localCellIndex.x >= halfBrickSize)
                    centreIndex.x++;
            }
            if (volume.y % 2 == 0)
            {
                if (localCellIndex.y >= halfBrickSize)
                    centreIndex.y++;
            }
            if (volume.z % 2 == 0)
            {
                if (localCellIndex.z >= halfBrickSize)
                    centreIndex.z++;
            }

            // Convert the central volume position to a brick index and offset by half the volume to get the initial brick index.
            initialIndex = centreIndex - (volume / 2);
        }

        void UpdateBrickMap()
        {
            if (m_ModifiedBricks.Count == 0)
                return;

            Stopwatch.Start(ref m_DebugInfo.recomputationTime);

            int3[] recomputeQueue = new int3[m_ModifiedBricks.Count];
            m_ModifiedBricks.CopyTo(recomputeQueue);

            m_DebugInfo.recomputedBricks = recomputeQueue.Length;

            foreach (int3 brickIndex in recomputeQueue)
            {
                m_BrickMap.EvaluateBrick(brickIndex, m_Scene, k_TerrainScale, m_DensityEvaluator, out double evaluationTime);
                m_ModifiedBricks.Remove(brickIndex);
                m_DebugInfo.AddJobTime(evaluationTime);
            }

            Stopwatch.End(ref m_DebugInfo.recomputationTime);

            m_DebugInfo.numBricksAllocated = m_BrickMap.NumBricksAllocated;
        }

        /// <summary>
        /// Takes a 3D position in world space out outputs its indices within the terrain.
        /// </summary>
        public void ComputeIndices(float3 positionWS, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex)
        {
            // Scale position by inverse terrain scale.
            positionWS *= 1.0f / k_TerrainScale;

            // Output the global cell index of the position.
            globalCellIndex = (int3)math.floor(positionWS);

            // Output the brick index containing the position.
            brickIndex = (int3)math.floor(positionWS / k_BrickSize);

            // Ouput the cells index within it's encompassing brick.
            localCellIndex = globalCellIndex - (brickIndex * k_BrickSize);
        }

        /// <summary>
        /// Sample the density cache at the given world space position.
        /// </summary>
        public float SampleDensity(float3 positionWS)
        {
            ComputeIndices(positionWS, out _, out int3 brickIndex, out int3 localCellIndex);
            return m_BrickMap.Sample(brickIndex, localCellIndex);
        }

        /// <summary>
        /// Sample the density cache at the given indices.
        /// </summary>
        public float SampleDensity(int3 brickIndex, int3 cellIndex)
        {
            return m_BrickMap.Sample(brickIndex, cellIndex);
        }

        void OnGUI() => m_DebugInfo.DisplayGUI();

#if UNITY_EDITOR
        [SerializeField]
        bool m_DrawShapeVolumes;

        [SerializeField]
        bool m_DrawBricks;

        const float k_MaxBrickDrawDistance = 256.0f;

        void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.identity;

            if (m_DrawShapeVolumes) DrawShapeVolumes();
            if (m_DrawBricks) DrawBrickDebugOverlay();
        }

        void DrawShapeVolumes()
        {
            HashSet<int3> bricksInShapeVolumes = new();

            foreach (ShapeBrush shapeBrush in m_ShapeBrushes)
            {
                shapeBrush.EvaluateCurrentShape(out float3 boundsPosition, out float3 boundsVolume);
                GetBrickVolumeFromAABB(boundsPosition, boundsVolume, out int3 initialIndex, out int3 volume);

                for (int x = 0; x < volume.x; x++)
                    for (int y = 0; y < volume.y; y++)
                        for (int z = 0; z < volume.z; z++)
                            bricksInShapeVolumes.Add(initialIndex + new int3(x, y, z));
            }

            Gizmos.color = new Color(1.0f, 0.1f, 0.0f, 0.1f);
            foreach (int3 brickIndex in bricksInShapeVolumes)
            {
                float3 brickSize = k_BrickSize * k_TerrainScale;
                float3 brickCorner = brickSize * brickIndex;
                float3 bricksCentre = brickCorner + (brickSize / 2.0f);

                Gizmos.DrawCube(bricksCentre, brickSize);
            }
        }

        void DrawBrickDebugOverlay()
        {
            Camera sceneCamera = SceneView.currentDrawingSceneView.camera;
            float viewingDistance;

            float3 worldBrickSize = k_BrickSize * k_TerrainScale;
            float3 brickCorner;
            float3 brickCentre;

            foreach (int3 brickIndex in m_BrickMap.GetKeys())
            {
                brickCorner = worldBrickSize * brickIndex;
                brickCentre = brickCorner + (worldBrickSize / 2.0f);

                viewingDistance = math.length((float3)sceneCamera.transform.position - brickCentre);
                //if (viewingDistance > k_MaxBrickDrawDistance)
                //    continue;

                Color color = RandomColor(brickIndex);
                //color.a = 1.0f - (viewingDistance / k_MaxBrickDrawDistance);

                Gizmos.color = color;
                Gizmos.DrawWireCube(brickCentre, worldBrickSize);
            }
        }

        static Color RandomColor(int3 position)
        {
            System.Random random = new(position.GetHashCode());

            // Fill rgb channels with random values.
            int r = random.Next(256);
            int g = random.Next(256);
            int b = random.Next(256);

            // Mix with off-white for a pleasing pastel effect.
            r = (r + 200) / 2;
            g = (g + 200) / 2;
            b = (b + 200) / 2;

            // Convert range (0, 256) -> (0.0f, 1.0f) and return.
            return new Color(
                r / 256.0f,
                g / 256.0f,
                b / 256.0f,
                1.0f);
        }
#endif

        class DebugInfo
        {
            // Constants
            public int brickSize;
            public int cellsPerBrick;
            public int clipmapLevelSize;

            // Runtime info
            public int shapeCount;
            public int numBricksAllocated;
            public double recomputedBricks;
            public double recomputationTime;

            readonly double[] densityJobTimes;
            int densityJobTimeIdx;

            public void AddJobTime(double time)
            {
                densityJobTimes[densityJobTimeIdx] = time;

                densityJobTimeIdx++;
                if (densityJobTimeIdx >= densityJobTimes.Length)
                    densityJobTimeIdx = 0;
            }

            double AvarageDensityJobTime()
            {
                double t = 0;
                int count = 0;
                for (int i = 0; i < densityJobTimes.Length; i++)
                {
                    t += densityJobTimes[i];
                    if (t > 0)
                        count++;
                }
                
                t /= count;

                return t;
            }

            const float k_SingleLineHeight = 20.0f;

            public DebugInfo()
            {
                brickSize = 0;
                cellsPerBrick = 0;
                clipmapLevelSize = 0;

                shapeCount = 0;
                numBricksAllocated = 0;
                recomputedBricks = 0;
                recomputationTime = 0;

                densityJobTimes = new double[10];
                densityJobTimeIdx = 0;
            }

            public void DisplayGUI()
            {
                Rect rect = new(10.0f, 10.0f, 260.0f, k_SingleLineHeight);

                // Constants
                GUI.Label(rect, $"Brick Size: {brickSize} (Total cells: {cellsPerBrick})");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Brickmap Level Size: {clipmapLevelSize}");
                rect.y += k_SingleLineHeight;

                // Runtime info
                GUI.Label(rect, $"Shapes: {shapeCount}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Bricks allocated: {numBricksAllocated}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Last recomputed bricks: {recomputedBricks}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Total recomputation time: {Stopwatch.ToMilliseconds(recomputationTime)}ms");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Avarage density evaluation time: {Stopwatch.ToMilliseconds(AvarageDensityJobTime())}ms");
            }
        }
    }

    unsafe struct DensityBrickMap : IDisposable
    {
        /*
         * Note: this should be revised to look like this:
         * void*[] map;
         * List<DistanceBrick> bricks;
         *
         * However, the map would have to be managed very carefully when the player moves around the scene.
         * The benefits are fixed memory usage for the pointer map and extremely fast lookup.
        */

        NativeHashMap<int3, DensityBrick> bricks;

        int mapSize;    // Number of bricks per axis contained in this brick map.
        int brickSize;  // Number of cells per axis contained in a single brick.

        public readonly int NumBricksAllocated => bricks.Count;

        public void Allocate(int mapSize, int brickSize)
        {
            this.mapSize = mapSize;
            this.brickSize = brickSize;
            bricks = new(mapSize * mapSize * mapSize, Allocator.Persistent);
        }

        public void Dispose()
        {
            NativeArray<int3> keys = bricks.GetKeyArray(Allocator.Temp);
            foreach (int3 brickIndex in keys)
                bricks[brickIndex].Dispose();
            keys.Dispose();

            bricks.Dispose();
        }

        // Called when a brick is updated by a new shape, or existing shape change.
        public readonly void EvaluateBrick(int3 brickIndex, Scene scene, float terrainScale, DensityEvaluator densityEvaluator, out double evaluationTime)
        {
            DensityEvaluationResult result = densityEvaluator.ExecuteJob(scene.Shapes, brickIndex, brickSize, terrainScale);

            evaluationTime = result.ExecutionTime;

            if (result.IntersectsSurface)
            {
                if (!bricks.ContainsKey(brickIndex))
                    AllocateBrick(brickIndex);

                bricks[brickIndex].SetDenstiy(result.Density);
            }
            else
            {
                if (bricks.ContainsKey(brickIndex))
                    DeallocateBrick(brickIndex);
            }
        }

        readonly void AllocateBrick(int3 brickIndex)
        {
            DensityBrick brick = new();
            brick.Allocate(brickSize);
            bricks.Add(brickIndex, brick);
        }

        readonly void DeallocateBrick(int3 brickIndex)
        {
            bricks[brickIndex].Dispose();
            bricks.Remove(brickIndex);
        }

        public readonly float Sample(int3 brickIndex, int3 cellIndex)
        {
            if (!bricks.ContainsKey(brickIndex))
                return 0.0f;

            int i = (cellIndex.z * brickSize * brickSize) + (cellIndex.y * brickSize) + cellIndex.x;

            return bricks[brickIndex].Sample(i);
        }

        public readonly int3[] GetKeys()
        {
            NativeArray<int3> nativeKeys = bricks.GetKeyArray(Allocator.Temp);
            int3[] keys = nativeKeys.ToArray();
            nativeKeys.Dispose();

            return keys;
        }

        struct DensityBrick : IDisposable
        {
            NativeArray<float> density;

            public void Allocate(int size) => density = new(size * size * size, Allocator.Persistent);

            public void Dispose() => density.Dispose();

            public void SetDenstiy(NativeArray<float> density) => this.density.CopyFrom(density);

            public float Sample(int i) => density[i];

            public readonly void* GetPointer() => density.GetUnsafePtr();
        }
    }

    struct Scene : IDisposable
    {
        public readonly NativeList<Shape> Shapes => shapes;
        public readonly int NumShapes => shapes.Length;

        NativeList<Shape> shapes;

        public void Allocate()
        {
            shapes = new(Allocator.Persistent);
        }

        public void Dispose()
        {
            shapes.Dispose();
        }

        public void AddShape(Shape shape) => shapes.Add(shape);

        public void RemoveShape(int index) => shapes.RemoveAt(index);

        public void EditShape(int index, Shape shape) => shapes[index] = shape;

        public void Clear() => shapes.Clear();
    }
}
