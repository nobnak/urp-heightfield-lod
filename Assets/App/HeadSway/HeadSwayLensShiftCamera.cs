using UnityEngine;
using Motion = App.ViewMotion.ViewMotion;
using ViewMotionParams = App.ViewMotion.ViewMotion.Params;
using ViewMotionState = App.ViewMotion.ViewMotion.State;

namespace App.HeadSway
{
    /// <summary>
    /// 接線変位 d から u = d/A を求め、収束深度 z_f で P（レンズシフト）と V（視点オフセット）を更新する。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class HeadSwayLensShiftCamera : MonoBehaviour
    {
        [Header("Head sway")]
        [SerializeField] ViewMotionParams _motion = new();

        [Header("Convergence")]
        [SerializeField, Min(1e-4f)] float _focusDistance = 5f;
        [Tooltip("u=±1 相当の変位スケール A（m）。d から u = d/A。")]
        [SerializeField, Min(1e-6f)] float _amplitude = 0.0325f;
        [Tooltip("同じ u で投影 P とビュー V を連動する。")]
        [SerializeField] bool _link = true;

        [Header("Time")]
        [SerializeField] bool _useUnscaledTime;

        UnityEngine.Camera _cam;
        bool _storedPhysical;
        ViewMotionState _motionState;
        Vector2 _d;

        #region Unity lifecycle
        void OnEnable()
        {
            _cam = GetComponent<UnityEngine.Camera>();
            _storedPhysical = _cam != null && _cam.usePhysicalProperties;
        }

        void Update()
        {
            if (!isActiveAndEnabled)
                return;
            float t = _useUnscaledTime ? Time.unscaledTime : Time.time;
            float dt = Application.isPlaying
                ? (_useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime)
                : 0f;
            _d = Motion.Evaluate(_motion, t, dt, ref _motionState);
        }

        void LateUpdate()
        {
            if (_cam == null || !_cam.enabled)
                return;
            ApplyMatrices(_d, Mathf.Max(1e-4f, _focusDistance));
        }

        void OnDisable()
        {
            if (_cam == null)
                return;
            _cam.ResetWorldToCameraMatrix();
            _cam.ResetProjectionMatrix();
            _cam.usePhysicalProperties = _storedPhysical;
            _motionState = default;
            _d = Vector2.zero;
        }

        void OnValidate() => _focusDistance = Mathf.Max(1e-4f, _focusDistance);
        #endregion

        #region private methods
        void ApplyMatrices(Vector2 d, float zf)
        {
            float a = Mathf.Max(1e-6f, _amplitude);
            var u = d / a;
            float near = _cam.nearClipPlane;
            float far = _cam.farClipPlane;
            float aspect = Mathf.Max(1e-5f, _cam.aspect);

            _cam.usePhysicalProperties = false;
            _cam.ResetWorldToCameraMatrix();
            Matrix4x4 v0 = _cam.worldToCameraMatrix;
            Vector3 dw = Vector3.zero;
            if (_link) {
                var deye = new Vector3(-a * u.x, -a * u.y, 0f);
                dw = _cam.transform.TransformVector(deye);
                if (dw.sqrMagnitude > 1e-20f)
                    _cam.worldToCameraMatrix = v0 * Matrix4x4.Translate(-dw);
            }

            if (_cam.orthographic) {
                float halfH = _cam.orthographicSize;
                float halfW = halfH * aspect;
                float invZ = 1f / zf;
                float kx = u.x * a * invZ;
                float ky = u.y * a * invZ;
                _cam.projectionMatrix = Matrix4x4.Ortho(-halfW, halfW, -halfH, halfH, near, far)
                    * ShearViewZ(kx, ky);
            } else {
                float w = a * (near / zf);
                float sx = u.x * w;
                float sy = u.y * w;
                float fovY = _cam.fieldOfView * Mathf.Deg2Rad;
                float top = near * Mathf.Tan(fovY * 0.5f);
                float right = top * aspect;
                _cam.projectionMatrix = Matrix4x4.Frustum(
                    -right + sx, right + sx,
                    -top + sy, top + sy,
                    near, far);
            }
        }
        #endregion

        #region constants / static
        static Matrix4x4 ShearViewZ(float kx, float ky)
        {
            var m = Matrix4x4.identity;
            m.m02 = kx;
            m.m12 = ky;
            return m;
        }
        #endregion
    }
}
