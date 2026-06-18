# Flow Redesign — Stage 1 (reproduction record)

This records the **Stage 1 flow fix** so it can be re-applied if later stages are
`git stash`-ed away. Stage 1 touches only two scripts:
`ExperienceManager.cs` and `Plant.cs`.

## Why
Liking a plant while its poem was still reading killed the flow: `LikeCommit()`
never stopped the audio, and `LikeSelected()` cancelled the batch-unlock coroutine
(`UnlockAfterPoemRoutine`) that was waiting for the poem to finish — so the next
plants never appeared and the user was stranded on a droning plant.

## What Stage 1 does
- **Progression is like-driven.** Liking calls `UnlockNextBatch()` directly. Removed
  `unlockAfterPoem`, `UnlockAfterPoemRoutine`, `StartUnlockAfterPoem`, the first-touch
  unlock, and the `m_pendingCompletion` plumbing — there is no coroutine left for a
  like to cancel.
- **Like is gated until the poem finishes.** Selector enabling is split: context
  enables after the reveal animation; **like** enables only once
  `AudioSource.isPlaying` is false.
- **Audio stops on like** (`Plant.LikeCommit` now calls `audioSource.Stop()`).
- **Liked plants persist + stay explorable** — no longer `CompleteSpecies`-destroyed
  or hidden; they keep all their instances.

## Re-apply
If the working tree is clean of these changes, re-apply this patch from the repo root:

