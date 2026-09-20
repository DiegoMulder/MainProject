using UnityEngine;

namespace SurvivalFP
{
    [DisallowMultipleComponent]
    public sealed class PlayerInventory : MonoBehaviour
    {
        [SerializeField] Transform handAnchor;
        public Transform rightHandAnchor;
        [SerializeField] PlayerCamera playerCamera;
        [SerializeField] float pickupDistance = 5f;
        [Header("Dropping")]
        [Min(.1f)] public float dropForwardDistance=.9f;
        [Min(0)] public float dropForwardImpulse=2.3f;
        [Min(0)] public float dropMinimumClearance=.04f;
        [Min(0)] public float dropCheckPadding=.015f;
        [Min(0)] public float dropVerticalCorrectionLimit=.45f;
        public Vector3 DropVelocity(Transform view)=>view.forward*dropForwardImpulse;
        [SerializeField] PickupItem[] slots = new PickupItem[3];
        [SerializeField] int currentSlot = -1;

        void Awake()
        {
            if (!playerCamera) playerCamera = GetComponentInChildren<PlayerCamera>();
            if (!playerCamera) return;
            // The hand must belong to this player's camera, including on new prefabs.
            if (!handAnchor || handAnchor == transform || handAnchor == playerCamera.transform)
            {
                handAnchor = playerCamera.transform.Find("Item Hand Anchor");
                if (!handAnchor) handAnchor = new GameObject("Item Hand Anchor").transform;
            }
            handAnchor.SetParent(playerCamera.transform, false);
            handAnchor.localPosition = new Vector3(.42f, -.34f, .62f);
            handAnchor.localRotation = Quaternion.identity;
            handAnchor.localScale = Vector3.one;
        }

        public int CurrentSlot => currentSlot;
        public PickupItem Current => currentSlot >= 0 && currentSlot < slots.Length ? slots[currentSlot] : null;
        public PickupItem[] Slots => slots;

        public void Tick(PlayerCommand command, Transform view)
        {
            if (command.Slot1Pressed) Select(0);
            else if (command.Slot2Pressed) Select(1);
            else if (command.Slot3Pressed) Select(2);
            if (Mathf.Abs(command.Scroll) > .01f)
                Select(Mathf.RoundToInt(Mathf.Repeat((currentSlot < 0 ? 0 : currentSlot) + (command.Scroll > 0 ? 1 : -1), slots.Length)));
            // Pickup is deliberately bound to E only. Left click is reserved for
            // the equipped item's primary use and never acts as a pickup shortcut.
            var interaction = GetComponent<PlayerInteraction>();
            if (!interaction) { interaction = gameObject.AddComponent<PlayerInteraction>(); interaction.distance = pickupDistance; }
            if (view) interaction.Tick(view, command.PickupPressed);
            if (command.DropPressed) DropCurrent(view);
        }

        public bool HasSpace => System.Array.IndexOf(slots, null) >= 0;
        public Transform HandAnchor => GetComponent<NetworkPlayer>() is NetworkPlayer p && p.IsSpawned && !p.IsOwner && rightHandAnchor?rightHandAnchor:handAnchor;
        public void UsePrimary() { var authority = GetComponent<IPlayerAuthority>(); if (authority != null && authority.Active) authority.InventoryAction(2); else if (Current) Current.UsePrimary(); }


        public void Select(int index)
        {
            var authority = GetComponent<IPlayerAuthority>();
            if (authority != null && authority.Active) { authority.InventoryAction(0, index); return; }
            ApplySelection(index);
        }
        public void ApplySelection(int index)
        {
            if (index < 0 || index >= slots.Length || index == currentSlot) return;
            if (Current) Current.SetEquipped(false);
            currentSlot = index;
            if (Current)
            {
                Current.SetHeld(HandAnchor,HandAnchor==rightHandAnchor);
            }
        }

        public bool TryAdd(PickupItem item)
        {
            if (!item || item.Held || !handAnchor || !HasSpace) return false;
            var networkItem = item.GetComponent<NetworkPickup>();
            var player = GetComponent<NetworkPlayer>();
            if (networkItem && networkItem.IsSpawned)
                return player && player.IsServer && networkItem.Claim(player);
            int slot = System.Array.IndexOf(slots, null);
            ApplyItem(slot, item);
            return true;
        }
        public void ApplyItem(int slot, PickupItem item)
        {
            if (slot < 0 || slot >= slots.Length || slots[slot] == item) return;
            bool equip = !Current;
            slots[slot] = item;
            if (!item) return;
            item.SetHeld(HandAnchor,HandAnchor==rightHandAnchor);
            if (equip) currentSlot = slot;
            item.SetEquipped(currentSlot == slot);
        }
        public void RemoveItem(PickupItem item)
        {
            int slot = System.Array.IndexOf(slots, item);
            if (slot < 0) return;
            slots[slot] = null;
            if (currentSlot == slot)
            {
                currentSlot = -1;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i]) { currentSlot = i; slots[i].SetHeld(HandAnchor,HandAnchor==rightHandAnchor); break; }
            }
        }
        void DropCurrent(Transform view)
        {
            var authority = GetComponent<IPlayerAuthority>();
            if (authority != null && authority.Active) { authority.InventoryAction(1); return; }
            var item = Current;
            if (!item) return;
            if(!SafeItemDrop.TryFind(item,transform,view,out var position,out var rotation))return;
            item.Drop(position,rotation,DropVelocity(view));
            slots[currentSlot] = null;
            currentSlot = -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i]) { currentSlot = i; slots[i].SetHeld(HandAnchor,HandAnchor==rightHandAnchor); break; }
        }
    }
}
