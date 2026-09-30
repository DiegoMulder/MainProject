# Mansion models and adding new rooms

[All tutorials](../README.md) · [Furniture and item surfaces](02-Furniture-and-Hiding.md)

The Mansion still uses the grid-based room generator. A **gameplay prefab** is the complete room that the generator places. An **FBX** is only its visible model. Keep these separate so changing art does not move doorways or break walking surfaces.

## Find the files

- `Assets/Game/Art/Mansion/Current` contains the active V2 FBX models and their `Textures` folder. Their materials use PSX Lit (see `Materials`).
- Textures dropped into `Current/Textures` are set up automatically by `Editor/MansionTextureImporter.cs`:
  - `*_Normal.png` becomes a Normal Map;
  - only `*_BaseColor.png` is sRGB;
  - metallic, smoothness and roughness maps stay linear.

  A normal map imported as an ordinary texture tilts every surface's shading. You can spot it when a lantern's pool of light ends in a hard diagonal line across the wall. Check the importer before blaming the lighting.
- `Assets/Game/Art/Mansion/Archive/Version1` contains the old source package for comparison and rollback. No active gameplay prefab, scene, or other map asset uses it. The old `Closet.fbx` had already been removed before this change; the current closet uses the V2 model.
- `Assets/Game/Prefabs/Maps/Mansion` contains the actual playable room, hallway, staircase, door, and prop prefabs.

The active set includes V2 Standard, Small, and Large rooms; Straight, Corner, and T hallways; the Staircase; the Grand Hall; Door; Closet; Plant; and **WallLantern**. The room FBXs still contain the lanterns they were modelled with, but those are no longer shown: the furniture splitter removes every baked wall lantern from the room shell (visual and collision) and hangs a `Props/Wall Lantern.prefab` in exactly its place. The room FBXs stay unchanged as the splitter's source.

The supplied V2 set does not contain standalone Table, Shelf, Cabinet, or Plant FBXs. Some of those details are baked into the room meshes; the separate optional furniture groups still use the project's gameplay furniture prefabs. They are placed in clear areas so they do not sit on top of the baked-in details.

## Replace a room model

1. Add the new FBX and any textures to `Art/Mansion/Current`. Wait for Unity to import them. Select the FBX and check that its materials look right and use URP shaders.
2. Open the existing playable room prefab. Select its highest object and note its `Room Module` size and connectors. **Do not move or scale this root.**
3. Expand `_Visuals` and replace only the `Current V2 Visual` child. Keep the room's floor and wall colliders, connectors, item markers, and scripts.
4. Adjust the **visual child's** local position, rotation, and scale. The current V2 FBXs use about X rotation `270.02` and scale `100` because of their source axes and units. Copy a working sibling prefab's values as a starting point.
5. Look from above and from each doorway in Scene view. The model's floor should cover the gameplay floor. Its openings must meet the connector positions. If the model's pivot is off-center, offset only the visual child.
6. Save, host a Mansion round from Main Menu, and walk through doors and stairs. Check pickups, enemy movement, and several generated layouts.
7. After the new model works, check its old version has no active references. Move the old source asset into `Archive/Version1` through Unity's Project window, which preserves its `.meta` GUID.

### One physical scale

**Every Mansion visual uses scale `(100, 100, 100)`, and every room root uses scale 1. Never stretch a model to fit a footprint.** The generator joins rooms connector to connector and checks overlap with occupancy boxes. It has no grid, so a room can be any size.

Reference dimensions, measured with physics raycasts in every room:

| | Size |
| --- | --- |
| Doorway (all 8 rooms, every connector) | 1.80 m wide × 2.40 m high, centred on its connector |
| Door panel collider | 1.79 × 2.40 m |
| Player capsule | 1.75 m tall, 0.30 m radius, eye at 1.57 m |
| Connector `width` | 1.8 (the clear opening; only equal widths join) |

The Corner Hall used to be stretched to (114, 114, 100) to fill an 8×8 footprint. Its doorways were then 2.06 m wide, so the doors looked lost in them, and its furniture was 14% too wide. It now uses its real 7×7 m size, with connectors at (0, 0, 4) and (−4, 0, 0) and one occupancy box centred at (−0.5, 1.5, 0.5).

To check a new model, instantiate it and measure the opening at each connector before placing anything. The Staircase visual faces 180 degrees relative to its import so the upper landing meets the upper connector.

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

