// AIA EAI Hin Nr Claude Opus 4.6 v1.0

import { useRef, useState } from 'react'
import { Canvas } from '@react-three/fiber'
import { XR, createXRStore, XROrigin } from '@react-three/xr'
import { GamepadCamera, GamepadStatus } from './GamepadCamera'
import GardenScene from './GardenScene'
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

export default function App() {
  const playerRef = useRef(null)
  const [mode, setMode] = useState('garden') // 'garden' | 'gaze'

  return (
    <ExperimentProvider>
      <div style={{ position: 'fixed', bottom: 24, left: '50%', transform: 'translateX(-50%)', display: 'flex', gap: 12, zIndex: 10 }}>
        <button onClick={() => store.enterAR()}>Enter AR</button>
        <button onClick={() => store.enterVR()}>Enter VR</button>
        <button
          onClick={() => setMode(m => m === 'garden' ? 'gaze' : 'garden')}
          style={{ background: mode === 'gaze' ? '#e74c3c' : '#2ecc71', color: '#fff', border: 'none', padding: '4px 12px', borderRadius: 6, cursor: 'pointer' }}
        >
          {mode === 'gaze' ? 'Exit Gaze Demo' : 'Gaze Demo'}
        </button>
      </div>

      <GamepadStatus />

      {mode === 'gaze' && <ExperimentHUD />}
      {mode === 'gaze' && <Crosshair />}

      <Canvas
        style={{ width: '100vw', height: '100vh' }}
        camera={{ position: [0, 1.6, 0], fov: 75 }}
        shadows
      >
        <XR store={store}>
          <XROrigin ref={playerRef} />

          {mode === 'garden' && <GardenScene />}

          {mode === 'gaze' && (
            <>
              <GazeGardenScene />
              <GazeTracker />
              <GazeReticle />
              <IsoSnapshotHandler />
            </>
          )}

          <GamepadCamera playerRef={playerRef} />
        </XR>
      </Canvas>
    </ExperimentProvider>
  )
}
