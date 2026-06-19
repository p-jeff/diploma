# Multiplayer Spectator — Networked Garden View

Status: in progress · Branch: `multiplayer` · Started 2026-06-18

A second machine (a Mac) renders the Gaussian-splat garden **itself** as a live
"audience" view, from its own camera angle, while the Windows PC running the Quest
experience stays the single source of truth. It is a networked **peer renderer**, not
a video stream — the Mac runs the scene locally and is told the state to display.

---

## 1. Goal & roles

| Role | Machine | What it does |
|------|---------|--------------|
| **Host** | Windows PC + Quest via Link | Runs the full interactive experience. **Authoritative** over everything. Broadcasts garden state. Unchanged from before, plus a one-frame boot scene. |
| **Spectator (client)** | Mac (Editor or standalone build) | Runs **zero** game logic. Connects over LAN, receives state, renders the garden from a fixed-angle camera. Strips out all the headset-only machinery. |

The spectator is **decoupled** from the headset: it does *not* inherit the headset
user's head pose or room anchor. It shows the garden at a canonical pose and frames it
with its own camera.

---

## 2. Architecture: host-authoritative per-instance snapshot + spawn/despawn reconcile

```
HOST (Windows)                                 SPECTATOR (Mac)
─────────────                                  ───────────────
ExperienceManager / GardenPlacer / input        (all of the above DISABLED;
        │ drives + SPAWNS instances                Plant/PlantManager passive)
        ▼                                                ▲
   NetPlant.Sample() × every live instance              │ reconcile
   (hero bodies + scatter clones + fruit orbs)          │
        │                                               │  known id → Apply
        ▼                                               │  unknown  → SPAWN by kind
   GardenStateMessage { InstanceState[] }  ──►  GardenNetHub.ApplyState()
   (Mirror, ~15 Hz, KCP/UDP 7777)                       │  gone     → DESPAWN
```

Replication is per **live splat object**, not per authored plant. Each instance carries a
`NetPlant` with an `id`, a `kind` (`HeroBody` / `ScatterClone` / `FruitOrb`), and a
`speciesId` (its owning hero plant). The host streams a full snapshot of all live instances;
the client reconciles — applies known ids, **spawns** unknown dynamic instances by recreating
the same scatter clone / fruit orb the host made, and **despawns** ids the host dropped.

Why snapshot-and-reconcile rather than "just run the same thing"? Because the experience is
**non-deterministic** (`GardenPlacer` random darts + `Physics.ComputePenetration`, random
scatter, random canopy sampling), so two independent runs never produce the same garden. The
host therefore *sends* the authoritative poses; the client never simulates.

Each instance collapses to a tiny payload (`InstanceState`):
- **id / kind / speciesId** — identity + how the client recreates it (descriptor on first sight).
- **Pose** — position + rotation + scale, **relative to `SceneRoot`** so the spectator's
  framing is independent of where the headset anchored the garden.
- **Reveal** — splat kinds: a single float `GsplatRevealAnimator.progress` (0..1); the morph it
  drives is deterministic, so replicating that float reproduces the exact bloom on the Mac's GPU.
- **Ripe** — fruit orbs: a single bool (ripe/dormant).
- **Active** flag.

**Routing no longer depends on a plant being active.** `NetPlantRegistry` is filled at
garden-scene load by a rescan (`RescanAndRegisterScene`, incl. inactive hero bodies) driven
from `GardenNetHub`'s `sceneLoaded` hook, and `NetPlant` no longer unregisters on disable — so
a hidden plant stays routable and an incoming `active = true` can revive it (the old
OnEnable/OnDisable model dropped inactive plants out of the table for good). Runtime instances
register on `Configure()` and unregister on destroy.

---

## 3. Code (`Assets/_Projects/_Scripts/Net/`, namespace `Plants.Net`)

