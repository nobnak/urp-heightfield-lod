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

        ILodSource _lod;
        IHeightFieldSource _heightSource;
        HeightFieldLayout _layout;
        MaterialPropertyBlock _mpb;

        void Awake() => _mpb = new MaterialPropertyBlock();

        public void SetDependencies(ILodSource lod, IHeightFieldSource heightSource)
        {
            _lod = lod;
            _heightSource = heightSource;
        }

        public void Configure(HeightFieldLayout layout)
        {
            _layout = layout;
            if (_material != null && !_material.enableInstancing)
                _material.enableInstancing = true;
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!ShouldRender(camera)) return;
            if (_layout.TexWidth <= 0 || _lod == null || _material == null) return;

            if (_heightSource != null)
                _heightSource.EnsureUpdated(_layout, Time.time);
            var height = _heightSource != null ? _heightSource.HeightTexture : _lod.HeightTexture;
            if (height == null) return;

            _lod.EnsureUpdated(_layout, height);
            BindMaterialTextures(height, _lod.NormalTexture);
            DrawLayers(camera, _layout);
        }

        void BindMaterialTextures(RenderTexture height, RenderTexture normal)
        {
            _material.SetTexture("_HeightTex", height);
            _material.SetTexture("_NormalTex", normal);
        }

        void DrawLayers(Camera camera, HeightFieldLayout layout)
        {
            var meshes = _lod.LodMeshes;
            if (meshes == null) return;
            int levels = _lod.LodLevelCount;
            var bounds = new Bounds(transform.position, new Vector3(layout.TotalWorldWidth, layout.TotalWorldHeight, 50f));
            var castShadows = _castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            for (int lod = 0; lod < levels; lod++)
            {
                if (_lod.GetInstanceCount(lod) == 0) continue;
                _mpb.SetBuffer("_ChunkInstances", _lod.InstanceBuffers[lod]);
                Graphics.DrawMeshInstancedIndirect(
                    meshes[lod], 0, _material, bounds,
                    _lod.ArgsBuffers[lod], 0, _mpb,
                    castShadows, true, gameObject.layer,
                    camera, LightProbeUsage.Off, null);
            }
        }

        bool ShouldRender(Camera camera)
        {
            if (_material == null || _lod?.LodMeshes == null)
                return false;
            if (camera.cameraType == CameraType.Preview)
                return false;
            int layer = gameObject.layer;
            return (camera.cullingMask & (1 << layer)) != 0;
        }
    }
}
