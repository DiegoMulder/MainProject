using UnityEngine;
namespace SurvivalFP
{
    [CreateAssetMenu(menuName="Survival FP/PSX Visual Profile")]
    public sealed class PsxVisualProfile : ScriptableObject
    {
        public bool effectEnabled=true;
        [Range(120,720)] public int internalHeight=360;
        [Range(4,64)] public int colourLevels=32;
        [Range(0,1)] public float dithering=.35f;
        public Shader presentationShader;
    }
}
