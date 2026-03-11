// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useRef, useState, useEffect } from 'react'
import { useFrame } from '@react-three/fiber'
import * as THREE from 'three'
import { useExperiment } from './ExperimentContext'
import gazeStore from './gazeStore'
import { getHeatColor } from './colorRamp'
import GazeTimerBoard from './GazeTimerBoard'

// ─── TrackableGroup ────────────────────────────────────────────────────────────
// Tags all child meshes so GazeTracker can identify them.
// Stores original materials on mount so they can be fully restored on reset.
// On experiment end: clones current materials (which may already carry the paint
// emissiveMap) and tints them with the heat color.
function TrackableGroup({ id, position, rotation, scale, children }) {
  const groupRef = useRef()
  const { phase } = useExperiment()
  const originalMaterials = useRef(new Map()) // mesh.uuid → original material (pre-paint)

  useEffect(() => {
    gazeStore.register(id)
  }, [id])

  // Tag meshes + snapshot originals once on mount
  useEffect(() => {
    if (!groupRef.current) return
    groupRef.current.traverse(obj => {
      if (!obj.isMesh) return
      obj.userData.trackableId = id
      if (obj.material && !originalMaterials.current.has(obj.uuid)) {
        originalMaterials.current.set(obj.uuid, obj.material)
      }
    })
  }, [id])

  useEffect(() => {
    if (!groupRef.current) return

    if (phase === 'ended') {
      const tc = new THREE.Color(getHeatColor(gazeStore.getNormalized(id)))
      groupRef.current.traverse(obj => {
        if (!obj.isMesh || !obj.material) return
        // Clone whatever material is currently on the mesh — this preserves the
        // paint emissiveMap if gazePainter already patched it.
        const mat = obj.material.clone()
        mat.color.copy(tc)
        mat.transparent = false
        mat.opacity = 1
        if (mat.emissiveMap) {
          // Painted mesh: crank up intensity so glow punches through the heat tint
          mat.emissiveIntensity = 4.0
        } else {
          // Unpainted mesh: subtle emissive tint so it still reads in the white scene
          mat.emissive?.copy(tc)
          mat.emissiveIntensity = 0.5
        }
        obj.material = mat
      })
    } else if (phase === 'idle' && originalMaterials.current.size > 0) {
      // Restore pristine originals — paint canvas is cleared separately
      groupRef.current.traverse(obj => {
        if (obj.isMesh && originalMaterials.current.has(obj.uuid)) {
          obj.material = originalMaterials.current.get(obj.uuid)
        }
      })
    }
  }, [phase, id])

  return (
    <group ref={groupRef} position={position} rotation={rotation} scale={scale}>
      {children}
    </group>
  )
}

// ─── Geometry components ───────────────────────────────────────────────────────

function Ground() {
  return (
    <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0, 0]} receiveShadow>
      <planeGeometry args={[40, 40]} />
      <meshStandardMaterial color="#4a7c3f" roughness={1} />
    </mesh>
  )
}

function Sky() {
  return (
    <mesh>
      <sphereGeometry args={[30, 32, 16]} />
      <meshBasicMaterial color="#87ceeb" side={1} />
    </mesh>
  )
}

function Sun() {
  return (
    <mesh position={[10, 20, -15]}>
      <sphereGeometry args={[1.2, 16, 16]} />
      <meshBasicMaterial color="#fff8c0" />
    </mesh>
  )
}

