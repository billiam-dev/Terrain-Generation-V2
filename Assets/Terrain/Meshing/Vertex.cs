using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace LevelGeneration.Terrain.Meshing
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public float3 position;
        public float3 normal;
        public float3 secondaryPosition; // Padded position to make room for transition cells.
        public ushort edgeMask;          // Vertex edge mask, use in combination with neighbor LOD data to select secondaty positions.

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

        public static readonly int Size = Marshal.SizeOf(typeof(Vertex));
    }
}
