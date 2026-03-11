// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useRef, useMemo, useEffect } from 'react'
import { useFrame } from '@react-three/fiber'
import * as THREE from 'three'
import { useExperiment } from './ExperimentContext'

function fmt(s) {
  const m = Math.floor(s / 60)
  const sec = s % 60
  return `${m}:${String(sec).padStart(2, '0')}`
}

function drawTimer(canvas, text, textColor) {
  const ctx = canvas.getContext('2d')
  const w = canvas.width
  const h = canvas.height
  ctx.clearRect(0, 0, w, h)

  // Background
  ctx.fillStyle = 'rgba(0,0,0,0.72)'
  ctx.beginPath()
  ctx.roundRect(4, 4, w - 8, h - 8, 20)
  ctx.fill()

  // Time text
  ctx.fillStyle = textColor
  ctx.font = 'bold 72px monospace'
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillText(text, w / 2, h / 2)
}

export const timerBoardRef = { current: null }

export default function GazeTimerBoard() {
  const { phase, remaining } = useExperiment()
  const boardRef = useRef()

  const { canvas, texture } = useMemo(() => {
    const canvas = document.createElement('canvas')
    canvas.width = 256
    canvas.height = 128
    const texture = new THREE.CanvasTexture(canvas)
    return { canvas, texture }
  }, [])

  // Redraw whenever remaining changes
  useEffect(() => {
    const color = remaining <= 10 ? '#e74c3c' : '#2ecc71'
    drawTimer(canvas, fmt(remaining), color)
    texture.needsUpdate = true
  }, [remaining, canvas, texture])

  // Draw once when experiment ends
  useEffect(() => {
    if (phase === 'ended') {
      drawTimer(canvas, 'Done!', '#f1c40f')
      texture.needsUpdate = true
    }
  }, [phase, canvas, texture])

  // Billboard: always face the camera
  useFrame(({ camera }) => {
    if (boardRef.current) boardRef.current.lookAt(camera.position)
  })

  if (phase === 'idle') return null

  return (
    <mesh ref={el => { boardRef.current = el; timerBoardRef.current = el }} position={[0, 2.0, -1.5]}>
      <planeGeometry args={[1.1, 0.55]} />
      <meshBasicMaterial map={texture} transparent side={2} depthWrite={false} />
    </mesh>
  )
}
