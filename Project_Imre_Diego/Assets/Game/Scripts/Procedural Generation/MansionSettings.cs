using UnityEngine;
namespace SurvivalFP
{
    [CreateAssetMenu(menuName = "Survival FP/Mansion Settings")]
    public sealed class MansionSettings : ScriptableObject
    {
        [Header("Generation")]
        public int seed = 12345;
        public bool randomSeed = true;
        [Tooltip("Total rooms in a layout, including the starting room.")]
        [Range(4,180)] public int roomCount = 14;
        [Min(1)] public int attemptsPerRoom = 80;
        [Range(1,5)] public int floorCount=2;
        [Min(3)] public float floorHeight=4;
        [Header("Starting room")]
        [Tooltip("Always placed first, exactly once: the root of the room graph and where players spawn (its Player Start).")]
        public RoomModule startingRoom;
        [Tooltip("Never pick the starting room again from the normal pool, even if it is listed there.")]
        public bool uniqueStartingRoom = true;
        [Header("Normal room pool")]
        [Tooltip("Randomly selected rooms, hallways and staircases that grow outward from the starting room.")]
        public WeightedRoom[] rooms;
        // Placement module index: 0 is the starting room, 1..n are pool entries (rooms[i - 1]).
        // Server and clients share this mapping, so replicated layouts rebuild identically.
        public const int StartModule = 0;
        public RoomModule Module(int index) => index == StartModule ? startingRoom : rooms[index - 1].prefab;
        public int PoolModule(int poolIndex) => poolIndex + 1;
        public bool Selectable(int poolIndex) => rooms[poolIndex].prefab && !(uniqueStartingRoom && rooms[poolIndex].prefab == startingRoom);
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
        [Min(0)] public int enemyCount=1;
        [Header("Objectives")]
        [Min(1)] public int objectiveCount = 5;
        public string[] objectiveTypes = { "Seal" };
        public bool preferDifferentRooms = true;
        public bool allowExitInStartingRoom;
        [Header("Presentation")]
        public Material wallMaterial;
        // Returns the first configuration problem, or null. Called before generation so
        // bad content fails with a readable message instead of a NullReferenceException.
        public string Validate()
        {
            if(!startingRoom)return $"{name}: no Starting Room assigned. Assign the room every layout starts from (for the Mansion: Grand Hall).";
            if(startingRoom.staircase)return $"{name}: the Starting Room '{startingRoom.name}' cannot be a staircase module.";
            if(!startingRoom.playerStart)return $"{name}: the Starting Room '{startingRoom.name}' has no Player Start transform.";
            if(startingRoom.connectors==null || !System.Array.Exists(startingRoom.connectors,c=>c))return $"{name}: the Starting Room '{startingRoom.name}' has no connectors.";
            if(rooms==null || rooms.Length==0)return $"{name} has no rooms in the normal pool.";
            for(int i=0;i<rooms.Length;i++)
            {
                var room=rooms[i].prefab;
                if(!room)return $"{name}: Rooms element {i} has no prefab.";
                if(room.connectors==null || !System.Array.Exists(room.connectors,c=>c))return $"{name}: room '{room.name}' has no connectors.";
            }
            if(!System.Array.Exists(rooms,r=>r.prefab && !r.prefab.staircase && !(uniqueStartingRoom && r.prefab==startingRoom)))return $"{name}: the normal pool needs at least one ordinary (non-staircase) room.";
            if(!player)return $"{name}: assign the Player network prefab.";
            if(!flashlight)return $"{name}: assign the Flashlight network prefab.";
            if(!objective)return $"{name}: assign the Objective network prefab.";
            if(!door)return $"{name}: assign the ordinary Door prefab.";
            if(!exit)return $"{name}: assign the Exit door prefab.";
            if(!wallMaterial)return $"{name}: assign the Wall Material used to close unused doorways.";
            return null;
        }
        // Non-fatal configuration issues, shown in the Inspector and logged once per round.
        public System.Collections.Generic.IEnumerable<string> Warnings()
        {
            if(!startingRoom || rooms==null)yield break;
            int copies=System.Array.FindAll(rooms,r=>r.prefab==startingRoom).Length;
            if(copies>0 && uniqueStartingRoom)yield return $"{name}: the Starting Room '{startingRoom.name}' is also listed in the normal pool; it will never be selected from there (Unique Starting Room).";
        }
        void OnValidate(){foreach(var warning in Warnings())Debug.LogWarning(warning,this);}
    }
}