| File | Responsibility |
|------|----------------|
| `GardenNet.cs` | `NetKind` enum, `InstanceState` (the per-instance payload), `GardenStateMessage` (Mirror `NetworkMessage` carrying `InstanceState[]`), `NetPlantRegistry` (id → NetPlant lookup + dynamic-id allocator + `Clear`/`RescanAndRegisterScene`), and `SpectatorState` (a global flag — see §6). |
| `NetPlant.cs` | Identity (`id` + `kind` + `speciesId`) on each live instance. **Sample** (host reads pose + reveal progress *or* fruit ripe + active) / **Apply** (client writes them, across **all** child reveal animators). `Configure()` tags + registers a runtime instance. Registers in OnEnable when id≠0; does **not** unregister on disable (only on destroy). |
| `GardenNetHub.cs` | The pump. On the host, broadcasts a snapshot of every live `NetPlant` ~15×/s. On the client, `ApplyState` reconciles: known id → `Apply`, unknown dynamic id → **spawn** (clone the species' `ScatterCloneSource` / `BuildFruitOrb`), id gone → **despawn**. Rescans+registers the scene's NetPlants on `sceneLoaded`. Host never applies to itself. Resolves the garden root by name (`SceneRoot`). |
| `GardenNetworkManager.cs` | `Mirror.NetworkManager` subclass. Registers the `GardenStateMessage` handler when the client starts. |
| `NetworkBootstrap.cs` | Decides role and starts networking. Sets `SpectatorState.IsSpectator`. CLI/UI hooks. |
| `SpectatorModeController.cs` | On the spectator, strips the headset layer (see §5) and brings up the spectator camera. No-op on host. |

Mirror is **96.0.1**, installed at `Assets/Mirror`. It is auto-referenced, so scripts
in `Assembly-CSharp` can `using Mirror;` directly. Transport is `kcp2k.KcpTransport`
(UDP, default port **7777**).

---

## 4. Scene setup

**`Boot.unity`** — Build Settings **index 0** (the app now launches here). Contains one
persistent `NetworkManager` GameObject:
- `GardenNetworkManager` — `offlineScene = Boot`, `onlineScene = VerticalSlice`,
  `autoCreatePlayer = false` (spectators have no player object), `dontDestroyOnLoad`.
- `kcp2k.KcpTransport`
- `GardenNetHub`
- `NetworkBootstrap`

Flow: launch → Boot → `NetworkBootstrap` starts host or client → Mirror loads
`VerticalSlice` as the online scene on both ends.

**`VerticalSlice.unity`**:
- 9 plants under `SceneRoot/Content/` tagged with `NetPlant`, ids **1–9** by sorted
  name (Crocus=1, Date_Palm=2, Fern=3, Hibiscus=4, Lavender=5, Narcissus=6,
  Pear_Tree=7, Poppy=8, Rhododentron=9). `TitleSequence` is **not** a plant and is
  excluded.
- A `SpectatorMode` GameObject with `SpectatorModeController` (see §5).

Both machines load the **same** `VerticalSlice.unity` from git, so `NetPlant.id`s match
automatically.

---

## 5. What the spectator strips — the disable lists

`SpectatorModeController` is **data-driven**. To stop a headset-only thing from running
(or throwing) on the Mac, add it to a list in the Inspector — no code change.

- **`objectsToDisable`** — `List<GameObject>`, fully `SetActive(false)`. Currently:
  `[BuildingBlock] Camera Rig`, `[BuildingBlock] Passthrough`, `LikeRight`, `LikeLeft`,
  `ContextLeft`, `ContextRight`, `HandProximity`, `[Hand Ready Cue]`.
- **`componentsToDisable`** — `List<Behaviour>`, `enabled = false` (use when you can't
  deactivate the whole object). Currently: `ExperienceManager`, `TitleSequenceController`.

> **Limitation — `componentsToDisable` cannot stop a destructive `Awake`.** Setting
> `enabled = false` only suppresses `OnEnable`/`Start`/`Update`/coroutines; `Awake` still
> runs on every component of an active GameObject. `TitleSequenceController.Awake` *hides
> the whole garden* (`experienceManager.SetActive(false)` + everything in `hideDuringTitle`)
> and only `RevealGarden()` — reached by **touching the poppy** — brings it back. The Mac
> has no hands, so the touch never comes and the garden never reappears: the spectator sits
> on the intro forever. Listing it in `componentsToDisable` does **not** help (its Awake
> already hid the garden). The real fix is an **in-code spectator guard** at the top of
> `TitleSequenceController.Awake` (`if (Plants.Net.SpectatorState.IsSpectator) { … return; }`,
> the same pattern as `SceneLockController`) that hides the title visuals and leaves the
> garden visible. Keep it in `componentsToDisable` too (belt-and-suspenders), but the guard
> is what fixes it.

The disabling runs in **`Awake` at `[DefaultExecutionOrder(-1000)]`** — i.e. *before*
those objects' own `Awake`/`Update` — so e.g. the Like/Context gestures can't NRE on
null hand anchors. `Start` then repurposes the scene's `SpectatorCamera` to **display 0**
(disabling the `SpectatorCamera` helper that forces display 1 for the host's projector),
tags it `MainCamera`, and adds an `AudioListener`.

> **If you see a red error on the Mac from some other object:** copy the object's name
> from the stack trace and drag that object into `objectsToDisable`. Done.

---

## 6. The Scene-Lock bypass (important)

`SceneLockController` hides its `content` object — which holds the **entire garden,
the ExperienceManager, and all the NetPlants** — until you poke **LOCK IN** on the
headset. The spectator never locks in, so without a bypass the Mac sits forever on the
calibration box and the client's NetPlants never even register.

Fix: `SpectatorState.IsSpectator` (a static set by `NetworkBootstrap` when starting as
client, *before* the garden scene loads, and reset each play session). `SceneLockController.Start`
early-returns into `EnterSpectatorBypass()` on the spectator: hides the calibration
box/chair handle, enables `content`, sets state `Locked`, **no anchor** (uses the
default `SceneRoot` pose). The host's lock flow is untouched.

---

## 7. How to run / test

### Prerequisites
- Both machines on the **same subnet**. The PC has two NICs — Ethernet `192.168.0.154`
  and Wi-Fi `10.1.x`. Use the host IP that matches the Mac's network. Avoid managed/guest
  Wi-Fi with client isolation; a simple shared router/switch is most reliable.
- **Windows Firewall**: allow UDP 7777 inbound:
  ```powershell
  New-NetFirewallRule -DisplayName "Mirror KCP 7777" -Direction Inbound -Protocol UDP -LocalPort 7777 -Action Allow
  ```
- The Mac needs the latest branch: **commit + push, then `git pull` on the Mac** after
  any change here (both load the same scenes/scripts).

### Editor testing (no build needed)
The editor's auto-role only picks "client" in a real macOS **build**
(`#if UNITY_STANDALONE_OSX && !UNITY_EDITOR`), so in the editor set the role by hand on
`NetworkBootstrap`:

1. **Windows editor (host):** open `Boot.unity` → `role = Host` → Play → then **lock in
   on the headset** (the garden + plant broadcasting only switch on after lock-in).
2. **Mac editor (client):** open `Boot.unity` → `role = Client`, `hostAddress = 192.168.0.154`
   → Play.

> Press Play from **Boot.unity** so `NetworkBootstrap` runs — the editor plays whatever
> scene is open, not necessarily build index 0.

### Expected & confirmation
The Mac skips the lock box and shows the garden through the spectator camera; plants sit
in the host's layout, and blooming a plant on the headset reveals it on the Mac. Mac
Console should show:
- `[SceneLock] Spectator bypass — calibration skipped, content enabled.`
- `[SpectatorMode] Disabled N objects + M components (spectator).` / `Spectator camera live.`

### Auto-pause troubleshooting
If Play mode auto-pauses on the client, the Console's **Error Pause** is on and something
threw. Turn off Error Pause, read the first red error, and add the offending object to
`objectsToDisable` (§5).

---

## 8. What syncs / current limitations

**Synced:**
- the 9 static plants' pose + reveal progress + active (incl. staged like-unlocks — the
  active-flag trapdoor is fixed, so hidden plants now revive on the Mac);
- **dynamic instances** — runtime scatter clones (preview spread, liked spread, flourish) and
  canopy fruit orbs, via spawn/despawn-by-id. The whole-roster flourish mirrors.

So the client's `Plant` / `PlantManager` are made **passive** (a `SpectatorState` guard skips
sprout / garden-placement / idle-desat / round flow) so they don't fight the replicated state.

