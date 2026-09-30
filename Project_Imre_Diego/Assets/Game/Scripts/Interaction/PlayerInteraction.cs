using System;
using System.Collections.Generic;
using UnityEngine;
namespace SurvivalFP
{
    // The single interaction raycast for pickups, doors, closets, revives and the exit.
    [DisallowMultipleComponent, RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerInteraction : MonoBehaviour
    {
        [Min(.1f)] public float distance = 5f;
        public LayerMask layers = ~0;
        IInteractable target;
        MonoBehaviour targetComponent;
        PlayerInventory inventory;
        NetworkPlayer player;
        static readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly List<IInteractable> candidates = new();
        static readonly Comparison<RaycastHit> byDistance = (a, b) => a.distance.CompareTo(b.distance);
        void Awake() { inventory = GetComponent<PlayerInventory>(); player = GetComponent<NetworkPlayer>(); }
        // Only living players interact; downed, dead and spectating players never see a stale target.
        bool Eligible => !player || !player.IsSpawned || player.Alive;
        public IInteractable Target => Eligible ? target : null;
        public MonoBehaviour TargetComponent => Eligible ? targetComponent : null;
        public string CurrentPrompt => Target != null ? Target.Prompt(this) : "";
        // True when pressing interact would do something; false for requirement text such as "Needs a medkit".
        public bool PromptAvailable => Target != null && Target.CanInteract(this);
        public PlayerInventory Inventory => inventory ? inventory : inventory = GetComponent<PlayerInventory>();
        public void Tick(Transform view, bool pressed)
        {
            if (!Eligible) { target = null; targetComponent = null; return; }
            targetComponent = player && player.IsHidden ? player.HiddenCloset : FindTarget(view.position, view.forward);
            target = targetComponent as IInteractable;
            if (!pressed || target == null || !target.CanInteract(this)) return;
            var authority = GetComponent<IPlayerAuthority>();
            if (authority != null && authority.Active) authority.RequestInteract(targetComponent, view.forward);
            else target.Interact(this);
        }
        public MonoBehaviour FindTarget(Vector3 origin, Vector3 direction)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, layers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, 0, count, Comparer<RaycastHit>.Create(byDistance));
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.transform.IsChildOf(transform)) continue;
                // The first solid surface decides: interactables behind walls are never selected.
                hit.collider.GetComponentsInParent(false, candidates);
                foreach (var interactable in candidates)
                    if (interactable is MonoBehaviour behaviour && !string.IsNullOrEmpty(interactable.Prompt(this))) return behaviour;
                break;
            }
            return null;
        }
    }
}
