# Mansion models and adding new rooms

[All tutorials](../README.md) · [Furniture and item surfaces](02-Furniture-and-Hiding.md)

The Mansion still uses the grid-based room generator. A **gameplay prefab** is the complete room that the generator places. An **FBX** is only its visible model. Keep these separate so changing art does not move doorways or break walking surfaces.

## Find the files

- `Assets/Game/Art/Mansion/Current` contains the active V2 FBX models and their `Textures` folder. Their materials use URP.
- `Assets/Game/Art/Mansion/Archive/Version1` contains the old source package for comparison and rollback. No active gameplay prefab, scene, or other map asset uses it. The old `Closet.fbx` had already been removed before this change; the current closet uses the V2 model.
- `Assets/Game/Prefabs/Maps/Mansion` contains the actual playable room, hallway, staircase, door, and prop prefabs.

The active set includes V2 Standard, Small, and Large rooms; Straight, Corner, and T hallways; the Staircase; Door; and Closet. `WallLantern.fbx` is a source model only. Its meshes are already part of each room FBX, so do not add separate lantern meshes.

The supplied V2 set does not contain standalone Table, Shelf, Cabinet, or Plant FBXs. Some of those details are baked into the room meshes; the separate optional furniture groups still use the project's gameplay furniture prefabs. They are placed in clear areas so they do not sit on top of the baked-in details.

## Replace a room model

1. Add the new FBX and any textures to `Art/Mansion/Current`. Wait for Unity to import them. Select the FBX and check that its materials look right and use URP shaders.
2. Open the existing playable room prefab. Select its highest object and note its `Room Module` size and connectors. **Do not move or scale this root.**
3. Expand `_Visuals` and replace only the `Current V2 Visual` child. Keep the room's floor and wall colliders, connectors, item markers, and scripts.
4. Adjust the **visual child's** local position, rotation, and scale. The current V2 FBXs use about X rotation `270.02` and scale `100` because of their source axes and units. Copy a working sibling prefab's values as a starting point.
5. Look from above and from each doorway in Scene view. The model's floor should cover the gameplay floor. Its openings must meet the connector positions. If the model's pivot is off-center, offset only the visual child.
6. Save, host a Mansion round from Main Menu, and walk through doors and stairs. Check pickups, enemy movement, and several generated layouts.
7. After the new model works, check its old version has no active references. Move the old source asset into `Archive/Version1` through Unity's Project window, which preserves its `.meta` GUID.

The current Corner Hall and T Junction visuals have child-only scale and position adjustments to fit the existing 8×8 and 10×10 grid footprints. The Staircase visual faces 180 degrees relative to its import so the upper landing meets the upper connector. Preserve those settings when changing only materials.

## Add an extra room to generation

1. Duplicate the closest playable room prefab under `Rooms`, `Hallways`, or `Stairs`. Rename it, such as `Library Room`.
2. Keep the root, `Room Module`, connectors, collision, and item markers. If the footprint changes, update the module size, floor/wall colliders, and connector positions together. Otherwise rooms may overlap or leave gaps.
3. Replace its visual child as described above. Keep the visible floor at Y=0. Move the visual child to fix an FBX pivot; never move the gameplay root to do that.
4. Add furniture groups and item markers using [the furniture tutorial](02-Furniture-and-Hiding.md). Keep doorways and stair landings clear.
5. Open `Assets/Game/Data/Maps/Mansion/MansionContentSet.asset` and add the new room prefab to its room list with a small weight. Do not add it to Slaughterhouse.
6. Host several rounds and confirm the new room appears, connections meet, the exit is reachable, and items rest on real surfaces.

## Doors, closets, and lantern lights

Door prefabs keep their `Hinge`, door panel collider, scripts, and networking. Their V2 mesh is only a child under `Hinge`. If changing the visual, keep the existing hinge and collider. The V2 door visual uses X rotation `270.02`, Y rotation `180`, and scale `100`.

The Closet prefab keeps its hide interaction, entry/exit points, AI approach, and collision. Its V2 model is only the visible child. Closets remain separate networked hiding props and use their original closet spawn anchors.

Every room's `LanternLights` child has Point Lights at the built-in lantern glass locations. Select a `LanternLight` child to edit warm color, intensity, range, or shadows in the Inspector. For a new room model, place a Point Light at each built-in bulb on the room side of the wall and check it from multiple angles. Keep range short and shadows off unless needed; a generated Mansion has many lanterns. Do not put lanterns into `Randomized Structures`.

## Final check

The room root, connectors, colliders, and spawn markers should remain intact. The visible model must fit the gameplay bounds; doors must retain their colliders; closets must still hide players. Generate several seeds and check the Console for missing art, navigation, or multiplayer errors.
