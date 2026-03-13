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
  a.download = `gaze-data-${Date.now()}.json`
  a.click()
  URL.revokeObjectURL(url)
}

const hudStyle = {
  position: 'fixed',
  top: 16,
  left: '50%',
  transform: 'translateX(-50%)',
  background: 'rgba(0,0,0,0.65)',
  color: '#fff',
  padding: '10px 20px',
  borderRadius: 10,
  fontFamily: 'monospace',
  fontSize: 15,
  display: 'flex',
  gap: 12,
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
    downloadPositionHeatmap()
    setTimeout(() => triggerIsoSnapshot(), 300)
  }, [phase])

  if (phase === 'idle') return null

  return (
    <div style={hudStyle}>
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
          <span>Session complete — heat map visible</span>
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
            Download Isometric View
          </button>
          <button
            style={{ ...btnStyle, background: '#27ae60', color: '#fff' }}
            onClick={downloadPositionHeatmap}
          >
            Download Floor Heatmap
          </button>
          <button style={{ ...btnStyle, background: '#888', color: '#fff' }} onClick={reset}>
            Reset
          </button>
        </>
      )}
    </div>
  )
}
