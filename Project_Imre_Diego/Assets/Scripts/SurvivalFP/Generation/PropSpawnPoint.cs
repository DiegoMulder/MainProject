using UnityEngine;
namespace SurvivalFP
{
    public sealed class PropSpawnPoint : MonoBehaviour
    {
        public string category = "Furniture";
        [Range(0,1)] public float chance = 1f;
        public WeightedProp[] variants;
        public bool randomHalfTurn;
    }
}
