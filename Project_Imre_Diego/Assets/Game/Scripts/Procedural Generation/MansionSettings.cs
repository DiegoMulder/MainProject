using UnityEngine;
namespace SurvivalFP
{
    [CreateAssetMenu(menuName = "Survival FP/Mansion Settings")]
    public sealed class MansionSettings : ScriptableObject
    {
        [Header("Generation")]
        public int seed = 12345;
        public bool randomSeed = true;
        [Range(4,180)] public int roomCount = 14;
        [Min(1)] public int attemptsPerRoom = 80;
        [Range(1,5)] public int floorCount=2;
        [Min(3)] public float floorHeight=4;
        public WeightedRoom[] rooms;
        [Header("Network prefabs")]
        public NetworkPlayer player;
        public NetworkPickup flashlight;
        public NetworkPickup objective;
        public NetworkPickup medkit;
        public NetworkPickup walkieTalkie;
        [Min(0)] public int radioCount=4;
        public DifficultyConfig difficultyConfig;
        [Min(0)] public int medkitCount=8;
        public DoorInteractable door;
        public ExitDoor exit;
        public EnemyController enemy;
        [Header("Objectives")]
        [Min(1)] public int objectiveCount = 5;
        public string[] objectiveTypes = { "Seal" };
        public bool preferDifferentRooms = true;
        public bool allowExitInStartingRoom;
        [Header("Presentation")]
        public Material wallMaterial;
    }
}
