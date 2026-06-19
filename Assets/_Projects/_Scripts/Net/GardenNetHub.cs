using System.Collections.Generic;
using Mirror;
using Plants;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Plants.Net
{
    /// <summary>
    /// The replication pump. Lives on the (persistent) NetworkManager GameObject.
    ///   - HOST: every 1/<see cref="sendRate"/>s, samples every live <see cref="NetPlant"/>
    ///     and broadcasts a <see cref="GardenStateMessage"/> to all clients.
    ///   - CLIENT: <see cref="ApplyState"/> (invoked from the registered message handler)
    ///     reconciles local instances to the snapshot — applying known ids, SPAWNING unknown
    ///     dynamic instances (scatter clones / fruit orbs), and DESPAWNING ones the host dropped.
    ///
    /// The host renders its own authoritative truth, so it never applies snapshots to itself.
    /// Instance poses are sent relative to <see cref="GardenRoot"/>.
    /// </summary>
    public class GardenNetHub : MonoBehaviour
    {
        public static GardenNetHub Instance { get; private set; }

        [Tooltip("Instance poses are sent relative to this transform. If null, found by name at runtime.")]
        public Transform gardenRoot;

        [Tooltip("Name to find the garden root by when not explicitly assigned.")]
        public string gardenRootName = "SceneRoot";

        [Tooltip("Host broadcast rate (snapshots per second).")]
        [Range(1f, 60f)] public float sendRate = 15f;

        [Tooltip("Log a throttled (~1/s) summary of broadcast (host) / apply (client) to help debug the spectator sync.")]
        public bool verboseLogging = true;

        float m_nextSend;
        float m_nextLog;
        readonly List<InstanceState> m_scratch = new List<InstanceState>(64);

        // Client only: id -> GameObject for instances WE spawned to mirror the host, so we can
        // despawn the ones the host drops. Hero bodies are scene-static and never live here.
        readonly Dictionary<ushort, GameObject> m_clientSpawned = new Dictionary<ushort, GameObject>();
        readonly List<ushort> m_despawnScratch = new List<ushort>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        // Register every NetPlant in the freshly-loaded garden scene (incl. inactive hero bodies) so
        // routing no longer depends on a plant being active. Clearing first drops stale entries from
        // a previous scene/restart.
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            NetPlantRegistry.Clear();
            NetPlantRegistry.RescanAndRegisterScene();
            m_clientSpawned.Clear();
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
            if (m_scratch.Count == 0)
            {
                if (verboseLogging && Time.unscaledTime >= m_nextLog)
                {
                    m_nextLog = Time.unscaledTime + 1f;
                    Debug.LogWarning("[GardenNetHub] host: 0 NetPlants registered — nothing to broadcast " +
                                     "(garden content inactive until LOCK IN?).");
                }
                return;
            }

            NetworkServer.SendToAll(new GardenStateMessage { instances = m_scratch.ToArray() });

            if (verboseLogging && Time.unscaledTime >= m_nextLog)
            {
                m_nextLog = Time.unscaledTime + 1f;
                Debug.Log($"[GardenNetHub] host → {m_scratch.Count} instances to {NetworkServer.connections.Count} connection(s).");
            }
        }

        /// <summary>Client message handler: reconcile local instances to the received snapshot.</summary>
        public void ApplyState(GardenStateMessage msg)
        {
            if (NetworkServer.active) return;                // host owns its own truth
            if (msg.instances == null) return;

            var root = GardenRoot;
            int matched = 0, spawned = 0, despawned = 0;

            // Apply known, spawn unknown dynamic instances.
            for (int i = 0; i < msg.instances.Length; i++)
            {
                var s = msg.instances[i];
                if (NetPlantRegistry.TryGet(s.id, out var p) && p != null)
                {
                    p.Apply(in s, root);
                    matched++;
                }
                else if (s.kind != (byte)NetKind.HeroBody)
                {
                    var np = SpawnDynamic(in s, root);
                    if (np != null) { np.Apply(in s, root); spawned++; }
                }
            }

            // Despawn anything we spawned that the host no longer reports.
            m_despawnScratch.Clear();
            foreach (var kv in m_clientSpawned)
            {
                if (!Contains(msg.instances, kv.Key))
                    m_despawnScratch.Add(kv.Key);
            }
            for (int i = 0; i < m_despawnScratch.Count; i++)
            {
                ushort id = m_despawnScratch[i];
                if (m_clientSpawned.TryGetValue(id, out var go) && go != null)
                    Destroy(go);                              // NetPlant.OnDestroy unregisters
                m_clientSpawned.Remove(id);
                despawned++;
            }

            if (verboseLogging && Time.unscaledTime >= m_nextLog)
            {
                m_nextLog = Time.unscaledTime + 1f;
                if (matched == 0 && spawned == 0)
                    Debug.LogWarning($"[GardenNetHub] client received {msg.instances.Length} instances but matched/spawned 0 " +
                                     "(content not enabled yet, or ids don't match the host's).");
                else
                    Debug.Log($"[GardenNetHub] client applied {matched}, spawned {spawned}, despawned {despawned} " +
                              $"(of {msg.instances.Length}, root '{(root != null ? root.name : "null")}').");
            }
        }

        static bool Contains(InstanceState[] arr, ushort id)
        {
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].id == id) return true;
            return false;
        }

        /// <summary>Client: create the local object for an unknown dynamic instance, by kind. Looks up
        /// the owning species' <see cref="Plant"/> (its hero <see cref="NetPlant"/> id) so it can clone
        /// the same scatter source / build the same fruit orb the host used.</summary>
        NetPlant SpawnDynamic(in InstanceState s, Transform root)
        {
            if (!NetPlantRegistry.TryGet(s.speciesId, out var heroNet) || heroNet == null)
            {
                if (verboseLogging)
                    Debug.LogWarning($"[GardenNetHub] client cannot spawn id {s.id}: no species {s.speciesId} hero in scene.");
                return null;
            }
            var plant = heroNet.GetComponent<Plant>();
            if (plant == null) return null;

            GameObject go = null;
            switch ((NetKind)s.kind)
            {
                case NetKind.ScatterClone:
                {
                    var src = plant.ScatterCloneSource;
                    if (src == null) return null;
                    go = Instantiate(src, root);
                    go.name = $"{src.name}_NetClone_{s.id}";
                    go.SetActive(true);
                    break;
                }
                case NetKind.FruitOrb:
                {
                    go = plant.BuildFruitOrb();   // dormant; Apply() sets ripe + pose from the snapshot
                    if (go == null) return null;
                    go.transform.SetParent(root, false);
                    break;
                }
            }
            if (go == null) return null;

            var np = go.GetComponent<NetPlant>();
            if (np == null) np = go.AddComponent<NetPlant>();
            np.Configure(s.id, (NetKind)s.kind, s.speciesId);

            m_clientSpawned[s.id] = go;
            return np;
        }
    }
}
