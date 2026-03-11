// AIA EAI Hin Nr Claude Opus 4.6 v1.0

import { useRef, useEffect, useState } from 'react'
import { useFrame, useThree } from '@react-three/fiber'
import * as THREE from 'three'

// Shared so GamepadCamera can trigger gaze clicks
let _gazeClick  = null   // () => void  — call on primary press
let _gazeRelease = null  // () => void  — call on primary release

export function registerGazeHandlers(click, release) {
  _gazeClick   = click
  _gazeRelease = release
}

export function fireGazeClick()   { _gazeClick?.() }
export function fireGazeRelease() { _gazeRelease?.() }

// Shared for crosshair color
let notifyHit = null

const _origin    = new THREE.Vector3()
const _direction = new THREE.Vector3()
const _ray       = new THREE.Raycaster()
_ray.far = 20

// ---------------------------------------------------------------
// 3D component — place inside <Canvas>/<XR>
// ---------------------------------------------------------------
export function GazeReticle() {
  const { camera, scene, gl } = useThree()

  const hitObjectRef  = useRef(null)
  const origEmissive  = useRef(new THREE.Color())
  const hasSavedColor = useRef(false)
  const pressing      = useRef(false)

  // Synthetic canvas events dispatch pointer events at screen centre.
  // R3F re-raycasts from those coords and fires onPointerDown/Up/Click on hit meshes.
  function dispatchCentre(type) {
    const canvas = gl.domElement
    const cx = canvas.clientWidth  / 2
    const cy = canvas.clientHeight / 2
    const rect = canvas.getBoundingClientRect()
    canvas.dispatchEvent(new PointerEvent(type, {
      clientX: rect.left + cx,
      clientY: rect.top  + cy,
      pointerId: 1,
      bubbles: true,
      cancelable: true,
    }))
  }

  // Register gaze click/release so GamepadCamera can call them
  useEffect(() => {
    registerGazeHandlers(
      () => { pressing.current = true;  dispatchCentre('pointerdown') },
      () => { pressing.current = false; dispatchCentre('pointerup');  dispatchCentre('click') },
    )
    // Also send a pointermove on mount so R3F's hover state is initialised
    dispatchCentre('pointermove')
    return () => registerGazeHandlers(null, null)
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  useFrame(() => {
    // Cast a ray from camera centre into the scene
    _origin.setFromMatrixPosition(camera.matrixWorld)
    _direction.set(0, 0, -1).transformDirection(camera.matrixWorld)
    _ray.set(_origin, _direction)

    const hits = _ray.intersectObjects(scene.children, true)
      .filter(h => {
        // only count meshes that have R3F interactive handlers
        const handlers = h.object?.__r3f?.handlers
        return handlers && Object.keys(handlers).length > 0
      })

    const hit = hits[0]?.object ?? null

    // Hover-out previous
    if (hitObjectRef.current && hitObjectRef.current !== hit) {
      const mat = hitObjectRef.current.material
      if (mat && hasSavedColor.current) {
        mat.emissive?.copy(origEmissive.current)
        mat.needsUpdate = true
      }
      hasSavedColor.current = false
      hitObjectRef.current = null
      notifyHit?.(false)
    }

    // Hover-in new
    if (hit && hit !== hitObjectRef.current) {
      const mat = hit.material
      if (mat?.emissive) {
        origEmissive.current.copy(mat.emissive)
        hasSavedColor.current = true
        mat.emissive.setRGB(0.35, 0.35, 0.35)
        mat.needsUpdate = true
      }
      hitObjectRef.current = hit
      notifyHit?.(true)
      // Keep R3F's hover state fresh each frame while hovering
      dispatchCentre('pointermove')
    }
  })

  return null
}

// ---------------------------------------------------------------
// DOM component — place outside <Canvas>
// ---------------------------------------------------------------
export function Crosshair() {
  const [hit, setHit] = useState(false)

  useEffect(() => {
    notifyHit = setHit
    return () => { notifyHit = null }
  }, [])

  const color  = hit ? '#fff' : 'rgba(255,255,255,0.6)'
  const shadow = hit ? '0 0 6px #000, 0 0 12px #00f8' : '0 0 4px #0006'
  const size   = hit ? 14 : 10

  return (
    <div style={{
      position: 'fixed',
      top: '50%', left: '50%',
      transform: 'translate(-50%,-50%)',
      width: size, height: size,
      borderRadius: '50%',
      border: `2px solid ${color}`,
      boxShadow: shadow,
      pointerEvents: 'none',
      zIndex: 9998,
      transition: 'all 0.1s ease',
    }} />
  )
}
