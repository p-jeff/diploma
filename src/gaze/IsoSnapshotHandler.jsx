// AIA EAI Hin Nr Claude Sonnet 4.6 v1.0
// R3F component — lives inside <Canvas>, registers the iso snapshot handler.
import { useEffect } from 'react'
import { useThree } from '@react-three/fiber'
import * as THREE from 'three'
import { registerIsoHandler, unregisterIsoHandler } from './isoExport'
import { timerBoardRef } from './GazeTimerBoard'

const W = 1920
const H = 1920

export default function IsoSnapshotHandler() {
  const { gl, scene } = useThree()

  useEffect(() => {
    function doSnapshot() {
      console.log('[IsoSnapshot] rendering…')
      // ── Isometric orthographic camera ──────────────────────────────────
      const aspect = W / H
      const frustum = 7
      const cam = new THREE.OrthographicCamera(
        -frustum * aspect, frustum * aspect,
         frustum,          -frustum,
         0.1, 200
      )
      // Steeper isometric: ~60° elevation, zoomed in on the compact garden
      cam.position.set(8, 16, 6)
      cam.lookAt(-1, 0, -1)
      cam.updateProjectionMatrix()

      // ── Render to off-screen target ────────────────────────────────────
      const rt = new THREE.WebGLRenderTarget(W, H, {
        minFilter: THREE.LinearFilter,
        magFilter: THREE.LinearFilter,
        format: THREE.RGBAFormat,
        type: THREE.UnsignedByteType,
      })

      if (timerBoardRef.current) timerBoardRef.current.visible = false

      // Disable XR so gl.render() uses our isometric camera instead of the headset cameras
      const prevXR = gl.xr.enabled
      gl.xr.enabled = false
      const prevRT = gl.getRenderTarget()
      gl.setRenderTarget(rt)
      gl.render(scene, cam)
      gl.setRenderTarget(prevRT)
      gl.xr.enabled = prevXR

      if (timerBoardRef.current) timerBoardRef.current.visible = true

      // ── Read pixels (WebGL Y is flipped) ──────────────────────────────
      const pixels = new Uint8Array(W * H * 4)
      gl.readRenderTargetPixels(rt, 0, 0, W, H, pixels)
      rt.dispose()

      // ── Flip Y on a 2D canvas ─────────────────────────────────────────
      const out = document.createElement('canvas')
      out.width  = W
      out.height = H
      const ctx  = out.getContext('2d')
      const img  = ctx.createImageData(W, H)
      for (let y = 0; y < H; y++) {
        const srcRow = (H - 1 - y) * W * 4
        const dstRow = y * W * 4
        img.data.set(pixels.subarray(srcRow, srcRow + W * 4), dstRow)
      }
      ctx.putImageData(img, 0, 0)

      const a   = document.createElement('a')
      a.href     = out.toDataURL('image/png')
      a.download = `gaze-iso-${Date.now()}.png`
      a.click()
    }

    registerIsoHandler(doSnapshot)
    return () => unregisterIsoHandler()
  }, [gl, scene])

  return null
}
