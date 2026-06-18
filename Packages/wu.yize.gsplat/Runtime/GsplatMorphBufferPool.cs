using System.Collections.Generic;
using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// Lends out private, per-instance <see cref="GsplatResource"/>s (own GPU buffers) so that an
    /// actively-morphing renderer can write its blended position/scale/rotation/colour without
    /// touching the asset's SHARED resource.
    ///
    /// Why this exists: <see cref="GsplatResourceManager"/> hands every renderer of the same asset
    /// ONE shared resource (shared GPU buffers). The morph components
    /// (<c>GsplatRevealAnimator</c> / <c>GsplatSplatMorph</c>) overwrite those buffers every frame,
    /// so two live renderers of the same .ply would fight over one buffer — a grey copy parked at
    /// progress 0 stomps a revealed hero back to grey. The fix is an invariant:
    ///
    ///   * The SHARED resource is read-only "full detail" — only UploadData ever writes it, so it
    ///     always holds the progress-1 state. Completed / static renderers read it for free.
    ///   * An ACTIVELY morphing renderer borrows a PRIVATE resource from this pool and writes into
    ///     that instead (via <c>GsplatRenderer.SetResourceOverride</c>). When it settles at
    ///     progress 1 it stops dispatching and reverts to the shared buffer (the two are identical
    ///     at t=1, so the swap is seamless), and returns the private resource here.
    ///
    /// Resources are pooled per asset (instance id), so the cascade of same-species copies recycles
    /// buffers instead of allocating one per instance. Peak live count == max concurrent morphs,
    /// which is far smaller than the total instance count, keeping the extra VRAM bounded.
    /// </summary>
    public static class GsplatMorphBufferPool
    {
        static readonly Dictionary<int, Stack<GsplatResource>> k_free = new();

        /// <summary>
        /// Borrow a private resource sized/typed for <paramref name="asset"/>. Reuses a previously
        /// released one when available; otherwise creates and uploads a fresh copy (the upload fills
        /// SH and a full-detail baseline — the morph overwrites pos/scale/rot/colour each frame).
        /// </summary>
        public static GsplatResource Acquire(GsplatAsset asset)
        {
            if (asset == null) return null;

            int key = asset.GetInstanceID();
            if (k_free.TryGetValue(key, out var stack) && stack.Count > 0)
                return stack.Pop();

            var res = asset.CreateResource();
            asset.UploadData(res); // one-time per pooled buffer; reuse skips it (resource.Uploaded)
            return res;
        }

        /// <summary>Return a private resource so a later morph of the same asset can recycle it.</summary>
        public static void Release(GsplatAsset asset, GsplatResource resource)
        {
            if (asset == null || resource == null) return;

            int key = asset.GetInstanceID();
            if (!k_free.TryGetValue(key, out var stack))
            {
                stack = new Stack<GsplatResource>();
                k_free[key] = stack;
            }
            stack.Push(resource);
        }

        /// <summary>Dispose every pooled buffer. Call on teardown to avoid leaking GraphicsBuffers.</summary>
        public static void Clear()
        {
            foreach (var stack in k_free.Values)
                while (stack.Count > 0)
                    stack.Pop()?.Dispose();
            k_free.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookRuntime()
        {
            // Start each play session with an empty pool, and free buffers on quit.
            Clear();
            Application.quitting -= Clear;
            Application.quitting += Clear;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void HookEditor()
        {
            // GraphicsBuffers must be released before a domain reload, and when leaving play mode,
            // so edit-mode preview allocations and runtime allocations don't leak across reloads.
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Clear;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Clear;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode ||
                state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                Clear();
        }
#endif
    }
}
