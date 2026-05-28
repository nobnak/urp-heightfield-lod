# Heightfield LOD — Layered Design

> Japanese: [heightfield-lod-layered-design.ja.md](heightfield-lod-layered-design.ja.md)  
> Base: [urp-heightfield-lod-design.md](urp-heightfield-lod-design.md)

## Discussion Summary

| Topic | Conclusion |
| --- | --- |
| Nanite-like? | No — **orthographic adaptive heightfield**, not virtualized arbitrary meshes |
| Multiple layers | Today: **1 rig = 1 height + 1 LOD + 1 draw**; Transform barely used |
| Mode enum | **Not needed** — App supplies RT(s); layers **reference** height / LOD cache |
| Pipeline | **3 stages**: (1) Height (2) Curvature/LOD (3) Chunk mesh draw |
| `h` coordinates | Spec: **object-space -Z**; code: **world XY from layout + world -h** — matches only when rig/camera are identity |
| Depth / stacking | Layer **Transform** + shared `h` when same RT; interpret depth along **view** for display |
| LOD thresholds | **Same per HeightTex** (not varied per layer); no “params-based” branching design |
| LOD sharing | Share via **`ILodSource` references** (cache is optional / not required) |
| `GetData` | **Single call site** in `BuildInstanceLists`; shared height → one read per frame |

---

## Goals

- Multiple **layers** in the scene; each layer’s **Transform** defines spatial and depth relationships.
- App writes **one or many** height RTs; layers either **compute** curvature/LOD or **reference** another layer’s cache.
- Keep chunk LOD, curvature classification, indirect draw; **fix world-space shortcut** in the vertex shader.

## Non-Goals (initial scope)

- Nanite-style clusters / streaming
- Per-layer different `HeightFieldLayout` (resolution / camera)
- Full GPU bucketing without `GetData` (noted as future work)

---

## Architecture (adopted model)

**N height RTs, K LOD computes, M drawers (M ≥ 1, K ≥ 1).**  
References follow “consumers own references”: drawers share `ILodSource`. **No compute→compute references**. LOD thresholds are fixed per height.

```text
App
  └─ HeightTex × N          (N ≥ 1)

HeightFieldLodCompute × K
  Input:
    · RenderTexture _height (required)
  Output: Normal, Curvature, LOD + instance/args buffers

HeightFieldChunkMeshDrawer × M
  Input: ILodSource _lod (required)
  Draw: sample _lod.HeightTex in VS, indirect draw from _lod buffers
  Transform on Drawer (or parent) for layer placement
```

| Symbol | Meaning |
| --- | --- |
| **N** | Height RTs the app writes |
| **K** | LOD compute components in the scene |
| **M** | Drawers (layers) in the scene |

Reference patterns:
- One compute per height, many drawers reference it (recommended).
- Multiple computes referencing the same height is allowed but duplicates work (not recommended).

### Reference direction

- **Forward references (types):** drawers reference `ILodSource`.
- **Update control:** prefer **pull**: drawer calls `EnsureUpdated` before drawing. Host-based push is optional for large scenes.

Thin **`HeightFieldLayoutHost`**: shared layout + optional frame ordering (no cache required).

Drawer↔Compute: **M:M by default** on one GameObject; multiple drawers may reference one compute if needed.

---

## Coordinates and `h`

### Principles (Unity Quad)

| Item | Rule |
| --- | --- |
| Plane | Layer **local XY** |
| Normal | **-Z** (local) |
| Height | `P_os = (x_os, y_os, z_skirt - h(uv))` meters |
| To world | `P_ws = TransformObjectToWorld(P_os)` |
| To clip | `P_cs = TransformWorldToHClip(P_ws)` |

**Avoid:** writing **world XY** from layout and **world -h** without `ObjectToWorld`.

### Why current code “works”

Identity rig rotation and identity camera rotation make local -Z equal world -Z and layout world XY equal local XY. Rotating the rig does **not** rotate displacement today.

