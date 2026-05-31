using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeightFieldLod
{
    public static class ChunkMeshBuilder
    {
        static readonly int[] SegmentCounts = { 32, 16, 8, 4 };

        public static Mesh[] BuildLodMeshes(float skirtDepthMeters)
        {
            var meshes = new Mesh[SegmentCounts.Length];
            for (int i = 0; i < SegmentCounts.Length; i++)
                meshes[i] = BuildChunkMesh(SegmentCounts[i], skirtDepthMeters);
            return meshes;
        }

        public static Mesh BuildChunkMesh(int segments, float skirtDepthMeters)
        {
            int n = segments + 1;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            int AddVert(Vector2 xy, float zLocal)
            {
                vertices.Add(new Vector3(xy.x, xy.y, zLocal));
                normals.Add(-Vector3.forward);
                uvs.Add(xy);
                return vertices.Count - 1;
            }

            var grid = new int[n, n];
            for (int gy = 0; gy < n; gy++)
            {
                for (int gx = 0; gx < n; gx++)
                {
                    var xy = new Vector2(gx / (float)segments, gy / (float)segments);
                    grid[gx, gy] = AddVert(xy, 0f);
                }
            }

            for (int gy = 0; gy < segments; gy++)
            {
                for (int gx = 0; gx < segments; gx++)
                {
                    int i0 = grid[gx, gy];
                    int i1 = grid[gx + 1, gy];
                    int i2 = grid[gx, gy + 1];
                    int i3 = grid[gx + 1, gy + 1];
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            }

            void AddSkirtQuad(int ia, int ib, Vector2 a, Vector2 b)
            {
                int sa = AddVert(a, skirtDepthMeters);
                int sb = AddVert(b, skirtDepthMeters);
                tris.Add(ia); tris.Add(sa); tris.Add(ib);
                tris.Add(ib); tris.Add(sa); tris.Add(sb);
            }

            for (int gx = 0; gx < segments; gx++)
            {
                int ia = grid[gx, 0];
                int ib = grid[gx + 1, 0];
                var a = new Vector2(gx / (float)segments, 0f);
                var b = new Vector2((gx + 1) / (float)segments, 0f);
                AddSkirtQuad(ia, ib, a, b);
            }

            for (int gy = 0; gy < segments; gy++)
            {
                int ia = grid[segments, gy];
                int ib = grid[segments, gy + 1];
                var a = new Vector2(1f, gy / (float)segments);
                var b = new Vector2(1f, (gy + 1) / (float)segments);
                AddSkirtQuad(ia, ib, a, b);
            }

            for (int gx = 0; gx < segments; gx++)
            {
                int ia = grid[segments - gx, segments];
                int ib = grid[segments - gx - 1, segments];
                var a = new Vector2((segments - gx) / (float)segments, 1f);
                var b = new Vector2((segments - gx - 1) / (float)segments, 1f);
                AddSkirtQuad(ia, ib, a, b);
            }

            for (int gy = 0; gy < segments; gy++)
            {
                int ia = grid[0, segments - gy];
                int ib = grid[0, segments - gy - 1];
                var a = new Vector2(0f, (segments - gy) / (float)segments);
                var b = new Vector2(0f, (segments - gy - 1) / (float)segments);
                AddSkirtQuad(ia, ib, a, b);
            }

            ValidateTriangles(tris, vertices.Count, segments);

            var mesh = new Mesh
            {
                name = $"ChunkMesh_{segments}",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void ValidateTriangles(List<int> tris, int vertCount, int segments)
        {
            for (int i = 0; i < tris.Count; i++)
            {
                int idx = tris[i];
                if (idx < 0 || idx >= vertCount)
                    throw new System.InvalidOperationException(
                        $"ChunkMesh_{segments}: triangle index {idx} out of range [0, {vertCount}) at tri offset {i}");
            }
        }
    }
}
