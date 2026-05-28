using System;
using UnityEngine;

namespace HeightFieldLod
{
    /// <summary>Legacy monolithic component. Use <see cref="HeightFieldLodCompute"/> + <see cref="HeightFieldChunkMeshDrawer"/>.
    /// Migrate via GameObject/Height Field/Migrate Rig To Split Components.</summary>
    [Obsolete("Use HeightFieldLodCompute and HeightFieldChunkMeshDrawer.")]
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
    }
}