**Not synced (by design / TODO):**
- **Instance fade-out** — `CompleteSpecies` fades ungrown clones out via a shader opacity anim
  that isn't replicated; on the Mac they pop out when the host destroys them (cosmetic).
- **Idle desaturation** — the dormant half-grey overlay (`Plant.Update`) is host-only; the
  reveal animator's own start-state desat carries most of the look on the Mac.
- **Title sequence / intro** — host-only; the spectator skips it and shows the garden.
- **Audio / VO** — host plays its own; not synced. The spectator just has an AudioListener.
- **Gaze / context highlights** — by design the spectator is an *independent* free view
  and does not mirror the visitor's gaze.
- **Spectator camera angle** — currently reuses the scene's existing `SpectatorCamera`
  pose; fixed-angle presets / framing tuning is a later art pass.
- **Connect UI** — role/IP are set on the `NetworkBootstrap` inspector for now; a Mac
  connect screen (host-IP field + Connect button) is planned for the Boot scene.
- **Mac VRAM/perf** — each replicated clone allocates its own morph buffer (as on the host);
  a heavy flourish spawns many clones, so watch frame time on the Mac.

---

## 9. File map

```
Assets/_Projects/_Scripts/Net/
  GardenNet.cs                 PlantState, GardenStateMessage, NetPlantRegistry, SpectatorState
  NetPlant.cs                  per-plant Sample/Apply (pose + reveal progress)
  GardenNetHub.cs              host broadcast (~15 Hz) / client reconcile
  GardenNetworkManager.cs      Mirror NetworkManager subclass
  NetworkBootstrap.cs          role selection + connect + SpectatorState flag
  SpectatorModeController.cs   list-driven headset-layer stripping + spectator camera
Assets/_Projects/_Scripts/SceneLock/
  SceneLockController.cs        + EnterSpectatorBypass() spectator path
Assets/_Projects/_Scenes/
  Boot.unity                    build index 0; NetworkManager GO
  VerticalSlice.unity           plants tagged NetPlant; SpectatorMode object
Assets/Mirror/                  Mirror 96.0.1 (Asset Store import)
```
