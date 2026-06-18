using Mirror;

namespace Plants.Net
{
    /// <summary>
    /// Project NetworkManager. The only behaviour it adds over the stock manager is
    /// registering the spectator's <see cref="GardenStateMessage"/> handler when the
    /// client starts. Scene flow (offlineScene = boot, onlineScene = garden) and
    /// "no auto player" are configured on the component in the boot scene.
    ///
    /// Spectators have no player object — this is a one-way broadcast of garden state
    /// from the authoritative host, so <c>autoCreatePlayer</c> should be disabled and
    /// no <c>playerPrefab</c> assigned.
    /// </summary>
    public class GardenNetworkManager : NetworkManager
    {
        public override void OnStartClient()
        {
            base.OnStartClient();
            NetworkClient.RegisterHandler<GardenStateMessage>(OnGardenState, requireAuthentication: false);
        }

        static void OnGardenState(GardenStateMessage msg)
        {
            if (GardenNetHub.Instance != null)
                GardenNetHub.Instance.ApplyState(msg);
        }
    }
}
