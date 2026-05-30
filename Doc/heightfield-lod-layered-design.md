# Heightfield LOD — Layered Design

> Japanese: [heightfield-lod-layered-design.ja.md](heightfield-lod-layered-design.ja.md)  
> Algorithm spec: [urp-heightfield-lod-design.md](urp-heightfield-lod-design.md)

---

## System overview

```text
HeightFieldLod (core)     contracts + layout + compute + draw + shaders
        ▲
HeightField.Samples       optional height implementations (Sine, Musgrave)
App                       Bridge wiring
```

**Pipeline:** Height `N` RTs → LOD `K` computes → Draw `M` drawers.

---

## Context map

| Context | asmdef | Role |
| --- | --- | --- |
| **HeightFieldLod** | `HeightFieldLod` | `IHeightFieldSource`, `HeightFieldLayout`, `ILodSource`, LOD/draw |
| **Samples** | `HeightField.Samples` | Sample `IHeightFieldSource` implementations |
| **App** | `App` | `HeightFieldBridge` |

### Dependencies

```text
App                 → HeightFieldLod, HeightField.Samples
HeightField.Samples → HeightFieldLod (contracts only)
HeightFieldLod      → URP only
```

---

## Folder layout (`Assets/`)

```text
HeightFieldLod/
  Contracts/   IHeightFieldSource, HeightFieldLayout, ILodSource
  Layout/      HeightFieldLayoutHost
  Compute/     HeightFieldLodCompute
  Draw/        ChunkMeshDrawer, ChunkInstanceData, ChunkMeshBuilder
  Util/        HeightFieldSourceUtil
  Shaders/

Samples/HeightField/
  SineHeightFieldSource, MusgraveHeightFieldSource
  Shaders/
  HeightField.Samples.asmdef

App/Bridge/, App/Editor/
```

---

## Related

- [urp-heightfield-lod-design.md](urp-heightfield-lod-design.md)
