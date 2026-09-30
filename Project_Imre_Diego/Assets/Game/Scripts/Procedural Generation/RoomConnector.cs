using UnityEngine;
namespace SurvivalFP
{
    public sealed class RoomConnector : MonoBehaviour
    {
        public string connectorType = "Mansion";
        [Tooltip("Clear width of the doorway opening in metres (Mansion: 1.8). Only connectors of equal width and type can join.")]
        [Min(1)] public float width = 1.8f;
        public bool allowDoor = true;
        public bool exitEligible = true;
        public bool Compatible(RoomConnector other) => other && connectorType == other.connectorType && Mathf.Abs(width - other.width) < .01f;
    }
}
