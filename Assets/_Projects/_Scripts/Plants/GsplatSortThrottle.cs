using UnityEngine;
using Gsplat;

namespace Plants
{
    /// <summary>
    /// Garden-wide GPU depth-sort throttle for gsplat renderers.
    ///
    /// Profiling the flourished garden showed `SortGsplats` eating ~72% of GPU time: every
    /// <see cref="GsplatRenderer"/> defaults to <c>SortMode.Always</c>, so all ~45 bloomed instances
    /// (heroes + scatter clones, each at its own depth → its own sort) re-sort their gaussians EVERY
    /// frame. The package can sort once every N frames instead (<c>SortEveryNFrames</c> +
    /// <c>SortRefreshRate</c>), and still force a re-sort when the camera rotates past
    /// <c>GsplatSettings.CameraRotationRefreshTreshold</c> (10°), so blending stays correct while the
    /// user looks around. The garden is otherwise static after flourish, so per-frame sorting is
    /// wasted GPU.
    ///
    /// <see cref="ExperienceManager"/> sets <see cref="RefreshRate"/> and applies it to the loaded
    /// scene at startup; <see cref="PlantInstanceScatterer"/> applies it to each clone it spawns at
    /// runtime (new renderers wouldn't otherwise pick it up). RefreshRate 0 = leave renderers as
    /// authored (Always); ≥ 1 = sort once every that many frames (2–4 is a good range; 1 == every
    /// frame == no throttle).
    /// </summary>
    public static class GsplatSortThrottle
    {
        /// <summary>Frames between depth-sorts. 0 = disabled (leave renderers on Always).</summary>
        public static uint RefreshRate = 0;

        /// <summary>Apply the current throttle to every <see cref="GsplatRenderer"/> under
        /// <paramref name="root"/> (includes inactive children). No-op when disabled.</summary>
        public static void Apply(GameObject root)
        {
            if (RefreshRate < 1 || root == null) return;
            var rs = root.GetComponentsInChildren<GsplatRenderer>(true);
            for (int i = 0; i < rs.Length; i++) ApplyTo(rs[i]);
        }

        /// <summary>Apply to every <see cref="GsplatRenderer"/> in the loaded scenes (includes
        /// inactive). Call once at startup. No-op when disabled.</summary>
        public static void ApplyToScene()
        {
            if (RefreshRate < 1) return;
            var rs = Object.FindObjectsByType<GsplatRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < rs.Length; i++) ApplyTo(rs[i]);
        }

        static void ApplyTo(GsplatRenderer r)
        {
            if (r == null) return;
            r.SortMode = GsplatRenderer.GsplatSortMode.SortEveryNFrames;
            r.SortRefreshRate = RefreshRate;   // package sorts every SortRefreshRate frames
        }
    }
}
