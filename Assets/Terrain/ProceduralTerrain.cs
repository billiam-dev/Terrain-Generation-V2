using System;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace LevelGeneration.Terrain
{
    [ExecuteInEditMode]
    public class ProceduralTerrain : MonoBehaviour
    {
        DensityBrickMap m_BrickMap; // Note: eventually this can be converted to BrickMapLevels, for multiple LODs stretching into the horizen.
        Scene m_Scene;
        DensityEvaluator m_DensityEvaluator;

        ShapeBrush[] m_ShapeBrushes;

        DebugInfo m_DebugInfo;

        public float CellSize => k_TerrainScale;                // Uniform size in world units of a single cell.
        public float BrickSize => k_BrickSize * k_TerrainScale; // Uniform size in world units of a single brick.

        const float k_TerrainScale = 1.0f;    // The size of a single cell in world units, effectively controls the scale of the whole terrain.
        const int k_BrickSize = 16;           // The number of cells per axis contained in a single brick.
        const int k_CellsPerBrick = 4096;     // The total number of cells contained in a single brick (brickSize ^ 3).
        const int k_BrickmapLevelSize = 8;    // The number of bricks per axis of a single brickmap level.

        void OnEnable()
        {
            m_BrickMap.Allocate(k_BrickmapLevelSize, k_BrickSize);
            m_Scene.Allocate();
            m_DensityEvaluator.Allocate(k_CellsPerBrick);

            m_ShapeBrushes = GetComponentsInChildren<ShapeBrush>(false);
            foreach (ShapeBrush shapeBrush in m_ShapeBrushes)
                m_Scene.AddShape(shapeBrush.Shape);

            BrickTest();

            m_DebugInfo.brickSize = k_BrickSize;
            m_DebugInfo.cellsPerBrick = k_CellsPerBrick;
            m_DebugInfo.clipmapLevelSize = k_BrickmapLevelSize;
            m_DebugInfo.shapeCount = m_Scene.NumShapes;
        }

        void OnDisable()
        {
            m_BrickMap.Dispose();
            m_Scene.Dispose();
            m_DensityEvaluator.Dispose();
        }

        /*
        void Update()
        {
            HashSet<int3> recomputeBricks = new();

            m_ShapeBrushes = GetComponentsInChildren<ShapeBrush>(false);
            foreach (ShapeBrush shapeBrush in m_ShapeBrushes)
            {
                if (shapeBrush.IsDirty)
                {
                    // Add new and old volume to bricks thing.
                    recomputeBricks.Add();
                    shapeBrush.IsDirty = false;
                }

                // m_Scene.AddShape(shapeBrush.Shape);
            }

            //Evaluate();
        }
        */

        void BrickTest()
        {
            // Test function, simply evaluate every brick originating at (0, 0, 0).

            Stopwatch.Start(ref m_DebugInfo.recomputationTime);

            for (int x = 0; x < k_BrickmapLevelSize; x++)
                for (int y = 0; y < k_BrickmapLevelSize; y++)
                    for (int z = 0; z < k_BrickmapLevelSize; z++)
                        m_BrickMap.EvaluateBrick(new int3(x, y, z), m_Scene, k_TerrainScale, m_DensityEvaluator);

            Stopwatch.End(ref m_DebugInfo.recomputationTime);

            m_DebugInfo.numBricksAllocated = m_BrickMap.NumBricksAllocated;
        }

        /// <summary>
        /// Takes a 3D position in world space out outputs its indices within the terrain.
        /// </summary>
        public void ComputeIndices(float3 positionWS, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex)
        {
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
            ComputeIndices(positionWS, out int3 globalCellIndex, out int3 brickIndex, out int3 localCellIndex);
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
        bool m_DrawBricks;

        const float k_MaxBrickDrawDistance = 256.0f;

        void OnDrawGizmos()
        {
            if (m_DrawBricks) DrawBrickDebugOverlay();
        }

        void DrawBrickDebugOverlay()
        {
            Camera sceneCamera = SceneView.currentDrawingSceneView.camera;
            float viewingDistance;

            float3 brickCorner;
            float3 brickCentre;

            foreach (int3 brickIndex in m_BrickMap.GetKeys())
            {
                brickCorner = BrickSize * (float3)brickIndex;
                brickCentre = brickCorner + (BrickSize / 2.0f);

                viewingDistance = math.length((float3)sceneCamera.transform.position - brickCentre);
                if (viewingDistance > k_MaxBrickDrawDistance)
                    continue;

                Color color = RandomColor(brickIndex);
                color.a = 1.0f - (viewingDistance / k_MaxBrickDrawDistance);

                Gizmos.color = color;
                Gizmos.DrawWireCube(brickCentre, (float3)BrickSize);
            }
        }

        static Color RandomColor(int3 position)
        {
            System.Random random = new(position.GetHashCode());

            // Fill rgb channels with random values.
            int r = random.Next(256);
            int g = random.Next(256);
            int b = random.Next(256);

            // Mix with white for a pleasing pastel effect.
            r = (r + 256) / 2;
            g = (g + 256) / 2;
            b = (b + 256) / 2;

            // Convert range (0, 256) -> (0.0f, 1.0f) and return.
            return new Color(
                r / 256.0f,
                g / 256.0f,
                b / 256.0f,
                1.0f);
        }
#endif

        struct DebugInfo
        {
            // Constants
            public int brickSize;
            public int cellsPerBrick;
            public int clipmapLevelSize;

            // Runtime info
            public int shapeCount;
            public int numBricksAllocated;
            public double recomputationTime;

            const float k_SingleLineHeight = 20.0f;

            public readonly void DisplayGUI()
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
                GUI.Label(rect, $"Bricks Allocated: {numBricksAllocated}");
                rect.y += k_SingleLineHeight;
                GUI.Label(rect, $"Recomputation Time: {Stopwatch.ToMilliseconds(recomputationTime)}ms");
            }
        }
    }

    unsafe struct DensityBrickMap : IDisposable
    {
        /*
         * Note: it is possible this could be made faster like this:
         * void*[] map;
         * List<DistanceBrick> bricks;
         *
         * However, the map would have to be managed very carefully when the player moves around the scene.
        */

        NativeHashMap<int3, DistanceBrick> bricks;

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
            for (int x = 0; x < mapSize; x++)
            {
                for (int y = 0; y < mapSize; y++)
                {
                    for (int z = 0; z < mapSize; z++)
                    {
                        int3 brickIndex = new(x, y, z);

                        if (bricks.ContainsKey(brickIndex))
                            bricks[brickIndex].Dispose();
                    }
                }
            }

            bricks.Dispose();
        }

        // Called when a brick is updated by a new shape, or existing shape change.
        public readonly void EvaluateBrick(int3 brickIndex, Scene scene, float terrainScale, DensityEvaluator densityEvaluator)
        {
            DensityEvaluationResult result = densityEvaluator.ExecuteJob(scene.Shapes, brickIndex, brickSize, terrainScale);

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
            DistanceBrick brick = new();
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

        struct DistanceBrick : IDisposable
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
    }
}
