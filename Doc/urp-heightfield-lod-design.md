# urp-heightfield-lod

## Overview

URP-based adaptive heightfield rendering system using:

- Orthographic Camera
- Curvature-driven LOD
- Chunk-based rendering
- GPU-driven rendering
- Compute Shader reduction
- Indirect rendering

Primary goals:

- Fullscreen heightfield visualization
- Media art rendering
- Dynamic simulation visualization
- GPU-friendly adaptive surface rendering

---

# Core Concepts

## Heightfield

Surface representation:

```math
P(x,z) = (x, h(x,z), z)
```

The system renders a world-space heightfield plane.

---

# Camera Model

## Orthographic Projection

Runtime rendering assumes an orthographic camera.

LOD selection is based on:

- curvature
- local complexity
- normal variance

NOT camera distance.

---

# World-space Rendering

Meshes are generated in world-space rather than screen-space.

Advantages:

- Scene View visualization
- Lighting support
- Perspective debug view
- Easier mesh inspection

The rendered plane fits the orthographic camera bounds.

---

# Chunk System

## Chunk Grid

The visible plane is divided into fixed-size chunks.

Example:

```text
Screen: 1920x1080
Chunk Size: 32x32 pixels
Chunk Grid: 60x34
```

Each chunk corresponds to:

- a world-space region
- a texture-space region
- a screen-space tile

---

# Geometry Refinement

Two backend strategies are under consideration.

---

# Strategy A: Chunk LOD Mesh

## Overview

Each chunk selects one of several prebuilt meshes.

Example:

| LOD | Mesh Resolution |
| --- | --- |
| LOD0 | 32x32 |
| LOD1 | 16x16 |
| LOD2 | 8x8 |
| LOD3 | 4x4 |

Chunk size remains constant.

Only vertex density changes.

---

## Advantages

- URP friendly
- Compute friendly
- Indirect rendering friendly
- Stable topology
- Easy debugging
- Mesh reuse
- Good GPU cache locality

---

## Disadvantages

- Crack prevention required
- LOD transition artifacts
- Multiple mesh assets required

---

## Recommended Initial Implementation

```text
Chunk LOD Mesh
+
Skirt
+
LOD Difference <= 1
```

---

# Strategy B: Connected Chunk Tessellation

## Overview

Connected topology refinement:

- adaptive subdivision
- procedural tessellation
- quadtree refinement
- compute-generated topology

---

## Advantages

- smoother topology
- fewer seams
- curvature-following refinement
- more continuous surfaces

---

## Disadvantages

- high implementation complexity
- URP tessellation limitations
- indirect rendering complexity
- topology synchronization difficulty

---

## Current Recommendation

Use Strategy A first.

Keep refinement backend abstracted for future replacement.

---

# Height Sampling

Vertex displacement:

```math
P(x,z) = (x, h(x,z), z)
```

Height texture is sampled in the vertex shader.

---

# Curvature-driven LOD

## Curvature Metric

Possible metrics:

- Laplacian
- normal variance
- local frequency energy

Initial recommendation:

```math
\nabla^2 h(x,y)
```

or:

```text
max(abs(laplacian))
```

---

# Curvature Reduction

Generate a reduction hierarchy from the curvature map.

Use custom reduction instead of standard mipmaps.

Recommended reductions:

- max
- variance
- percentile

Avoid average-only reduction.

---

# Reduction Pyramid

Example:

```text
2048x2048
↓
1024x1024
↓
512x512
↓
256x256
```

---

# Virtual Padding

Do not physically resize textures.

Use virtual extents instead.

Example:

```text
Actual:
1920x1080

Virtual:
1920x1088
```

Out-of-range pixels are:

- ignored
- clamped

during reduction.

---

# Chunk-aligned Reduction

Prefer chunk-aligned reduction rather than arbitrary screen-space reduction.

Chunk/grid alignment simplifies:

- LOD selection
- stitching
- indirect rendering
- culling

---

# LOD Classification

LOD is selected from chunk complexity metrics.

Example thresholds:

```text
LOD0:
curvature > 0.7

LOD1:
0.4 - 0.7

LOD2:
0.15 - 0.4

LOD3:
< 0.15
```

---

# Hysteresis

Prevent flickering during LOD transitions.

Example:

```text
LOD UP:
> 0.6

LOD DOWN:
< 0.45
```

---

# Rendering Pipeline

## Step 1

Update heightfield.

---

## Step 2

Generate curvature map.

Compute Shader.

---

## Step 3

Generate reduction hierarchy.

Compute Shader.

---

## Step 4

Classify chunk LODs.

Compute Shader.

---

## Step 5

Build instance lists.

Per LOD.

---

## Step 6

Indirect rendering.

Recommended:

```text
Graphics.RenderMeshIndirect
```

---

# GPU Buffers

## ChunkData

```cpp
struct ChunkData
{
    uint lod;

    float2 worldOffset;
    float2 worldScale;

    float2 uvOffset;
    float2 uvScale;
};
```

---

# Mesh Reuse

All chunk meshes are reused.

Only transform data changes:

- offset
- scale
- UV transform

---

# Vertex Shader Example

```hlsl
float2 uv =
    localUV * chunkUVScale +
    chunkUVOffset;

float h =
    HeightTex.SampleLevel(
        samplerLinearClamp,
        uv,
        0
    ).r;

float3 pos;

pos.xz =
    localXZ * chunkScale +
    chunkOffset;

pos.y =
    h * heightScale;
```

---

# Crack Prevention

Recommended order:

1. Skirt
2. Stitch Mesh
3. Geomorph

Initial implementation should use skirts.

---

# Unity URP Requirements

## Required

- Compute Shader
- GraphicsBuffer
- StructuredBuffer
- RenderMeshIndirect

---

## Avoid

- Tessellation Shader
- Geometry Shader
- CPU mesh rebuild

---

# Recommended Initial Settings

## Chunk Size

```text
32x32 pixels
```

---

## LOD Count

```text
4 levels
```

---

## Mesh Resolutions

```text
32x32
16x16
8x8
4x4
```

---

## Compute Thread Group

```text
8x8
```

or:

```text
16x16
```

---

# Future Extensions

- GPU culling
- temporal stabilization
- async compute
- clipmap integration
- wavelet refinement
- mesh shader backend
- connected topology refinement
- dynamic simulation coupling

---

# Suggested Repository Name

```text
urp-heightfield-lod
```

Suggested package name:

```text
com.yourname.urp-heightfield-lod
```
