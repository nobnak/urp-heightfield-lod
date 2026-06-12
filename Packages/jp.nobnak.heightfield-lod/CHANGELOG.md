# Changelog

## [0.3.0] - 2026-06-12

### Added
- `HeightFieldLayoutHost.LayoutApplied` event.
- `HeightFieldChunkMeshDrawer.SetDependencies(ILodSource, IHeightFieldSource)`.
- Serialized rig references on `HeightFieldLayoutHost` (`_heightSourceBehaviour`, `_lodCompute`, `_drawers`).

### Changed
- `HeightFieldLayoutHost` now performs rig initialization (`Allocate`, `Configure`) and dependency injection.
- `HeightFieldChunkMeshDrawer` no longer auto-resolves LOD/height sources via `GetComponent`.

### Removed
- `HeightFieldRigUtil.FindHeightSource` — use serialized references on `HeightFieldLayoutHost` instead.
- `HeightFieldChunkMeshDrawer.Release()` — render callback is unsubscribed in `OnDisable`.
- Sample `HeightFieldBridge` — functionality merged into `HeightFieldLayoutHost`.

### Migration
- Remove `HeightFieldBridge` from rigs; wire `_heightSourceBehaviour`, `_lodCompute`, and `_drawers` on `HeightFieldLayoutHost`.
- Or use **GameObject → Height Field → Setup Sample Rig** to create a fresh rig.
