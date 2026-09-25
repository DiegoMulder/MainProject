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
        [Tooltip("Only listed child groups are rolled. Keep walls, doors, lanterns and closets out of this list.")]
        public RandomizedStructure[] randomizedStructures;
    }
    [Serializable] public struct RandomizedStructure
    {
        [Tooltip("An existing child group of this room prefab.")]
        public GameObject target;
        [Range(0, 100)] public float spawnChance;
    }
    [Serializable] public struct WeightedRoom { public RoomModule prefab; [Min(1)] public int weight; }
    [Serializable] public struct WeightedProp { public GameObject prefab; [Min(1)] public int weight; }
}
