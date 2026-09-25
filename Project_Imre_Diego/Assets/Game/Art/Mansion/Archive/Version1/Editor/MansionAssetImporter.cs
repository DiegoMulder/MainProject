using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

// Scoped to folders carrying MansionUnityPackage.json; no effect on other assets.
public sealed class MansionAssetImporter : AssetPostprocessor
{
    internal static string RootFor(string path)
    {
        string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
        while (!string.IsNullOrEmpty(directory) && directory.StartsWith("Assets", StringComparison.Ordinal))
        {
            if (File.Exists(directory + "/MansionUnityPackage.json")) return directory;
            directory = Path.GetDirectoryName(directory)?.Replace('\\', '/');
        }
        return null;
    }
    void OnPreprocessTexture()
    {
        if (RootFor(assetPath) == null) return;
        var importer = (TextureImporter)assetImporter;
        bool normal = assetPath.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = assetPath.EndsWith("_BaseColor.png", StringComparison.OrdinalIgnoreCase);
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 1024;
    }
    void OnPreprocessModel()
    {
        if (RootFor(assetPath) == null) return;
        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 1f;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.generateSecondaryUV = assetPath.Contains("/Rooms/");
        importer.isReadable = false;
        importer.addCollider = false;
    }
    Material OnAssignMaterialModel(Material source, Renderer renderer)
    {
        string root = RootFor(assetPath);
        return root == null ? null : AssetDatabase.LoadAssetAtPath<Material>(root + "/Materials/" + source.name + ".mat");
    }
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] previous)
    {
        if (MansionPackageSetup.Busy) return;
        if (imported.Any(p => RootFor(p) != null && (p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.EndsWith("MansionUnityPackage.json"))))
            MansionPackageSetup.Schedule();
    }
}

[InitializeOnLoad]
public static class MansionPackageSetup
{
    internal static bool Busy;
    static bool queued;
    static MansionPackageSetup() { Schedule(); }
    internal static void Schedule()
    {
        if (queued || Busy) return;
        queued = true;
        EditorApplication.delayCall += BuildAll;
    }
    [MenuItem("Tools/Mansion/Refresh packaged materials and FBXs")]
    public static void BuildAll()
    {
        queued = false;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Schedule(); return; }
        Busy = true;
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("MansionUnityPackage"))
            {
                string marker = AssetDatabase.GUIDToAssetPath(guid);
                if (!marker.EndsWith("/MansionUnityPackage.json", StringComparison.Ordinal)) continue;
                string root = Path.GetDirectoryName(marker).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(root + "/Materials")) AssetDatabase.CreateFolder(root, "Materials");
                foreach (string texture in Directory.GetFiles(root + "/Textures", "*.png"))
                    AssetDatabase.ImportAsset(texture.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
                bool urp = GraphicsSettings.currentRenderPipeline != null && GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal");
                Shader shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
                if (shader == null) throw new InvalidOperationException("Mansion requires URP Lit or Built-in Standard.");
                string[] kinds = { "Wood", "Wallpaper", "Floor", "Brass" };
                string[] names = { "M_Mansion_AgedWalnut", "M_Mansion_AgedIvory", "M_Mansion_DarkFloorboards", "M_Mansion_TarnishedBrass" };
                for (int i = 0; i < kinds.Length; ++i)
                {
                    string path = root + "/Materials/" + names[i] + ".mat";
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                    else material.shader = shader;
                    string prefix = root + "/Textures/T_Mansion_" + kinds[i];
                    var color = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_BaseColor.png");
                    var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_MetallicSmoothness.png");
                    var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Normal.png");
                    material.SetTexture(urp ? "_BaseMap" : "_MainTex", color);
                    material.SetColor(urp ? "_BaseColor" : "_Color", Color.white);
                    material.SetTexture("_MetallicGlossMap", metallic);
                    material.SetFloat("_Metallic", 1f);
                    material.SetFloat(urp ? "_Smoothness" : "_GlossMapScale", 1f);
                    material.SetFloat("_SmoothnessTextureChannel", 0f);
                    material.EnableKeyword(urp ? "_METALLICSPECGLOSSMAP" : "_METALLICGLOSSMAP");
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_BumpScale", .25f);
                    if (normal != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
                    material.enableInstancing = true;
                    EditorUtility.SetDirty(material);
                }
                AssetDatabase.SaveAssets();
                foreach (string model in Directory.GetFiles(root, "*.fbx", SearchOption.AllDirectories))
                    AssetDatabase.ImportAsset(model.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
            }
        }
        finally { Busy = false; }
    }
}
