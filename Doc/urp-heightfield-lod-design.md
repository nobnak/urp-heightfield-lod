# urp-heightfield-lod

## Overview

URP-based adaptive heightfield rendering for orthographic cameras:

- Chunk LOD Mesh (prebuilt meshes per LOD level)
- Curvature-driven LOD (Laplacian + max reduction)
- GPU classification, hysteresis, neighbor constraint
- Indirect instanced drawing (`DrawMeshInstancedIndirect`)
- Height supplied externally; curvature and rendering in LOD module

Primary goals: fullscreen heightfield visualization, media art, dynamic simulation display.

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

Do **not** use `Mesh.RecalculateNormals()` on procedural meshes; set normals explicitly to **-Z**.

When in doubt, compare against the Unity **Quad** primitive in Scene View.

---

# Project Layout (initial)

Folders + asmdef inside `Assets/`; UPM packages later when stable.

```text
Assets/
  HeightField/           # asmdef: HeightField
  HeightFieldLod/        # asmdef: HeightFieldLod (references HeightField)
  App/                   # asmdef: App + App.Editor
    Bridge/
    Editor/              # scene rig setup menu
```

| Assembly | Responsibility |
| --- | --- |
| `HeightField` | `HeightFieldLayout`, height RT allocation, `IHeightFieldSource`, samples (sine) |
| `HeightFieldLod` | Height → curvature → reduction → LOD classify → draw |
| `App` | Bridge: layout, rebuild detection, wires height → LOD |

Editor menu: **GameObject → Height Field → Setup Sample Rig**.

---

# HeightFieldLayout (single source of truth)

Defined in **`HeightField`**. Created only by the **Bridge** (`HeightFieldBridge`) and passed to Height + LOD.

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
| No rebuild | `lensShift`, camera position/rotation, projection matrix-only changes |
| LOD metric | Curvature / complexity — **not** camera distance |
| Scene View | Optional draw (`CameraType.SceneView`, default ON); same buffers as target camera |

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

# Height Field (`HeightField` assembly)

- `IHeightFieldSource`: writes meters into `RenderTexture` each frame.
- `RenderTexture`: `RFloat`, size = `layout.TexWidth × layout.TexHeight`.
- Sample: `SineHeightFieldSource` (procedural sine in world XY over full texture including barrier).
- External simulation implements `IHeightFieldSource`; Bridge connects to `HeightFieldLodRenderer`.

Update order per frame (Bridge `Update`):

```text
1. Rebuild if pixel size or orthoSize changed
2. IHeightFieldSource.UpdateHeight(layout, time)   // ComputeShader.Dispatch
3. HeightFieldLodRenderer.Tick(height)
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
1. Update HeightTex          (HeightField)
2. Curvature compute         (mirror boundaries)
3. Reduction max pyramid     (optional / future)
4. Classify LOD              (exclude border texels from max)
5. Neighbor constraint       (LodIn → LodOut, swap)
6. GetData + bucket instances per LOD (CPU; implicit GPU sync)
7. Copy LOD → _PrevLod for next frame hysteresis
8. Draw                      (beginCameraRendering)
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

# Rendering (URP)

| Item | Detail |
| --- | --- |
| Hook | `RenderPipelineManager.beginCameraRendering` |
| Draw | `Graphics.DrawMeshInstancedIndirect` per LOD mesh |
| Cameras | Target camera + optional Scene View |
| Depth | Write ON |
| Lighting | Main directional light only (simplified forward) |
| Avoid | Geometry shader, CPU mesh rebuild, runtime topology, hardware tessellation |

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
| Draw in Scene View | ON |

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