function TreeGeometry({ scale = 1 }) {
  return (
    <group scale={scale}>
      <mesh position={[0, 0.7, 0]} castShadow>
        <cylinderGeometry args={[0.12, 0.18, 1.4, 8]} />
        <meshStandardMaterial color="#6b4226" roughness={0.9} />
      </mesh>
      <mesh position={[0, 1.9, 0]} castShadow>
        <coneGeometry args={[0.9, 1.4, 10]} />
        <meshStandardMaterial color="#2d6e2d" roughness={1} />
      </mesh>
      <mesh position={[0, 2.6, 0]} castShadow>
        <coneGeometry args={[0.7, 1.2, 10]} />
        <meshStandardMaterial color="#357a35" roughness={1} />
      </mesh>
      <mesh position={[0, 3.2, 0]} castShadow>
        <coneGeometry args={[0.45, 1.0, 10]} />
        <meshStandardMaterial color="#3d8a3d" roughness={1} />
      </mesh>
    </group>
  )
}

function BushGeometry() {
  return (
    <>
      <mesh position={[0, 0.25, 0]} castShadow>
        <sphereGeometry args={[0.35, 10, 8]} />
        <meshStandardMaterial color="#3a6e2a" roughness={1} />
      </mesh>
      <mesh position={[0.28, 0.2, 0.1]} castShadow>
        <sphereGeometry args={[0.27, 10, 8]} />
        <meshStandardMaterial color="#4a7e38" roughness={1} />
      </mesh>
      <mesh position={[-0.22, 0.18, -0.05]} castShadow>
        <sphereGeometry args={[0.28, 10, 8]} />
        <meshStandardMaterial color="#336222" roughness={1} />
      </mesh>
    </>
  )
}

function Flower({ position, color }) {
  return (
    <group position={position}>
      <mesh position={[0, 0.12, 0]}>
        <cylinderGeometry args={[0.01, 0.01, 0.24, 6]} />
        <meshStandardMaterial color="#4a8a2a" roughness={1} />
      </mesh>
      <mesh position={[0, 0.26, 0]}>
        <sphereGeometry args={[0.055, 8, 8]} />
        <meshStandardMaterial color={color} roughness={0.8} />
      </mesh>
      <mesh position={[0, 0.27, 0]}>
        <sphereGeometry args={[0.025, 6, 6]} />
        <meshStandardMaterial color="#f5d020" roughness={0.7} />
      </mesh>
    </group>
  )
}

function FlowerBedGeometry({ rotation = [0, 0, 0] }) {
  const flowers = [
    { offset: [0, 0, 0],        color: '#e74c3c' },
    { offset: [0.18, 0, 0.1],   color: '#9b59b6' },
    { offset: [-0.15, 0, 0.12], color: '#e67e22' },
    { offset: [0.08, 0, -0.16], color: '#e74c3c' },
    { offset: [-0.2, 0, -0.08], color: '#f1c40f' },
    { offset: [0.3, 0, -0.05],  color: '#9b59b6' },
    { offset: [-0.05, 0, 0.28], color: '#e67e22' },
    { offset: [0.22, 0, 0.22],  color: '#e74c3c' },
  ]
  return (
    <group rotation={rotation}>
      {flowers.map((f, i) => (
        <Flower key={i} position={f.offset} color={f.color} />
      ))}
    </group>
  )
}

function Path() {
  const stones = [-0.5, 0.3, 1.1, 1.9, 2.7]
  return (
    <group>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.002, 0.5]}>
        <planeGeometry args={[0.9, 5]} />
        <meshStandardMaterial color="#b0a898" roughness={1} />
      </mesh>
      {stones.map((z, i) => (
        <mesh key={i} rotation={[-Math.PI / 2, (i % 3) * 0.15 - 0.1, 0]} position={[0, 0.012, z]}>
          <circleGeometry args={[0.28, 7]} />
          <meshStandardMaterial color="#8a8070" roughness={0.9} />
        </mesh>
      ))}
    </group>
  )
}

