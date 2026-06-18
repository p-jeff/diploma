# Gsplat Morph — Private Buffer Pooling

**Date:** 2026-06-16
**Goal:** Stop a revealed splat from snapping back to its progress-0 (grey/collapsed) look after
the reveal plays or after a like, and cap the VRAM the morph system burns in the flourished garden.

---

## The bug

A plant would reveal correctly (its `GsplatRevealAnimator.progress` reaching **1** — confirmed in the
Inspector, the field stays at 1), then visually collapse back to its grey progress-0 state shortly
after the reveal finished, or right after the user liked a species. It was **not** a logic reset —
the `progress` value never changed. It was a **GPU-buffer collision**.

## Root cause

`GsplatResourceManager.Get(asset)` hands **every** `GsplatRenderer` that points at the same `.ply`
**one shared** `GsplatResource` — i.e. one shared set of position/scale/rotation/colour GPU buffers
(refcounted per asset). See `GsplatRendererImpl.BindGsplatAsset`.

The morph components (`GsplatRevealAnimator`, `GsplatSplatMorph`) overwrite those buffers **every
frame** in `Update()` via a compute dispatch. So two live renderers of the same species fight over
one buffer and **the last one to run each frame wins**:

- After a reveal, `Plant.ShowAfterAnimation` spawns grey preview copies (`ResetToStart` → progress 0).
  Those copies share the hero's buffer and, while they sit at progress 0, their per-frame dispatch
  (idle sparkle) writes the grey/collapsed state into the shared buffer — dragging the revealed hero
  back to grey.
- Liking spawns *more* progress-0 copies → same thing.

Because all 12 species were wired to share their final `.ply` assets, the hero and its scatter copies
all landed on the same buffer.

## The fix — one invariant + a pool

> **Invariant: a live morph never writes the shared buffer.**
> The shared buffer is read-only "full detail" — only `UploadData` ever writes it, so it always holds
> the progress-1 state. Completed and static renderers read it for free.

Mechanism:

1. **While actively morphing**, a renderer borrows a **private** `GsplatResource` (its own buffers)
   from a pool and writes into that instead, via `GsplatRenderer.SetResourceOverride(...)`.
2. **When it settles at progress 1**, it stops dispatching, returns the private resource to the pool,
   frees its per-instance scratch, and reverts to the shared buffer. The two are identical at t=1
   (the morph output at progress 1 *is* the untouched asset), so the swap is seamless — no pop.
3. Resources are pooled **per asset**, so the cascade of same-species copies recycles buffers instead
   of allocating one per instance. Peak live buffers == max concurrent morphs (hero + live grey
   previews + cascade overlap, ~6–10), far below the total instance count.

This fixes the visual bug (an animating grey copy is on its own private buffer, so it can no longer
overwrite a settled hero) **and** is a perf/VRAM win (settled instances do zero per-frame compute and
hold zero morph buffers).

---

## Files changed

### NEW `Packages/wu.yize.gsplat/Runtime/GsplatMorphBufferPool.cs`
Static pool that lends private `GsplatResource`s keyed by asset instance id.
- `Acquire(asset)` — pops a free resource, or `CreateResource()` + `UploadData()` a fresh one (the
  upload fills SH + a full-detail baseline; the morph overwrites pos/scale/rot/colour each frame).
- `Release(asset, resource)` — pushes it back for reuse.
- `Clear()` — disposes every pooled `GraphicsBuffer`. Hooked to run on play-session start,
  `Application.quitting`, editor assembly reload, and play-mode exit, so nothing leaks.

### `Packages/wu.yize.gsplat/Runtime/GsplatRendererImpl.cs`
- Added `GsplatResource m_sharedResource` — the refcounted shared resource, kept for the renderer's
  whole life. The existing `GsplatResource` field now points either here (static/completed) or at the
  borrowed private resource (while morphing).
- `BindGsplatAsset` sets `GsplatResource = m_sharedResource = GsplatResourceManager.Get(...)`.
- `ReleaseGsplatAsset` nulls `m_sharedResource` too (it does **not** dispose an override — that's
  pool-owned and returned by the morph component).
