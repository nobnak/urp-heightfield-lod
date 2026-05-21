using UnityEngine;
using Motion = App.ViewMotion.ViewMotion;
using ViewMotionParams = App.ViewMotion.ViewMotion.Params;
using ViewMotionState = App.ViewMotion.ViewMotion.State;

namespace App.HeadSway
{
    /// <summary>
    /// <see cref="ViewMotion"/> の d（m）で <see cref="ConvergingLensShift"/> を駆動し、リグ固定のまま頭部動揺を再現する。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class HeadSwayLensShiftCamera : MonoBehaviour
    {
        [SerializeField] ViewMotionParams _motion = new();
        [SerializeField, Min(1e-4f)] float _focusDistance = 5f;
        [SerializeField] bool _useUnscaledTime;

        UnityEngine.Camera _cam;
        bool _storedPhysical;
        ViewMotionState _motionState;

        #region Unity lifecycle
        void OnEnable()
        {
            _cam = GetComponent<UnityEngine.Camera>();
            _storedPhysical = _cam != null && _cam.usePhysicalProperties;
        }

        void LateUpdate()
        {
            if (_cam == null || !_cam.enabled)
                return;
            float t = _useUnscaledTime ? Time.unscaledTime : Time.time;
            float dt = Application.isPlaying
                ? (_useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime)
                : 0f;
            var d = Motion.Evaluate(_motion, t, dt, ref _motionState);
            ConvergingLensShift.Apply(_cam, d, Mathf.Max(1e-4f, _focusDistance));
        }

        void OnDisable()
        {
            if (_cam == null)
                return;
            ConvergingLensShift.Reset(_cam, _storedPhysical);
            _motionState = default;
        }

        void OnValidate() => _focusDistance = Mathf.Max(1e-4f, _focusDistance);
        #endregion
    }
}
