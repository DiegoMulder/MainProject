# Mansion — final Unity FBX package

Import this entire **FBX** folder into your Unity project's **Assets** folder. After scripts finish compiling, the included Editor importer automatically creates shared materials, assigns URP Lit shaders and textures, configures normal maps, and generates room lightmap UVs. Then drag the models from Rooms or Doors into your scene. Keep Textures, Editor and MansionUnityPackage.json with the models for this automatic setup.

Target checked: Unity 6000.6.0f1 with Universal Render Pipeline. Built-in Standard is supported as a fallback; HDRP is not targeted. The importer compiles in the connected Unity editor. Geometry and texture exports were re-imported and verified in Blender; an end-to-end production-project import was not performed. No existing Unity scene was edited.

## Final model files
```
FBX/
  Rooms/
    CornerHall.fbx
    StraightHall.fbx
    TJunction.fbx
    LargeRoom.fbx
    SmallRoom.fbx
    StandardRoom.fbx
    Staircase.fbx
  Doors/
    Door.fbx
  Textures/
  Editor/MansionAssetImporter.cs
  MansionUnityPackage.json
  Asset_Manifest.json
  Validation_Report.json
```

Each model contains one mesh, correct metre scale, correct pivot, UV0, assigned materials and embedded source texture maps. No socket empties, cameras, lights, rigging helpers or unrelated geometry are exported. Room origins are bottom-centre; Door origin is its left hinge axis. Room apertures remain doorless. Room sockets are documented in the source asset READMEs and validation.json files.

## Material setup
Four shared materials cover the set: dark mansion wood, aged ivory wallpaper, dark floorboards and tarnished brass. Base colour is sRGB. Roughness, normal and metallic/smoothness maps are linear. Normal maps use OpenGL tangent convention; the importer sets Unity's Normal Map texture type. Metallic is packed in red and smoothness (1 minus roughness) in alpha. Roughness originals remain available alongside the Unity maps. The importer is scoped to a folder containing MansionUnityPackage.json and does not change unrelated assets. A Tools > Mansion menu action can refresh this package after later updates.

This channel convention follows [Unity's URP Lit material documentation](https://docs.unity.com/en-us/engine/6000.0/manual/materials-and-shaders/built-in/shaders-in-universalrp/reference/lit-shader). Shared material assignment uses [OnAssignMaterialModel](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityeditor/assetpostprocessor/onassignmaterialmodel).

## Checked geometry
All eight files passed mesh-only content, dimensions, unit scale, pivot, UV, normals, material, texture-path and duplicate-triangle checks. Every room's crown is a single connected closed sweep. Intended wooden panel coverage was sampled across every wall and corner. Standard doorway and staircase checks passed; the staircase is 10 x 14 x 7 m, climbs 4 m in one straight run and has exactly two openings. Door remains separate and passed 0–90 degree swing tests.

Floors and ceilings are deliberate inward-facing single surfaces. Collision, navigation baking, lighting, LOD strategy and door interaction scripts are application-specific and are not added by this render-asset importer. The Unity importer generates secondary UVs on rooms but does not bake lightmaps or attach colliders.

Editable source files remain under ../Rooms and ../Door. The central versions here are the latest final exports. Future Mansion assets should be delivered in this folder under an appropriate category created only when needed.
