using System.Linq;
using UnityEngine;
namespace SurvivalFP
{
    [RequireComponent(typeof(NetworkPlayer))]
    public sealed class DownedInteractable : MonoBehaviour, IInteractable
    {
        public BoxCollider interactionBody;
        NetworkPlayer target;
        void Awake(){target=GetComponent<NetworkPlayer>();}
        public void SetDowned(bool downed){if(interactionBody)interactionBody.enabled=downed;}
        MedkitItem Kit(PlayerInteraction interaction)=>interaction.Inventory.Slots.Where(i=>i).Select(i=>i.GetComponent<MedkitItem>()).FirstOrDefault(i=>i);
        public string Prompt(PlayerInteraction interaction)=>$"E  Revive {target.DisplayName}"+(Kit(interaction)?" (consume medkit)":" — medkit required");
        public bool CanInteract(PlayerInteraction interaction)
        {
            var helper=interaction.GetComponent<NetworkPlayer>();
            return helper && helper!=target && helper.Alive && target.Life.Value==PlayerLife.Downed && Kit(interaction);
        }
        public void Interact(PlayerInteraction interaction)
        {
            if(!target.IsServer || !CanInteract(interaction))return;
            var kit=Kit(interaction).GetComponent<NetworkPickup>();
            var helper=interaction.GetComponent<NetworkPlayer>();
            if(!kit.IsSpawned || !kit.Location.Value.Held || kit.Location.Value.carrier!=helper.OwnerClientId)return;
            // One atomic server operation. The life transition invalidates competing requests.
            if(target.Revive())kit.NetworkObject.Despawn();
        }
    }
}
