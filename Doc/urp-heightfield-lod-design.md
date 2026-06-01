# urp-heightfield-lod

> Japanese: [urp-heightfield-lod-design.ja.md](urp-heightfield-lod-design.ja.md)

## Overview

URP-based adaptive heightfield rendering for orthographic cameras:

- Chunk LOD Mesh (prebuilt meshes per LOD level)
- Curvature-driven LOD (Laplacian + max reduction)
- GPU classification, hysteresis, neighbor constraint
- Indirect instanced drawing (`DrawMeshInstancedIndirect`)
- Height supplied externally; curvature and rendering in LOD module

Primary goals: fullscreen heightfield visualization, media art, dynamic simulation display.

Layered stacks, context map, module layout: [heightfield-lod-layered-design.md](heightfield-lod-layered-design.md) (algorithm detail in this doc)

Target platform: **Windows** (DX11/12, Shader Model 5.0).

---

# Coordinate System (Unity Quad)

Follow the official Quad convention ([Create a quad mesh via script](https://docs.unity3d.com/6000.0/Documentation/Manual/Example-CreatingaBillboardPlane.html)).

| Item | Convention |
| --- | --- |
| Plane | Local **XY** |
| Front-face normal | **-Z** (`-Vector3.forward`) |
| Winding | Clockwise in view space (front faces camera on **-Z** side) |
| Default camera | Looks along **+Z** (e.g. position `(0,0,-d)`, identity rotation) |
| Height displacement | Along normal → **-Z** (meters from `HeightTex`) |
| Skirt | **+Z** only (opposite to normal), world-depth extrusion; edge vertices sample `HeightTex` |

```text
        +Y
         |
         |
    -----+----- +X
        /
       /
     +Z  (skirt extends here)

Camera at -Z side, forward = +Z, sees front face (normal -Z).
```

Heightfield position in local space:

```math
P_{local} = (x,\ y,\ -h(x,y))
```

where `h` is height in **meters** from `HeightTex` (R channel).

Do **not** use `Mesh.RecalculateNormals()` on procedural meshes; mesh vertex normals stay **-Z** for shadow bias only.

**Lighting and DepthNormals** use a GPU **normal map** derived from height gradients (world-space, see [Normal map](#normal-map-from-height)). When in doubt, compare mesh orientation against the Unity **Quad** primitive in Scene View.

---

# Project Layout (initial)

Folders + asmdef inside `Assets/`; UPM packages later when stable.

```text
Assets/
  HeightFieldLod/        # asmdef: HeightFieldLod (contracts + LOD + draw)
    Contracts/ Layout/ Compute/ Draw/ Util/ Shaders/
  Samples/HeightField/   # asmdef: HeightField.Samples
  App/                   # asmdef: App + App.Editor
Doc/
```

| Assembly | Responsibility |
| --- | --- |
| `HeightFieldLod` | Contracts (`IHeightFieldSource`, `HeightFieldLayout`), curvature/LOD/draw, shaders |
| `HeightField.Samples` | Sample height sources (Sine, Musgrave) |
| `App` | Bridge; optional `HeadSwayLensShiftCamera` + `ViewMotion` |

Editor menu (sample import required): **GameObject → Height Field → Setup Sample Rig** (`HeightField.Samples.Editor`).

---

# HeightFieldLayout (single source of truth)

Defined in **`HeightFieldLod/Contracts`**. Created by **Bridge** / **LayoutHost** and passed to Height + LOD.

```csharp
struct HeightFieldLayout
{
    int BarrierChunks;           // B, chunks per side (Bridge Inspector)
    int CoreWidth, CoreHeight;   // Align32(camera.pixelWidth/Height)
    int TexWidth, TexHeight;
    int ChunkCountX, ChunkCountY;
    float TotalWorldWidth, TotalWorldHeight;
    float PixelWorldX, PixelWorldY;
}
```

### Size

```text
chunkPixelSize = 32
coreW = AlignUp(camera.pixelWidth,  32)
coreH = AlignUp(camera.pixelHeight, 32)
texW  = coreW + 2 * B * chunkPixelSize
texH  = coreH + 2 * B * chunkPixelSize
chunkCountX = texW / 32
chunkCountY = texH / 32
```

`HeightTex`, curvature RT, and chunk grid all use the same **`texW × texH`** (barrier included).

### World extent (center origin)

```text
coreWorldW = 2 * orthographicSize * aspect
coreWorldH = 2 * orthographicSize
pixelWorldX = coreWorldW / camera.pixelWidth
pixelWorldY = coreWorldH / camera.pixelHeight
totalWorldW = texW * pixelWorldX
totalWorldH = texH * pixelWorldY
```

Chunk `(ix, iy)` center (grid origin = world center):

```text
chunkWorldW = 32 * pixelWorldX
chunkWorldH = 32 * pixelWorldY
centerX = -totalWorldW/2 + (ix + 0.5) * chunkWorldW
centerY = -totalWorldH/2 + (iy + 0.5) * chunkWorldH
```

UV tile:

```text
uvOffset = (ix * 32 / texW, iy * 32 / texH)
uvScale  = (32 / texW, 32 / texH)
```

Mesh local coordinates per chunk: `x,y ∈ [0,1]` on XY; vertex shader maps to world via `ChunkInstanceData`.

### Core vs barrier

| Region | Description |
| --- | --- |
| **Core** | Centered `coreW × coreH` texels (simulation / primary content) |
| **Barrier** | Outer `B` chunks per side; same Height RT and **same LOD rules** as core |
| **Height VS** | `clamp` sampler (extends border heights for displacement) |
| **Curvature** | Separate boundary rules (see below) — do not reuse clamp for Laplacian |

There is **no barrier LOD cap** (no `min(lod, barrierMaxLod)`). Outer chunks must not be forced to coarse LOD for optimization; boundary artifacts are fixed in curvature/classify instead.

---

# Transform / Camera

| Rule | Detail |
| --- | --- |
| Renderer Transform | Place at scene origin (or camera center) **once** in Editor; **no runtime follow** |
| Rebuild triggers | `pixelWidth`, `pixelHeight`, `orthographicSize` |
| No rebuild | `lensShift`, camera position/rotation, custom `projectionMatrix` / `worldToCameraMatrix` (e.g. head sway) |
| LOD metric | Curvature / complexity — **not** camera distance |
| Draw cameras | All cameras except `CameraType.Preview`, when `cullingMask` includes the rig **layer** (`gameObject.layer`) |
| Layout camera | `HeightFieldLayoutHost` / `HeightFieldBridge` `_camera` is for layout generation only, not draw filtering |
| Head sway (optional) | `HeadSwayLensShiftCamera` on the ortho camera: rig Transform fixed; `ConvergingLensShift` updates view/projection — see [Head sway camera](#head-sway-camera-optional) |

---

# Geometry: Chunk LOD Mesh

| LOD | Segments per side (quads) | Vertices per side |
| --- | --- | --- |
| LOD0 | 32 | 33 |
| LOD1 | 16 | 17 |
| LOD2 | 8 | 9 |
| LOD3 | 4 | 5 |

- Chunk screen/world footprint: **32×32 px** per chunk (fixed).
- Four shared mesh assets (`ChunkMeshBuilder`); `IndexFormat.UInt32`.
- Grid built with integer `(gx, gy)` indices; skirt uses explicit edge loops (no `RoundToInt` on UV).
- **Skirt**: extrude boundary along **+Z** by `skirtDepthMeters`; skirt verts store `z = skirtDepth` in mesh local space; VS applies height sample on all verts.
- **Neighbor constraint**: 1 pass, 4-connectivity, `|lod_i - lod_j| <= 1`.

---

# Height Field (`HeightField.Samples` / external)

- Contract `IHeightFieldSource` lives in **`HeightFieldLod/Contracts`**.
- Samples: `SineHeightFieldSource`, `MusgraveHeightFieldSource` in `Assets/Samples/HeightField/`.
- External simulations implement the same contract; Bridge connects to LOD/draw components.

Update order per frame (Bridge `Update`):

```text
1. Rebuild if pixel size or orthoSize changed
2. IHeightFieldSource.UpdateHeight(layout, time)   // ComputeShader.Dispatch
3. HeightFieldLodCompute.EnsureUpdated(layout, height) (pulled by Drawer)
```

---

# Curvature (`HeightFieldLod`)

### Laplacian compute

3×3 discrete Laplacian on `HeightTex` (meters):

```text
lap = |4h - h(-1,0) - h(+1,0) - h(0,-1) - h(0,+1)| * scale
scale = curvatureScale / (pixelWorldX)²
```

### Boundary sampling (important)

**Do not use clamp** for Laplacian stencil neighbors. Clamp at texture edges duplicates the border texel as a neighbor, which **inflates** Laplacian at `x=0`, `x=texW-1`, etc.

Outermost barrier chunks sit on those edge texels, so their per-chunk max curvature was almost always highest → **stuck at LOD0**.

**Use mirrored coordinates** for out-of-bounds stencil offsets:

```text
MirrorCoord(p + offset, maxP)
  x < 0        → x = -x
  x > maxP.x   → x = 2*maxP.x - x
  (same for y)
```

Height **rendering** still uses clamp; only the curvature pass uses mirror.

### Curvature RT init

Clear curvature (and reduction mips) to zero on allocate/rebuild.

---

# LOD Classification

### Per-chunk metric

For each chunk, take the **max** Laplacian over its `32×32` texel block.

**Exclude texture-border texels** from the max (`x==0`, `y==0`, `x==texW-1`, `y==texH-1`). Otherwise a single edge spike still forces LOD0 for the outermost chunk ring even with mirror sampling.

If a chunk would have no valid texels (degenerate), fall back to the chunk center texel.

### Thresholds (tunable placeholders)

```text
LOD0: curvature > 0.7
LOD1: 0.4 .. 0.7
LOD2: 0.15 .. 0.4
LOD3: < 0.15

Hysteresis (finer requires higher metric than coarser release):
  going finer: blocked if metric < DownThreshold(prevLod)
  DownThreshold: LOD0→0.6, LOD1→0.45, LOD2→0.12
```

First frame: all chunks start at **LOD3** (coarsest) in `_PrevLod`.

### Reduction pyramid

Custom **max** reduction chain is generated each frame (halving resolution). Currently used for future extensions; **classify reads full-resolution curvature** with per-chunk 32×32 max loop.

Avoid average-only reduction.

---

# Neighbor LOD Constraint

Enforce `|lod_i - lod_j| <= 1` (4-neighbor, **one pass**).

### Ping-pong buffers (required)

**Do not read/write the same `_Lod` buffer in one dispatch.** In-place neighbor updates race on the GPU and cause nondeterministic LOD (holes / missing chunks over time).

```text
Classify:  _PrevLod → _LodBuffer
Neighbor:  _LodIn = _LodBuffer  →  _LodOut = _LodScratch
           swap(_LodBuffer, _LodScratch)   // result in _LodBuffer
```

`clamp(lo, hi)` in neighbor pass: if `lo > hi` after neighbor min/max, swap before clamp.

---

# LOD Pipeline (per frame)

```text
1. Update HeightTex          (IHeightFieldSource / Samples)
2. Normal map from height    (NormalFromHeight.compute, mirror boundaries)
3. Curvature compute         (mirror boundaries)
4. Reduction max pyramid     (optional / future)
5. Classify LOD              (exclude border texels from max)
6. Neighbor constraint       (LodIn → LodOut, swap)
7. GetData + bucket instances per LOD (CPU; implicit GPU sync)
8. Copy LOD → _PrevLod for next frame hysteresis
9. Draw                      (beginCameraRendering)
```

### Instance lists and draw

- After GPU passes, `_LodBuffer.GetData` fills CPU `_lodData` (forces GPU completion).
- Build per-LOD `ChunkInstanceData` lists; upload to per-LOD `ComputeBuffer`.
- Indirect args (`indexCount`, `instanceCount`) cached in CPU `_argsCpu` — **do not** `GetData` args in the render callback.
- Draw with `MaterialPropertyBlock` for `_ChunkInstances` (do not share material buffer state across LOD draws).
- `MaterialPropertyBlock` must be created in `Awake`, not in field initializer (Unity object construction rules).

### Procedural instancing shader

- `StructuredBuffer<float4>` as two float4s per instance (`worldScaleCenter`, `uvScaleOffset`).
- `#pragma instancing_options procedural:SetupProcedural`
- No global `struct` instance variables in the shader (D3D11 restriction).

---

# GPU: ChunkInstanceData

```csharp
struct ChunkInstanceData
{
    float4 WorldScaleCenter; // xy = chunk world size, zw = world center xy
    float4 UvScaleOffset;    // xy = uv scale, zw = uv offset
}
```

LOD index is implicit from which draw call / buffer is used.

---

# Normal map from height

Each frame, before curvature, `HeightFieldLodCompute` dispatches **`NormalFromHeight.compute`** into an `ARGBHalf` RT (`_NormalTex`), same size as `HeightTex`.

### Gradient → world normal

Texture **+Y = world +Y**. Surface world position uses `z = -h`. Outward front-face normal (toward camera on -Z):

```text
∂h/∂x ≈ (hR - hL) / 2
∂h/∂y ≈ (hN - hS) / 2   (hS = smaller tex y, hN = larger tex y)

n ∝ (-∂h/∂x · pixelWorldY, -∂h/∂y · pixelWorldX, -pixelWorldX · pixelWorldY)
```

Encode: `normalRT = n * 0.5 + 0.5`. Height sampling for stencil uses **mirror** coords (same family as curvature).

**Sign rule:** Y component must use **`-∂h/∂y`**. A `+∂h/∂y` bug inverts lighting along world Y (e.g. downward light wrongly shadows +Y-facing slopes) while X can still look correct.

### Shader usage

`HeightFieldLitCommon.hlsl`:

- Vertex: `positionWS.z -= h` from `_HeightTex` (clamp sampler).
- Fragment / DepthNormals / ShadowCaster bias: `SampleHeightFieldNormalWS(heightUv)` — decode RT ×2−1, normalize. **Do not** use mesh `normalOS` for lighting.

---

# Shading (URP)

| Shader | Path | Forward pass |
| --- | --- | --- |
| `HeightFieldLit` | `HeightFieldLod/HeightFieldLit.shader` | URP `LightingPhysicallyBased`, optional specular |
| `HeightFieldToon` | `HeightFieldLod/HeightFieldToon.shader` | `diffuse = saturate(N·L) * attenuation * shadow`; `lerp(ShadowColor, LightColor, diffuse)` |

Shared: `HeightFieldLitCommon.hlsl`, procedural instancing, `_HeightTex` + `_NormalTex`.

| Pass | LightMode | Purpose |
| --- | --- | --- |
| ForwardLit | `UniversalForward` | Color |
| DepthNormals | `DepthNormals` | World normals from `_NormalTex` |
| ShadowCaster | `ShadowCaster` | Displaced positions; normal bias from height normal |

Main light: URP `GetMainLight` (+ shadow coord when enabled). Directional **distance attenuation** is forced to `1` when `< 0.5` (avoid bogus dimming). Indirect / additional lights are not implemented.

---

# Rendering (URP)

| Item | Detail |
| --- | --- |
| Hook | `RenderPipelineManager.beginCameraRendering` |
| Draw | `Graphics.DrawMeshInstancedIndirect` per LOD mesh |
| Cameras | Any non-Preview camera whose mask includes the heightfield rig layer; shadow passes use the same rule |
| Depth | Write ON |
| Materials | `HeightFieldLit` or `HeightFieldToon` on `HeightFieldChunkMeshDrawer` |
| Avoid | Geometry shader, CPU mesh rebuild, runtime topology, hardware tessellation |

---

# Head sway camera (optional)

Component: **`App.HeadSway.HeadSwayLensShiftCamera`** on the orthographic camera (same rig as heightfield). Does **not** move the rig Transform; simulates small head motion for parallax at a chosen convergence depth.

| Piece | Role |
| --- | --- |
| `ViewMotion` | Builds tangent-plane offset `d` (m): circular, noise, bob, inertial sway, rotate |
| `ConvergingLensShift` | Applies `V` (view translation) + `P` (asymmetric frustum or ortho shear) from `d` and `z_f` |
| `_focusDistance` | Convergence depth `z_f` (m); same `d` scales both passes |

Detail: [head-sway-lens-shift-camera.md](head-sway-lens-shift-camera.md).

Does not trigger `HeightFieldBridge` rebuild (projection / view matrix only).

---

# Crack Prevention (initial)

1. Skirt (+Z, height-sampled)
2. LOD neighbor diff ≤ 1 (ping-pong neighbor pass)
3. (Future) stitch / geomorph

---

# Recommended Defaults

| Parameter | Value |
| --- | --- |
| Chunk pixel size | 32 |
| Barrier chunks `B` | 2 (Bridge Inspector) |
| LOD count | 4 |
| Skirt depth | ~0.5–2.0 m (tunable) |
| Curvature scale | 1.0 (tune with thresholds) |
| Compute thread group | 8×8 |
| First-frame LOD | 3 |
| Rig layer | Set on Bridge / LOD renderer; only cameras that render this layer draw the heightfield |

---

# Implementation Notes

### Resolved issues (reference)

| Symptom | Cause | Fix |
| --- | --- | --- |
| Holes / missing chunks over time | In-place neighbor LOD GPU race | LodIn / LodOut + buffer swap |
| Outermost barrier always LOD0 | Clamp Laplacian at texture edge + max picks spike | Mirror stencil; exclude border texels from classify max |
| LOD1 (16×16) never visible | Curvature scale `/(pixelWorld)²` applied twice | Single `scale / dx²` in compute |
| Compile: `unity_InstanceID` | Procedural variant not defined | `#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED` in `SetupProcedural` |
| Compile: global struct `_Instance` | D3D11 shader limit | Per-float4 globals + buffer fetch |
| `MaterialPropertyBlock` ctor error | Field initializer on MonoBehaviour | Create in `Awake` |
| +Y slopes wrong shadow under Y-axis light; X OK | Normal map Y used `+∂h/∂y` instead of `-∂h/∂y` for `z = -h` | `NormalFromHeight`: `n.y = -dhdy * px` |
| `Camera` compile error in `App` | Namespace `App.Camera` hid `UnityEngine.Camera` | Rename to `App.HeadSway` |

### Not used (platform / API constraints)

- `Graphics.WaitForAllAsyncGPUOperations` (unavailable in this project’s Unity API)
- `Graphics.CopyBuffer` between `ComputeBuffer`s (use buffer swap instead)
- Hardware / hull-domain tessellation
- Barrier-specific LOD cap (optimization only; not used)

---

# Future Extensions

- GPU instance bucketing (append/consume buffers) to avoid `GetData`
- Multi-pass neighbor constraint for stricter convergence
- Use reduction mips in classify
- GPU culling, temporal stabilization, async compute
- Clipmap, geomorph / stitch
- UPM package split
- Dynamic simulation coupling
