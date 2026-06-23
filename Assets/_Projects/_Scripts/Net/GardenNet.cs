using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Plants.Net
{
    /// <summary>
    /// What kind of live splat object a <see cref="NetPlant"/> represents. The host streams a
    /// snapshot of every live instance; the client recreates unknown ones by kind.
    /// </summary>
    public enum NetKind : byte
    {
        /// <summary>A static authored plant root (toggled active by the experience). Reveal is its
        /// child <c>GsplatRevealAnimator.progress</c>. Never spawned/destroyed at runtime — the
        /// client only toggles it active.</summary>
        HeroBody = 0,

        /// <summary>A runtime <c>Instantiate(scatterer.source)</c> clone (preview / liked spread /
        /// flourish). Reveal is its child <c>GsplatRevealAnimator.progress</c>. Spawned and
        /// despawned on the client to match the host.</summary>
        ScatterClone = 1,

        /// <summary>A runtime <c>ContextFruit</c> canopy orb. Visual state is ripe vs dormant.
        /// Spawned and despawned on the client to match the host.</summary>
        FruitOrb = 2,
    }

    /// <summary>
    /// Authoritative per-instance state replicated host -> spectator clients once per snapshot.
    /// Pose is expressed RELATIVE to the shared garden root, so the spectator view is decoupled
    /// from wherever the headset user anchored the garden in their own room.
    /// </summary>
    public struct InstanceState
    {
        public ushort id;
        public byte kind;          // NetKind
        public ushort speciesId;   // owning hero-plant id (lets the client recreate the right clone/orb)
        public Vector3 localPos;
        public Quaternion localRot;
        public Vector3 localScale;
        public float progress;     // splat kinds: GsplatRevealAnimator.progress, 0..1 (drives the whole reveal)
        public bool ripe;          // FruitOrb: ripe (bright) vs dormant (dim)
        public bool active;
    }

    /// <summary>One snapshot of every live instance, broadcast every tick by the host.
    /// Mirror's weaver auto-generates serialization for the contained struct array.</summary>
    public struct GardenStateMessage : NetworkMessage
    {
        public InstanceState[] instances;
    }

    /// <summary>
    /// Set once in the boot scene (NetworkBootstrap) when this instance starts as a
    /// spectator client — BEFORE the garden scene loads — so scene-load-time code
    /// (e.g. SceneLockController, Plant) can branch on spectator-ness without taking a Mirror
    /// dependency itself.
    /// </summary>
    public static class SpectatorState
    {
        public static bool IsSpectator;

        /// <summary>The single source of truth for "should this instance run as a spectator?".
        /// True if the boot flag was set (NetworkBootstrap started us as a Client) OR this is a
        /// live Mirror client that is not also the server. Branch on THIS (not the raw flag) so
        /// SceneLockController and SpectatorModeController can never disagree — a half-state where
        /// the headset layer is stripped but the calibration box stays up (content hidden).</summary>
        public static bool Active => IsSpectator || (NetworkClient.active && !NetworkServer.active);

        // Reset at the start of every play session, so a stale value (statics persist when the
        // editor's domain reload is disabled, or across a host<->client switch) can't make a
        // non-spectator run skip the scene lock. NetworkBootstrap re-sets it per role.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => IsSpectator = false;
    }

    /// <summary>
    /// Scene-wide lookup of every live <see cref="NetPlant"/> by its stable id. The host samples
    /// this set to build a snapshot; the client uses it to route incoming state back to the
    /// matching local instance.
    ///
    /// Static hero bodies are registered up-front by <see cref="RescanAndRegisterScene"/> (incl.
    /// inactive ones) so an incoming <c>active = true</c> can re-activate a hidden plant — routing
    /// no longer depends on a plant being active (the old OnEnable/OnDisable model dropped inactive
    /// plants out of the table for good). Runtime instances register themselves when configured and
    /// unregister on destroy.
    /// </summary>
    public static class NetPlantRegistry
    {
        static readonly Dictionary<ushort, NetPlant> s_byId = new Dictionary<ushort, NetPlant>();

        // Host-authoritative id allocator for runtime-spawned instances. Hero bodies use authored
        // ids 1..N, so dynamic ids start well above that and only ever come from the host.
        const ushort k_firstDynamicId = 1000;
        static ushort s_nextDynamicId = k_firstDynamicId;

        public static IReadOnlyDictionary<ushort, NetPlant> All => s_byId;

        /// <summary>Host only: allocate the next free id for a runtime-spawned instance.</summary>
        public static ushort NextDynamicId() => s_nextDynamicId++;

        public static void Register(NetPlant p)
        {
            if (p == null || p.id == 0) return;
            if (s_byId.TryGetValue(p.id, out var existing) && existing != p && existing != null)
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

        /// <summary>Clear the table and reset the dynamic-id allocator. Called on garden-scene load
        /// so a restart / scene reload can't leave stale (destroyed) entries behind.</summary>
        public static void Clear()
        {
            s_byId.Clear();
            s_nextDynamicId = k_firstDynamicId;
        }

        /// <summary>Register every <see cref="NetPlant"/> in the loaded scenes, INCLUDING inactive
        /// ones, so hidden hero bodies stay routable and can be re-activated by a snapshot. Safe to
        /// call repeatedly (idempotent). Runs on host and client at garden-scene load.</summary>
        public static void RescanAndRegisterScene()
        {
            var all = Object.FindObjectsByType<NetPlant>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var p in all)
                Register(p);
        }
    }
}