### Layout vs Transform

| Data | Space | Role |
| --- | --- | --- |
| `HeightFieldLayout` | Shared | Texel size, chunk grid, `PixelWorld*` — **same for all layers** initially |
| `ChunkInstanceData` | **Layer-local** | Chunk center/scale in parent local space |
| `Transform` | Per layer | Offset, orientation, scale between layers |
| `h` | Texture scalar | Shape; same RT → same shape |

### Camera / view depth

- **h** along **object -Z** is primary; orient rig toward camera so -Z aligns with view.
- **Layer ordering** uses Transform position (depth) and/or render queue; same RT + same transform → coincident surfaces.
- **Head sway** keeps geometry fixed, camera matrices change — same as current design. Optional future: add **h in view space** if requirements change.

### Normals / curvature

- Compute passes use layout-aligned texel gradients (unchanged).
- When layer rotates, transform normals with **object-to-world** 3×3 in draw or VS.

---

## Components

### `HeightFieldStack`

- Builds / rebuilds `HeightFieldLayout` from camera pixel size and ortho size.
- Owns `HeightFieldLodCache` registry.
- Frame order: height updates → unique cache updates → layer draws.

Replaces `HeightFieldBridge` over time.

### `HeightFieldLayer`

Per GameObject:

- `HeightFieldLodCompute` (stage 2)
- `HeightFieldLodDraw` (stage 3)

References:

- `_height` — RT from App
- `_lodCompute` — if set, **skip** stage 2 and use referenced cache
- `_drawCamera` — default stack camera

### `HeightFieldLodCache`

Cache key:

```text
HeightTex.GetInstanceID()
+ layout (TexW/H, PixelWorld, barrier)
+ LOD params (curvature scale, thresholds)
```

Holds normal/curvature/LOD buffers and optionally shared instance/args buffers.  
Inspector reference to another `HeightFieldLodCompute` is clearer than RT instance ID alone.

### App stage 1

Keep `IHeightFieldSource`. Multiple sources → multiple RTs. One simulation, many layers → **one RT, many layer references**.

---

## Frame Pipeline

1. Rebuild layout if needed  
2. App writes each distinct `HeightTex`  
3. For each unique cache key: normals → curvature → classify → neighbor → **`GetData` once** → build instance lists  
4. Per layer: indirect draw with layer **Transform** and material  

Draw order: stack layer list (back to front) and/or material render queue.

---

## `GetData`

Only in `BuildInstanceLists`. Shared cache → **one CPU read per cache per frame**, multiple draws.

---

## Shader (`HFVert`)

```hlsl
float3 posOS = float3(localXY.x, localXY.y, v.positionOS.z - h);
float3 posWS = TransformObjectToWorld(posOS);
o.positionCS = TransformWorldToHClip(posWS);
```

Rename `WorldScaleCenter` → `LocalScaleCenter` in `ChunkInstanceData`.

---

## Migration

| Current | Target |
| --- | --- |
| `HeightFieldBridge` | `HeightFieldStack` + `HeightFieldLayer`(s) |
| Legacy monolith | `HeightFieldLodCompute` + `HeightFieldChunkMeshDrawer` |
| Single rig menu | Stack + layer setup |

One layer, identity transform → should match current look after OS path fix.

---

## Phases

| Phase | Work |
| --- | --- |
| 0 | This document |
| 1 | Object-space `h` + local chunk data; regression single layer |
| 2 | Split Compute/Draw + cache by height key |
| 3 | Stack + multiple layers |
| 4 | Rotated normals, sort order, editor |

---

## Tests

- Single layer ≈ current visuals  
- Two layers, same RT, Z offset → separated shells  
- Two layers, shared `_lodCompute` → one classify + one `GetData`  
- Rig yaw → displacement follows local -Z  

---

## Related

- [urp-heightfield-lod-design.md](urp-heightfield-lod-design.md)  
- [head-sway-lens-shift-camera.md](head-sway-lens-shift-camera.md)
