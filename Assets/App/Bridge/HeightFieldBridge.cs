using HeightField;
using HeightFieldLod;
using UnityEngine;

namespace App.Bridge
{
    [DisallowMultipleComponent]
    public sealed class HeightFieldBridge : MonoBehaviour
    {
        [SerializeField] HeightFieldLayoutHost _layoutHost;
        [SerializeField] Camera _camera;
        [SerializeField] int _barrierChunks = 2;
        [SerializeField] MonoBehaviour _heightSource;
        [SerializeField] HeightFieldLodCompute _lodCompute;
        [SerializeField] HeightFieldChunkMeshDrawer _drawer;

        int _lastW = -1;
        int _lastH = -1;
        float _lastOrtho = -1f;

        IHeightFieldSource HeightSource => _heightSource as IHeightFieldSource;

        void Reset()
        {
            _layoutHost = GetComponent<HeightFieldLayoutHost>();
            if (_layoutHost == null)
                _layoutHost = gameObject.AddComponent<HeightFieldLayoutHost>();
            _camera = Camera.main;
            _heightSource = FindHeightSourceOn(gameObject);
            _lodCompute = GetComponent<HeightFieldLodCompute>();
            _drawer = GetComponent<HeightFieldChunkMeshDrawer>();
        }

        void OnValidate()
        {
            if (_heightSource != null && _heightSource is not IHeightFieldSource)
                Debug.LogWarning($"{name}: _heightSource must implement {nameof(IHeightFieldSource)}.", this);
        }

        void OnEnable() => TryRebuild(force: true);

        void OnDisable()
        {
            HeightSource?.Release();
            _lodCompute?.Release();
            _drawer?.Release();
        }

        void Update()
        {
            var cam = ResolveCamera();
            if (cam == null) return;
            if (NeedsRebuild(cam))
                TryRebuild(force: true);
        }

        bool NeedsRebuild(Camera cam)
        {
            return cam.pixelWidth != _lastW
                || cam.pixelHeight != _lastH
                || !Mathf.Approximately(cam.orthographicSize, _lastOrtho);
        }

        void TryRebuild(bool force)
        {
            var source = HeightSource;
            if (source == null || _lodCompute == null) return;

            var cam = ResolveCamera();
            if (cam == null) return;
            if (!force && !NeedsRebuild(cam) && _lodCompute.LodMeshes != null)
                return;

            var layout = ResolveLayout(cam);
            if (layout.TexWidth <= 0) return;

            _lastW = cam.pixelWidth;
            _lastH = cam.pixelHeight;
            _lastOrtho = cam.orthographicSize;

            source.Allocate(layout);
            _lodCompute.Configure(layout);
            if (_drawer != null)
                _drawer.Configure(layout, cam);
        }

        Camera ResolveCamera()
        {
            if (_layoutHost != null && _layoutHost.Camera != null)
                return _layoutHost.Camera;
            return _camera;
        }

        HeightFieldLayout ResolveLayout(Camera cam)
        {
            if (_layoutHost != null && _layoutHost.EnsureLayout())
                return _layoutHost.Layout;
            return HeightFieldLayout.FromCamera(cam, _barrierChunks);
        }

        static MonoBehaviour FindHeightSourceOn(GameObject go)
        {
            var components = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IHeightFieldSource)
                    return components[i];
            }
            return null;
        }
    }
}
