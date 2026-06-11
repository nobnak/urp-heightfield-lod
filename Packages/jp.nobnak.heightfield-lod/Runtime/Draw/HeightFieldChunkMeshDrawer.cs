using HeightField;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeightFieldLod
{
    [DisallowMultipleComponent]
    public sealed class HeightFieldChunkMeshDrawer : MonoBehaviour
    {
        [SerializeField] Material _material;
        [SerializeField] int _sortOrder;
        [SerializeField] bool _castShadows = true;

        HeightFieldLayoutHost _layoutHost;
        HeightFieldLodCompute _lod;
        IHeightFieldSource _heightSource;
        HeightFieldLayout _layout;
        MaterialPropertyBlock _mpb;

        ILodSource Lod => _lod;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            ResolveRefs();
        }

        void Reset() => ResolveRefs();

        void OnValidate() => ResolveRefs();

        public void Configure(HeightFieldLayout layout)
        {
            _layout = layout;
            if (_material != null && !_material.enableInstancing)
                _material.enableInstancing = true;
        }

        public void Release()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        void OnEnable()
        {
            ResolveRefs();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void OnDisable() => Release();

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!ShouldRender(camera))
                return;
            var layout = ResolveLayout();
            if (layout.TexWidth <= 0 || Lod == null || _material == null)
                return;

            if (_heightSource != null)
                _heightSource.EnsureUpdated(layout, Time.time);
            var height = _heightSource != null ? _heightSource.HeightTexture : Lod.HeightTexture;
            if (height == null) return;

            Lod.EnsureUpdated(layout, height);
            BindMaterialTextures(height, Lod.NormalTexture);
            DrawLayers(camera, layout);
        }

        void ResolveRefs()
        {
            _layoutHost = GetComponent<HeightFieldLayoutHost>();
            _lod = GetComponent<HeightFieldLodCompute>();
            _heightSource = HeightFieldRigUtil.FindHeightSource(gameObject);
        }

        HeightFieldLayout ResolveLayout()
        {
            if (_layoutHost != null && _layoutHost.EnsureLayout())
                return _layoutHost.Layout;
            return _layout;
        }

        void BindMaterialTextures(RenderTexture height, RenderTexture normal)
        {
            _material.SetTexture("_HeightTex", height);
            _material.SetTexture("_NormalTex", normal);
        }

        void DrawLayers(Camera camera, HeightFieldLayout layout)
        {
            var meshes = Lod.LodMeshes;
            if (meshes == null) return;
            int levels = Lod.LodLevelCount;
            var bounds = new Bounds(transform.position, new Vector3(layout.TotalWorldWidth, layout.TotalWorldHeight, 50f));
            var castShadows = _castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            for (int lod = 0; lod < levels; lod++)
            {
                if (Lod.GetInstanceCount(lod) == 0) continue;
                _mpb.SetBuffer("_ChunkInstances", Lod.InstanceBuffers[lod]);
                Graphics.DrawMeshInstancedIndirect(
                    meshes[lod], 0, _material, bounds,
                    Lod.ArgsBuffers[lod], 0, _mpb,
                    castShadows, true, gameObject.layer,
                    camera, LightProbeUsage.Off, null);
            }
        }

        bool ShouldRender(Camera camera)
        {
            if (_material == null || Lod?.LodMeshes == null)
                return false;
            if (camera.cameraType == CameraType.Preview)
                return false;
            int layer = gameObject.layer;
            return (camera.cullingMask & (1 << layer)) != 0;
        }
    }
}
