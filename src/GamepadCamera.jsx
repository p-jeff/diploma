// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useRef, useEffect, useState } from 'react'
import { useFrame, useThree } from '@react-three/fiber'
import { useXR } from '@react-three/xr'
import * as THREE from 'three'

// --- Tuning ---
const DEADZONE    = 0.15
const MOVE_SPEED  = 3.0   // m/s
const LOOK_SPEED  = 2.0   // rad/s
const PITCH_LIMIT = Math.PI / 2.5

function dz(v) {
  if (Math.abs(v) < DEADZONE) return 0
  return (v - Math.sign(v) * DEADZONE) / (1 - DEADZONE)
}

const _fwd   = new THREE.Vector3()
const _right = new THREE.Vector3()
const _euler  = new THREE.Euler(0, 0, 0, 'YXZ')

let notifyStatus = null

// -------------------------------------------------------------------
// R3F component — place inside <Canvas> / <XR>
// playerRef → the <XROrigin> group (used for position in XR mode)
// onAction  → optional callback({ hand, type }) on button press
// -------------------------------------------------------------------
export function GamepadCamera({ playerRef, onAction }) {
  const { camera } = useThree()
  const session  = useXR((s) => s.session)
  const emulator = useXR((s) => s.emulator)
const gpIndex  = useRef(-1)
  const yaw      = useRef(0)
  const pitch    = useRef(0)
  const prevLB   = useRef(false)
  const prevRB   = useRef(false)
  const prevLT   = useRef(false)
  const prevRT   = useRef(false)

  useEffect(() => {
    _euler.setFromQuaternion(camera.quaternion, 'YXZ')
    yaw.current   = _euler.y
    pitch.current = _euler.x

    const onConnect = ({ gamepad }) => {
      gpIndex.current = gamepad.index
      const id = gamepad.id
      notifyStatus?.(id.length > 35 ? id.slice(0, 35) + '…' : id)
    }
    const onDisconnect = ({ gamepad }) => {
      if (gpIndex.current === gamepad.index) {
        gpIndex.current = -1
        notifyStatus?.(null)
      }
    }
    window.addEventListener('gamepadconnected', onConnect)
    window.addEventListener('gamepaddisconnected', onDisconnect)
    return () => {
      window.removeEventListener('gamepadconnected', onConnect)
      window.removeEventListener('gamepaddisconnected', onDisconnect)
    }
  }, [camera])

  useFrame((_, delta) => {
    // --- find gamepad ---
    const pads = navigator.getGamepads()
    let gp = gpIndex.current >= 0 ? pads[gpIndex.current] : null
    if (!gp?.connected) {
      gp = null
      for (const g of pads) {
        if (g?.connected) { gp = g; gpIndex.current = g.index; break }
      }
    }
    if (!gp) return

    // --- axes ---
    const moveX = dz(gp.axes[0] ?? 0)   // left stick X  → strafe
    const moveY = dz(gp.axes[1] ?? 0)   // left stick Y  → forward/back
    const lookX = dz(gp.axes[2] ?? 0)   // right stick X → yaw
    const lookY = dz(gp.axes[3] ?? 0)   // right stick Y → pitch

    // --- shoulder buttons ---
    const lb = gp.buttons[4]?.pressed ?? false
    const rb = gp.buttons[5]?.pressed ?? false
    const lt = (gp.buttons[6]?.value ?? 0) > 0.5
    const rt = (gp.buttons[7]?.value ?? 0) > 0.5

    if (lb && !prevLB.current)  { onAction?.({ hand: 'left',  type: 'primary' });   emulator?.hands?.left?.updatePinchValue(1) }
    if (!lb && prevLB.current)  emulator?.hands?.left?.updatePinchValue(0)
    if (rb && !prevRB.current)  { onAction?.({ hand: 'right', type: 'primary' });   emulator?.hands?.right?.updatePinchValue(1) }
    if (!rb && prevRB.current)  emulator?.hands?.right?.updatePinchValue(0)
    if (lt && !prevLT.current) onAction?.({ hand: 'left',  type: 'secondary' })
    if (rt && !prevRT.current) onAction?.({ hand: 'right', type: 'secondary' })
    prevLB.current = lb; prevRB.current = rb
    prevLT.current = lt; prevRT.current = rt

    // --- update yaw + pitch ---
    yaw.current  -= lookX * LOOK_SPEED * delta
    pitch.current = Math.max(-PITCH_LIMIT, Math.min(PITCH_LIMIT, pitch.current - lookY * LOOK_SPEED * delta))

    // --- apply look + move ---
    if (session && playerRef?.current) {
      // XR mode: rotate the XROrigin group for both yaw and pitch.
      // XROrigin is a regular Three.js group whose world transform @react-three/xr
      // applies as an offset to the XR reference space, so rotating it actually
      // moves the camera view — the same mechanism that makes yaw work.
      const origin = playerRef.current
      origin.rotation.order = 'YXZ'
      origin.rotation.y = yaw.current
      origin.rotation.x = pitch.current

      _fwd.set(-Math.sin(yaw.current), 0, -Math.cos(yaw.current))
      _right.set( Math.cos(yaw.current), 0, -Math.sin(yaw.current))
      origin.position.addScaledVector(_fwd,   -moveY * MOVE_SPEED * delta)
      origin.position.addScaledVector(_right,  moveX * MOVE_SPEED * delta)
    } else {
      // Plain browser mode: drive the perspective camera directly
      camera.quaternion.setFromEuler(_euler.set(pitch.current, yaw.current, 0, 'YXZ'))

      _fwd.set(-Math.sin(yaw.current), 0, -Math.cos(yaw.current))
      _right.set( Math.cos(yaw.current), 0, -Math.sin(yaw.current))
      camera.position.addScaledVector(_fwd,   -moveY * MOVE_SPEED * delta)
      camera.position.addScaledVector(_right,  moveX * MOVE_SPEED * delta)
    }
  })

  return null
}

// -------------------------------------------------------------------
// DOM component — place outside <Canvas>
// -------------------------------------------------------------------
export function GamepadStatus() {
  const [label, setLabel] = useState(null)

  useEffect(() => {
    notifyStatus = setLabel
    return () => { notifyStatus = null }
  }, [])

  return (
    <div style={{
      position: 'fixed',
      top: 14,
      right: 14,
      background: label ? 'rgba(30,160,30,0.88)' : 'rgba(60,60,60,0.75)',
      color: '#fff',
      padding: '5px 12px',
      borderRadius: 6,
      fontSize: 11,
      fontFamily: 'monospace',
      zIndex: 9999,
      pointerEvents: 'none',
      userSelect: 'none',
    }}>
      {label ? `Gamepad: ${label}` : 'Gamepad: none — press a button to activate'}
    </div>
  )
}
