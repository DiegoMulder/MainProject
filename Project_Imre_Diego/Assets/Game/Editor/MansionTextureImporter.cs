using System;
using UnityEditor;
using UnityEngine;

namespace SurvivalFP.EditorTools
{
    // Import rules for the current Mansion textures. Normal maps must be Normal Map textures: read as colour, they
    // tilt every surface's shading normal and a lantern's light ends in a hard diagonal line across the wall.
    // Only base colour is sRGB; metallic/smoothness, roughness and normal data stay linear.
    sealed class MansionTextureImporter : AssetPostprocessor
    {
        const string Folder = "Assets/Game/Art/Mansion/Current/Textures/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && assetPath.EndsWith("_BaseColor.png", StringComparison.OrdinalIgnoreCase);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
        }
    }
}