function BenchGeometry() {
  return (
    <>
      {[-0.08, 0.08].map((z, i) => (
        <mesh key={i} position={[0, 0.5, z]} castShadow receiveShadow>
          <boxGeometry args={[1.2, 0.05, 0.14]} />
          <meshStandardMaterial color="#8b5e3c" roughness={0.8} />
        </mesh>
      ))}
      {[-0.06, 0.06].map((z, i) => (
        <mesh key={i} position={[0, 0.85, -0.22 + z]} castShadow>
          <boxGeometry args={[1.2, 0.05, 0.14]} />
          <meshStandardMaterial color="#8b5e3c" roughness={0.8} />
        </mesh>
      ))}
      {[[-0.52, 0], [0.52, 0]].map(([x], i) => (
        <group key={i} position={[x, 0, 0]}>
          <mesh position={[0, 0.25, 0.1]} castShadow>
            <boxGeometry args={[0.06, 0.5, 0.06]} />
            <meshStandardMaterial color="#5c3d1e" roughness={0.9} />
          </mesh>
          <mesh position={[0, 0.25, -0.1]} castShadow>
            <boxGeometry args={[0.06, 0.5, 0.06]} />
            <meshStandardMaterial color="#5c3d1e" roughness={0.9} />
          </mesh>
          <mesh position={[0, 0.28, 0]} castShadow>
            <boxGeometry args={[0.06, 0.06, 0.45]} />
            <meshStandardMaterial color="#5c3d1e" roughness={0.9} />
          </mesh>
        </group>
      ))}
    </>
  )
}

function MushroomGeometry() {
  const [pressed, setPressed] = useState(false)
  const groupRef = useRef()
  const scaleY = useRef(1)

  useFrame(() => {
    const target = pressed ? 0.4 : 1
    scaleY.current += (target - scaleY.current) * 0.15
    if (groupRef.current) groupRef.current.scale.y = scaleY.current
  })

  return (
    <group
      ref={groupRef}
      onPointerDown={() => setPressed(true)}
      onPointerUp={() => setPressed(false)}
      onPointerLeave={() => setPressed(false)}
    >
      <mesh position={[0, 0.14, 0]} castShadow>
        <cylinderGeometry args={[0.07, 0.09, 0.28, 12]} />
        <meshStandardMaterial color="#ede0c4" roughness={0.9} />
      </mesh>
      <mesh position={[0, 0.32, 0]} castShadow>
        <sphereGeometry args={[0.22, 16, 12, 0, Math.PI * 2, 0, Math.PI * 0.58]} />
        <meshStandardMaterial color="#c0392b" roughness={0.6} />
      </mesh>
      {[[0, 0.46, 0.14], [0.13, 0.5, 0.05], [-0.11, 0.5, 0.08], [0.05, 0.53, -0.1]].map(([x, y, z], i) => (
        <mesh key={i} position={[x, y, z]}>
          <sphereGeometry args={[0.03, 8, 6]} />
          <meshStandardMaterial color="#ffffff" roughness={0.8} />
        </mesh>
      ))}
    </group>
  )
}

function PondGeometry() {
  return (
    <>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.03, 0]}>
        <ringGeometry args={[0.7, 0.85, 32]} />
        <meshStandardMaterial color="#8a8070" roughness={0.8} />
      </mesh>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.025, 0]}>
        <circleGeometry args={[0.7, 32]} />
        <meshStandardMaterial color="#4a90c4" transparent opacity={0.75} roughness={0} metalness={0.3} />
      </mesh>
    </>
  )
}

function Fence() {
  const posts = Array.from({ length: 9 }, (_, i) => (i - 4) * 1.2)
  return (
    <group position={[0, 0, -7]}>
      {posts.map((x, i) => (
        <mesh key={i} position={[x, 0.55, 0]} castShadow>
          <boxGeometry args={[0.1, 1.1, 0.1]} />
          <meshStandardMaterial color="#c4a068" roughness={0.9} />
        </mesh>
      ))}
      <mesh position={[0, 0.8, 0]}>
        <boxGeometry args={[9.6, 0.07, 0.07]} />
        <meshStandardMaterial color="#b89050" roughness={0.9} />
      </mesh>
      <mesh position={[0, 0.45, 0]}>
        <boxGeometry args={[9.6, 0.07, 0.07]} />
        <meshStandardMaterial color="#b89050" roughness={0.9} />
      </mesh>
    </group>
  )
}

