// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useRef, useState, useEffect } from 'react'
import { useFrame } from '@react-three/fiber'
import * as THREE from 'three'
import { useExperiment } from './ExperimentContext'
import gazeStore from './gazeStore'
import { getHeatColor } from './colorRamp'
import GazeTimerBoard from './GazeTimerBoard'

// ─── TrackableGroup ────────────────────────────────────────────────────────────
function TrackableGroup({ id, position, rotation, scale, children }) {
  const groupRef = useRef()
  const { phase } = useExperiment()
  const originalMaterials = useRef(new Map())

  useEffect(() => {
    gazeStore.register(id)
  }, [id])

  useEffect(() => {
    if (!groupRef.current) return
    groupRef.current.traverse(obj => {
      if (!obj.isMesh || obj.userData.isBoundingBox) return
      obj.userData.trackableId = id
      if (obj.material && !originalMaterials.current.has(obj.uuid)) {
        originalMaterials.current.set(obj.uuid, obj.material)
      }
    })
  }, [id])

  useEffect(() => {
    if (!groupRef.current) return
    groupRef.current.updateWorldMatrix(true, true)
    const box = new THREE.Box3().setFromObject(groupRef.current)
    if (box.isEmpty()) return

    const size = new THREE.Vector3()
    const center = new THREE.Vector3()
    box.getSize(size)
    box.getCenter(center)
    groupRef.current.worldToLocal(center)

    const geom = new THREE.BoxGeometry(size.x, size.y, size.z)
    const mat = new THREE.MeshBasicMaterial({ color: 0x00ffff, transparent: true, opacity: 0.15, depthWrite: false, wireframe: true })
    const proxy = new THREE.Mesh(geom, mat)
    proxy.position.copy(center)
    proxy.userData.trackableId = id
    proxy.userData.isBoundingBox = true
    groupRef.current.add(proxy)

    return () => {
      if (groupRef.current) groupRef.current.remove(proxy)
      geom.dispose()
      mat.dispose()
    }
  }, [id])

  useEffect(() => {
    if (!groupRef.current) return

    if (phase === 'ended') {
      const tc = new THREE.Color(getHeatColor(gazeStore.getNormalized(id)))
      groupRef.current.traverse(obj => {
        if (!obj.isMesh || !obj.material || obj.userData.isBoundingBox) return
        const mat = obj.material.clone()
        mat.color.copy(tc)
        mat.transparent = false
        mat.opacity = 1
        if (mat.emissiveMap) {
          mat.emissiveIntensity = 4.0
        } else {
          mat.emissive?.copy(tc)
          mat.emissiveIntensity = 0.5
        }
        obj.material = mat
      })
    } else if (phase === 'idle' && originalMaterials.current.size > 0) {
      groupRef.current.traverse(obj => {
        if (obj.isMesh && !obj.userData.isBoundingBox && originalMaterials.current.has(obj.uuid)) {
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
      <planeGeometry args={[50, 50]} />
      <meshStandardMaterial color="#4a7c3f" roughness={1} />
    </mesh>
  )
}

function Sky() {
  return (
    <mesh>
      <sphereGeometry args={[40, 32, 16]} />
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

// Cross-shaped path radiating from the player's center
function CrossPath() {
  const nsStones = [-4.5, -3.6, -2.7, -1.8, -0.9, 0, 0.9, 1.8, 2.7, 3.6, 4.5]
  const ewStones = [-4.5, -3.6, -2.7, -1.8, -0.9, 0.9, 1.8, 2.7, 3.6, 4.5]
  return (
    <group>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.002, 0]}>
        <planeGeometry args={[0.85, 10]} />
        <meshStandardMaterial color="#b0a898" roughness={1} />
      </mesh>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.002, 0]}>
        <planeGeometry args={[10, 0.85]} />
        <meshStandardMaterial color="#b0a898" roughness={1} />
      </mesh>
      {nsStones.map((z, i) => (
        <mesh key={`ns-${i}`} rotation={[-Math.PI / 2, (i * 0.22) - 0.1, 0]} position={[0, 0.012, z]}>
          <circleGeometry args={[0.27, 7]} />
          <meshStandardMaterial color="#8a8070" roughness={0.9} />
        </mesh>
      ))}
      {ewStones.map((x, i) => (
        <mesh key={`ew-${i}`} rotation={[-Math.PI / 2, (i * 0.18) - 0.1, 0]} position={[x, 0.012, 0]}>
          <circleGeometry args={[0.27, 7]} />
          <meshStandardMaterial color="#8a8070" roughness={0.9} />
        </mesh>
      ))}
    </group>
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
    { offset: [0, 0, 0],         color: '#e74c3c' },
    { offset: [0.18, 0, 0.1],    color: '#9b59b6' },
    { offset: [-0.15, 0, 0.12],  color: '#e67e22' },
    { offset: [0.08, 0, -0.16],  color: '#e74c3c' },
    { offset: [-0.2, 0, -0.08],  color: '#f1c40f' },
    { offset: [0.3, 0, -0.05],   color: '#9b59b6' },
    { offset: [-0.05, 0, 0.28],  color: '#e67e22' },
    { offset: [0.22, 0, 0.22],   color: '#e74c3c' },
    { offset: [-0.28, 0, 0.25],  color: '#f1c40f' },
    { offset: [0.35, 0, 0.18],   color: '#3498db' },
    { offset: [-0.1, 0, -0.3],   color: '#e74c3c' },
    { offset: [0.12, 0, 0.35],   color: '#9b59b6' },
  ]
  return (
    <group rotation={rotation}>
      {flowers.map((f, i) => (
        <Flower key={i} position={f.offset} color={f.color} />
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
        <ringGeometry args={[0.8, 1.0, 32]} />
        <meshStandardMaterial color="#8a8070" roughness={0.8} />
      </mesh>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.025, 0]}>
        <circleGeometry args={[0.8, 32]} />
        <meshStandardMaterial color="#4a90c4" transparent opacity={0.75} roughness={0} metalness={0.3} />
      </mesh>
    </>
  )
}

function LanternPost() {
  return (
    <>
      <mesh position={[0, 1.1, 0]} castShadow>
        <cylinderGeometry args={[0.035, 0.05, 2.2, 8]} />
        <meshStandardMaterial color="#4a3728" roughness={0.9} />
      </mesh>
      {/* Lantern housing */}
      <mesh position={[0, 2.35, 0]} castShadow>
        <boxGeometry args={[0.22, 0.28, 0.22]} />
        <meshStandardMaterial color="#3a2a1a" roughness={0.7} transparent opacity={0.55} />
      </mesh>
      {/* Glow orb */}
      <mesh position={[0, 2.35, 0]}>
        <sphereGeometry args={[0.09, 10, 8]} />
        <meshStandardMaterial color="#fff8c0" emissive="#ffe880" emissiveIntensity={3} />
      </mesh>
      {/* Cap */}
      <mesh position={[0, 2.52, 0]} castShadow>
        <coneGeometry args={[0.15, 0.14, 8]} />
        <meshStandardMaterial color="#3a2a1a" roughness={0.8} />
      </mesh>
    </>
  )
}

function BirdBath() {
  return (
    <>
      <mesh position={[0, 0.45, 0]} castShadow>
        <cylinderGeometry args={[0.06, 0.13, 0.9, 12]} />
        <meshStandardMaterial color="#b8a898" roughness={0.9} />
      </mesh>
      <mesh position={[0, 0.92, 0]} castShadow>
        <cylinderGeometry args={[0.38, 0.3, 0.09, 16]} />
        <meshStandardMaterial color="#b8a898" roughness={0.9} />
      </mesh>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.96, 0]}>
        <circleGeometry args={[0.3, 16]} />
        <meshStandardMaterial color="#5a9fd4" transparent opacity={0.85} roughness={0} metalness={0.2} />
      </mesh>
    </>
  )
}

// ─── Main scene ───────────────────────────────────────────────────────────────

export default function GazeGardenScene() {
  const { phase } = useExperiment()
  const isEnded = phase === 'ended'

  return (
    <>
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

      {!isEnded && <fogExp2 attach="fog" color="#87ceeb" density={0.2} />}
      {!isEnded && <Sky />}
      {!isEnded && <Sun />}
      {!isEnded && <CrossPath />}

      <Ground />

      {/* ── Tree border ring — surrounds the entire garden ── */}

      <TrackableGroup id="tree-1"  position={[0.3,  0,  7.3]} >
        <TreeGeometry scale={1.2} />
      </TrackableGroup>
      <TrackableGroup id="tree-2"  position={[3.9,  0,  6.1]} >
        <TreeGeometry scale={1.0} />
      </TrackableGroup>
      <TrackableGroup id="tree-3"  position={[6.4,  0,  3.3]} >
        <TreeGeometry scale={1.15} />
      </TrackableGroup>
      <TrackableGroup id="tree-4"  position={[7.2,  0, -0.4]} >
        <TreeGeometry scale={0.9} />
      </TrackableGroup>
      <TrackableGroup id="tree-5"  position={[6.0,  0, -3.9]} >
        <TreeGeometry scale={1.1} />
      </TrackableGroup>
      <TrackableGroup id="tree-6"  position={[3.4,  0, -6.5]} >
        <TreeGeometry scale={1.0} />
      </TrackableGroup>
      <TrackableGroup id="tree-7"  position={[-0.4, 0, -7.4]} >
        <TreeGeometry scale={1.25} />
      </TrackableGroup>
      <TrackableGroup id="tree-8"  position={[-3.7, 0, -6.2]} >
        <TreeGeometry scale={0.95} />
      </TrackableGroup>
      <TrackableGroup id="tree-9"  position={[-6.3, 0, -3.6]} >
        <TreeGeometry scale={1.1} />
      </TrackableGroup>
      <TrackableGroup id="tree-10" position={[-7.3, 0,  0.3]} >
        <TreeGeometry scale={1.05} />
      </TrackableGroup>
      <TrackableGroup id="tree-11" position={[-6.1, 0,  3.7]} >
        <TreeGeometry scale={1.15} />
      </TrackableGroup>
      <TrackableGroup id="tree-12" position={[-3.4, 0,  6.3]} >
        <TreeGeometry scale={0.9} />
      </TrackableGroup>

      {/* Outer corner trees for denser border */}
      <TrackableGroup id="tree-13" position={[ 6.8, 0,  6.8]} >
        <TreeGeometry scale={1.3} />
      </TrackableGroup>
      <TrackableGroup id="tree-14" position={[ 6.8, 0, -6.8]} >
        <TreeGeometry scale={1.1} />
      </TrackableGroup>
      <TrackableGroup id="tree-15" position={[-6.8, 0, -6.8]} >
        <TreeGeometry scale={1.2} />
      </TrackableGroup>
      <TrackableGroup id="tree-16" position={[-6.8, 0,  6.8]} >
        <TreeGeometry scale={1.05} />
      </TrackableGroup>

      {/* ── Bushes — scattered through the garden interior ── */}

      <TrackableGroup id="bush-1"  position={[ 3.2, 0,  1.3]} scale={1.0}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-2"  position={[-3.2, 0,  1.3]} scale={1.1}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-3"  position={[ 3.2, 0, -1.3]} scale={0.9}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-4"  position={[-3.2, 0, -1.3]} scale={1.0}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-5"  position={[ 1.3, 0,  3.8]} scale={1.1}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-6"  position={[-1.3, 0,  3.8]} scale={0.85}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-7"  position={[ 1.3, 0, -3.8]} scale={1.05}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-8"  position={[-1.3, 0, -3.8]} scale={0.95}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-9"  position={[ 5.2, 0,  0.0]} scale={1.0}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-10" position={[-5.2, 0,  0.0]} scale={1.1}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-11" position={[ 0.0, 0,  5.5]} scale={0.9}>
        <BushGeometry />
      </TrackableGroup>
      <TrackableGroup id="bush-12" position={[ 0.0, 0, -5.5]} scale={1.0}>
        <BushGeometry />
      </TrackableGroup>

      {/* ── Flower beds — blanket the garden floor ── */}

      <TrackableGroup id="flowerbed-1"  position={[ 1.8, 0,  1.6]}>
        <FlowerBedGeometry rotation={[0, 0.0, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-2"  position={[-1.8, 0,  1.6]}>
        <FlowerBedGeometry rotation={[0, 0.5, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-3"  position={[ 1.8, 0, -1.6]}>
        <FlowerBedGeometry rotation={[0, 0.9, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-4"  position={[-1.8, 0, -1.6]}>
        <FlowerBedGeometry rotation={[0, 1.3, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-5"  position={[ 4.2, 0,  2.6]}>
        <FlowerBedGeometry rotation={[0, 0.3, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-6"  position={[-4.2, 0,  2.6]}>
        <FlowerBedGeometry rotation={[0, -0.3, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-7"  position={[ 3.8, 0, -4.2]}>
        <FlowerBedGeometry rotation={[0, 0.7, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-8"  position={[-3.8, 0, -4.2]}>
        <FlowerBedGeometry rotation={[0, -0.7, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-9"  position={[ 2.2, 0,  5.0]}>
        <FlowerBedGeometry rotation={[0, 0.4, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-10" position={[-2.2, 0,  5.0]}>
        <FlowerBedGeometry rotation={[0, -0.4, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-11" position={[ 5.2, 0, -2.2]}>
        <FlowerBedGeometry rotation={[0, 1.1, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-12" position={[-5.2, 0, -2.2]}>
        <FlowerBedGeometry rotation={[0, -1.1, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-13" position={[ 5.0, 0,  4.5]}>
        <FlowerBedGeometry rotation={[0, 0.6, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-14" position={[-5.0, 0,  4.5]}>
        <FlowerBedGeometry rotation={[0, -0.6, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-15" position={[ 5.0, 0, -4.5]}>
        <FlowerBedGeometry rotation={[0, 1.4, 0]} />
      </TrackableGroup>
      <TrackableGroup id="flowerbed-16" position={[-5.0, 0, -4.5]}>
        <FlowerBedGeometry rotation={[0, -1.4, 0]} />
      </TrackableGroup>

      {/* ── Feature elements ── */}

      <TrackableGroup id="bench" position={[3.5, 0, -2.0]} rotation={[0, 0.6, 0]}>
        <BenchGeometry />
      </TrackableGroup>

      <TrackableGroup id="pond" position={[-3.2, 0, 2.5]}>
        <PondGeometry />
      </TrackableGroup>

      <TrackableGroup id="birdbath" position={[2.5, 0, 3.2]}>
        <BirdBath />
      </TrackableGroup>

      <TrackableGroup id="lantern-1" position={[ 1.4, 0,  1.4]}>
        <LanternPost />
      </TrackableGroup>
      <TrackableGroup id="lantern-2" position={[-1.4, 0,  1.4]}>
        <LanternPost />
      </TrackableGroup>
      <TrackableGroup id="lantern-3" position={[ 1.4, 0, -1.4]}>
        <LanternPost />
      </TrackableGroup>
      <TrackableGroup id="lantern-4" position={[-1.4, 0, -1.4]}>
        <LanternPost />
      </TrackableGroup>

      <TrackableGroup id="mushroom-1" position={[ 2.3, 0, -3.6]}>
        <MushroomGeometry />
      </TrackableGroup>
      <TrackableGroup id="mushroom-2" position={[-2.8, 0, -0.9]}>
        <MushroomGeometry />
      </TrackableGroup>
      <TrackableGroup id="mushroom-3" position={[ 4.5, 0,  3.8]}>
        <MushroomGeometry />
      </TrackableGroup>
    </>
  )
}
