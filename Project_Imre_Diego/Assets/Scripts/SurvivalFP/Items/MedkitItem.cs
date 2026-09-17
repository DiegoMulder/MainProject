using UnityEngine;
namespace SurvivalFP
{
    // Inventory knows only PickupItem. Revival discovers this optional capability.
    [RequireComponent(typeof(PickupItem),typeof(NetworkPickup))]
    public sealed class MedkitItem : MonoBehaviour { }
}
