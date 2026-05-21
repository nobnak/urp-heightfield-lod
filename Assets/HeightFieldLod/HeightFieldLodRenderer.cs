using System;
using HeightField;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeightFieldLod
{
    [DisallowMultipleComponent]
    public sealed class HeightFieldLodRenderer : MonoBehaviour
    {
        [SerializeField] Material _material;
        [SerializeField] ComputeShader _normalShader;
        [SerializeField] ComputeShader _curvatureShader;
        [SerializeField] ComputeShader _reductionShader;
        [SerializeField] ComputeShader _classifyShader;
        [SerializeField] ComputeShader _neighborShader;
        [SerializeField] float _skirtDepthMeters = 1f;
        [SerializeField] float _curvatureScale = 1f;
        [Header("LOD thresholds")]
        [SerializeField] float _lodUpHigh = 0.7f;
        [SerializeField] float _lodUpMid = 0.4f;
        [SerializeField] float _lodUpLow = 0.15f;
        [SerializeField] float _lodDownHigh = 0.6f;
        [SerializeField] float _lodDownMid = 0.45f;
        [SerializeField] float _lodDownLow = 0.12f;
        [SerializeField] bool _castShadows = true;

        HeightFieldLayout _layout;
        Camera _camera;
        Mesh[] _lodMeshes;
        RenderTexture _normalMap;
        RenderTexture _curvature;
        RenderTexture[] _reductionMips;
        ComputeBuffer _lodBuffer;
        ComputeBuffer _lodScratchBuffer;
        ComputeBuffer _prevLodBuffer;
        ComputeBuffer[] _instanceBuffers;
        ComputeBuffer[] _argsBuffers;
        uint[] _lodData;
        readonly uint[][] _argsCpu = new uint[LodLevels][];
        MaterialPropertyBlock _mpb;
        bool _firstFrame = true;

        int _kNormal = -1;
        int _kCurvature = -1;
        int _kReduction = -1;
        int _kClassify = -1;
        int _kNeighbor = -1;

        const int LodLevels = 4;

        public void Configure(HeightFieldLayout layout, Camera camera)
        {
            _layout = layout;
            _camera = camera;
            if (_material == null)
            {
                var sh = Shader.Find("HeightFieldLod/HeightFieldLit");
                if (sh != null)
                    _material = new Material(sh);
            }
            RebuildResources();
        }

        public void Release()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            DestroyMeshes();
            ReleaseGpu();
        }

        void Awake() => _mpb = new MaterialPropertyBlock();

        void OnDisable() => Release();

        void RebuildResources()
        {
            ReleaseGpu();
            DestroyMeshes();
            _lodMeshes = ChunkMeshBuilder.BuildLodMeshes(_skirtDepthMeters);

            _normalMap = CreateNormalRtf(_layout.TexWidth, _layout.TexHeight, "HeightFieldNormal");
            _curvature = CreateRtf(_layout.TexWidth, _layout.TexHeight, "Curvature");
            ClearRtf(_curvature);
            BuildReductionChain();
            int n = _layout.ChunkCount;
            _lodBuffer = new ComputeBuffer(n, sizeof(uint), ComputeBufferType.Structured);
            _lodScratchBuffer = new ComputeBuffer(n, sizeof(uint), ComputeBufferType.Structured);
            _prevLodBuffer = new ComputeBuffer(n, sizeof(uint), ComputeBufferType.Structured);
            _lodData = new uint[n];
            for (int i = 0; i < n; i++)
                _lodData[i] = 3;
            _prevLodBuffer.SetData(_lodData);
            _lodBuffer.SetData(_lodData);

            _instanceBuffers = new ComputeBuffer[LodLevels];
            _argsBuffers = new ComputeBuffer[LodLevels];
            for (int lod = 0; lod < LodLevels; lod++)
            {
                _argsCpu[lod] = new uint[5];
                _instanceBuffers[lod] = new ComputeBuffer(Mathf.Max(1, n), ChunkInstanceData.Stride, ComputeBufferType.Structured);
                _argsBuffers[lod] = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
                UpdateArgsBuffer(lod, 0);
            }

            if (_normalShader != null) _kNormal = _normalShader.FindKernel("CSMain");
            if (_curvatureShader != null) _kCurvature = _curvatureShader.FindKernel("CSMain");
            if (_reductionShader != null) _kReduction = _reductionShader.FindKernel("CSMain");
            if (_classifyShader != null) _kClassify = _classifyShader.FindKernel("CSMain");
            if (_neighborShader != null) _kNeighbor = _neighborShader.FindKernel("CSMain");

            _firstFrame = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void DestroyMeshes()
        {
            if (_lodMeshes == null) return;
            foreach (var m in _lodMeshes)
            {
                if (m != null) Destroy(m);
            }
            _lodMeshes = null;
        }

        void ReleaseGpu()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _normalMap?.Release();
            Destroy(_normalMap);
            _normalMap = null;
            _curvature?.Release();
            Destroy(_curvature);
            _curvature = null;
            if (_reductionMips != null)
            {
                foreach (var rt in _reductionMips)
                {
                    if (rt != null) { rt.Release(); Destroy(rt); }
                }
                _reductionMips = null;
            }
            _lodBuffer?.Release();
            _lodScratchBuffer?.Release();
            _prevLodBuffer?.Release();
            _lodBuffer = _lodScratchBuffer = _prevLodBuffer = null;
            if (_instanceBuffers != null)
            {
                foreach (var b in _instanceBuffers) b?.Release();
                _instanceBuffers = null;
            }
            if (_argsBuffers != null)
            {
                foreach (var b in _argsBuffers) b?.Release();
                _argsBuffers = null;
            }
        }

        void BuildReductionChain()
        {
            int levels = 0;
            int w = _layout.TexWidth;
            int h = _layout.TexHeight;
            while (w > 1 || h > 1) { w = Mathf.Max(1, w / 2); h = Mathf.Max(1, h / 2); levels++; }
            _reductionMips = new RenderTexture[levels];
            w = _layout.TexWidth;
            h = _layout.TexHeight;
            for (int i = 0; i < levels; i++)
            {
                int nw = Mathf.Max(1, w / 2);
                int nh = Mathf.Max(1, h / 2);
                _reductionMips[i] = CreateRtf(nw, nh, $"CurvatureMip{i}");
                ClearRtf(_reductionMips[i]);
                w = nw;
                h = nh;
            }
        }

        static RenderTexture CreateRtf(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        static RenderTexture CreateNormalRtf(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        static void ClearRtf(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;
        }

        public void Tick(RenderTexture height, float deltaTime)
        {
            if (_material == null || height == null || _lodMeshes == null)
                return;

            RunNormals(height);
            RunCurvature(height);
            RunReduction();
            RunClassify();
            RunNeighbor();
            BuildInstanceLists();
            _prevLodBuffer.SetData(_lodData);
            _material.SetTexture("_HeightTex", height);
            _material.SetTexture("_NormalTex", _normalMap);
            if (!_material.enableInstancing)
                _material.enableInstancing = true;
        }

        void RunNormals(RenderTexture height)
        {
            if (_kNormal < 0 || _normalMap == null) return;
            _normalShader.SetTexture(_kNormal, "_Height", height);
            _normalShader.SetTexture(_kNormal, "_NormalMap", _normalMap);
            _normalShader.SetInts("_TexSize", _layout.TexWidth, _layout.TexHeight);
            _normalShader.SetVector("_PixelWorld", new Vector2(_layout.PixelWorldX, _layout.PixelWorldY));
            Dispatch2D(_normalShader, _kNormal, _layout.TexWidth, _layout.TexHeight);
        }

        void RunCurvature(RenderTexture height)
        {
            if (_kCurvature < 0) return;
            _curvatureShader.SetTexture(_kCurvature, "_Height", height);
            _curvatureShader.SetTexture(_kCurvature, "_Curvature", _curvature);
            _curvatureShader.SetInts("_TexSize", _layout.TexWidth, _layout.TexHeight);
            float dx = Mathf.Max(_layout.PixelWorldX, 1e-6f);
            _curvatureShader.SetFloat("_InvTexelSize", _curvatureScale / (dx * dx));
            _curvatureShader.SetFloat("_HeightScale", 1f);
            Dispatch2D(_curvatureShader, _kCurvature, _layout.TexWidth, _layout.TexHeight);
        }

        void RunReduction()
        {
            if (_kReduction < 0 || _reductionMips == null || _reductionMips.Length == 0)
                return;

            var src = _curvature;
            for (int i = 0; i < _reductionMips.Length; i++)
            {
                var dst = _reductionMips[i];
                _reductionShader.SetTexture(_kReduction, "_Source", src);
                _reductionShader.SetTexture(_kReduction, "_Dest", dst);
                _reductionShader.SetInts("_DestSize", dst.width, dst.height);
                Dispatch2D(_reductionShader, _kReduction, dst.width, dst.height);
                src = dst;
            }
        }

        void RunClassify()
        {
            if (_kClassify < 0) return;
            _classifyShader.SetTexture(_kClassify, "_Curvature", _curvature);
            _classifyShader.SetBuffer(_kClassify, "_PrevLod", _prevLodBuffer);
            _classifyShader.SetBuffer(_kClassify, "_Lod", _lodBuffer);
            _classifyShader.SetInts("_ChunkCount", _layout.ChunkCountX, _layout.ChunkCountY);
            _classifyShader.SetInts("_TexSize", _layout.TexWidth, _layout.TexHeight);
            _classifyShader.SetInt("_ChunkPixelSize", HeightFieldLayout.ChunkPixelSize);
            _classifyShader.SetFloat("_LodUpHigh", _lodUpHigh);
            _classifyShader.SetFloat("_LodDownHigh", _lodDownHigh);
            _classifyShader.SetFloat("_LodUpMid", _lodUpMid);
            _classifyShader.SetFloat("_LodDownMid", _lodDownMid);
            _classifyShader.SetFloat("_LodUpLow", _lodUpLow);
            _classifyShader.SetFloat("_LodDownLow", _lodDownLow);
            if (_firstFrame)
            {
                for (int i = 0; i < _lodData.Length; i++)
                    _lodData[i] = 3;
                _prevLodBuffer.SetData(_lodData);
                _firstFrame = false;
            }
            Dispatch2D(_classifyShader, _kClassify, _layout.ChunkCountX, _layout.ChunkCountY);
        }

        void RunNeighbor()
        {
            if (_kNeighbor < 0) return;
            _neighborShader.SetBuffer(_kNeighbor, "_LodIn", _lodBuffer);
            _neighborShader.SetBuffer(_kNeighbor, "_LodOut", _lodScratchBuffer);
            _neighborShader.SetInts("_ChunkCount", _layout.ChunkCountX, _layout.ChunkCountY);
            Dispatch2D(_neighborShader, _kNeighbor, _layout.ChunkCountX, _layout.ChunkCountY);
            (_lodBuffer, _lodScratchBuffer) = (_lodScratchBuffer, _lodBuffer);
        }

        void BuildInstanceLists()
        {
            if (_lodData == null || _lodBuffer == null || _lodData.Length != _lodBuffer.count)
                return;

            _lodBuffer.GetData(_lodData);
            int cx = _layout.ChunkCountX;
            int cy = _layout.ChunkCountY;
            var lists = new System.Collections.Generic.List<ChunkInstanceData>[LodLevels];
            for (int i = 0; i < LodLevels; i++)
                lists[i] = new System.Collections.Generic.List<ChunkInstanceData>();

            for (int iy = 0; iy < cy; iy++)
            {
                for (int ix = 0; ix < cx; ix++)
                {
                    uint lod = _lodData[iy * cx + ix];
                    if (lod > 3u) lod = 3u;
                    lists[(int)lod].Add(ChunkInstanceData.Create(_layout, ix, iy));
                }
            }

            for (int lod = 0; lod < LodLevels; lod++)
            {
                var arr = lists[lod].ToArray();
                int count = arr.Length;
                if (count == 0)
                {
                    UpdateArgsBuffer(lod, 0);
                    continue;
                }
                if (_instanceBuffers[lod].count < count)
                {
                    _instanceBuffers[lod].Release();
                    _instanceBuffers[lod] = new ComputeBuffer(count, ChunkInstanceData.Stride, ComputeBufferType.Structured);
                }
                _instanceBuffers[lod].SetData(arr, 0, 0, count);
                UpdateArgsBuffer(lod, count);
            }
        }

        void UpdateArgsBuffer(int lod, int instanceCount)
        {
            uint indexCount = _lodMeshes[lod].GetIndexCount(0);
            _argsCpu[lod][0] = indexCount;
            _argsCpu[lod][1] = (uint)instanceCount;
            _argsCpu[lod][2] = 0;
            _argsCpu[lod][3] = 0;
            _argsCpu[lod][4] = 0;
            _argsBuffers[lod].SetData(_argsCpu[lod]);
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!ShouldRender(camera))
                return;

            var bounds = new Bounds(transform.position, new Vector3(_layout.TotalWorldWidth, _layout.TotalWorldHeight, 50f));
            for (int lod = 0; lod < LodLevels; lod++)
            {
                if (_argsCpu[lod][1] == 0) continue;

                _mpb ??= new MaterialPropertyBlock();
                _mpb.SetBuffer("_ChunkInstances", _instanceBuffers[lod]);
                var castShadows = _castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                Graphics.DrawMeshInstancedIndirect(
                    _lodMeshes[lod], 0, _material, bounds,
                    _argsBuffers[lod], 0, _mpb,
                    castShadows, true, gameObject.layer,
                    camera, LightProbeUsage.Off, null);
            }
        }

        bool ShouldRender(Camera camera)
        {
            if (_material == null || _lodMeshes == null || _layout.TexWidth <= 0)
                return false;
            if (camera.cameraType == CameraType.Preview)
                return false;
            int layer = gameObject.layer;
            return (camera.cullingMask & (1 << layer)) != 0;
        }

        static void Dispatch2D(ComputeShader cs, int kernel, int w, int h)
        {
            cs.Dispatch(kernel, (w + 7) / 8, (h + 7) / 8, 1);
        }
    }
}
