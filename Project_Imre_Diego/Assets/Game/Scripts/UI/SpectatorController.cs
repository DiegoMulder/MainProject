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
            if(target)
            {
                Vector3 focus=target.View.position;
                Vector3 direction=(-target.View.forward+Vector3.up*.15f).normalized;
                float distance=2.4f;
                foreach(var hit in Physics.SphereCastAll(focus,.18f,direction,distance,~0,QueryTriggerInteraction.Ignore))
                    if(!hit.transform.IsChildOf(target.transform) && !hit.transform.IsChildOf(player.transform))distance=Mathf.Min(distance,Mathf.Max(.1f,hit.distance-.05f));
                player.View.SetPositionAndRotation(focus+direction*distance,Quaternion.LookRotation(-direction));
                player.View.GetComponent<Camera>().fieldOfView=LocalSettings.Fov;
            }
        }
    }
}
