using UnityEngine;

namespace App.HeadSway
{
    /// <summary>収束深度 z_f で、接線変位 d（m）に応じて V（視点）と P（レンズシフト）を更新する。</summary>
    public static class ConvergingLensShift
    {
        public static void Apply(UnityEngine.Camera cam, Vector2 d, float zf)
        {
            float near = cam.nearClipPlane;
            float far = cam.farClipPlane;
            float aspect = Mathf.Max(1e-5f, cam.aspect);
            float invZf = 1f / zf;

            cam.usePhysicalProperties = false;
            cam.ResetWorldToCameraMatrix();
            Matrix4x4 v0 = cam.worldToCameraMatrix;
            if (d.sqrMagnitude > 1e-20f) {
                var dw = cam.transform.TransformVector(new Vector3(-d.x, -d.y, 0f));
                cam.worldToCameraMatrix = v0 * Matrix4x4.Translate(-dw);
            }

            if (cam.orthographic) {
                float halfH = cam.orthographicSize;
                float halfW = halfH * aspect;
                cam.projectionMatrix = Matrix4x4.Ortho(-halfW, halfW, -halfH, halfH, near, far)
                    * ShearZ(d.x * invZf, d.y * invZf);
            } else {
                float scale = near * invZf;
                float sx = d.x * scale;
                float sy = d.y * scale;
                float fovY = cam.fieldOfView * Mathf.Deg2Rad;
                float top = near * Mathf.Tan(fovY * 0.5f);
                float right = top * aspect;
                cam.projectionMatrix = Matrix4x4.Frustum(
                    -right + sx, right + sx,
                    -top + sy, top + sy,
                    near, far);
            }
        }

        public static void Reset(UnityEngine.Camera cam, bool usePhysicalProperties)
        {
            cam.ResetWorldToCameraMatrix();
            cam.ResetProjectionMatrix();
            cam.usePhysicalProperties = usePhysicalProperties;
        }

        static Matrix4x4 ShearZ(float kx, float ky)
        {
            var m = Matrix4x4.identity;
            m.m02 = kx;
            m.m12 = ky;
            return m;
        }
    }
}
