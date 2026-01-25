using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace LevelGeneration.Terrain.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public float3 position;
        public float3 normal;
        public float3 secondaryPosition; // Padded position to make room for transition cells.
        public int edgeMask;

        public Vertex(float3 position, float3 normal, float3 secondaryPosition, int edgeMask)
        {
            this.position = position;
            this.normal = normal;
            this.secondaryPosition = secondaryPosition;
            this.edgeMask = edgeMask;
        }

        public static readonly VertexAttributeDescriptor[] Format =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 3),  // Used to store secondary positions.
            new(VertexAttribute.Color, VertexAttributeFormat.UInt32, 1)      // Used to store edge index of regular cells so the shader knows which cells to check against the packed LOD data.
        };
    }
}
