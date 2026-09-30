# Furniture, item surfaces, and hiding places

[All tutorials](../README.md) · [Pickups next](03-Pickups-and-Objectives.md)

Every optional thing in a room (bookcases, tables, chairs, sideboards **and hiding closets**) uses one system: the room root's `Room Module > Randomized Structures` list. Each entry points at a child group that already exists inside the room prefab, plus a `Spawn Chance` percentage. The old prop spawner and its primitive cube/sphere props (table, shelf, plant, cabinet) have been removed completely.

## Mansion furniture comes from the room models

The furniture in the Mansion is the furniture **baked into each room FBX**. `Survival FP > Rooms > Split Baked Furniture (All Mansion Rooms)` separates it:

- Each floor-standing piece that uses the `Dressing` material becomes its own object: `Bookcase Setup`, `Table Setup`, `Chair Setup`, `Sideboard Setup` and so on. It gets its own mesh, material slots, collider and item spots on its real surfaces.
- Books, papers and boxes resting on a piece move with it, so nothing floats when the piece is switched off.
- The walls, floor, panelling, rugs, door frames, wall-hung decoration and stair rails stay in the room shell.
- Baked wall lanterns are removed from the shell and replaced by `Wall Lantern.prefab` at the same spot (see [the Mansion models tutorial](12-Mansion-FBX-Integration.md)).
- Faces whose UVs are broken in the model are re-mapped at the room's own texture scale, with wood grain along the piece. These are the collapsed doorway soffits and the strip-mapped wooden door liners. Other surfaces keep their UVs.
- The room's **Room Material Scheme** is applied to the shell and every piece.
- Meshes are written to `Assets/Game/Art/Mansion/Current/Split/<Room>/`. The original FBX is untouched, and `_Visuals` keeps a reference to it (`Room Visual Source`).

- Plants baked into a model (the `PlantAtlas` material, in the T-Junction and Grand Hall) stay visible in the shell. They are left out of the shell's collider (`<Room> Shell Collision`) and each gets a small capsule around its pot under `Plant Collision`, so leaves never snag players or cut holes in the NavMesh.

**Rerun it whenever a room model changes** (for example the new T-Junction). It starts again from the original FBX, replaces the pieces it made earlier, and keeps any spawn chances you changed. Default chances: Bookcase 70%, Table 75%, Sideboard 70%, Chair 60%. Item spots need a fully supported 46 × 30 cm patch with 45 cm of headroom, so pickups never overhang an edge or lean on books.

## Decorative plants

`Props/Plant.prefab` (from `Plant.fbx`) is static decoration with one capsule collider at the pot. It is **not** randomized and has no spawner: plants are ordinary children named `Plant (Authored)` inside the room prefabs, so every client builds them identically.

`Survival FP > Rooms > Place Authored Plants (All Mansion Rooms)` places them: Small Room 1, Standard Room 2, Large Room 2, Straight Hall 1, Corner Hall 1. Rooms with baked plants and the staircase get none. Each spot is tested with physics on a copy of the room, with every furniture piece and the hiding closet present. A spot must be in a corner or along a wall or furniture, on the room floor (never stairs or rugs), at least 2.2 m from doorways and 1 m from item spots and closet entry, exit and search points. The pot and leaves must not touch anything, and there must be a clear walkway in front. Rerun it after moving furniture or closets; it replaces the plants it placed before. You can also drag `Plant.prefab` into a room by hand, as long as you keep it out of doorways and walkways.

## How it works

1. When a round starts, the **host** rolls every entry independently: `random(0–100) < Spawn Chance` means the group is on.
2. A group that would sit inside a doorway's swing or the exit approach is forced off. Furniture can never block a door.
3. The results are sent to every client in the round manifest. Everyone sees the same furniture, and nothing rerolls when players enter a room or join late.
4. Each client enables or disables the existing child groups. A **disabled** group's renderers, colliders, item spawn points and network markers all go with it: no collision, no floating objectives, and it is not baked into navigation.
5. Only then are item surfaces collected and the NavMesh built, so objectives and medkits only use surfaces that really exist.

Only listed children are ever touched. The system refuses (with a Console warning) any entry that is empty, is the room root, is not a child of the room, or contains a connector, a lantern light or the room shell.

## Add a randomized piece: `NewTableSetup → 50%`

1. Open the room prefab (for example `Assets/Game/Prefabs/Maps/Mansion/Rooms/Standard Room.prefab`).
2. Select the room root (the object with `Room Module`), right-click it and create an empty child named `NewTableSetup`. Move and rotate it to where the table should stand. Keep its scale at `(1, 1, 1)`.
3. Put the table model **inside** `NewTableSetup` and zero its local position and rotation.
4. Optional: add `Item Spawn Point` children on the tabletop so objectives can appear there.
5. Select the room root. In `Randomized Structures`, press **+**, drag `NewTableSetup` **from the Hierarchy** into `Target`, and set `Spawn Chance` to `50`.
6. Save the prefab and host a few rounds. About half of the copies of this room will have the table.

`NewBookshelf → 75%` is the same recipe with a shelf prefab and a chance of `75`. Several objects may live in one group (a table with books and item markers); they always appear together. `0` means never, `100` means always.

## Add a networked piece (closets)

Closets synchronize their occupant, so the host spawns them over the network. They still use a randomized structure:

1. Create a child group, for example `Closet Setup`, at the closet position, facing into the room.
2. Inside it, create an empty `Closet Spawn` and add **Network Spawn Marker**. Assign `Props/Closet.prefab` (or `Slaughterhouse Closet.prefab`) to `Prefab`. The orange gizmo shows its footprint.
3. Add `Closet Setup` to `Randomized Structures` with its chance (the supplied rooms use 45%).
4. The prefab must be listed in `Assets/Game/Data/NetworkPrefabs.asset`. The supplied closets already are.

When the group is enabled, the server spawns the closet at the marker. When it is disabled, no closet exists.

## Adding furniture

The easiest way is to model it into the room FBX with a `Dressing` material, standing on the floor, then rerun the splitter. To place a separate model by hand instead:

1. Put the model inside a new child group of the room, as in the recipe above.
2. Give it colliders that match what you can see; a Mesh Collider on the model is fine.
3. Put `Item Spawn Point` markers about 20 cm above clear surfaces and set `Approach Offset` to reachable floor.

Static furniture does not need a network registration.

## Item surfaces

The game rejects item points whose approach is unreachable, and it avoids the starting room for required items. If a round reports too few surfaces, add more **100%** groups with reachable markers; low-chance decorations cannot reliably supply required pickups.

## Hiding closets

The hiding closets (`Props/Closet.prefab`, the current closet model) are separate network objects, so the splitter does not create them. It does **place** them: every split run (or `Survival FP > Rooms > Place Hiding Closets`) tests each `Closet Setup` against a copy of the room with **every** furniture piece enabled and picks a spot where the closet:

- stands on the real floor, square to a wall, with its back 1–2 cm from the wall's most protruding surface;
- touches nothing (walls, lanterns, decoration or furniture) and keeps at least 25 cm of floor between itself and every piece;
- keeps its entry, exit and enemy approach clear, stays 2.2 m from doorways, and never covers another piece's item spot.

If a room has no such spot, the closet's chance is set to 0% and the report says why. Move the closet by hand only if you also rerun this check.

Tables are ordinary furniture, not hiding spots. For hiding, use a closet. See [closets, gameplay noise, heartbeat, and PSX visuals](06-Closets-Noise-and-Presentation.md).
