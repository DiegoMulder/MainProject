using System;
using UnityEngine;
namespace SurvivalFP
{
    [DisallowMultipleComponent, RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerInteraction : MonoBehaviour
    {
        [Min(.1f)] public float distance = 5f;
        public LayerMask layers = ~0;
        public IInteractable Target { get; private set; }
        public string CurrentPrompt => Target != null ? Target.Prompt(this) : "";
        public PlayerInventory Inventory => GetComponent<PlayerInventory>();
        public MonoBehaviour TargetComponent { get; private set; }
        public void Tick(Transform view, bool pressed)
        {
            var player=GetComponent<NetworkPlayer>();
            TargetComponent = player && player.IsHidden ? player.HiddenCloset : FindTarget(view.position, view.forward);
            Target = TargetComponent as IInteractable;
            if (!pressed || Target == null || !Target.CanInteract(this)) return;
            var authority = GetComponent<IPlayerAuthority>();
            if (authority != null && authority.Active) authority.RequestInteract(TargetComponent, view.forward);
            else Target.Interact(this);
        }
        public MonoBehaviour FindTarget(Vector3 origin, Vector3 direction)
        {
            var hits = Physics.RaycastAll(origin, direction, distance, layers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                foreach (var behaviour in hit.collider.GetComponentsInParent<MonoBehaviour>())
                    if (behaviour is IInteractable interactable && !string.IsNullOrEmpty(interactable.Prompt(this))) return behaviour;
                break;
            }
            return null;
        }
    }
}
