// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useFrame, useThree } from '@react-three/fiber'
import { useRef } from 'react'
import * as THREE from 'three'
import gazeStore from './gazeStore'
// import { paint } from './gazePainter'
import { useExperiment } from './ExperimentContext'

const _raycaster  = new THREE.Raycaster()
const _origin     = new THREE.Vector3()
const _direction  = new THREE.Vector3()
const _posVec     = new THREE.Vector3()

_raycaster.far = 20

function findTrackableId(object) {
  let obj = object
  while (obj) {
    if (obj.userData?.trackableId) return obj.userData.trackableId
    obj = obj.parent
  }
  return null
}

export default function GazeTracker() {
  const { camera, scene } = useThree()
  const { phase } = useExperiment()
  const frameCount = useRef(0)

  useFrame((state, delta) => {
    if (phase !== 'active') return

    const deltaMs = delta * 1000

    _origin.setFromMatrixPosition(camera.matrixWorld)
    _direction.set(0, 0, -1).transformDirection(camera.matrixWorld)
    _raycaster.set(_origin, _direction)

    const hits = _raycaster.intersectObjects(scene.children, true)

    let hitId = null
    for (const hit of hits) {
      hitId = findTrackableId(hit.object)
      if (hitId) {
        break
      }
    }

    if (hitId) gazeStore.addDwell(hitId, deltaMs)

    frameCount.current++
    if (frameCount.current % 6 === 0) {
      camera.getWorldPosition(_posVec)
      gazeStore.addPosition(_posVec.x, _posVec.z, state.clock.elapsedTime)
    }
  })

  return null
}
