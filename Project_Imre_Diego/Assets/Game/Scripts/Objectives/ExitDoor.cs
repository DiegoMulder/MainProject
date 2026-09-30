using System.Linq;
using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    public sealed class ExitDoor : DoorInteractable
    {
        public bool searchOtherSlots=true;
        public NetworkVariable<int> Deposited = new(0), Required = new(5);
        public bool Unlocked => Deposited.Value>=Required.Value;
        public event System.Action<NetworkPlayer> PlayerEscaped;
        public override string Prompt(PlayerInteraction player) => Unlocked ? "Escape" :
            Carried(player) ? $"Deposit objective  ({Deposited.Value}/{Required.Value})" : $"Exit locked  —  {Deposited.Value}/{Required.Value} objectives deposited";
        // The prompt is actionable only when escaping or when a needed objective is being carried.
        public override bool CanInteract(PlayerInteraction player) => interactable && (Unlocked || Carried(player));
        PickupItem Carried(PlayerInteraction player)
        {
            if(!player || !RoundManager.Instance)return null;
            var inventory=player.Inventory;
            if(Needed(inventory.Current))return inventory.Current;
            return searchOtherSlots?inventory.Slots.FirstOrDefault(Needed):null;
        }
        // Per-kind counts live on the server; clients use replicated state and the server re-validates.
        bool Needed(PickupItem candidate) => candidate && candidate.TryGetComponent<ObjectiveItem>(out var objective) && !objective.Deposited.Value && !Unlocked
            && (!IsServer || RoundManager.Instance.Needs(objective.Kind.Value.ToString()));
        public override void Interact(PlayerInteraction interaction)
        {
            if(!IsServer || !RoundManager.Instance) return;
            var player=interaction.GetComponent<NetworkPlayer>(); if(!player || !player.Alive) return;
            if(Unlocked)
            {
                SetOpen(true); RoundManager.Instance.ReleaseItems(player);
                player.Life.Value=PlayerLife.Escaped; PlayerEscaped?.Invoke(player); RoundManager.Instance.EvaluateRound(); return;
            }
            var inventory=player.Inventory;
            var item=Carried(interaction);
            if(!item) return;
            var objectiveItem=item.GetComponent<ObjectiveItem>();
            var networkItem=item.GetComponent<NetworkPickup>();
            if(networkItem.Location.Value.carrier!=player.OwnerClientId || !networkItem.Location.Value.Held) return;
            objectiveItem.Deposited.Value=true;
            RoundManager.Instance.Deposit(objectiveItem.Kind.Value.ToString());
            inventory.RemoveItem(item); player.Selection.Value=inventory.CurrentSlot;
            item.GetComponent<NetworkObject>().Despawn(); Deposited.Value++;
            if(Unlocked) SetOpen(true);
        }
    }
}
