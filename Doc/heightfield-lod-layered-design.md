# Heightfield LOD — Layered Design

> Japanese: [heightfield-lod-layered-design.ja.md](heightfield-lod-layered-design.ja.md)  
> Algorithm spec: [urp-heightfield-lod-design.md](urp-heightfield-lod-design.md)

---

## System overview

```text
jp.nobnak.heightfield-lod (core UPM)   contracts + layout + compute + draw + shaders
        ▲
Assets/Samples/HeightField             sample height sources
```

**Pipeline:** Height `N` RTs → LOD `K` computes → Draw `M` drawers.

---

## Context map

| Context | asmdef | Role |
| --- | --- | --- |
| **HeightFieldLod** | `HeightFieldLod` | `IHeightFieldSource`, `HeightFieldLayout`, `ILodSource`, LOD/draw |
| **Samples** | `HeightField.Samples` | Sample height implementations |
| **Samples.Editor** | `HeightField.Samples.Editor` | `Setup Sample Rig` menu (requires sample import) |

### Dependencies

```text
HeightField.Samples.Editor → HeightField.Samples, HeightFieldLod
HeightField.Samples        → HeightFieldLod (contracts only)
HeightFieldLod               → URP only
```

---

## Draw cameras

Same rule as [urp-heightfield-lod-design.md](urp-heightfield-lod-design.md#drawing-urp):

- Hook: `RenderPipelineManager.beginCameraRendering`
- Draw on every camera except `CameraType.Preview` when `cullingMask` includes the rig **layer** (`gameObject.layer`)
- Shadow passes use the same rule
- `HeightFieldLayoutHost._camera` is for **layout only**, not draw filtering

---

## Repository layout (development)

```text
urp-heightfield-lod/
├── Packages/jp.nobnak.heightfield-lod/   # UPM core (package.json, Runtime/, Samples~/)
├── Assets/Samples/HeightField/           # dev samples (synced to Samples~ on compile)
├── Assets/Editor/HeightFieldLod.Dev/     # dev-only sample sync scripts
└── Doc/                                  # design docs
```

## Folder layout

```text
Packages/jp.nobnak.heightfield-lod/Runtime/   core (HeightFieldLod.asmdef)
Assets/Samples/HeightField/                   dev samples + Editor/ (UPM sample source)
Assets/Editor/HeightFieldLod.Dev/             dev-only: sync `Assets/Samples/HeightField` → `Packages/.../Samples~` on compile
Packages/jp.nobnak.heightfield-lod/Samples~/  **tracked in git** for UPM sample import (refresh via dev sync before release)
```

## Package internals (`Runtime/`)

```text
Runtime/
├── Contracts/     IHeightFieldSource, ILodSource, HeightFieldLayout
├── Layout/        HeightFieldLayoutHost
├── Compute/       HeightFieldLodCompute
├── Draw/          HeightFieldChunkMeshDrawer, ChunkMeshBuilder, ChunkInstanceData
├── Util/          HeightFieldSourceUtil
└── Shaders/       NormalFromHeight, Curvature, ReductionMax, ClassifyLOD, NeighborLOD,
                   HeightFieldLit, HeightFieldToon
```

Typical rig: `HeightFieldLayoutHost`, an `IHeightFieldSource`, `HeightFieldLodCompute`, `HeightFieldChunkMeshDrawer`.  
Updates are **pull**-driven from `beginCameraRendering` (`EnsureUpdated` per stage). See the [Japanese doc](heightfield-lod-layered-design.ja.md) for full detail.

---

## Related

- [urp-heightfield-lod-design.md](urp-heightfield-lod-design.md)
