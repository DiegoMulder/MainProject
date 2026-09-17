using UnityEngine;

namespace SurvivalFP
{
    /// Optional compatibility adapter for older pickups. It toggles the flashlight on this item,
    /// never a different flashlight elsewhere in the scene.
    public sealed class FlashlightItemUse : MonoBehaviour, IPrimaryUse
    {
        public void PrimaryUse()
        {
            var flashlight = GetComponent<PlayerFlashlight>();
            if (!flashlight) flashlight = gameObject.AddComponent<PlayerFlashlight>();
            if (flashlight) flashlight.PrimaryUse();
        }
    }
}
