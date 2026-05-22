using HeightField;
using HeightFieldLod;
using UnityEngine;

namespace App.Bridge
{
    [DisallowMultipleComponent]
    public sealed class HeightFieldBridge : MonoBehaviour
    {
        [SerializeField] Camera _camera;
        [SerializeField] int _barrierChunks = 2;
        [SerializeField] MonoBehaviour _heightSource;
        [SerializeField] HeightFieldLodRenderer _lodRenderer;

        HeightFieldLayout _layout;
        int _lastW = -1;
        int _lastH = -1;
        float _lastOrtho = -1f;

        IHeightFieldSource HeightSource => _heightSource as IHeightFieldSource;

        void Reset()
        {
            _camera = Camera.main;
            _heightSource = FindHeightSourceOn(gameObject);
            _lodRenderer = GetComponent<HeightFieldLodRenderer>();
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
            _lodRenderer?.Release();
        }

        void Update()
        {
            var source = HeightSource;
            if (_camera == null || source == null || _lodRenderer == null)
                return;

            if (NeedsRebuild())
                TryRebuild(force: true);

            source.UpdateHeight(_layout, Time.time);
            _lodRenderer.Tick(source.HeightTexture, Time.deltaTime);
        }

        bool NeedsRebuild()
        {
            return _camera.pixelWidth != _lastW
                || _camera.pixelHeight != _lastH
                || !Mathf.Approximately(_camera.orthographicSize, _lastOrtho);
        }

        void TryRebuild(bool force)
        {
            var source = HeightSource;
            if (_camera == null || source == null) return;
            if (!force && !NeedsRebuild()) return;

            _layout = HeightFieldLayout.FromCamera(_camera, _barrierChunks);
            _lastW = _camera.pixelWidth;
            _lastH = _camera.pixelHeight;
            _lastOrtho = _camera.orthographicSize;

            source.Allocate(_layout);
            _lodRenderer.Configure(_layout, _camera);
        }

        static MonoBehaviour FindHeightSourceOn(GameObject go)
        {
            var components = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++) {
                if (components[i] is IHeightFieldSource)
                    return components[i];
            }
            return null;
        }
    }
}
