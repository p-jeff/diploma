// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useRef, useMemo } from 'react'
import { useFrame, useThree } from '@react-three/fiber'
import * as THREE from 'three'
import { useExperiment } from './ExperimentContext'

function makeButtonTexture() {
  const canvas = document.createElement('canvas')
  canvas.width = 512
  canvas.height = 256
  const ctx = canvas.getContext('2d')

  // Background
  ctx.fillStyle = 'rgba(0,0,0,0.75)'
  const r = 32
  ctx.beginPath()
  ctx.moveTo(r, 0)
  ctx.lineTo(canvas.width - r, 0)
  ctx.quadraticCurveTo(canvas.width, 0, canvas.width, r)
  ctx.lineTo(canvas.width, canvas.height - r)
  ctx.quadraticCurveTo(canvas.width, canvas.height, canvas.width - r, canvas.height)
  ctx.lineTo(r, canvas.height)
  ctx.quadraticCurveTo(0, canvas.height, 0, canvas.height - r)
  ctx.lineTo(0, r)
  ctx.quadraticCurveTo(0, 0, r, 0)
  ctx.closePath()
  ctx.fill()

  // Text
  ctx.fillStyle = '#2ecc71'
  ctx.font = 'bold 96px monospace'
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillText('▶  Start', canvas.width / 2, canvas.height / 2)

  return new THREE.CanvasTexture(canvas)
}

export default function GazeStartButton() {
  const { phase, start } = useExperiment()
  const { camera } = useThree()
  const meshRef = useRef()
  const texture = useMemo(() => makeButtonTexture(), [])

  useFrame(() => {
    if (meshRef.current) {
      meshRef.current.lookAt(camera.position)
    }
  })

  if (phase !== 'idle') return null

  return (
    <mesh
      ref={meshRef}
      position={[0, 1.6, -2]}
      onClick={start}
    >
      <planeGeometry args={[1.6, 0.8]} />
      <meshBasicMaterial map={texture} transparent />
    </mesh>
  )
}
