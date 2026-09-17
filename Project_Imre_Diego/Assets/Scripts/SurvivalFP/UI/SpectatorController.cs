using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
namespace SurvivalFP
{
    [DefaultExecutionOrder(500)]
    public sealed class SpectatorController : MonoBehaviour
    {
        NetworkPlayer player, target;
        public NetworkPlayer Target=>target;
        void Awake()=>player=GetComponent<NetworkPlayer>();
        void LateUpdate()
        {
            if(!player.IsSpawned || !player.IsOwner || (player.Life.Value!=PlayerLife.Dead && player.Life.Value!=PlayerLife.Escaped)) return;
            var living=NetworkPlayer.Players.Where(p=>p && p.Alive && p!=player).OrderBy(p=>p.OwnerClientId).ToArray();
            if(!target || !target.Alive) target=living.FirstOrDefault();
            if(Keyboard.current!=null && Keyboard.current.tabKey.wasPressedThisFrame && living.Length>1)
                target=living[(System.Array.IndexOf(living,target)+1)%living.Length];
            if(target) player.View.SetPositionAndRotation(target.View.position,target.View.rotation);
        }
    }
}
