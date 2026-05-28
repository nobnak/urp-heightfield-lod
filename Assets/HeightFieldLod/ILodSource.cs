using HeightField;
using UnityEngine;

namespace HeightFieldLod
{
    public interface ILodSource
    {
        RenderTexture HeightTexture { get; }
        RenderTexture NormalTexture { get; }
        Mesh[] LodMeshes { get; }
        ComputeBuffer[] InstanceBuffers { get; }
        ComputeBuffer[] ArgsBuffers { get; }
        int LodLevelCount { get; }
        uint GetInstanceCount(int lod);
        void EnsureUpdated(HeightFieldLayout layout, RenderTexture height);
    }
}
