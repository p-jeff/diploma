// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import * as THREE from 'three'

const CANVAS_SIZE  = 512
const BRUSH_PX     = 18      // brush radius in pixels
const PAINT_ALPHA  = 0.012   // per-frame alpha — with 'lighter' compositing this adds up
const PAINT_RGB    = '255, 220, 120'  // warm yellow glow

const store = new Map()  // mesh.uuid → { canvas, ctx, texture }

function getOrCreate(uuid) {
  if (store.has(uuid)) return store.get(uuid)
  const canvas = document.createElement('canvas')
  canvas.width  = CANVAS_SIZE
  canvas.height = CANVAS_SIZE
  const ctx = canvas.getContext('2d')
  ctx.fillStyle = '#000000'
  ctx.fillRect(0, 0, CANVAS_SIZE, CANVAS_SIZE)
  const texture = new THREE.CanvasTexture(canvas)
  const entry = { canvas, ctx, texture }
  store.set(uuid, entry)
  return entry
}

// Called each frame GazeTracker hits a mesh with a valid UV.
export function paint(mesh, uv) {
  if (!uv) return

  const { ctx, texture } = getOrCreate(mesh.uuid)

  // Lazily patch the material with an emissiveMap the first time we hit it
  if (mesh.material?.emissiveMap !== texture) {
    mesh.material = mesh.material.clone()
    mesh.material.emissive = new THREE.Color(1, 1, 1)
    mesh.material.emissiveMap = texture
    mesh.material.emissiveIntensity = 2.0
  }

  const x = uv.x * CANVAS_SIZE
  const y = (1 - uv.y) * CANVAS_SIZE   // flip Y: UV origin is bottom-left, canvas is top-left

  // 'lighter' compositing adds RGB values — revisiting the same spot accumulates
  // toward pure white, giving a natural "dwell density" effect.
  ctx.globalCompositeOperation = 'lighter'
  const g = ctx.createRadialGradient(x, y, 0, x, y, BRUSH_PX)
  g.addColorStop(0,   `rgba(${PAINT_RGB}, ${PAINT_ALPHA})`)
  g.addColorStop(0.4, `rgba(${PAINT_RGB}, ${PAINT_ALPHA * 0.5})`)
  g.addColorStop(1,   `rgba(${PAINT_RGB}, 0)`)
  ctx.fillStyle = g
  ctx.beginPath()
  ctx.arc(x, y, BRUSH_PX, 0, Math.PI * 2)
  ctx.fill()
  ctx.globalCompositeOperation = 'source-over'  // reset

  texture.needsUpdate = true
}

// Clear all canvases back to black (call on experiment reset).
export function clearAll() {
  store.forEach(({ canvas, ctx, texture }) => {
    ctx.fillStyle = '#000000'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    texture.needsUpdate = true
  })
}
