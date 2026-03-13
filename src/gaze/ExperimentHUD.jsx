// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useEffect } from 'react'
import { useExperiment } from './ExperimentContext'
import gazeStore from './gazeStore'
import { triggerIsoSnapshot } from './isoExport'
import { downloadPositionHeatmap } from './PositionHeatmapFloor'

function downloadJSON(data) {
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  const t = new Date(); const id = `${String(t.getHours()).padStart(2,'0')}${String(t.getMinutes()).padStart(2,'0')}`
  a.download = `data-${id}.json`
  a.click()
  URL.revokeObjectURL(url)
}

const smallBarStyle = {
  position: 'fixed',
  bottom: 24,
  left: '50%',
  transform: 'translateX(-50%)',
  background: 'rgba(0,0,0,0.65)',
  color: '#fff',
  padding: '8px 16px',
  borderRadius: 10,
  fontFamily: 'monospace',
  fontSize: 14,
  display: 'flex',
  gap: 10,
  alignItems: 'center',
  zIndex: 100,
  pointerEvents: 'auto',
}

const btnStyle = {
  padding: '5px 14px',
  borderRadius: 6,
  border: 'none',
  cursor: 'pointer',
  fontFamily: 'monospace',
  fontSize: 14,
  fontWeight: 'bold',
}

export default function ExperimentHUD() {
  const { phase, end, reset } = useExperiment()

  // Auto-download when session ends.
  // Snapshot is deferred by 300ms so R3F has time to render the heat-colored
  // scene (TrackableGroup material effects + background change) before we read pixels.
  useEffect(() => {
    if (phase !== 'ended') return
    downloadJSON(gazeStore.exportJSON())
    setTimeout(() => triggerIsoSnapshot(), 300)
  }, [phase])

  if (phase === 'idle') return null

  return (
    <>
      {/* Big Reset — center of screen */}
      {phase === 'ended' && (
        <button
          onClick={reset}
          style={{ position: 'fixed', top: '50%', left: '50%', transform: 'translate(-50%, -50%)', fontSize: 48, padding: '40px 100px', borderRadius: 16, fontWeight: 'bold', cursor: 'pointer', zIndex: 100 }}
        >
          Reset
        </button>
      )}

      {/* Small bar — bottom */}
      <div style={smallBarStyle}>
        {phase === 'active' && (
          <>
            <span style={{ color: '#2ecc71' }}>● Recording</span>
            <button style={{ ...btnStyle, background: '#e74c3c', color: '#fff' }} onClick={end}>
              End early
            </button>
          </>
        )}

        {phase === 'ended' && (
          <>
            <span>Session complete</span>
            <button
              style={{ ...btnStyle, background: '#3498db', color: '#fff' }}
              onClick={() => downloadJSON(gazeStore.exportJSON())}
            >
              Download JSON
            </button>
            <button
              style={{ ...btnStyle, background: '#8e44ad', color: '#fff' }}
              onClick={triggerIsoSnapshot}
            >
              Download Iso
            </button>
            <button
              style={{ ...btnStyle, background: '#27ae60', color: '#fff' }}
              onClick={downloadPositionHeatmap}
            >
              Download Heatmap
            </button>
          </>
        )}
      </div>
    </>
  )
}
