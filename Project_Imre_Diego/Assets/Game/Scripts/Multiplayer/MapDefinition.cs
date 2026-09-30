using UnityEngine;
namespace SurvivalFP
{
    [CreateAssetMenu(menuName="Survival FP/Map Definition")]
    public sealed class MapDefinition:ScriptableObject
    {
        public string displayName;
        public MansionSettings content;
        public Color ambientColor=new(.25f,.3f,.35f);
        [Tooltip("Fog, ambient, grime and post-processing for this map. Optional; without it only Ambient Color is applied.")]
        public MapAtmosphere atmosphere;
    }
}
