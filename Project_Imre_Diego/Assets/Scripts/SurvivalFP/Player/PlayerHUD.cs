using UnityEngine;
using UnityEngine.UI;

namespace SurvivalFP
{
    public sealed class PlayerHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerMovement movement;
        [SerializeField] PlayerStamina stamina;
        [SerializeField] PlayerController controller;
        [SerializeField] Image staminaFill;
        [SerializeField] Text status;
        [SerializeField] Text staminaText;
        [SerializeField] Text cursorHint;
        [SerializeField] PlayerInventory inventory;
        [SerializeField] Text inventoryText;
        [Header("Test course recovery")]
        [SerializeField] Vector3 spawnPosition = new Vector3(0f, 0.1f, -14f);
        [SerializeField] float resetBelowY = -12f;
        readonly Color rested = new Color(0.32f, 0.86f, 0.72f);
        readonly Color exhausted = new Color(1f, 0.46f, 0.28f);

        void Update()
        {
            staminaFill.fillAmount = stamina.Normalized;
            staminaFill.color = Color.Lerp(exhausted, rested, Mathf.InverseLerp(0.1f, 0.45f, stamina.Normalized));
            staminaText.text = $"STAMINA   {Mathf.CeilToInt(stamina.Current):000} / {stamina.Maximum:0}";
            var shortcut = inventory ? inventory.GetComponent<PlayerFlashlightShortcut>() : null;
            var heldLight = shortcut ? shortcut.Flashlight : null;
            status.text = $"{movement.State.ToString().ToUpperInvariant()}   /   {movement.ActualSpeed:0.0} m/s\n" +
                (movement.CeilingBlocked ? "LOW CLEARANCE" : "CLEARANCE OK") + "   /   LIGHT " + (heldLight && heldLight.IsOn ? "ON" : "OFF");
            cursorHint.text = controller.Captured ? "ESC  release cursor" : "CLICK TO EXPLORE";
            if (inventoryText && inventory)
            {
                var s = "ITEMS  "; for (int i = 0; i < inventory.Slots.Length; i++) s += $"[{i + 1}] {(inventory.Slots[i] ? inventory.Slots[i].displayName : "EMPTY")}  ";
                inventoryText.text = s + (inventory.Current ? "\nHOLDING  " + inventory.Current.displayName : "\nHOLDING  NONE");
            }
            if (movement.transform.position.y < resetBelowY)
            {
                movement.Teleport(spawnPosition);
                stamina.Restore();
            }
        }
    }
}
