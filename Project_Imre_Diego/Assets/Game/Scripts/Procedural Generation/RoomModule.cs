using System;
using UnityEngine;
namespace SurvivalFP
{
    public sealed class RoomModule : MonoBehaviour
    {
        public Vector3 size = new Vector3(10, 3, 10);
        public bool staircase;
        public Vector3 navigationAnchor;
        public Vector3 NavigationPosition=>transform.TransformPoint(navigationAnchor);
        public RoomConnector[] connectors;
        public PropSpawnPoint[] propAnchors;
    }
    [Serializable] public struct WeightedRoom { public RoomModule prefab; [Min(1)] public int weight; }
    [Serializable] public struct WeightedProp { public GameObject prefab; [Min(1)] public int weight; }
}
