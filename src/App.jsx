// AIA EAI Hin Nr Claude Opus 4.6 v1.0

import { useRef, useEffect } from 'react'
import { Canvas } from '@react-three/fiber'
import { XR, createXRStore, XROrigin, useXR } from '@react-three/xr'
import { GamepadCamera, GamepadStatus } from './GamepadCamera'
import { useExperiment } from './gaze/ExperimentContext'
import GazeGardenScene from './gaze/GazeGardenScene'
import GazeTracker from './gaze/GazeTracker'
import { ExperimentProvider } from './gaze/ExperimentContext'
import ExperimentHUD from './gaze/ExperimentHUD'
import IsoSnapshotHandler from './gaze/IsoSnapshotHandler'
import { GazeReticle, Crosshair } from './GazeReticle'

const store = createXRStore({
  emulate: {
    primaryInputMode: 'hand',
  },
})

function XRSessionWatcher() {
  const session = useXR(s => s.session)
  const { phase, start } = useExperiment()

  useEffect(() => {
    if (session && phase === 'idle') start()
  }, [session])

  return null
}

export default function App() {
  const playerRef = useRef(null)

  return (
    <ExperimentProvider>
      <div style={{ position: 'fixed', bottom: 40, left: '50%', transform: 'translateX(-50%)', display: 'flex', gap: 20, zIndex: 10 }}>
        <button onClick={() => store.enterAR()} style={{ fontSize: 36, padding: '32px 80px', borderRadius: 12, fontWeight: 'bold', cursor: 'pointer' }}>Enter AR</button>
        <button onClick={() => store.enterVR()} style={{ fontSize: 36, padding: '32px 80px', borderRadius: 12, fontWeight: 'bold', cursor: 'pointer' }}>Enter VR</button>
      </div>

      <GamepadStatus />
      <ExperimentHUD />
      <Crosshair />

      <Canvas
        style={{ width: '100vw', height: '100vh' }}
        camera={{ position: [0, 1.6, 0], fov: 75 }}
        shadows
      >
        <XR store={store}>
          <XROrigin ref={playerRef} />

          <GazeGardenScene />
          <GazeTracker />
          <GazeReticle />
          <IsoSnapshotHandler />

          <XRSessionWatcher />
          <GamepadCamera playerRef={playerRef} />
        </XR>
      </Canvas>
    </ExperimentProvider>
  )
}
