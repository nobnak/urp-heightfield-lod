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

---

# Project Layout (initial)

Folders + asmdef inside `Assets/`; UPM packages later when stable.

```text
Assets/
  HeightField/           # asmdef: HeightField
  HeightFieldLod/         # asmdef: HeightFieldLod (references HeightField)
  App/                   # asmdef: App (references both)
    Bridge/
```

| Assembly | Responsibility |
| --- | --- |
| `HeightField` | `HeightFieldLayout`, height RT allocation, `IHeightFieldSource`, samples (sine) |
| `HeightFieldLod` | Height → curvature → reduction → LOD → draw |
| `App` | Bridge: layout, rebuild detection, wires height → LOD |

---

# HeightFieldLayout (single source of truth)

Defined in **`HeightField`**. Created only by the **Bridge** and passed to Height + LOD.

```csharp
struct HeightFieldLayout
{
    int BarrierChunks;      // B, chunks per side (Bridge Inspector)
    int CoreWidth, CoreHeight;  // Align32(camera.pixelWidth/Height)
    int TexWidth, TexHeight;
    int ChunkCountX, ChunkCountY;
    float TotalWorldWidth, TotalWorldHeight;
    float PixelWorldX, PixelWorldY;  // coreWorld / pixelSize
}
```

### Size

```text
chunkPixelSize = 32
coreW = AlignUp(camera.pixelWidth,  32)
coreH = AlignUp(camera.pixelHeight, 32)
texW  = coreW + 2 * B * 32
texH  = coreH + 2 * B * 32
chunkCountX = texW / 32
chunkCountY = texH / 32
```

`HeightTex` and chunk grid use **the same** `texW × texH` (barrier included).

### World extent (center origin)

```text
coreWorldW = 2 * orthographicSize * aspect
coreWorldH = 2 * orthographicSize
pixelWorldX = coreWorldW / camera.pixelWidth
pixelWorldY = coreWorldH / camera.pixelHeight
totalWorldW = texW * pixelWorldX
totalWorldH = texH * pixelWorldY
```

Chunk `(ix, iy)` center (origin = grid center):

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

Mesh local coordinates per chunk: `x,y ∈ [0,1]` on XY; VS maps to world using `ChunkInstanceData`.

### Core vs barrier in texture

- **Core** (simulation / primary): centered `coreW × coreH` region inside the texture.
- **Barrier** (margin): outer ring of `B` chunks per side; same LOD rules as core (no LOD cap).
- Height sampling: **clamp** sampler on `HeightTex`.

---

# Transform / Camera

| Rule | Detail |
| --- | --- |
| Renderer Transform | Place at scene origin (or camera center) **once** in Editor; **no runtime follow** |
| Rebuild triggers | `pixelWidth`, `pixelHeight`, `orthographicSize` |
| No rebuild | `lensShift`, camera position/rotation, projection matrix-only changes |
| LOD metric | Curvature / complexity — **not** camera distance |
| Barrier LOD cap | **None** (same rules for core and margin chunks) |

---

# Geometry: Chunk LOD Mesh

| LOD | Segments per side (quads) | Vertices per side |
| --- | --- | --- |
| LOD0 | 32 | 33 |
| LOD1 | 16 | 17 |
| LOD2 | 8 | 9 |
| LOD3 | 4 | 5 |

- Chunk screen/world footprint: **32×32 px** equivalent per chunk (fixed).
- Four shared mesh assets; per-chunk `ChunkInstanceData` only.
- **Skirt**: extrude boundary along **+Z** by `skirtDepthMeters`; edge positions sample height.
- **Neighbor constraint**: 1 pass, 4-connectivity, `|lod_i - lod_j| <= 1`.
- **No** barrier-specific LOD cap (same classification everywhere).

---

# Height Field (`HeightField` assembly)

- `IHeightFieldSource`: writes meters into `RenderTexture` each frame.
- `RenderTexture` format: `RFloat` (or `RHalf`), size = `layout.TexWidth × layout.TexHeight`.
- Sample: `SineHeightFieldSource` (procedural sine in world XY).
- External sim implements `IHeightFieldSource` in app code later.

---

# LOD Pipeline (`HeightFieldLod` assembly)

Every frame (after height update):

1. **Curvature** — 3×3 Laplacian compute on `HeightTex`.
2. **Reduction** — custom **max** pyramid (not average mip).
3. **Classify** — per-chunk LOD from reduced curvature (thresholds tunable).
4. **Hysteresis** — scalar UP/DOWN thresholds; buffer on GPU; **first frame = LOD3** (coarsest).
5. **Neighbor pass** — 4-neighbor, 1 pass, enforce LOD diff ≤ 1.
6. **Instance lists** — per LOD level for `DrawMeshInstancedIndirect`.
7. **Draw** — `beginCameraRendering`, **depth write ON**, shading with **main light** only.

### Thresholds (initial placeholders)

```text
LOD0: curvature > 0.7
LOD1: 0.4 .. 0.7
LOD2: 0.15 .. 0.4
LOD3: < 0.15

Hysteresis UP:   > 0.6
Hysteresis DOWN: < 0.45
```

Normalize curvature in compute (scale by height range / texel size).

---

# GPU: ChunkInstanceData

```csharp
struct ChunkInstanceData
{
    float4 worldScaleOffset; // xy scale (chunk world size), zw world center xy
    float4 uvScaleOffset;    // xy uv scale, zw uv offset
}
```

LOD index is implicit from which draw call (per-LOD buffer).

---

# Rendering (URP)

- Hook: `RenderPipelineManager.beginCameraRendering`.
- `Graphics.DrawMeshInstancedIndirect` per LOD mesh.
- Draw for the **target camera** and optionally **Scene View** (`CameraType.SceneView`, enabled by default on `HeightFieldLodRenderer`).
- Layout / height simulation remain tied to the target camera; Scene View reuses the same buffers for inspection from any angle.
- Shader: URP Forward+, main directional light, writes depth.
- Avoid: geometry shader, CPU mesh rebuild, runtime topology generation, hardware tessellation.

---

# Crack Prevention (initial)

1. Skirt (+Z)
2. LOD neighbor diff ≤ 1
3. (Future) stitch / geomorph

---

# Recommended Defaults

| Parameter | Value |
| --- | --- |
| Chunk pixel size | 32 |
| Barrier chunks `B` | 2 (Bridge) |
| LOD count | 4 |
| Skirt depth | ~0.5–2.0 m (tunable) |
| Compute thread group | 8×8 |
| First-frame LOD | 3 |

---

# Future Extensions

- GPU culling, temporal stabilization, async compute
- Clipmap, geomorph / stitch
- UPM package split
- Dynamic simulation coupling
