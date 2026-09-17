# Start here: expanding your Unity game

These beginner tutorials explain the systems actually present in this URP project. Start with the basics, then follow the recipe for what you want to add.

1. [Unity basics, starting the game, and settings](Mansion/README.md)
2. [Adding rooms, hallways, stairs, and floors](Mansion/Tutorials/01-Rooms-and-Stairs.md)
3. [Adding furniture, item surfaces, and hiding places](Mansion/Tutorials/02-Furniture-and-Hiding.md)
4. [Adding pickups, objectives, medkits, and item actions](Mansion/Tutorials/03-Pickups-and-Objectives.md)
5. [Changing players, enemies, doors, and sound](Mansion/Tutorials/04-Player-Enemies-and-Doors.md)
6. [Multiplayer, voice, menus, testing, and troubleshooting](Mansion/Tutorials/05-Multiplayer-and-Troubleshooting.md)

The tutorials distinguish Inspector changes from optional programming examples. Code printed in a tutorial does not run until you create and attach the described script. These documentation changes do not change gameplay.

## Where to play

**Multiplayer mansion:** open **Assets/Scenes/MainMenu.unity**. Host a lobby, let others join by code, then start as host. The mansion is created when the round starts; it is normal not to see it in the scene editor.

**Original offline controller course:** open **Assets/Scenes/CharacterController_Test.unity** and press Play. Use this to experiment with movement and offline items without an online session.

The offline player is **Assets/SurvivalFP/Prefabs/SurvivalPlayer.prefab**. Multiplayer uses **Assets/SurvivalFP/Mansion/Prefabs/Network Survivor.prefab**. Editing one does not automatically edit the other.

## Controls

| Input | Action |
| --- | --- |
| WASD / arrow keys | Move |
| Mouse | Look |
| Hold Left Shift | Sprint forwards, including modest diagonals |
| Hold Left Ctrl or C | Crouch; overhead obstacles prevent standing |
| Space | Jump |
| E | Interact with the targeted pickup, door, exit, or downed teammate |
| Left click | Use the equipped item's action, if it has one |
| 1 / 2 / 3 or mouse wheel | Select an inventory slot |
| Q | Drop selected item |
| F | Toggle a flashlight carried in any slot |
| Escape | Mansion pause menu; release cursor in the offline course |
| Tab while spectating | Change spectator target |

Click the Game view if it does not receive your input. Multiplayer pause is local: the enemy and other players continue moving.

## Useful folders

| Path | Purpose |
| --- | --- |
| Assets/SurvivalFP/Mansion/MansionSettings.asset | Generation settings, item counts, gameplay prefab references |
| Assets/SurvivalFP/Mansion/Rooms | Building blocks for the mansion |
| Assets/SurvivalFP/Mansion/Props | Furniture choices |
| Assets/SurvivalFP/Mansion/Prefabs | Multiplayer player, items, doors, enemy, lobby, and round |
| Assets/SurvivalFP/Mansion/NetworkPrefabs.asset | Registry of prefabs the server can spawn for clients |
| Assets/SurvivalFP/Pickupables | Original offline item examples |
| Assets/Scripts/SurvivalFP | Runtime C# scripts |
| Assets/Editor/SurvivalFP | Editor builders and validation utilities |

Sound sources and license information remain in [Audio/SOURCES.md](Audio/SOURCES.md), with original license texts beside the clips.
