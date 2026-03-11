// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { createContext, useContext, useState, useRef, useCallback, useEffect } from 'react'
import gazeStore from './gazeStore'
import { clearAll as clearPaint } from './gazePainter'

export const DURATION_S = 20

const ExperimentContext = createContext(null)

export function ExperimentProvider({ children }) {
  const [phase, setPhase] = useState('idle') // 'idle' | 'active' | 'ended'
  const [elapsed, setElapsed] = useState(0)
  const intervalRef = useRef(null)

  const start = useCallback(() => {
    gazeStore.reset()
    setElapsed(0)
    setPhase('active')
  }, [])

  const end = useCallback(() => {
    setPhase('ended')
    if (intervalRef.current) clearInterval(intervalRef.current)
  }, [])

  const reset = useCallback(() => {
    gazeStore.reset()
    clearPaint()
    setPhase('idle')
    setElapsed(0)
    if (intervalRef.current) clearInterval(intervalRef.current)
  }, [])

  useEffect(() => {
    if (phase === 'active') {
      intervalRef.current = setInterval(() => {
        setElapsed(e => {
          const next = e + 1
          if (next >= DURATION_S) {
            setPhase('ended')
            clearInterval(intervalRef.current)
            return DURATION_S
          }
          return next
        })
      }, 1000)
    }
    return () => { if (intervalRef.current) clearInterval(intervalRef.current) }
  }, [phase])

  return (
    <ExperimentContext.Provider value={{
      phase,
      elapsed,
      remaining: DURATION_S - elapsed,
      start,
      end,
      reset,
    }}>
      {children}
    </ExperimentContext.Provider>
  )
}

export function useExperiment() {
  return useContext(ExperimentContext)
}
