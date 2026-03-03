using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace TerrainSystem.Meshing
{
    [StructLayout(LayoutKind.Sequential)]
    struct Vertex
    {
        public float3 position;
        public float3 normal;
        public float3 secondaryPosition; // Padded position to make room for transition cells.
        public ushort edgeMask;          // Vertex edge mask, use in combination with neighbor LOD data to select secondaty positions.

        public const int SizeBytes = 40; // Size in bytes of a single vertex.

        public Vertex(float3 position, float3 secondaryPosition, float3 normal, ushort edgeMask)
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
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 3),  // Secondary position.
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.UInt32, 1)    // Edge mask.
        };
    }
}
