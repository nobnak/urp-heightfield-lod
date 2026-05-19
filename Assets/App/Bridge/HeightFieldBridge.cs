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
        [SerializeField] SineHeightFieldSource _heightSource;
        [SerializeField] HeightFieldLodRenderer _lodRenderer;

        HeightFieldLayout _layout;
        int _lastW = -1;
        int _lastH = -1;
        float _lastOrtho = -1f;

        void Reset()
        {
            _camera = Camera.main;
            _heightSource = GetComponent<SineHeightFieldSource>();
            _lodRenderer = GetComponent<HeightFieldLodRenderer>();
        }

        void OnEnable() => TryRebuild(force: true);

        void OnDisable()
        {
            _heightSource?.Release();
            _lodRenderer?.Release();
        }

        void Update()
        {
            if (_camera == null || _heightSource == null || _lodRenderer == null)
                return;

            if (NeedsRebuild())
                TryRebuild(force: true);

            _heightSource.UpdateHeight(_layout, Time.time);
            _lodRenderer.Tick(_heightSource.HeightTexture, Time.deltaTime);
        }

        bool NeedsRebuild()
        {
            return _camera.pixelWidth != _lastW
                || _camera.pixelHeight != _lastH
                || !Mathf.Approximately(_camera.orthographicSize, _lastOrtho);
        }

        void TryRebuild(bool force)
        {
            if (_camera == null) return;
            if (!force && !NeedsRebuild()) return;

            _layout = HeightFieldLayout.FromCamera(_camera, _barrierChunks);
            _lastW = _camera.pixelWidth;
            _lastH = _camera.pixelHeight;
            _lastOrtho = _camera.orthographicSize;

            _heightSource.Allocate(_layout);
            _lodRenderer.Configure(_layout, _camera);
        }
    }
}
