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
        [Tooltip("PSX vertex precision: geometry snaps to a coarse screen grid (half the internal resolution) and shimmers slightly in motion. PSX Lit materials only.")]
        public bool vertexSnap=true;
        [Tooltip("How much of the snap may move a vertex up or down in the world. 0 keeps floors, table tops and shelves exactly at their height, so the PSX shimmer never lifts a floor over the items resting on it (a snapped floor rose up to ~2 cm at small items). 1 is the classic unconstrained screen snap.")]
        [Range(0,1)] public float verticalSnap=0f;
        public Shader presentationShader;
    }
}
