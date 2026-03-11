// AIA EAI Hin Nr Claude Opus 4.6 v1.0
import { useRef, useState } from 'react'
import { useFrame } from '@react-three/fiber'

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

function Tree({ position, scale = 1 }) {
  return (
    <group position={position} scale={scale}>
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

function Bush({ position, scale = 1 }) {
  return (
    <group position={position} scale={scale}>
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
    </group>
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

function FlowerBed({ position, rotation = [0, 0, 0] }) {
  const flowers = [
    { offset: [0, 0, 0], color: '#e74c3c' },
    { offset: [0.18, 0, 0.1], color: '#9b59b6' },
    { offset: [-0.15, 0, 0.12], color: '#e67e22' },
    { offset: [0.08, 0, -0.16], color: '#e74c3c' },
    { offset: [-0.2, 0, -0.08], color: '#f1c40f' },
    { offset: [0.3, 0, -0.05], color: '#9b59b6' },
    { offset: [-0.05, 0, 0.28], color: '#e67e22' },
    { offset: [0.22, 0, 0.22], color: '#e74c3c' },
  ]
  return (
    <group position={position} rotation={rotation}>
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
      {/* Gravel base */}
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.002, 0.5]}>
        <planeGeometry args={[0.9, 5]} />
        <meshStandardMaterial color="#b0a898" roughness={1} />
      </mesh>
      {/* Stepping stones */}
      {stones.map((z, i) => (
        <mesh key={i} rotation={[-Math.PI / 2, (i % 3) * 0.15 - 0.1, 0]} position={[0, 0.012, z]}>
          <circleGeometry args={[0.28, 7]} />
          <meshStandardMaterial color="#8a8070" roughness={0.9} />
        </mesh>
      ))}
    </group>
  )
}

function Bench({ position, rotation = [0, 0, 0] }) {
  return (
    <group position={position} rotation={rotation}>
      {/* Seat slats */}
      {[-0.08, 0.08].map((z, i) => (
        <mesh key={i} position={[0, 0.5, z]} castShadow receiveShadow>
          <boxGeometry args={[1.2, 0.05, 0.14]} />
          <meshStandardMaterial color="#8b5e3c" roughness={0.8} />
        </mesh>
      ))}
      {/* Backrest slats */}
      {[-0.06, 0.06].map((z, i) => (
        <mesh key={i} position={[0, 0.85, -0.22 + z]} castShadow>
          <boxGeometry args={[1.2, 0.05, 0.14]} />
          <meshStandardMaterial color="#8b5e3c" roughness={0.8} />
        </mesh>
      ))}
      {/* Legs */}
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
    </group>
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

function PushMushroom({ position }) {
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
      position={position}
      onPointerDown={() => setPressed(true)}
      onPointerUp={() => setPressed(false)}
      onPointerLeave={() => setPressed(false)}
    >
      {/* Stem */}
      <mesh position={[0, 0.14, 0]} castShadow>
        <cylinderGeometry args={[0.07, 0.09, 0.28, 12]} />
        <meshStandardMaterial color="#ede0c4" roughness={0.9} />
      </mesh>
      {/* Cap */}
      <mesh position={[0, 0.32, 0]} castShadow>
        <sphereGeometry args={[0.22, 16, 12, 0, Math.PI * 2, 0, Math.PI * 0.58]} />
        <meshStandardMaterial color="#c0392b" roughness={0.6} />
      </mesh>
      {/* Spots */}
      {[[0, 0.46, 0.14], [0.13, 0.5, 0.05], [-0.11, 0.5, 0.08], [0.05, 0.53, -0.1]].map(([x, y, z], i) => (
        <mesh key={i} position={[x, y, z]}>
          <sphereGeometry args={[0.03, 8, 6]} />
          <meshStandardMaterial color="#ffffff" roughness={0.8} />
        </mesh>
      ))}
    </group>
  )
}

function Pond() {
  return (
    <group position={[2.5, 0, -2]}>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.03, 0]}>
        <ringGeometry args={[0.7, 0.85, 32]} />
        <meshStandardMaterial color="#8a8070" roughness={0.8} />
      </mesh>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.025, 0]}>
        <circleGeometry args={[0.7, 32]} />
        <meshStandardMaterial color="#4a90c4" transparent opacity={0.75} roughness={0} metalness={0.3} />
      </mesh>
    </group>
  )
}

export default function GardenScene({ playerRef }) {
  return (
    <>
      <ambientLight intensity={0.6} />
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

      <fogExp2 attach="fog" color="#87ceeb" density={0.2} />
      <Sky />
      <Sun />
      <Ground />
      <Path />
      <Fence />
      <Pond />

      <Tree position={[-3, 0, -4]} scale={1.1} />
      <Tree position={[3.5, 0, -5]} scale={0.9} />
      <Tree position={[-4.5, 0, -1]} scale={1.3} />
      <Tree position={[4, 0, -2]} scale={0.85} />
      <Tree position={[-2.5, 0, -6.5]} scale={1.0} />

      <Bush position={[-1.5, 0, -3.5]} />
      <Bush position={[1.6, 0, -3.8]} scale={0.9} />
      <Bush position={[-2, 0, -1]} scale={1.1} />
      <Bush position={[3, 0, -0.5]} scale={0.8} />

      <FlowerBed position={[-1.1, 0, -1]} />
      <FlowerBed position={[1.1, 0, -1]} rotation={[0, 0.3, 0]} />
      <FlowerBed position={[-1.1, 0, -4]} />
      <FlowerBed position={[1.1, 0, -4]} />

      <Bench position={[-1.8, 0, -2.5]} rotation={[0, 0.4, 0]} />
      <PushMushroom position={[-1.2, 0, -1.8]} />
    </>
  )
}
