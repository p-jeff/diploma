// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import * as THREE from 'three'

// Single hue, saturation encodes attention.
// t=0 → white (unseen), t=1 → full saturated color (most looked at).
const HUE = 0.0   // 0.0 = red, 0.6 = blue, 0.35 = green, 0.08 = orange

const tmp = new THREE.Color()

export function getHeatColor(t) {
  t = Math.max(0, Math.min(1, t))
  // Mild power curve so mid-range objects read as tinted rather than near-white
  t = Math.pow(t, 0.6)
  const saturation = t
  const lightness = 1.0 - 0.5 * t  // 1.0 (white) → 0.5 (full color)
  tmp.setHSL(HUE, saturation, lightness)
  return '#' + tmp.getHexString()
}
