using UnityEngine;

namespace App
{
    /// <summary>エディタの Scene View カメラに Transform と <see cref="UnityEngine.Camera"/> 設定を同期する。</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnityEngine.Camera))]
    [DefaultExecutionOrder(32000)]
    public sealed class SceneViewCameraTracker : MonoBehaviour
    {
        [SerializeField] bool _syncWhenPlaying;

        UnityEngine.Camera _cam;

        #region Unity lifecycle
        void OnEnable()
        {
            _cam = GetComponent<UnityEngine.Camera>();
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui += OnSceneGui;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui -= OnSceneGui;
#endif
        }

        void LateUpdate()
        {
#if UNITY_EDITOR
            if (!ShouldSync())
                return;
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null)
                Sync(sv);
#endif
        }
        #endregion

#if UNITY_EDITOR
        void OnSceneGui(UnityEditor.SceneView sv)
        {
            if (!ShouldSync() || sv != UnityEditor.SceneView.lastActiveSceneView)
                return;
            Sync(sv);
        }

        bool ShouldSync()
        {
            if (!isActiveAndEnabled || _cam == null || !_cam.enabled)
                return false;
            if (Application.isPlaying && !_syncWhenPlaying)
                return false;
            return true;
        }

        void Sync(UnityEditor.SceneView sv)
        {
            var src = sv.camera;
            if (src == null)
                return;

            transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);

            _cam.orthographic = src.orthographic;
            _cam.orthographicSize = src.orthographicSize;
            _cam.fieldOfView = src.fieldOfView;
            _cam.nearClipPlane = src.nearClipPlane;
            _cam.farClipPlane = src.farClipPlane;
            _cam.lensShift = src.lensShift;
            _cam.usePhysicalProperties = src.usePhysicalProperties;
            if (src.usePhysicalProperties) {
                _cam.focalLength = src.focalLength;
                _cam.sensorSize = src.sensorSize;
            }
            _cam.ResetWorldToCameraMatrix();
            _cam.ResetProjectionMatrix();
        }
#endif
    }
}