- **New `SetResourceOverride(GsplatResource)`** — repoints depth-sort/init-order (`GsplatResource`)
  and rendering (re-runs `SetupMaterialPropertyBlock`) at the given resource, or back at the shared
  one when passed null. Does **not** touch the manager refcount.

### `Packages/wu.yize.gsplat/Runtime/GsplatRenderer.cs`
- Public passthroughs `SetResourceOverride(resource)` / `ClearResourceOverride()` (no-op until the
  renderer has bound its asset).

### `Assets/_Projects/_Scripts/Gsplat/GsplatRevealAnimator.cs`
- New state: `m_active`, `m_privateResource`, `m_privateAsset`, `k_settleEps`.
- `Update()` reworked into a state machine:
  - Advance the tween first, then test `settled = !m_playing && progress >= 1 - eps`.
  - **Settled** → if active, `Deactivate()`; **return** (no dispatch; renderer draws the shared buffer).
  - **Active** → wait for the renderer to bind, `Activate()` (borrow private + override), re-assert the
    override defensively, then the existing init/dispatch path (now writing the private buffer).
- `Activate()` borrows a private resource and overrides the renderer.
- `Deactivate()` reverts to shared, returns the private resource to the pool, and `DisposeBuffers()`
  (frees the per-instance src/dst scratch — the big VRAM reclaim). Replaces the old `RestoreHost()`.
- `OnDisable()` calls `Deactivate()` (+ defensive `DisposeBuffers()`).
- Removed `RestoreHost()` — obsolete, since the morph no longer writes the shared buffer.

### `Assets/_Projects/_Scripts/Gsplat/GsplatSplatMorph.cs`
- Same state machine and `Activate`/`Deactivate` as the reveal animator (the two-capture morph shares
  the invariant).
- Source-renderer hiding now tracked by a dedicated `m_sourceHidden` flag (captured once, restored in
  `OnDisable`) so a settle→re-activate cycle doesn't lose the source's original enabled state. The
  coarse source stays hidden at the settled detailed state.

---

## VRAM / perf model

Per-gaussian cost (uncompressed):
- shared output buffer: ~56 B/gaussian, paid **once per asset**.
- morph scratch (src + dst): **~112 B/gaussian per *active* instance**.
- private output buffer: ~56 B/gaussian per *active* instance (+ SH), pooled.

Before: every revealed instance kept its animator enabled → held ~112 B/g scratch **forever**, and
all shared one output buffer (the source of the collision). After: live morph VRAM is bounded by
`poolSize × splatCount × (~168 B/g + SH)`; everything settled is a plain static gsplat with zero
morph footprint, and zero per-frame compute.

---

## Caveats / notes

- **1-frame full-detail flash** is possible the first frame a dormant/grey instance appears: the
  renderer binds (and draws the shared full-detail buffer) on the frame *before* the morph's first
  override takes effect. Hidden for sprouting plants (opacity ~0 at sprout start); cosmetic elsewhere.
- **First reveal per species** pays one `UploadData` (CPU→GPU copy) to fill a pooled buffer; reuse is
  free. If a hitch is noticeable, pre-warm the pool or switch the baseline fill to a GPU
  `CopyBuffer` from the shared resource (SH only is strictly needed).
- **`GsplatSplatMorph`** rebuilds its endpoint scratch (O(n·m) nearest-neighbour) on each
  re-activate. Fine for its one-shot showcase use; would matter only if scrubbed repeatedly.
- The fix is entirely runtime/script — **no prefab, scene, or asset changes**, and no serialized flag.
- `Plant`'s `_GsplatDesat` / `_GsplatOpacityMul` property-block writes survive a resource swap
  (`SetupMaterialPropertyBlock` only sets buffers, never `Clear()`s the block).

## Verification

Scripts recompiled clean in Unity 6000.3.10f1 (0 errors). **Confirmed working in play (2026-06-16):**
revealed plants stay revealed through preview spread and like; no progress-0 snap-back.
