# Furniture, item surfaces, and hiding places

[All tutorials](../README.md) · [Pickups next](03-Pickups-and-Objectives.md)

## Three different things

A **furniture prefab** is the table or shelf. A **PropSpawnPoint** marks a position inside a room where furniture is chosen. An **ItemSpawnPoint** marks a position on furniture where an objective or medkit may appear. These components do separate jobs.

## Recipe: create a furniture variant

1. In Assets/Game/Prefabs/Maps/Mansion/Props, duplicate Table.prefab. Rename it Dining Table.
2. Open it in Prefab Mode and change its material or visual details.
3. Keep solid colliders for the tabletop and legs.
4. Inspect ItemSpawnPoint children: they are item locations. Reposition them if the tabletop changes size.
5. Preserve ordinary tabletop/leg collision. Use Cabinet as a starting point for a solid cabinet; use Closet for an interactable hiding spot.
6. Save.

Static furniture is not registered in NetworkPrefabs.asset. A room selects it through PropSpawnPoint variants. Closets are interactive network props: register those in NetworkPrefabs.asset as well.

## Recipe: place your furniture in a room

1. Open the room prefab.
2. Select an existing PropSpawnPoint, or create an empty child and add Prop Spawn Point.
3. Move the marker to the desired furniture origin and rotate it appropriately. Keep scale (1, 1, 1).
4. Set Chance to 1 for an appearance every time, 0.5 for a 50% appearance chance, or 0 for none.
5. Expand Variants, add an entry, and drag Dining Table.prefab from Project into Prefab.
6. Set Weight to 2. Optionally add Shelf with Weight 1 if either shape fits here.
7. Leave Random Half Turn disabled if the furniture must face a particular wall. Enabling it allows a 180-degree variation.
8. Select the room root. Add the new marker to Room Module > Prop Anchors.
9. Save and generate several rounds.

After the appearance-chance roll succeeds, the example chooses the table about two-thirds of the time and the shelf one-third. Both variants need compatible origins and enough physical room.

Category is an organisational label. Typing Kitchen does not automatically find kitchen furniture. Variants determines the actual choices. Use positive weights; remove an entry to exclude it.

## Recipe: add more item surfaces

1. Open the furniture prefab.
2. Create an empty child called Item Surface 01.
3. Add Item Spawn Point.
4. Place it above a solid surface. The item's origin appears here, so leave clearance for the portion of its collider below that origin.
5. Set Approach Offset to reach clear nearby floor where someone could stand.
6. Add more markers only where their items will not overlap. Allow for your largest objective/medkit collider.
7. Save and confirm that a room actually selects this furniture.
8. Start a round. Check that items rest on the surface, can be targeted with E, and are reachable.

Example: a tabletop ending at Y = 1.5 could use a marker at (0, 1.75, 0) for an item with less than 0.25 metres of collider below its origin. Approach Offset (0, -1.75, 1.2) points to floor 1.2 metres in front, assuming an unrotated marker with unit scale. The offset rotates/scales with its marker. Adjust for actual geometry; the component does not calculate clearance automatically.

Navigation validates the approach, not simply the visible item position. An item can look fine on a shelf while its approach point inside the shelf is rejected.

## Why surface capacity matters

Objectives and medkits share eligible markers and use separate positions. Markers in the starting room are excluded. Unreachable approaches are filtered out. Furniture that did not appear contributes no markers.

The current 100 objectives plus 8 medkits need at least **108 eligible markers outside the starting room** after generation/filtering. Keep spare capacity. Authoring 108 possible markers across all your prefabs does not guarantee every generated map contains them.

For reliable required-item placement, use Chance 1 on enough furniture anchors and give all their variants sufficient surfaces. Low appearance chances suit decorations but can make required-item capacity unreliable.

ItemSpawnPoint currently supplies objectives and medkits. It does not automatically spawn arbitrary keys or tools. The next tutorial supplies an optional dedicated-marker script for those.

## Tables are ordinary furniture

Tables no longer register as hiding spots or have special AI concealment/memory behavior. The solid tabletop and legs are now ordinary furniture height, not elevated for crawling underneath. Crouching underneath is possible where normal collision permits it, and the tabletop prevents standing through it. There is no table hiding state and no cloth-trigger exception in enemy vision.

For an actual hiding spot, use an interactable **Closet**. Enter it with E; walking into it does not activate hiding. See [closets, gameplay noise, heartbeat, and PSX visuals](06-Closets-Noise-and-Presentation.md) for the full setup recipe.

## Final checks

- Every furniture variant fits without blocking doors or landings.
- All item approaches are on clear floor.
- Items begin above the surface and away from edges.
- Half-turn variation does not turn the usable side into a wall.
- Guaranteed furniture provides enough required-item surfaces.
- Closet anchors reserve space for entry, exit, and the enemy approach.
