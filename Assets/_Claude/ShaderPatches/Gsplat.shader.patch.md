# Shader Patch Instructions

The modified `Gsplat.shader` file is at `Assets/_Claude/ShaderPatches/Gsplat_Modified.shader`.

## How to apply:
1. Find the gsplat-unity package folder (referenced in Packages/manifest.json)
2. Replace `Runtime/Shaders/Gsplat.shader` with `Gsplat_Modified.shader` (rename it to `Gsplat.shader`)
3. The modification adds support for `_HueShift`, `_TintColor`, and `_OpacityMul` global shader properties

## What changed:
- Added `_HueShift`, `_TintColor`, `_OpacityMul` uniform declarations
- Added `RGBtoHSV` and `HSVtoRGB` helper functions
- Modified fragment shader to apply hue shift, tint, and opacity multiplier after normal color calculation
- All new uniforms default to identity values (no effect) when not set
