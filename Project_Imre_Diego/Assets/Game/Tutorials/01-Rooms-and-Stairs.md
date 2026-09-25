# Adding rooms, hallways, and stairs

[All tutorials](../README.md) · [Furniture next](02-Furniture-and-Hiding.md)

## How a room becomes part of the mansion

A room prefab contains visible geometry, physical colliders, a RoomModule component, and doorway markers called RoomConnectors. The generator joins compatible markers and checks that the declared room bounds do not overlap another room. Mansion furniture is now placed as child groups listed in RoomModule's Randomized Structures array; closets still use PropSpawnPoints. See [the furniture tutorial](02-Furniture-and-Hiding.md).

Ordinary static rooms and furniture are reconstructed from the shared layout. They do not need NetworkObjects or entries in NetworkPrefabs.asset. Interactive network items and doors do.

## Recipe: make a library room

1. Stop Play Mode. Open Assets/Game/Prefabs/Maps/Mansion/Rooms in Project.
2. Select Standard Room.prefab, press Ctrl+D, and rename the copy Library Room.
3. Double-click the copy to enter Prefab Mode.
4. Select its root at the top of the Hierarchy. Keep Room Module's Size and Connectors unchanged for this first version.
5. Change wall/floor materials or add decorative meshes as children. Keep doorways and the centre route clear.
6. Give solid obstacles suitable colliders. A painting against an existing wall need not become a large obstacle across the floor.
7. Save and leave Prefab Mode.
8. Select MansionContentSet.asset, expand Rooms, and add an entry at the end.
9. Drag Library Room.prefab into that entry's Prefab field and set Weight to 2.
10. Host and start a round. Try several seeds if the room does not appear immediately.

**Expected result:** copies of the library can appear alongside the original room types. Adding a type does not increase Room Count automatically.

Weights are relative preferences, not guaranteed quantities. With two equally feasible choices weighted 4 and 1, the first is selected more often. Rejected placements and the required floor route change final proportions. Weight 0 does not disable a room: weights are treated as at least 1. Remove its list entry to exclude it.

## Room Module, field by field

**Size** reserves width X, occupied height Y, and depth Z. A normal 10 by 10 room with 3-metre walls uses (10, 3, 10). Its origin is at floor level. Keep root rotation (0, 0, 0) and root scale (1, 1, 1). Resize geometry and update Size together; Size alone does not resize a mesh.

The bounds are centred horizontally and extend upward from the base. The planner uses right-angle rotations. An arbitrarily rotated root is not a supported way to pack diagonal rooms.

**Navigation Anchor** is a local point on clear floor used to verify enemy access. For an open room, (0, 0, 0) works. If a statue fills the centre, move the anchor to another clear floor spot. Avoid furniture interiors, solid stairs, and isolated ledges.

**Connectors** lists this room's doorway markers. **Prop Anchors** lists its furniture selection points. Creating components in the Hierarchy is not enough: drag them into these arrays.

## Recipe: add or move a doorway

Start from a duplicated room so you can compare a working opening.

1. Make an actual hole in both wall geometry and its collider. RoomConnector does not cut a hole automatically.
2. Create an empty GameObject directly under the room root. Name it Connector North.
3. Add Room Connector. Keep it a **direct child**: the planner reads its local transform. An extra parent folder can change the meaning of that transform.
4. Put it at the doorway centre, at walking-floor height.
5. Rotate it so its blue local +Z arrow points **out of the room**. Use Local transform handles in the Scene view.
6. Set Connector Type to Mansion and Width to 2.4 to match the supplied catalog.
7. Enable Allow Door if an ordinary door may appear here. Enable Exit Eligible if it may become the exit.
8. Select the root, add an entry to Room Module > Connectors, and drag the marker into it.
9. Save, generate, and walk through the opening in both directions.

Typical sockets for a 10 by 10 room centred on its root:

