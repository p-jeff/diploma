using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Plants.Net
{
    /// <summary>
    /// The replication pump. Lives on the (persistent) NetworkManager GameObject.
    ///   - HOST: every 1/<see cref="sendRate"/>s, samples every <see cref="NetPlant"/>
    ///     and broadcasts a <see cref="GardenStateMessage"/> to all clients.
    ///   - CLIENT: <see cref="ApplyState"/> (invoked from the registered message
    ///     handler) reconciles local plants to the snapshot.
    ///
    /// The host renders its own authoritative truth, so it never applies snapshots
    /// to itself. Plant poses are sent relative to <see cref="GardenRoot"/>.
    /// </summary>
    public class GardenNetHub : MonoBehaviour
    {
        public static GardenNetHub Instance { get; private set; }

        [Tooltip("Plant poses are sent relative to this transform. If null, found by name at runtime.")]
        public Transform gardenRoot;

        [Tooltip("Name to find the garden root by when not explicitly assigned.")]
        public string gardenRootName = "SceneRoot";

        [Tooltip("Host broadcast rate (snapshots per second).")]
        [Range(1f, 60f)] public float sendRate = 15f;

        float m_nextSend;
        readonly List<PlantState> m_scratch = new List<PlantState>(32);

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Lazily-resolved garden root. Re-resolves if the cached one was destroyed
        /// (e.g. after a network scene change).</summary>
        public Transform GardenRoot
        {
            get
            {
                if (gardenRoot == null && !string.IsNullOrEmpty(gardenRootName))
                {
                    var go = GameObject.Find(gardenRootName);
                    if (go != null) gardenRoot = go.transform;
                }
                return gardenRoot;
            }
        }

        void Update()
        {
            if (!NetworkServer.active) return;               // only the host broadcasts
            if (Time.unscaledTime < m_nextSend) return;
            m_nextSend = Time.unscaledTime + 1f / Mathf.Max(1f, sendRate);
            Broadcast();
        }

        void Broadcast()
        {
            var root = GardenRoot;
            m_scratch.Clear();
            foreach (var kv in NetPlantRegistry.All)
            {
                var p = kv.Value;
                if (p != null) m_scratch.Add(p.Sample(root));
            }
            if (m_scratch.Count == 0) return;

            NetworkServer.SendToAll(new GardenStateMessage { plants = m_scratch.ToArray() });
        }

        /// <summary>Client message handler: reconcile local plants to the received snapshot.</summary>
        public void ApplyState(GardenStateMessage msg)
        {
            if (NetworkServer.active) return;                // host owns its own truth
            if (msg.plants == null) return;

            var root = GardenRoot;
            for (int i = 0; i < msg.plants.Length; i++)
            {
                var s = msg.plants[i];
                if (NetPlantRegistry.TryGet(s.id, out var p) && p != null)
                    p.Apply(in s, root);
            }
        }
    }
}
