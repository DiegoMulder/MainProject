using UnityEngine;

namespace SurvivalFP
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerFlashlightShortcut : MonoBehaviour
    {
        PlayerInventory inventory;

        void Awake() => inventory = GetComponent<PlayerInventory>();

        public PlayerFlashlight Flashlight
        {
            get
            {
                if (!inventory) inventory = GetComponent<PlayerInventory>();
                foreach (var item in inventory.Slots)
                {
                    if (!item || !item.Held) continue;
                    var flashlight = item.GetComponent<PlayerFlashlight>();
                    if (flashlight) return flashlight;
                }
                return null;
            }
        }

        // PlayerController reads F; this component routes it only to an owned item.
        public void Tick(bool pressed)
        {
            if (!pressed) return;
            var flashlight = Flashlight;
            if (flashlight) flashlight.PrimaryUse();
        }
    }
}
