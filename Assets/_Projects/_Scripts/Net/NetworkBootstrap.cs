using Mirror;
using UnityEngine;

namespace Plants.Net
{
    /// <summary>
    /// Decides this instance's role and starts networking. Lives in the boot scene
    /// on the NetworkManager GameObject.
    ///   - Windows / Android / Editor : HOST  (runs the full interactive experience)
    ///   - macOS standalone           : CLIENT (spectator; connects to hostAddress)
    ///
    /// Command-line overrides (useful for testing two players on one machine):
    ///   -host                 force host
    ///   -client               force client
    ///   -address &lt;ip&gt;     host ip to connect to
    /// The last-used host ip is remembered in PlayerPrefs ("host_ip"), so the
    /// spectator UI only has to set it once.
    /// </summary>
    [RequireComponent(typeof(GardenNetworkManager))]
    public class NetworkBootstrap : MonoBehaviour
    {
        public enum Role { Auto, Host, Client }

        [Tooltip("Auto = host on Windows/Android/Editor, client (spectator) on macOS.")]
        public Role role = Role.Auto;

        [Tooltip("Host IP the spectator connects to. Remembered across runs in PlayerPrefs.")]
        public string hostAddress = "127.0.0.1";

        [Tooltip("Start automatically on launch. Turn off to drive Start from UI buttons.")]
        public bool autoStart = true;

        const string k_prefKey = "host_ip";

        GardenNetworkManager m_nm;

        void Awake()
        {
            m_nm = GetComponent<GardenNetworkManager>();
            if (PlayerPrefs.HasKey(k_prefKey))
                hostAddress = PlayerPrefs.GetString(k_prefKey);
        }

        void Start()
        {
            ParseCommandLine();      // CLI overrides the remembered/inspector address
            if (autoStart)
                StartRole(ResolveRole());
        }

        Role ResolveRole()
        {
            if (role != Role.Auto) return role;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            return Role.Client;
#else
            return Role.Host;
#endif
        }

        public void StartRole(Role r)
        {
            // Tell scene-load-time code (SceneLockController etc.) we're a spectator,
            // before the garden scene is loaded by Mirror.
            SpectatorState.IsSpectator = (r == Role.Client);

            switch (r)
            {
                case Role.Host:
                    m_nm.StartHost();
                    break;
                case Role.Client:
                    m_nm.networkAddress = hostAddress;
                    m_nm.StartClient();
                    break;
            }
        }

        // ---- UI hooks ----

        /// <summary>Bind to a TMP/InputField onEndEdit so the spectator can type the host ip.</summary>
        public void SetHostAddress(string ip)
        {
            hostAddress = ip;
            PlayerPrefs.SetString(k_prefKey, ip);
            PlayerPrefs.Save();
        }

        /// <summary>Bind to a "Connect" button.</summary>
        public void ConnectAsClient() => StartRole(Role.Client);

        /// <summary>Bind to a "Host" button (manual host start when autoStart is off).</summary>
        public void StartAsHost() => StartRole(Role.Host);

        void ParseCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-host":    role = Role.Host;   break;
                    case "-client":  role = Role.Client; break;
                    case "-address": if (i + 1 < args.Length) hostAddress = args[++i]; break;
                }
            }
        }
    }
}
