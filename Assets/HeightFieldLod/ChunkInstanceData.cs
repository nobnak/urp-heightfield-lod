using System.Runtime.InteropServices;
using UnityEngine;

namespace HeightFieldLod
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkInstanceData
    {
        public const int Stride = sizeof(float) * 8;

        public Vector4 WorldScaleCenter;
        public Vector4 UvScaleOffset;

        public static ChunkInstanceData Create(HeightField.HeightFieldLayout layout, int ix, int iy)
        {
            layout.GetChunkCenter(ix, iy, out float cx, out float cy);
            layout.GetChunkUvTile(ix, iy, out Vector2 uvOff, out Vector2 uvScale);
            return new ChunkInstanceData
            {
                WorldScaleCenter = new Vector4(layout.ChunkWorldWidth, layout.ChunkWorldHeight, cx, cy),
                UvScaleOffset = new Vector4(uvScale.x, uvScale.y, uvOff.x, uvOff.y)
            };
        }
    }
}
