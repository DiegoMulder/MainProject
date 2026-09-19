using Unity.Netcode;
namespace SurvivalFP
{
    public static class EnemyTargetRules
    {
        public static bool CanTarget(NetworkPlayer p)=>p&&p.IsSpawned&&p.Life.Value==PlayerLife.Alive&&!p.IsGrabbed&&p.NetworkManager&&p.NetworkManager.ConnectedClients.ContainsKey(p.OwnerClientId);
    }
}