```sh
git apply <<'PATCH'
diff --git a/Assets/_Projects/_Scripts/Plants/Experience/ExperienceManager.cs b/Assets/_Projects/_Scripts/Plants/Experience/ExperienceManager.cs
index 2ba8b28..94f3c16 100644
--- a/Assets/_Projects/_Scripts/Plants/Experience/ExperienceManager.cs
+++ b/Assets/_Projects/_Scripts/Plants/Experience/ExperienceManager.cs
@@ -57,8 +57,6 @@ namespace Plants
 
         [Header("Timing")]
         [SerializeField, Min(0f)] private float likeEnableDelay = 0f;
-        [Tooltip("When true, batch-unlock (first touch + species completion) waits until the selected plant's poem audio finishes before revealing the next batch.")]
-        [SerializeField] private bool unlockAfterPoem = true;
 
         [Header("Gaze Explode")]
         [Tooltip("How much the gaussians push outward from center when gaze-targeted (0 = off, 1 = 100% size boost, can go higher).")]
@@ -102,14 +100,11 @@ namespace Plants
         // ── Private state ─────────────────────────────────────────────────────────
 
         private Plant m_selected;
-        private Plant m_pendingCompletion;
         private int m_likedCount;
         private int m_unlockedBatches;
         private bool m_flourished;
-        private bool m_firstTouchDone;
 
         private Coroutine m_enableSelectorsRoutine;
-        private Coroutine m_unlockAfterPoemRoutine;
 
         // Gaze explode state
         private GameObject m_gazeHighlightTarget;
@@ -272,20 +267,11 @@ namespace Plants
             if (moment == null) moment = GetComponentInChildren<EnvironmentMoment>();
             if (moment != null) moment.Interrupt();
 
-            // Hide the currently selected (non-liked) plant.
+            // Hide the currently selected (non-liked) plant — drops its grey previews.
+            // Liked plants are never hidden here: they stay in the world and explorable.
             if (m_selected != null && !m_selected.IsLiked)
                 m_selected.Hide();
 
-            // Complete any plant pending completion that is not the incoming selection.
-            bool needsUnlock = false;
-            if (m_pendingCompletion != null && m_pendingCompletion != p)
-            {
-                m_pendingCompletion.CompleteSpecies();
-                onSpeciesCompleted.Invoke();
-                m_pendingCompletion = null;
-                needsUnlock = true;
-            }
-
             p.Show();
 
             // Trigger 180° environment if the plant has one.
@@ -307,20 +293,8 @@ namespace Plants
             PlaySfx(selectedSfx);
             onSpeciesSelected.Invoke();
 
-            // First touch: also triggers a batch unlock.
-            if (!m_firstTouchDone)
-            {
-                m_firstTouchDone = true;
-                needsUnlock = true;
-            }
-
-            if (needsUnlock)
-            {
-                if (unlockAfterPoem)
-                    StartUnlockAfterPoem(p);
-                else
-                    UnlockNextBatch();
-            }
+            // Progression is like-driven now: selecting a plant never unlocks a batch.
+            // The next batch is revealed when the user *likes* a plant (see LikeSelected).
 
             // Re-gate selectors: disable, wait for animation + delay, then enable.
             DisableSelectorsAndCancelTimer();
@@ -397,43 +371,26 @@ namespace Plants
             if (moment == null) moment = GetComponentInChildren<EnvironmentMoment>();
             if (moment != null) moment.Interrupt();
 
-            m_pendingCompletion = m_selected;
+            // The liked plant stays in the world and stays explorable; we just release
+            // it as the active hero. No pending completion — liked plants keep all
+            // their instances (grown context can be recalled later via the gesture).
             m_selected = null;
 
             DisableSelectorsAndCancelTimer();
-            if (m_unlockAfterPoemRoutine != null)
-            {
-                StopCoroutine(m_unlockAfterPoemRoutine);
-                m_unlockAfterPoemRoutine = null;
-            }
             ClearGazeHighlight();
 
             m_likedCount++;
+
+            // Like-driven progression: committing to a plant reveals the next batch.
+            // The flourish ends the experience, so it takes priority over a normal unlock.
             if (m_likedCount >= flourishAfterLikes)
                 StartFlourish();
+            else
+                UnlockNextBatch();
         }
 
         // ── Batch unlocking ───────────────────────────────────────────────────────
 
-        private void StartUnlockAfterPoem(Plant plant)
-        {
-            if (m_unlockAfterPoemRoutine != null) StopCoroutine(m_unlockAfterPoemRoutine);
-            m_unlockAfterPoemRoutine = StartCoroutine(UnlockAfterPoemRoutine(plant));
-        }
-
-        private IEnumerator UnlockAfterPoemRoutine(Plant plant)
-        {
-            // Wait for reveal animation first (so instances exist).
-            yield return new WaitUntil(() => plant == null || plant.ShowAnimationDone);
-
-            // Then wait for poem audio to finish.
-            if (plant != null && plant.AudioSource != null && plant.AudioSource.isPlaying)
-                yield return new WaitWhile(() => plant != null && plant.AudioSource != null && plant.AudioSource.isPlaying);
-
-            UnlockNextBatch();
-            m_unlockAfterPoemRoutine = null;
-        }
-
         private void UnlockNextBatch()
         {
             if (m_unlockedBatches >= unlockBatches.Count) return;
@@ -456,13 +413,6 @@ namespace Plants
         {
             m_flourished = true;
 
-            // Complete any pending completion.
-            if (m_pendingCompletion != null)
-            {
-                m_pendingCompletion.CompleteSpecies();
-                m_pendingCompletion = null;
-            }
-
             // Hide all active, un-liked plants across all unlocked batches.
             for (int b = 0; b < m_unlockedBatches && b < unlockBatches.Count; b++)
             {
@@ -511,7 +461,17 @@ namespace Plants
         {
             yield return new WaitUntil(() => plant == null || plant.ShowAnimationDone);
             if (likeEnableDelay > 0f) yield return new WaitForSeconds(likeEnableDelay);
-            SetSelectorsActive(true);
+
+            // Context can be explored as soon as the reveal animation is done.
+            SetContextSelectorsActive(true);
+
+            // Like is gated until the poem audio has finished, so you can never like a
+            // plant out from under its own poem (that used to cancel the batch unlock
+            // and strand the user on a droning plant).
+            if (plant != null && plant.AudioSource != null && plant.AudioSource.isPlaying)
+                yield return new WaitWhile(() => plant != null && plant.AudioSource != null && plant.AudioSource.isPlaying);
+
+            SetLikeSelectorsActive(true);
             m_enableSelectorsRoutine = null;
         }
 
@@ -526,9 +486,19 @@ namespace Plants
         }
 
         private void SetSelectorsActive(bool active)
+        {
+            SetLikeSelectorsActive(active);
+            SetContextSelectorsActive(active);
+        }
+
+        private void SetLikeSelectorsActive(bool active)
         {
             foreach (var go in likeSelectorObjects)
                 if (go != null) go.SetActive(active);
+        }
+
+        private void SetContextSelectorsActive(bool active)
+        {
             foreach (var go in contextSelectorObjects)
                 if (go != null) go.SetActive(active);
         }
diff --git a/Assets/_Projects/_Scripts/Plants/Plant.cs b/Assets/_Projects/_Scripts/Plants/Plant.cs
index ac1c167..b333264 100644
--- a/Assets/_Projects/_Scripts/Plants/Plant.cs
+++ b/Assets/_Projects/_Scripts/Plants/Plant.cs
@@ -570,7 +570,12 @@ namespace Plants
                 m_showRoutine = null;
             }
 
-            // Fade poem and context labels out; keep info active so it can be reused.
+            // The poem has finished by the time a like is allowed; stop the source
+            // defensively so nothing keeps droning if it somehow hasn't.
+            if (audioSource != null) audioSource.Stop();
+
+            // Fade poem and context labels out; keep info active so it can be reused
+            // (the context gesture can recall the grown labels later).
             if (info != null)
             {
                 info.FadePoem(0f);
PATCH
```

Stages 2 (proximity reveal + gesture recall) and 3 (hero ground glow + gaussian
recolour + mesh-collider-aware scatterer) build on top of this. If they regress,
`git stash` everything, confirm these two files are back to their pre-Stage-1 state,
re-apply the patch above, and resume.
