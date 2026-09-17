using UnityEngine;
namespace SurvivalFP
{
    public interface IInteractable
    {
        string Prompt(PlayerInteraction player);
        bool CanInteract(PlayerInteraction player);
        void Interact(PlayerInteraction player);
    }
    public interface IPlayerAuthority
    {
        bool Active { get; }
        void SubmitMovement(PlayerCommand command, Transform view);
        void RequestInteract(MonoBehaviour target, Vector3 direction);
        void InventoryAction(int action, int slot = -1);
    }
}
