# Furniture, item surfaces, and hiding places

[All tutorials](../README.md) · [Pickups next](03-Pickups-and-Objectives.md)

The Mansion now handles **ordinary furniture** as child groups already placed inside each room prefab. The room root's `Room Module > Randomized Structures` list decides which groups appear. **Closets** remain on their separate hiding and network spawn system.

An `Item Spawn Point` is a marker on a table, shelf, or another surface where an objective, medkit, or radio can appear. Put it **inside the furniture group** so it disappears with the furniture. That prevents floating pickups.

## Put optional furniture in a room

1. In Unity's Project window, open `Assets/Game/Prefabs/Maps/Mansion/Rooms` and double-click the room prefab you want to edit.
2. In the Hierarchy, select the highest room object. It has the `Room Module` component.
3. Right-click the room and create an empty child named, for example, `Table Setup`. Move and rotate this group to the exact place the table should stand. Leave its scale at `(1, 1, 1)`.
4. Drag `Assets/Game/Prefabs/Maps/Mansion/Props/Table.prefab` **inside** the group. Set the furniture child's local position and rotation to zero. The parent group controls placement.
5. Select the room root again. Expand `Room Module > Randomized Structures` and add an element. Drag `Table Setup` **from the Hierarchy** into `Target`. Do not drag the prefab asset from the Project window; the target must be an existing child of this room.
6. Set `Spawn Chance` as a percentage: `0` means never, `100` means always, `60` means about six out of ten room copies. Save the room prefab.
7. Host several rounds. Each entry rolls independently: a table at 60% and a plant at 25% can appear together, separately, or neither.

You can put several objects under one group, such as a table, books, and item markers. They all turn on or off together. Only children explicitly listed in `Randomized Structures` are affected. **Never list** floors, walls, connectors, doors, stairs, lanterns, triggers, or the room root.

The host chooses each room's groups once per round and sends the choices to clients. Entering a room or joining later does not reroll it.

Some decorative furniture is baked into a V2 room FBX. Because that FBX is one combined mesh, those built-in pieces stay visible and cannot be switched individually by this list. The existing room child groups use separate furniture prefabs and are positioned away from the built-in details. To make a baked-in piece optional later, export it as its own model, remove it from the room FBX, and place the separate model inside a room child group.

## Make a new furniture prefab

1. Duplicate a simple prefab in `Assets/Game/Prefabs/Maps/Mansion/Props`, such as `Table.prefab`, and rename the copy.
2. Open it in Prefab Mode. Change its appearance and check the solid parts have sensible colliders.
3. Move its `Item Spawn Point` children to real, clear surfaces. Remove markers that are inside the model.
4. Save and place the new prefab inside a room child group using the steps above. Ordinary static furniture does not need an entry in `NetworkPrefabs.asset`.

For a hiding place, start from `Closet.prefab` instead. It has interaction, occupancy, hide points, and AI positions. Keep its network registration and the room's closet anchor.

## Add more pickup locations

1. Open the furniture prefab or child group that contains the surface.
2. Create an empty child named `Item Surface 01` and add `Item Spawn Point`.
3. Put the marker just above the solid tabletop or shelf. Leave room for the lower part of the largest pickup collider.
4. Set `Approach Offset` so it points to reachable clear floor where a player or enemy can stand.
5. Add more markers only where items will not overlap. Save, host a round, and try picking up items with E.

The game rejects item points whose approach is unreachable. It also avoids the starting room for required items. If a round reports too few surfaces, add more **guaranteed** groups with 100% chance and reachable markers. Low-chance decorations cannot reliably supply required pickups.

## Keep closets and paths working

The existing `Closet anchor` stays in the room's `Prop Anchors` list. It creates the networked closet with its own chance setting. Leave clear floor in front of its entry, exit, and AI approach. Keep ordinary furniture away from doorways and stair landings.

Tables are ordinary furniture, not hiding spots. For actual hiding, enter an interactable closet with E. See [closets, gameplay noise, heartbeat, and PSX visuals](06-Closets-Noise-and-Presentation.md).
