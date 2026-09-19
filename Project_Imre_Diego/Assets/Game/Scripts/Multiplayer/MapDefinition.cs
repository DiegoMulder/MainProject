using UnityEngine;
namespace SurvivalFP
{
    [CreateAssetMenu(menuName="Survival FP/Map Definition")]
    public sealed class MapDefinition:ScriptableObject
    {
        public string displayName;
        public MansionSettings content;
        public Color ambientColor=new(.25f,.3f,.35f);
    }
}
