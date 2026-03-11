// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useExperiment, DURATION_S } from './ExperimentContext'
import gazeStore from './gazeStore'
import { triggerIsoSnapshot } from './isoExport'

function fmt(s) {
  const m = Math.floor(s / 60)
  const sec = s % 60
  return `${m}:${String(sec).padStart(2, '0')}`
}

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
  const { phase, remaining, start, end, reset } = useExperiment()

  return (
    <div style={hudStyle}>
      {phase === 'idle' && (
        <>
          <span>Gaze tracking demo — look at garden objects</span>
          <button style={{ ...btnStyle, background: '#2ecc71', color: '#000' }} onClick={start}>
            Start ({DURATION_S}s)
          </button>
        </>
      )}

      {phase === 'active' && (
        <>
          <span style={{ color: '#2ecc71' }}>● Recording</span>
          <span style={{ fontSize: 18 }}>{fmt(remaining)}</span>
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
          <button style={{ ...btnStyle, background: '#888', color: '#fff' }} onClick={reset}>
            Reset
          </button>
        </>
      )}
    </div>
  )
}
