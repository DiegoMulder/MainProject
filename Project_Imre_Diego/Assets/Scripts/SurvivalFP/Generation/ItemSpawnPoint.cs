using UnityEngine;
namespace SurvivalFP
{
    public sealed class ItemSpawnPoint : MonoBehaviour
    {
        public Vector3 approachOffset = new Vector3(0, -1.1f, 1f);
        public Vector3 Approach => transform.TransformPoint(approachOffset);
        public int RoomIndex { get; set; }
    }
}