### Wall lanterns

`Props/Wall Lantern.prefab` is the current lantern: the `WallLantern.fbx` model (pivot on its back plate, facing +Z out of the wall), a small box collider, and a `Flame` point light driven by **Lantern Light**. After a split, each room lists its lanterns under `Wall Lanterns`. The light sits at the flame, in the centre of the glass and in front of the wall. There is no separate `LanternLights` fill any more, except the Grand Hall's two **Chandelier Candle Light**s. These sit at the chandelier's candle rings, below the ceiling and outside the opaque model, and are marked **Major** and **Always Burning**.

To relight the Mansion, edit the prefab's **Lantern Light**: Intensity, Range, Flame Colour, Glass Glow and Flicker. Every lantern updates.

### How the Mansion is lit (hybrid: baked rooms, real-time for moving things)

Unity keeps lightmaps and light probes per scene, and the Mansion is assembled from prefabs at runtime. So each room prefab carries its own bake:

- **`Survival FP > Lighting > Bake Mansion Room Lighting (All Rooms)`** bakes every room alone in an empty scene.
  - It bakes once per mood: Lit, Dim and Dark. A room with an authored mood, like the Grand Hall (Lit), only gets that one.
  - It generates lightmap UVs, because the FBX lightmap UVs are empty or overlapping.
  - Lightmaps are non-directional, so they stay correct under the generator's 90° rotations.
  - Furniture is lit but casts no baked shadow, so a piece the randomizer switches off leaves no ghost behind.
  - About 2.5 minutes for all rooms and about 10 MB of lightmaps.
- **`RoomBakedLighting`** on each room stores everything:
  - the lightmaps and each renderer's lightmap slot;
  - which lanterns burn steadily or gutter in each mood. Every lantern burns in every mood: a dead lantern looked like a broken light, so the mood only decides how many gutter (Lit about 10%, Dim about 40%, Dark about 65%). `LanternLight.StateFor` holds the odds; rebake after changing them;
  - a probe grid (six-direction light per probe, bounce only).

  When a room spawns, it picks its mood from its position (the same on every client), registers its lightmaps, and moves its surfaces to rendering layer 1 (Baked Environment).
- **Lantern real-time lights** only light rendering layer 0: players, enemies, doors, closets and pickups. They never light the walls a second time, and they cast no shadow maps. The environment's shadows are baked.
  - Each client still activates only the lights near its own camera: on at 20 m, off at 26 m (45 m and 55 m for chandeliers), with a 0.4 s fade and at most 40 lights.
  - About 22 are active at a time.
- **`DynamicProbeLighting`** is on the survivor, enemies, pickups, doors and closets. A few times a second it samples the nearest probes of the room it is in and feeds them to its renderers. Their shadow side gets the room's bounce light instead of going black.
- **Flashlights** light every layer, with real-time shadows from everything except rendering layer 2 (Local Player Body): on each client, the own player's invisible body is on that layer only, so it never throws a torch shadow in front of you. Walls, furniture, doors, enemies and other players still cast. Held items keep their normal layers.
- The lantern glass always glows from its material's emission, independent of any light.

Measured on the same 60-room layout, compared with lighting the whole Mansion in real time:
- CPU: 2.65 → 2.30 ms median.
- GPU: 2.27 → 1.90 ms median.
- Shadow casters: 246 → 0 per frame.
- Shadow-mapped lanterns: 4 → 0.

To compare setups on any layout, add `LightingBenchmark` to an object during a round (development builds only). Setting `RoomBakedLighting.Enabled = false` before a round starts uses the old all-real-time path.

**Rebake after:**
- splitting furniture (the splitter clears the old bake and warns);
- changing a room model;
- moving lanterns;
- changing lantern Intensity or Range;
- changing the Mansion atmosphere's ambient.

Lanterns in a room without a bake fall back to fully real-time light, and the 4 nearest cast shadows.

For a new room model, rerun the splitter (baked lanterns are detected by their glass), then rebake. You can also drag `Wall Lantern.prefab` onto a wall, with its back plate flush and +Z facing into the room. Never put lanterns into `Randomized Structures`.

## Final check

The room root, connectors, colliders, and spawn markers should remain intact. The visible model must fit the gameplay bounds; doors must retain their colliders; closets must still hide players. Generate several seeds and check the Console for missing art, navigation, or multiplayer errors.
