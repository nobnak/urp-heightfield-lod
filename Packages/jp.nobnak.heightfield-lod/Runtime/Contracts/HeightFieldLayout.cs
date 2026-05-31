using UnityEngine;

namespace HeightField
{
    public readonly struct HeightFieldLayout
    {
        public const int ChunkPixelSize = 32;

        public int BarrierChunks { get; }
        public int CoreWidth { get; }
        public int CoreHeight { get; }
        public int TexWidth { get; }
        public int TexHeight { get; }
        public int ChunkCountX { get; }
        public int ChunkCountY { get; }
        public int ChunkCount => ChunkCountX * ChunkCountY;
        public float TotalWorldWidth { get; }
        public float TotalWorldHeight { get; }
        public float PixelWorldX { get; }
        public float PixelWorldY { get; }
        public float ChunkWorldWidth => ChunkPixelSize * PixelWorldX;
        public float ChunkWorldHeight => ChunkPixelSize * PixelWorldY;

        public HeightFieldLayout(int barrierChunks, int coreW, int coreH, float pixelWorldX, float pixelWorldY)
        {
            BarrierChunks = barrierChunks;
            CoreWidth = coreW;
            CoreHeight = coreH;
            TexWidth = coreW + 2 * barrierChunks * ChunkPixelSize;
            TexHeight = coreH + 2 * barrierChunks * ChunkPixelSize;
            ChunkCountX = TexWidth / ChunkPixelSize;
            ChunkCountY = TexHeight / ChunkPixelSize;
            PixelWorldX = pixelWorldX;
            PixelWorldY = pixelWorldY;
            TotalWorldWidth = TexWidth * pixelWorldX;
            TotalWorldHeight = TexHeight * pixelWorldY;
        }

        public static int Align32(int v) => (v + 31) & ~31;

        public static HeightFieldLayout FromCamera(Camera camera, int barrierChunks)
        {
            int coreW = Align32(camera.pixelWidth);
            int coreH = Align32(camera.pixelHeight);
            float coreWorldH = 2f * camera.orthographicSize;
            float coreWorldW = coreWorldH * camera.aspect;
            float pixelWorldX = coreWorldW / camera.pixelWidth;
            float pixelWorldY = coreWorldH / camera.pixelHeight;
            return new HeightFieldLayout(barrierChunks, coreW, coreH, pixelWorldX, pixelWorldY);
        }

        public void GetChunkCenter(int ix, int iy, out float centerX, out float centerY)
        {
            centerX = -TotalWorldWidth * 0.5f + (ix + 0.5f) * ChunkWorldWidth;
            centerY = -TotalWorldHeight * 0.5f + (iy + 0.5f) * ChunkWorldHeight;
        }

        public void GetChunkUvTile(int ix, int iy, out Vector2 uvOffset, out Vector2 uvScale)
        {
            uvScale = new Vector2(ChunkPixelSize / (float)TexWidth, ChunkPixelSize / (float)TexHeight);
            uvOffset = new Vector2(ix * uvScale.x, iy * uvScale.y);
        }
    }
}
