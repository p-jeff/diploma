// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useMemo } from 'react'
import * as THREE from 'three'
import gazeStore from './gazeStore'

const GROUND_SIZE = 50
const RESOLUTION  = 512
const BLOB_RADIUS = RESOLUTION * 0.012  // ~0.6 m radius on a 50 m ground

function heatColor(t) {
  // Blue → Cyan → Green → Yellow → Red
  if (t < 0.25) { const s = t / 0.25;          return [0,                    Math.round(s * 255),       255] }
  if (t < 0.5)  { const s = (t - 0.25) / 0.25; return [0,                    255,                       Math.round((1 - s) * 255)] }
  if (t < 0.75) { const s = (t - 0.5)  / 0.25; return [Math.round(s * 255),  255,                       0] }
  const s = (t - 0.75) / 0.25;                  return [255,                  Math.round((1 - s) * 255), 0]
}

export function buildHeatmapCanvas(samples) {
  const canvas = document.createElement('canvas')
  canvas.width  = RESOLUTION
  canvas.height = RESOLUTION
  const ctx = canvas.getContext('2d')

  // Accumulate white blobs additively on black
  ctx.fillStyle = 'black'
  ctx.fillRect(0, 0, RESOLUTION, RESOLUTION)
  ctx.globalCompositeOperation = 'lighter'

  for (const { x, z } of samples) {
    const cx = ((x + GROUND_SIZE / 2) / GROUND_SIZE) * RESOLUTION
    const cy = ((z + GROUND_SIZE / 2) / GROUND_SIZE) * RESOLUTION
    const grad = ctx.createRadialGradient(cx, cy, 0, cx, cy, BLOB_RADIUS)
    grad.addColorStop(0, 'rgba(255,255,255,0.04)')
    grad.addColorStop(1, 'rgba(0,0,0,0)')
    ctx.fillStyle = grad
    ctx.beginPath()
    ctx.arc(cx, cy, BLOB_RADIUS, 0, Math.PI * 2)
    ctx.fill()
  }

  // Colorize: map accumulated brightness → heat ramp, transparent where empty
  ctx.globalCompositeOperation = 'source-over'
  const imgData = ctx.getImageData(0, 0, RESOLUTION, RESOLUTION)
  const d = imgData.data
  for (let i = 0; i < d.length; i += 4) {
    const brightness = d[i] / 255
    if (brightness < 0.005) { d[i + 3] = 0; continue }
    const t = Math.min(brightness * 3, 1)
    const [r, g, b] = heatColor(t)
    d[i]     = r
    d[i + 1] = g
    d[i + 2] = b
    d[i + 3] = Math.round(220 * Math.min(brightness * 5, 1))
  }
  ctx.putImageData(imgData, 0, 0)

  return canvas
}

export function downloadPositionHeatmap() {
  const canvas = buildHeatmapCanvas(gazeStore.positionSamples)
  const a = document.createElement('a')
  a.href = canvas.toDataURL('image/png')
  a.download = `gaze-floor-${Date.now()}.png`
  a.click()
}

// Mount this only when phase==='ended' so gazeStore.positionSamples is complete.
export default function PositionHeatmapFloor() {
  const texture = useMemo(() => {
    const canvas = buildHeatmapCanvas(gazeStore.positionSamples)
    return new THREE.CanvasTexture(canvas)
  }, [])

  return (
    <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.02, 0]}>
      <planeGeometry args={[GROUND_SIZE, GROUND_SIZE]} />
      <meshBasicMaterial map={texture} transparent depthWrite={false} />
    </mesh>
  )
}
