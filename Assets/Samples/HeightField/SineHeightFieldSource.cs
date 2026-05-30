using UnityEngine;

namespace HeightField
{
    [DisallowMultipleComponent]
    public sealed class SineHeightFieldSource : MonoBehaviour, IHeightFieldSource
    {
        [SerializeField] ComputeShader _fillShader;
        [SerializeField] float _amplitudeMeters = 1.5f;
        [SerializeField] float _frequency = 0.15f;
        [SerializeField] float _speed = 0.5f;

        RenderTexture _height;
        int _kernel = -1;

        public RenderTexture HeightTexture => _height;

        public void Allocate(HeightFieldLayout layout)
        {
            Release();
            _height = new RenderTexture(layout.TexWidth, layout.TexHeight, 0, RenderTextureFormat.RFloat)
            {
                name = "HeightField",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _height.Create();
            if (_fillShader != null)
                _kernel = _fillShader.FindKernel("CSMain");
        }

        public void Release()
        {
            if (_height != null)
            {
                _height.Release();
                Destroy(_height);
                _height = null;
            }
        }

        public void UpdateHeight(HeightFieldLayout layout, float time)
        {
            if (_height == null || _fillShader == null || _kernel < 0)
                return;

            _fillShader.SetTexture(_kernel, "_Height", _height);
            _fillShader.SetVector("_WorldOrigin", new Vector4(
                -layout.TotalWorldWidth * 0.5f,
                -layout.TotalWorldHeight * 0.5f, 0f, 0f));
            _fillShader.SetVector("_PixelWorld", new Vector4(layout.PixelWorldX, layout.PixelWorldY, 0f, 0f));
            _fillShader.SetInts("_TexSize", layout.TexWidth, layout.TexHeight);
            _fillShader.SetFloat("_Amplitude", _amplitudeMeters);
            _fillShader.SetFloat("_Frequency", _frequency);
            _fillShader.SetFloat("_Time", time * _speed);
            int gx = (layout.TexWidth + 7) / 8;
            int gy = (layout.TexHeight + 7) / 8;
            _fillShader.Dispatch(_kernel, gx, gy, 1);
        }

        void OnDestroy() => Release();
    }
}
