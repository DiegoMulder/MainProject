using System.Collections.Generic;
using UnityEngine;
namespace SurvivalFP
{
    // Remembers the original combined room mesh so the furniture splitter
    // (Survival FP > Rooms > Split Baked Furniture) can be rerun after the art changes.
    [DisallowMultipleComponent]
    public sealed class RoomVisualSource : MonoBehaviour
    {
        [Tooltip("The unsplit mesh from the room FBX. The splitter always starts from this.")]
        public Mesh sourceMesh;
        [Tooltip("Which Mansion materials this room uses (wallpaper, panelling and floor variants). Applied by the splitter.")]
        public RoomMaterialScheme materialScheme;
        [Tooltip("Furniture groups created by the splitter; replaced on the next split.")]
        public List<GameObject> generatedPieces = new();
    }
}
