using HeightField;
using HeightFieldLod;
using UnityEngine;

namespace HeightField.Samples
{
    [DisallowMultipleComponent]
    public sealed class HeightFieldBridge : MonoBehaviour
    {
        HeightFieldLayoutHost _layoutHost;
        IHeightFieldSource _heightSource;
        HeightFieldLodCompute _lodCompute;
        HeightFieldChunkMeshDrawer _drawer;

        void Reset()
        {
            _layoutHost = GetComponent<HeightFieldLayoutHost>();
            if (_layoutHost == null)
                _layoutHost = gameObject.AddComponent<HeightFieldLayoutHost>();
            ResolveRefs();
        }

        void OnEnable()
        {
            ResolveRefs();
            if (_layoutHost == null) return;
            _layoutHost.EnsureLayout(out _);
            ApplyLayout();
        }

        void OnDisable()
        {
            _heightSource?.Release();
            _lodCompute?.Release();
            _drawer?.Release();
        }

        void Update()
        {
            if (_layoutHost == null) return;
            if (!_layoutHost.EnsureLayout(out bool layoutChanged)) return;
            if (!layoutChanged && _lodCompute?.LodMeshes != null) return;
            ApplyLayout();
        }

        void ApplyLayout()
        {
            if (_heightSource == null || _lodCompute == null || _layoutHost == null) return;
            var layout = _layoutHost.Layout;
            if (layout.TexWidth <= 0) return;
            _heightSource.Allocate(layout);
            _lodCompute.Configure(layout);
            _drawer?.Configure(layout);
        }

        void ResolveRefs()
        {
            if (_layoutHost == null)
                _layoutHost = GetComponent<HeightFieldLayoutHost>();
            _heightSource = HeightFieldRigUtil.FindHeightSource(gameObject);
            _lodCompute = GetComponent<HeightFieldLodCompute>();
            _drawer = GetComponent<HeightFieldChunkMeshDrawer>();
        }
    }
}
