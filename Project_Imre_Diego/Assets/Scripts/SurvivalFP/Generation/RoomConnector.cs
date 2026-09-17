using UnityEngine;
namespace SurvivalFP
{
    public sealed class RoomConnector : MonoBehaviour
    {
        public string connectorType = "Mansion";
        [Min(1)] public float width = 2.4f;
        public bool allowDoor = true;
        public bool exitEligible = true;
        public bool Compatible(RoomConnector other) => other && connectorType == other.connectorType && Mathf.Abs(width - other.width) < .01f;
    }
}
