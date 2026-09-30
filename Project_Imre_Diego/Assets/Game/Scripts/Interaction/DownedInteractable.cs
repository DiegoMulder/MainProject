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
        bool Eligible(PlayerInteraction interaction)
        {
            var helper=interaction?interaction.GetComponent<NetworkPlayer>():null;
            return target && target.IsSpawned && target.Life.Value==PlayerLife.Downed && target.BleedOutRemaining>0
                && helper && helper!=target && helper.Alive && !helper.IsHidden;
        }
        // No prompt at all unless the target is actually downed and the helper is alive.
        public string Prompt(PlayerInteraction interaction)=>!Eligible(interaction)?"":Kit(interaction)
            ?$"Revive {target.DisplayName}  (uses medkit)":$"Needs a medkit to revive {target.DisplayName}";
        public bool CanInteract(PlayerInteraction interaction)
        {
            var helper=interaction.GetComponent<NetworkPlayer>();
            return Eligible(interaction) && Kit(interaction);
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
