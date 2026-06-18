# Experience Scene Build — Progress

Plan: C:\Users\lotze\.claude\plans\understand-the-plant-system-fluffy-narwhal.md

| WP | Description | Status |
|---|---|---|
| WP-A | Scripts: ExperienceManager, GazeInstanceTargeter, GsplatInstanceFader, TouchPrompt; edits to Plant.cs/PlantInfo.cs/PlantTouchTrigger.cs | DONE (clean compile) |
| WP-B | Scene skeleton: Experience.unity stripped (PlantManager removed; kept rig, LikeLeft/LikeRight, ContextLeft/Right IndexUp, hearts: RightSmallHeart/LeftSmallHeart, HandProximity, Passthrough, PCM, SpreadCollider) | DONE |
| WP-C | Asset audit: 12 plant prefabs, Fern splat placeholder applied to Fig_Tree/Hibiscus/Narcissus/Pear_Tree | DONE |
| WP-D | Integration: Experience Manager + SFX, 12 plants placed, TouchPrompt TMP, 7 unlockBatches, ScatterBounds boxes added (prefab bounds were null), build settings | DONE |
| WP-V | Full flow verified in play mode: idle→select→grow×2→like→fade-out (opacity fade PASS)→8 likes→flourish; Interaction Prototype regression PASS. Fix applied: Plant.scatterer field was unwired on all 12 scene plants. | DONE |

## Status: COMPLETE (2026-06-10)

## 2026-06-12 adjustments

- **Task 1 — Interaction collider copy:** Added BoxCollider m_Size (0.866, 0.513, 0.958) and m_Center (-0.902, -0.182, -0.098) overrides at prefab level in Fig_Tree.prefab, Hibiscus.prefab, Narcissus.prefab, Pear_Tree.prefab, targeting the base Plant.prefab BoxCollider (fileID 3801930527485883517). Values taken from the user's scene-level Fern instance tuning.
- **Task 2 — Shared SpreadCollider:** Rewired all 12 plant instances' `bounds` field to the single scene SpreadCollider BoxCollider (fileID 1699536236). Removed all 12 ScatterBounds child GOs (36 YAML objects) from Experience.unity and cleared their m_AddedGameObjects entries.
- **Task 3 — Labels/TouchPrompt inactive by default:** Set PlantInfos root GO `m_IsActive: 0` in PlantInfos.prefab; added `info.gameObject.SetActive(true)` at the start of `Plant.Show()` (additive; ResetState already had SetActive(true)); set TouchPrompt GO `m_IsActive: 0` in Experience.unity (TouchPrompt.Show() already calls SetActive(true)).
- **Task 4 — LookAtTarget Camera.main fallback:** Added lazy `ResolveTarget()` method in LookAtTarget.cs that returns the wired `target` if set, else resolves and caches `Camera.main.transform`; updated Start, Update, Snap, and GetDesiredRotation to use it. Existing wired targets are unaffected.

## 2026-06-12 adjustments

- **Poem centering:** `Plant.Show()` now reads `selectionCollider.bounds` and calls `PlantInfo.PositionPoem(bounds.center XZ, bounds.max.y + poemHeightOffset)` to place the poem canvas directly over the plant's visible mass. New serialized field `poemHeightOffset` (default 0.3 m). `PositionPoem()` moves the PlantInfos root transform and snaps its LookAtTarget billboard.
- **Context canvas split (issue 2 root cause):** `contextLabelRoots` list was empty in the serialized prefab — no roots existed; `PlaceContextAt` set positions on nothing and `FadeContext` faded labels that were inactive children of the poem canvas. Fix: rewrote `PlantInfos.prefab` to add two new world-space Canvas root GOs (`ContextLabelRoot0/1`), each with its own `Canvas + CanvasScaler + LookAtTarget`. Context PlantLabel instances moved under those roots. `PlaceContextAt` now calls `root.gameObject.SetActive(true)` before positioning. `HideContext` deactivates roots. `contextLabelRoots` is now wired in PlantInfo.
- **Gesture gating (issue 3):** `likeEnableDelay` default changed to 0 (both context + like gestures available immediately after reveal). Added `unlockAfterPoem` bool (default true): batch-unlock (first-touch and species-completion) now waits for reveal animation + poem audio to finish before activating the next batch of plants (`UnlockAfterPoemRoutine` coroutine). Field is serialized for tuning.
- **Gaze highlight + debug (issue 4):** `ExperienceManager.Update()` calls `gazeTargeter.TryGetTarget` each frame; on target change, applies `MaterialPropertyBlock` boost (`_GsplatSparkleIntensity` = `gazeHighlightSparkle` 0.8, `_GsplatDesat` reduced by `gazeHighlightDesat` 0.15) and restores previous values on clear. `gazeDebug` bool: `Debug.DrawLine` red to all candidates / green to target + `Debug.Log` on target change. Exposed `GazeInstanceTargeter.Head` property.

- **Internal centering pass:** Zeroed X and Z of Gaussian splat child localPosition, Collider child GO localPosition, BoxCollider m_Center, and PlantInfos localPosition in base Plant.prefab (6 values). Zeroed m_Center.x/z overrides in Fig_Tree, Hibiscus, Narcissus, Pear_Tree variant prefabs (8 values). In Experience.unity zeroed m_Center.x/z on the Poppy scene instance and the Fern scene instance (4 values). m_Center.y and all m_Size values preserved throughout. Garden-ring root positions and rotations untouched.

## 2026-06-12 adjustments (context-root expansion)

- **Roots added:** PlantInfos.prefab extended from 2 to 4 ContextLabelRoots (ContextLabelRoot2/3, fileIDs 9100000000000000020/30). Each root gets full GO + RectTransform + Canvas + CanvasScaler + GraphicRaycaster + LookAtTarget + PlantLabel PrefabInstance (PI fileIDs 9100000000000000040/50). PlantInfo.contextLabelRoots and contextLabels lists wired to all 4 in order. Both new roots m_IsActive: 0.
- **Runtime safety:** PlantInfo.SetData() now deactivates all contextLabelRoots after assigning content, so species with <4 contexts never show empty labels 3/4.
- **Stale refs audit:** All 12 variant prefabs checked — all m_Modifications fileIDs map to valid PlantInfos/PlantLabel components. No stale refs found; Hemp and others appear already clean from prior restructure.
- **Anchor wiring:** CenterEyeAnchor Transform (fileID 166075781) wired into LookAtTarget.target for all 5 LookAtTargets per plant (PlantInfos root + ContextRoot 0–3) across all 12 scene plant instances = 60 target modifications added. TouchPrompt LookAtTarget was already wired. Modification fileIDs computed via: variantPrefabInstanceID XOR plantInfosPrefabInstanceID_in_Plant XOR componentFileID_in_PlantInfos.

Remaining design/tuning work (not blockers):
- Poem/TouchPrompt world placement may sit too close to the face in headset — needs an on-device feel pass.
- Gaze cone defaults (25 deg / 4 m / 0.3 m height offset) on GazeInstanceTargeter need headset tuning.
- selectedSfx / likedSfx on ExperienceManager are empty scaffolding slots; 9 species lack poem audio (silent by design).
- Fern splat is placeholder for Fig_Tree, Hibiscus, Narcissus, Pear_Tree.
- Unused idea parked: end poem from user choices (ExperienceManager liked list + Plant.m_grown keep the data available).