| Edge | Local position | Local Y rotation |
| --- | --- | --- |
| North | (0, 0, 5) | 0 |
| East | (5, 0, 0) | 90 |
| South | (0, 0, -5) | 180 |
| West | (-5, 0, 0) | 270 |

Connector Type and Width must match the other socket. Also match the physical hole to the door. The supplied width is 2.4 metres, with doors about 2.6 metres tall; copy the existing opening/lintel for a first attempt. Unused sockets are closed at runtime apart from the chosen exit.

Keep clear floor one metre inside each doorway, opposite its outward arrow. Navigation validation checks this approach as well as the room's main anchor.

## Recipe: make a larger room

1. Duplicate Standard Room or Large Room.
2. Keep root scale at (1, 1, 1). Resize and move floor/wall children instead.
3. For a room 14 wide, 10 deep, and 3 high, set Size to (14, 3, 10).
4. Move east/west sockets to X = 7 and X = -7. North/south stay at Z = 5 and Z = -5.
5. Move the actual wall openings to those sockets.
6. Adjust furniture anchors to fit and keep routes clear.
7. Keep Navigation Anchor on clear floor.
8. Add the prefab to Rooms, save, and test several seeds.

Understating Size can allow intersecting geometry. Overstating it wastes space and rejects placements. Consider decorations that protrude outside the reserved area; bounds are not automatically calculated from every decorative mesh.

## Recipe: add a staircase style

1. Duplicate Rooms/Staircase.prefab and rename it Stone Staircase.
2. Open the copy. For the first version, change materials and rail decoration while preserving the treads and landings.
3. Keep Room Module > Staircase enabled.
4. Check the lower socket at (0, 0, -7), Y rotation 180.
5. Check the upper socket at (0, 4, 7), Y rotation 0.
6. Keep Navigation Anchor at (0, 0, -5.5) on the lower landing.
7. Keep bounds covering the whole module. The supplied footprint is 10 by 14, with occupied height 7 including upper walls.
8. Add the new prefab to Rooms with Weight 1.
9. Keep Floor Height at 4 and use at least 2 floors.
10. Walk up/down, jump near tread edges, and check enemy navigation.

The supplied stairs have 20 rises of 0.2 metres with 0.4-metre treads, making a total rise of 4 metres. Floor Height means level spacing, not visible wall height.

To change the rise, redesign the full tread sequence, landing, upper socket Y, and bounds together; then change Floor Height to match. Changing only Floor Height does not make the staircase taller.

The player's Player Movement > Step Height controls small-step traversal, currently 0.3. Keep rises within both player and enemy-agent step limits. Increasing ground-probe distance is not a substitute for suitable physical stairs.

## Recipe: add a third floor

1. Set MansionSettings > Floor Count to 3.
2. Keep Floor Height at 4 for the supplied stairs.
3. Keep compatible stairs and ordinary connecting rooms in the catalog.
4. Leave enough Room Count for the route and landings. The existing 60-room target provides much more room than the minimum slider value.
5. Generate and visit all three levels, including the return route.
6. Repeat with several seeds and a second player.

The generator builds the route between levels first, then fills towards the target room count. Attempts are limited, so a difficult catalog may produce fewer rooms or a clear generation failure.

The server bakes navigation from actual colliders and checks reachability before play. Upper-floor meshes need a physical route; adding a separate floor in the air is insufficient.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| New room never appears | Saved prefab, Rooms entry, weight, compatible sockets, honest bounds, several seeds |
| Door faces a wall | Socket's +Z arrow and the physical opening |
| Rooms overlap | Root scale/rotation, Size, socket positions, protruding geometry |
| Upper floor fails | Staircase flag, rise versus Floor Height, upper socket height, headroom |
| Navigation fails after decorating | Blocked sockets, navigation anchor, or landing |
| One isolated room works but maps fail | Every advertised doorway must work in every placement orientation |

Disable Random Seed and record the failing seed. Temporarily remove the new room from the catalog. If the original set works, restore your new room with one small change at a time.
