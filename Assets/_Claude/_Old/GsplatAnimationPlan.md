
# Gaussian Splat Animation & Interaction Plan

claude --resume "gaussian-splat-animation-interaction"

## Context

This project renders Gaussian splats on Meta Quest 3 using **gsplat-unity v1.2.1** (wuyize25) with URP 17.3.0. The current scene (`4.unity`) renders `tree27k.ply` (27K splats, SH degree 1) with hand tracking enabled. The goal is to add animation and interaction capabilities — recoloring, scaling, fading, displacement, and hand-based effects.

The architecture uses 5 GPU buffers (position, color, scale, rotation, SH), a 14-dispatch radix sort per frame, and renders via `Graphics.RenderMeshPrimitives`. The key insight: **shader-uniform-based effects are essentially free** (a few ALU ops per splat), while **compute-shader-based per-splat buffer modifications** add ~0.1-0.3ms each and must compete with the existing sort budget.

**Quest 3 budget**: ~11ms/frame at 90Hz. Current splat rendering uses ~4-5ms, leaving ~6ms headroom. Tier 1 effects add <0.1ms total. Tier 2 effects would consume 0.5-1.0ms each — only viable on PCVR.

---

## Tier 1: Quest 3 Standalone (Shader Uniforms Only)

These modify only the vertex/fragment shader via `MaterialPropertyBlock` uniforms. Zero extra GPU dispatches.

| Effect | Approach | Cost |
|--------|----------|------|
| **Color tint** | `_TintColor` float4 multiply in fragment | 1 ALU |
| **Opacity/fade** | `_OpacityMul` float multiply on alpha | 1 ALU |
| **Distance fade** | `_FadeCenter` + `_FadeRadius` — distance check in vertex, interpolated to fragment | ~5 ALU |
| **Hue shift** | `_HueShift` — RGB→HSV→RGB in fragment | ~15 ALU |
| **Emission/glow** | `_EmissionColor` * `_EmissionStrength` added before premultiply | 1 MAD |
| **Wind sway** | `_WindDir` * sin(dot(pos, dir) * freq + time) displacement in vertex | ~6 ALU |
| **Sphere highlight** | `_HighlightCenter` + `_HighlightRadius` — smoothstep tint near a point | ~8 ALU |
| **Scale pulse** | Animate existing `splatScale` from C# with sin(time) | 0 GPU (already a uniform) |
| **Portal/magic lens** | Animate existing `GsplatCutout` transform from C# | 0 extra GPU (cutout already runs) |
| **Temporal dissolve** | Combine distance fade + opacity + scale animation as a coordinated sequence | Reuses above |

---

## Tier 2: PCVR Only (Compute Shader Passes)

These dispatch compute shaders to modify per-splat buffers before the sort pass.

| Effect | Approach | Cost |
|--------|----------|------|
| **Explosion/gravity** | Compute reads position buffer, applies forces + velocity integration, writes animated buffer | ~0.1ms + velocity buffer |
| **Per-splat recolor** | Compute modifies color buffer based on spatial region or hand proximity | ~0.1ms |
| **Per-splat scale** | Compute modifies scale buffer (assembly/disassembly wave) | ~0.1ms |
| **Wave/ripple** | Compute applies sinusoidal displacement to positions | ~0.1ms |
| **Hand deformation** | Compute pushes splats away from hand joints (26 joints checked per splat) | ~0.3ms |
| **Morph between clouds** | Compute lerps position/color/scale between two loaded assets | ~0.2ms + 2x memory |

---

## Tier 3: Creative VR Ideas

### "Magic Lens" Portal (Quest 3 safe)
A `GsplatCutout` attached to the user's palm (inverted = show only inside). Raise your hand -> a window into the splat world appears. Zero extra GPU cost.

### "Seasonal Change" (Quest 3 safe)
Animate `_HueShift` on the tree splat from green -> autumn orange/red. Combine with gentle wind sway.

### "Breath of Life" Reveal (Quest 3 safe)
Splats start transparent. Distance fade sphere centered on user's head gradually expands.

### "Disintegration" (Quest 3 safe)
Triggered dissolve: a radial wave from a point scales down + fades out splats.

### "Splat Painting" (PCVR)
Hand proximity permanently recolors splats via compute shader.

### Splat Clustering for Rigid Interaction (Experimental)
Precompute clusters at import time. Animate cluster transforms instead of per-splat.

---

## Implementation Order

1. **Shader modification**: Add `_TintColor`, `_HueShift`, `_OpacityMul` uniforms to Gsplat.shader/hlsl
2. **GsplatSizeAnimator.cs**: Animate `SplatDownscaleFactor`
3. **GsplatHueAnimator.cs**: Animate `_HueShift` and `_TintColor` via MaterialPropertyBlock
4. **GaussianAnimationManager.cs**: Coordinate animations with sin-based driver
5. **Hand interaction**: Connect hand tracking to highlight/tint effects
6. **Compute effects**: PCVR-only per-splat buffer manipulation

---

## Key Files

### Package (gsplat-unity, referenced from manifest.json)
- `Runtime/GsplatRenderer.cs` — main component, public API
- `Runtime/GsplatRendererImpl.cs` — internal renderer, owns MaterialPropertyBlock
- `Runtime/Shaders/Gsplat.shader` — main shader
- `Runtime/Shaders/Gsplat.hlsl` — core HLSL (covariance, projection, SH)
- `Runtime/Shaders/GsplatUncompressed.hlsl` — buffer declarations for uncompressed mode

### Project
- `Assets/_Projects/HandPose/4.unity` — main VR scene
- `Assets/_Projects/HandPose/tree27k.ply` — active splat asset (27K splats)
- `Assets/Settings/Mobile_Renderer.asset` — Quest 3 URP renderer (has GsplatURPFeature)
- `Assets/Settings/PC_Renderer.asset` — PCVR URP renderer
