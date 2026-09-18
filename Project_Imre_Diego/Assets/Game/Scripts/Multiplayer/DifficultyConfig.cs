using System;
using UnityEngine;
namespace SurvivalFP
{
    [Serializable]
    public sealed class DifficultyProfile
    {
        public string name;
        [Min(4)] public int targetRoomCount;
        [Min(1)] public int requiredObjectiveCount;
        [Min(0)] public int enemyCount=1;
        public DifficultyProfile(string name,int rooms,int objectives,int enemies=1){enemyCount=enemies;this.name=name;targetRoomCount=rooms;requiredObjectiveCount=objectives;}
    }
    [CreateAssetMenu(menuName="Survival FP/Difficulty Config")]
    public sealed class DifficultyConfig : ScriptableObject
    {
        public DifficultyProfile[] profiles={new("Easy",24,20),new("Medium",60,100),new("Hard",85,140),new("Extreme",120,200,2)};
        public DifficultyProfile Get(int index)=>profiles[Mathf.Clamp(index,0,profiles.Length-1)];
        public void Apply(MansionSettings target,int index)
        {var p=Get(index);target.enemyCount=Mathf.Max(0,p.enemyCount);target.roomCount=Mathf.Max(4,p.targetRoomCount);target.objectiveCount=Mathf.Max(1,p.requiredObjectiveCount);}
    }
}
