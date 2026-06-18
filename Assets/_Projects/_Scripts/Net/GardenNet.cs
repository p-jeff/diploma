using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Plants.Net
{
    /// <summary>
    /// Authoritative per-plant state replicated host -> spectator clients once per
    /// snapshot. Pose is expressed RELATIVE to the shared garden root, so the
    /// spectator view is decoupled from wherever the headset user anchored the
    /// garden in their own room (the Mac renders the garden at a canonical pose
    /// and frames it with its own camera).
    /// </summary>
    public struct PlantState
    {
        public ushort id;
        public Vector3 localPos;
        public Quaternion localRot;
        public Vector3 localScale;
        public float progress;   // GsplatRevealAnimator.progress, 0..1 (drives the whole reveal)
        public bool active;
    }

    /// <summary>One snapshot of the whole garden, broadcast every tick by the host.
    /// Mirror's weaver auto-generates serialization for the contained struct array.</summary>
    public struct GardenStateMessage : NetworkMessage
    {
        public PlantState[] plants;
    }

    /// <summary>
    /// Set once in the boot scene (NetworkBootstrap) when this instance starts as a
    /// spectator client — BEFORE the garden scene loads — so scene-load-time code
    /// (e.g. SceneLockController) can branch on spectator-ness without taking a Mirror
    /// dependency itself.
    /// </summary>
    public static class SpectatorState
    {
        public static bool IsSpectator;

        // Reset at the start of every play session, so a stale value (statics persist when the
        // editor's domain reload is disabled, or across a host<->client switch) can't make a
        // non-spectator run skip the scene lock. NetworkBootstrap re-sets it per role.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => IsSpectator = false;
    }

    /// <summary>
    /// Scene-wide lookup of every <see cref="NetPlant"/> by its stable id. The host
    /// samples this set to build a snapshot; the client uses it to route incoming
    /// state back to the matching local plant. Host and client agree on ids because
    /// they load the same scene with the same serialized <see cref="NetPlant.id"/>s.
    /// </summary>
    public static class NetPlantRegistry
    {
        static readonly Dictionary<ushort, NetPlant> s_byId = new Dictionary<ushort, NetPlant>();

        public static IReadOnlyDictionary<ushort, NetPlant> All => s_byId;

        public static void Register(NetPlant p)
        {
            if (p == null) return;
            if (s_byId.TryGetValue(p.id, out var existing) && existing != p)
                Debug.LogWarning($"[NetPlant] duplicate id {p.id} on '{p.name}' (already held by '{existing.name}'). " +
                                 "Ids must be unique within the scene.", p);
            s_byId[p.id] = p;
        }

        public static void Unregister(NetPlant p)
        {
            if (p != null && s_byId.TryGetValue(p.id, out var cur) && cur == p)
                s_byId.Remove(p.id);
        }

        public static bool TryGet(ushort id, out NetPlant p) => s_byId.TryGetValue(id, out p);
    }
}
