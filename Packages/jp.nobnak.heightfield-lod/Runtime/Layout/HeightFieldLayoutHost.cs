using HeightField;
using UnityEngine;

namespace HeightFieldLod
{
    [DisallowMultipleComponent]
    public sealed class HeightFieldLayoutHost : MonoBehaviour
    {
        [SerializeField] Camera _camera;
        [SerializeField] int _barrierChunks = 2;

        HeightFieldLayout _layout;
        int _lastW = -1;
        int _lastH = -1;
        float _lastOrtho = -1f;

        public Camera Camera => _camera;
        public HeightFieldLayout Layout => _layout;

        public bool EnsureLayout()
        {
            if (_camera == null) return false;
            if (!NeedsRebuild()) return _layout.TexWidth > 0;
            _layout = HeightFieldLayout.FromCamera(_camera, _barrierChunks);
            _lastW = _camera.pixelWidth;
            _lastH = _camera.pixelHeight;
            _lastOrtho = _camera.orthographicSize;
            return true;
        }

        bool NeedsRebuild()
        {
            return _camera.pixelWidth != _lastW
                || _camera.pixelHeight != _lastH
                || !Mathf.Approximately(_camera.orthographicSize, _lastOrtho);
        }
    }
}
