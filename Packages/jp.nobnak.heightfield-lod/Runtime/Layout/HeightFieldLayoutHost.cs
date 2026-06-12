using System;
using HeightField;
using UnityEngine;

namespace HeightFieldLod
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class HeightFieldLayoutHost : MonoBehaviour
    {
        [SerializeField] Camera _camera;
        [SerializeField] int _barrierChunks = 2;
        [SerializeField] MonoBehaviour _heightSourceBehaviour;
        [SerializeField] HeightFieldLodCompute _lodCompute;
        [SerializeField] HeightFieldChunkMeshDrawer[] _drawers;

        HeightFieldLayout _layout;
        int _lastW = -1;
        int _lastH = -1;
        float _lastOrtho = -1f;

        public event Action<HeightFieldLayout> LayoutApplied;

        public Camera Camera => _camera;
        public HeightFieldLayout Layout => _layout;

        IHeightFieldSource HeightSource => _heightSourceBehaviour as IHeightFieldSource;

        void Reset()
        {
            if (_camera == null)
                _camera = Camera.main;
            ResolveRefs();
        }

        void OnValidate() => ResolveRefs();

        void OnEnable()
        {
            ResolveRefs();
            InjectDependencies();
            if (EnsureLayout(out _))
                ApplyLayout();
        }

        void OnDisable() => HeightSource?.Release();

        void Update()
        {
            if (!EnsureLayout(out bool layoutChanged)) return;
            if (!layoutChanged && _lodCompute?.LodMeshes != null) return;
            ApplyLayout();
        }

        public bool EnsureLayout() => EnsureLayout(out _);

        public bool EnsureLayout(out bool layoutChanged)
        {
            layoutChanged = false;
            if (_camera == null) return false;
            if (!NeedsRebuild())
                return _layout.TexWidth > 0;
            _layout = HeightFieldLayout.FromCamera(_camera, _barrierChunks);
            _lastW = _camera.pixelWidth;
            _lastH = _camera.pixelHeight;
            _lastOrtho = _camera.orthographicSize;
            layoutChanged = true;
            return true;
        }

        void ResolveRefs()
        {
            if (_lodCompute == null)
                _lodCompute = GetComponent<HeightFieldLodCompute>();
            if (_drawers == null || _drawers.Length == 0)
                _drawers = GetComponentsInChildren<HeightFieldChunkMeshDrawer>();
        }

        void InjectDependencies()
        {
            if (_drawers == null) return;
            var lod = (ILodSource)_lodCompute;
            var height = HeightSource;
            for (int i = 0; i < _drawers.Length; i++)
                _drawers[i]?.SetDependencies(lod, height);
        }

        void ApplyLayout()
        {
            if (_layout.TexWidth <= 0) return;
            HeightSource?.Allocate(_layout);
            _lodCompute?.Configure(_layout);
            if (_drawers == null) return;
            for (int i = 0; i < _drawers.Length; i++)
                _drawers[i]?.Configure(_layout);
            LayoutApplied?.Invoke(_layout);
        }

        bool NeedsRebuild()
        {
            return _camera.pixelWidth != _lastW
                || _camera.pixelHeight != _lastH
                || !Mathf.Approximately(_camera.orthographicSize, _lastOrtho);
        }
    }
}
