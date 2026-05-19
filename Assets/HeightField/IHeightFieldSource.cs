using UnityEngine;

namespace HeightField
{
    public interface IHeightFieldSource
    {
        void Allocate(HeightFieldLayout layout);
        void Release();
        void UpdateHeight(HeightFieldLayout layout, float time);
        RenderTexture HeightTexture { get; }
    }
}
