using System;
using UnityEngine;
namespace SurvivalFP
{
    // Maps the materials baked into a room FBX to the project's authored Mansion materials, so room
    // types can differ (wallpaper colour, wood tone, worn floors) while sharing one model set.
    [CreateAssetMenu(menuName = "Survival FP/Room Material Scheme")]
    public sealed class RoomMaterialScheme : ScriptableObject
    {
        [Serializable] public struct Entry
        {
            [Tooltip("Part of the FBX material name, e.g. V2Wallpaper, V2Wood, V2Floor, V2Dressing.")]
            public string match;
            public Material material;
        }
        public Entry[] entries = Array.Empty<Entry>();

        public Material Resolve(Material source)
        {
            if (!source) return source;
            foreach (var entry in entries)
                if (entry.material && !string.IsNullOrEmpty(entry.match) && source.name.Contains(entry.match)) return entry.material;
            return source;
        }
    }
}