// ─── Main scene ───────────────────────────────────────────────────────────────

export default function GazeGardenScene() {
  const { phase } = useExperiment()
  const isEnded = phase === 'ended'

  return (
    <>
      {/* White studio background when ended, sky blue during recording */}
      <color attach="background" args={[isEnded ? '#ffffff' : '#87ceeb']} />

      <ambientLight intensity={isEnded ? 2.0 : 0.6} />

      {!isEnded && (
        <directionalLight
          position={[10, 20, -15]}
          intensity={2.5}
          castShadow
          shadow-mapSize={[2048, 2048]}
          shadow-camera-near={0.5}
          shadow-camera-far={60}
          shadow-camera-left={-15}
          shadow-camera-right={15}
          shadow-camera-top={15}
          shadow-camera-bottom={-15}
        />
      )}

      <GazeTimerBoard />

      {/* Background elements — hidden on reveal */}
      {!isEnded && <fogExp2 attach="fog" color="#87ceeb" density={0.02} />}
      {!isEnded && <Sky />}
      {!isEnded && <Sun />}
      {!isEnded && <Path />}
      {!isEnded && <Fence />}

      <Ground />

      {/* ── Trackable objects — always present ── */}

      <TrackableGroup id="tree-1" position={[-3, 0, -4]}>
        <TreeGeometry scale={1.1} />
      </TrackableGroup>

      <TrackableGroup id="tree-2" position={[3.5, 0, -5]}>
        <TreeGeometry scale={0.9} />
      </TrackableGroup>

      <TrackableGroup id="tree-3" position={[-4.5, 0, -1]}>
        <TreeGeometry scale={1.3} />
      </TrackableGroup>

      <TrackableGroup id="tree-4" position={[4, 0, -2]}>
        <TreeGeometry scale={0.85} />
      </TrackableGroup>

      <TrackableGroup id="tree-5" position={[-2.5, 0, -6.5]}>
        <TreeGeometry scale={1.0} />
      </TrackableGroup>

      <TrackableGroup id="bush-1" position={[-1.5, 0, -3.5]}>
        <BushGeometry />
      </TrackableGroup>

      <TrackableGroup id="bush-2" position={[1.6, 0, -3.8]} scale={0.9}>
        <BushGeometry />
      </TrackableGroup>

      <TrackableGroup id="bush-3" position={[-2, 0, -1]} scale={1.1}>
        <BushGeometry />
      </TrackableGroup>

      <TrackableGroup id="bush-4" position={[3, 0, -0.5]} scale={0.8}>
        <BushGeometry />
      </TrackableGroup>

      <TrackableGroup id="flowerbed-1" position={[-1.1, 0, -1]}>
        <FlowerBedGeometry />
      </TrackableGroup>

      <TrackableGroup id="flowerbed-2" position={[1.1, 0, -1]}>
        <FlowerBedGeometry rotation={[0, 0.3, 0]} />
      </TrackableGroup>

      <TrackableGroup id="flowerbed-3" position={[-1.1, 0, -4]}>
        <FlowerBedGeometry />
      </TrackableGroup>

      <TrackableGroup id="flowerbed-4" position={[1.1, 0, -4]}>
        <FlowerBedGeometry />
      </TrackableGroup>

      <TrackableGroup id="bench" position={[-1.8, 0, -2.5]} rotation={[0, 0.4, 0]}>
        <BenchGeometry />
      </TrackableGroup>

      <TrackableGroup id="mushroom" position={[-1.2, 0, -1.8]}>
        <MushroomGeometry />
      </TrackableGroup>

      <TrackableGroup id="pond" position={[2.5, 0, -2]}>
        <PondGeometry />
      </TrackableGroup>
    </>
  )
}
