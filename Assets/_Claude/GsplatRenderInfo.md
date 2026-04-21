# gsplat-unity v1.2.1 — Renderer Architecture Reference

## Package Info
- **Author**: Yize Wu (wuyize25)
- **License**: MIT
- **Repo**: https://github.com/wuyize25/gsplat-unity
- **Local reference**: `"wu.yize.gsplat": "file:/Users/johanneslotze/Downloads/gsplat-unity-main"` in manifest.json

## Data Flow

```
PLY import -> GsplatAsset (uncompressed or Spark compressed)
    -> GPU Buffers uploaded (sync or async, batch size 100K)
    -> Per frame:
        1. CalcDepth.compute — compute depth per splat from camera
        2. InitOrder.compute — filter through cutouts, build order buffer
        3. Radix sort (14 dispatches) — sort by depth
        4. Gsplat.shader — render sorted splats as instanced quads
```

## GPU Buffers (Uncompressed Mode)

| Buffer | Type | Per-Splat Size | Description |
|--------|------|----------------|-------------|
| `_PositionBuffer` | `StructuredBuffer<float3>` | 12 bytes | World-space XYZ |
| `_ColorBuffer` | `StructuredBuffer<float4>` | 16 bytes | RGBA (before SH) |
| `_ScaleBuffer` | `StructuredBuffer<float3>` | 12 bytes | Per-axis scale |
| `_RotationBuffer` | `StructuredBuffer<float4>` | 16 bytes | Quaternion (wxyz) |
| `_SHBuffer` | `StructuredBuffer<float3>` | variable | Spherical harmonics |
| `_OrderBuffer` | `StructuredBuffer<uint>` | 4 bytes | Sorted indices |

## Shader Pipeline

### Gsplat.shader
- Tags: RenderType=Transparent, Queue=Transparent
- Blend: One OneMinusSrcAlpha (premultiplied alpha)
- ZWrite Off, Cull Off
- Keywords: `SH_BANDS_0/1/2/3`, `UNCOMPRESSED/SPARK`

### Vertex Shader Flow
1. `InitSource()` — get instance index from vertex.z + instanceID
2. `InitSplatData()` — read position/rotation/scale/color from buffers
3. `CalcCovariance()` — quaternion + scale -> 3x3 covariance matrix
4. `InitCorner()` — project 3D covariance to 2D screen ellipse, compute quad corners
5. `EvalSH()` — spherical harmonics for view-dependent color (if SH_BANDS > 0)
6. `ClipCorner()` — alpha-weighted corner clipping

### Fragment Shader
```hlsl
float A = dot(i.uv, i.uv);         // distance from center
if (A > 1.0) discard;               // outside unit circle
float alpha = exp(-A * 4.0) * i.color.a;  // Gaussian falloff
return float4(color.rgb * alpha * _Brightness, alpha);  // premultiplied
```

## MaterialPropertyBlock Uniforms

Set by `GsplatRendererImpl.Render()`:
- `_SplatCount` (int) — number of visible splats
- `_SplatInstanceSize` (int) — splats per mesh instance
- `_SHDegree` (int) — 0-3
- `_Brightness` (float) — color multiplier
- `_ScaleFactor` (float) — `1.0 - SplatDownscaleFactor`
- `_GammaToLinear` (bool as int)
- `_MATRIX_M` (float4x4) — object-to-world matrix
- `_OrderBuffer` (StructuredBuffer<uint>)

## GsplatRenderer Public Properties (C#)

```csharp
public GsplatAsset GsplatAsset;
public int SHDegree = 3;                    // 0-3
public uint RenderOrder = 0;
public float Brightness = 1.0f;
public float SplatDownscaleFactor = 0.0f;   // 0-1, maps to scaleFactor = 1 - this
public bool GammaToLinear;
public bool AsyncUpload;
public bool RenderBeforeUploadComplete = true;
public bool CutoutsUpdateBounds = true;
public GsplatSortMode SortMode;
public uint SortRefreshRate = 1;
public uint CutoutsRefreshRate = 1;
```

## Rendering Call Chain

```
GsplatRenderer.Update()
  -> m_renderer.EvaluateRefreshRequired()
  -> m_renderer.DispatchInitOrder(cutouts, ...)
  -> m_renderer.Render(transform, layer, gammaToLinear, shDegree, brightness, scaleFactor, renderOrder)
       -> m_propertyBlock.SetFloat/SetInteger/SetMatrix (all uniforms)
       -> Graphics.RenderMeshPrimitives(rp, mesh, 0, instanceCount)
```

## GsplatRendererImpl Key Internals

- `m_propertyBlock` — MaterialPropertyBlock, private, set in `Render()`
- `m_gsplatAsset` — bound asset reference
- `GsplatResource` — GPU buffer wrapper (public)
- `OrderBuffer` — sorted index buffer (public)
- `SorterResource` — sort working buffers (public)
- `m_remainingCount` — visible splat count after cutout filtering
- `m_bounds` — world-space bounds

## Quest 3 Performance Profile

- 27K splats: ~4-5ms total (sort + render per eye)
- Radix sort: ~2ms (14 compute dispatches)
- Render: ~1-2ms per eye
- Headroom at 90Hz: ~6ms
- Graphics API: Vulkan (preferred), GLES 3.1+ fallback
- No HDR, forward rendering, 2x MSAA
- Compute shaders supported but budget-constrained

## Extension Points

1. **MaterialPropertyBlock**: Uniforms set before render — can add custom properties
2. **Shader keywords**: multi_compile can be extended
3. **GsplatAsset subclass**: Override buffer data before upload
4. **Compute shaders**: Dispatch before sort to modify buffers
5. **Cutout system**: Runtime spatial filtering, already evaluates per frame
6. **SplatDownscaleFactor**: Animate for scale effects (no shader change needed)
7. **Brightness**: Animate for fade/glow (no shader change needed)
