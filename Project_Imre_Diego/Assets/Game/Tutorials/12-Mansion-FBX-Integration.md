# Mansion models and adding new rooms

The supplied Mansion FBX files provide the visible room and door models in the playable prefabs. The playable prefab still controls room size, connectors, collision, spawn points, doors and navigation; the FBX is kept on the visual child so it can be replaced without changing gameplay.

## Where the files are

The original source models are in `Assets/Game/FBX`:

| Folder | Contents |
| --- | --- |
| `Rooms` | CornerHall, LargeRoom, SmallRoom, Staircase, StandardRoom, StraightHall and TJunction |
| `Doors` | Door, authored around its left hinge |
| `Materials` and `Textures` | Shared URP material and texture files |

The source FBX files are art assets. The generator does not spawn them directly. It spawns the authored prefabs in `Assets/Game/Prefabs/Maps/Mansion`, which contain the matching FBX visual. Keep the `RoomModule`, connectors, spawn anchors, simple colliders and gameplay scripts unchanged when replacing or adding a model.

The supplied package does not include a closet model. `Props/Closet.prefab` therefore keeps its existing gameplay visual. Keep its hide points, occupancy trigger and collision when replacing that visual later.

## Which model is used where

| Gameplay prefab | Visible model |
| --- | --- |
| `Rooms/Standard Room.prefab` | `Rooms/StandardRoom.fbx` |
| `Rooms/Small Room.prefab` | `Rooms/SmallRoom.fbx` |
| `Rooms/Large Room.prefab` | `Rooms/LargeRoom.fbx` |
| `Hallways/Straight Hall.prefab` | `Rooms/StraightHall.fbx` |
| `Hallways/Corner Hall.prefab` | `Rooms/CornerHall.fbx` |
| `Hallways/T Junction.prefab` | `Rooms/TJunction.fbx` |
| `Stairs/Staircase.prefab` | `Rooms/Staircase.fbx` |
| `Doors/Ordinary Door.prefab` and `Doors/Exit Door.prefab` | `Doors/Door.fbx` |

## Add another room, step by step

1. Duplicate the closest existing prefab in `Rooms`, `Hallways` or `Stairs`. Rename the copy clearly, for example `Library Room.prefab`.
2. Open the copy and leave its root at the same origin and rotation as the original. Do not move the root to make the art fit.
3. Keep the `RoomModule`, `RoomConnector` children, player/enemy spawn points, item anchors and colliders. These are the measurements used by generation and gameplay.
4. Under `_Visuals`, add the new FBX as a child named `FBX Visual`. Keep its local position at zero. For a door, put it under `Hinge/FBX Visual`. If the model has a visible offset, correct that on the visual child only.
5. Check the model in the Scene view. Its floor should sit at the prefab floor, its doorway should meet the connector, and its height should match the other rooms. The supplied imports use a 100 scale because their mesh data is authored in centimetres, plus an X rotation of about 270 degrees (`270.02` as imported) to convert the FBX axes into Unity's upright orientation. Copy these values from the imported FBX root when adding another supplied model; do not rotate the gameplay prefab root.
6. Disable an old placeholder renderer only after the new renderer is visible. Keep its collider if it still represents a useful gameplay boundary. Use simple box or capsule colliders for walking surfaces and walls; decorative mesh detail does not need collision.
7. Open `Game/Data/Maps/Mansion/MansionContentSet.asset` and add the new prefab to the correct list. Set a small weight first so it is easy to test. Do not add it to the Slaughterhouse content set.
8. Start from `MainMenu`, host a round, and test several seeds. Walk through every doorway, walk up and down stairs, check that props and pickups land on their anchors, and confirm the enemy can navigate the room.

If the room looks correct but players fall through it, the visual is fine and the gameplay collider is missing or misplaced. Fix the collider on the gameplay prefab, not by adding a complex collider to the FBX source.

## Add or replace a door model

Open `Ordinary Door.prefab` or `Exit Door.prefab`. The moving panel is under `Hinge`, and the FBX belongs under `Hinge/FBX Visual`. Keep the Hinge transform where it is. This supplied mesh needs the same X-axis conversion as the rooms and a 180-degree local Y turn so the door extends from the preserved hinge into the doorway. Keep `DoorInteractable`, the door collider and the networking components on the existing prefab. Test opening, closing, the exit lock, the open-away-from-player direction and the door sound with two players.

## Materials and safe edits

Use the shared materials in `Assets/Game/FBX/Materials` or the existing URP materials in `Game/Materials`. Avoid making a new material for every room instance. Do not edit the FBX source asset to add collision, gameplay scripts or navigation data; those belong on the gameplay prefab. If a model needs a visual correction, use the child transform or a prefab-level material override.

## Final checklist

- The new prefab is referenced by `MansionContentSet`.
- The root, connectors, anchors, RoomModule and colliders are still present.
- The visual floor, doorway and ceiling line up with the gameplay bounds.
- The FBX visual is enabled and any obsolete placeholder renderer is disabled.
- Doors rotate around their existing Hinge and still synchronize in multiplayer.
- A generated round has no overlap, missing camera or navigation errors.
- The model is under the Mansion content folders and is not mixed into Slaughterhouse.
