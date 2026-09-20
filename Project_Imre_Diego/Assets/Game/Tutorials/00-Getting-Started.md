# Beginner setup and everyday editing

[All tutorials](../README.md)

## A few Unity words, explained simply

**Project window:** your saved files. A prefab here is a reusable template. Table.prefab describes how to make a table.

**Hierarchy:** objects in the open scene, or inside the prefab currently being edited. A table in a scene is an instance: one copy of its template.

**Inspector:** settings for the selected object. A component is a block of settings, such as Box Collider or Room Module. Add a component with **Add Component**.

**Transform:** position, rotation, and scale. X is left/right, Y is up/down, Z is forward/back. A child's position is relative to its parent. Scale (1, 1, 1) means original size. These systems treat one Unity unit as roughly one metre.

**Collider:** the invisible physical shape used for walls, floors, pickup targeting, and contact. A visible wall without a collider can let you walk through it.

**Prefab Mode:** double-click a prefab in Project to edit the reusable template. Use the back arrow above the Hierarchy to leave. Save with Ctrl+S. Auto Save also saves as you edit when enabled.

**Reference field:** an Inspector slot pointing to another object or asset. Drag the required object into it. If a field rejects a prefab, it may be missing the required component or be the wrong type of object.

**Array/list:** several entries grouped together. Expand the arrow, then use + or increase Size, depending on the Inspector. Element 0 is the first entry. Added entries may copy the previous entry, so check every field.

**Server/host:** the machine deciding shared gameplay. **Client:** a connected player. A NetworkObject identifies an object across machines. Registering a prefab tells clients what template to use; spawning actually creates it.

## Your everyday editing routine

1. Stop Play Mode. Changes to scene objects while playing usually disappear when you stop.
2. Find the asset in Project. For permanent reusable changes, edit its prefab, not a generated room in the Hierarchy.
3. Duplicate an existing asset with Ctrl+D when creating a variant. Give it a useful name such as Library Room.
4. Change one small thing and save with Ctrl+S.
5. Add the new asset to the appropriate catalog/reference field. Being in a folder does not automatically put a prefab into the game.
6. Open MainMenu, press Play, host, and start. Inspect the result.
7. Test with a second player for networked changes. Build both peers from the same updated project.

Changes to a prefab instance in a normal scene can be overrides on only that copy. Use Overrides/Apply deliberately, or edit the template in Prefab Mode. You do not need to rerun content-building tools to save ordinary prefab changes.

## First run

1. Open Assets/Game/Scenes/MainMenu.unity.
2. Press Play, enter your name, and choose Host Game.
3. Share the code. Another player enters it and chooses Join Lobby.
4. Check both names appear. Only the host chooses Start Game.
5. Explore, collect required items, and deposit them at the exit with E.
6. After the exit unlocks, interact again to escape.

Online errors are covered in [multiplayer setup](05-Multiplayer-and-Troubleshooting.md). The offline controller course works without an online lobby.

## The central settings asset

Select **Assets/Game/Data/Maps/Mansion/MansionContentSet.asset**. The existing authored configuration has **60 base target rooms, 4 floors, 4-metre floor spacing, 100 base objectives, and 8 medkits**. Lobby difficulty overrides room/objective/enemy counts; Medium currently uses 60 rooms, 15 objectives and 1 enemy; Easy, Hard and Extreme override them through Game/Data/Difficulty.asset. See the options/difficulty/radio tutorial for current settings.

| Setting | Meaning |
| --- | --- |
| Seed | Number used to repeat generation with the same content/settings |
| Random Seed | Enabled: new seed each round. Disabled: use Seed |
| Room Count | Target module count, including halls and stairs; not a guaranteed exact count |
| Attempts Per Room | Placement attempts; increasing this cannot fix incompatible geometry |
| Floor Count | Requested levels; requires a valid stair route |
| Floor Height | Vertical spacing between levels; must match stair rise |
| Rooms | Allowed room prefabs and relative selection weights |
| Player / Flashlight / Objective / Medkit / Door / Exit / Enemy | Templates used by the corresponding spawning systems |
| Objective Count | Total required items |
| Objective Types | Names distributed across that total; see the item tutorial |
| Medkit Count | Number placed on eligible surfaces |
| Prefer Different Rooms | Spreads objectives where possible, not a guarantee of one per room |
| Allow Exit In Starting Room | Whether the starting room may contain the exit |
| Wall Material | Material for generated closing walls; room meshes have their own materials |

For a shorter experiment, record your current settings, then try 12 rooms, 1 floor, 3 objectives, and 1 medkit. Keep connected rooms and sufficient surfaces. Disable Random Seed and use Seed 12345 to reproduce a layout while debugging. Restore your recorded settings afterwards. Changing the catalog or geometry can change a layout even with the same seed.

The first Rooms entry is special: it supplies the starting module and participates in the floor route. Keep a dependable ordinary room in Element 0. Add experiments at the end.

## Inspector changes versus new features

| Goal | Workflow |
| --- | --- |
| New room/furniture/stair variant | Duplicate, edit geometry/anchors, add to catalog |
| More floors/objectives/medkits | Change settings and check navigation/surface capacity |
| Replace the look of objectives or medkits | Duplicate/register network prefab and assign settings reference |
| Different enemy appearance or tuning | Duplicate/register enemy and assign Enemy |
| New ordinary collectible | Create/register pickup, then provide a spawn location; tutorial includes an optional script |
| New left-click behavior | Implement IPrimaryUse; tutorial includes an example |
| Extra inventory slots, random enemy types, new objective rules | Extend scripts and UI; these are not existing Inspector switches |

Continue with [rooms and stairs](01-Rooms-and-Stairs.md).
