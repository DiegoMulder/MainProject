# Start here: expanding your Unity game

These beginner tutorials explain the systems actually present in this URP project. Start with the basics, then follow the recipe for what you want to add.

1. [Unity basics, starting the game, and settings](Tutorials/00-Getting-Started.md)
2. [Adding rooms, hallways, stairs, and floors](Tutorials/01-Rooms-and-Stairs.md)
3. [Adding furniture, item surfaces, and hiding places](Tutorials/02-Furniture-and-Hiding.md)
4. [Adding pickups, objectives, medkits, and item actions](Tutorials/03-Pickups-and-Objectives.md)
5. [Changing players, enemies, doors, and sound](Tutorials/04-Player-Enemies-and-Doors.md)
6. [Multiplayer, voice, menus, testing, and troubleshooting](Tutorials/05-Multiplayer-and-Troubleshooting.md)
7. [Closets, gameplay noise, heartbeat, and PSX visuals](Tutorials/06-Closets-Noise-and-Presentation.md)
8. [Scenes, predicted movement, downed crawling, impact audio and replaying rounds](Tutorials/07-Scenes-Movement-and-Downed-Players.md)

9. [Options, difficulty, walkie talkies, safe drops and exit clearance](Tutorials/08-Options-Difficulty-Radios-and-Safe-Drops.md)

The tutorials distinguish Inspector changes from optional programming examples. Code printed in a tutorial does not run until you create and attach the described script. Optional code examples remain examples; the closet, noise, heartbeat, and PSX systems described in tutorial 7 are implemented in the project.

## Where to play

**Multiplayer mansion:** open **Assets/Game/Scenes/MainMenu.unity**. Host a lobby, let others join by code, then start as host. The mansion is created when the round starts; it is normal not to see it in the scene editor.

**Original offline controller course:** open **Assets/AI-Tools-DEV/Scenes/CharacterController_Test.unity** and press Play. Use this to experiment with movement and offline items without an online session.

The offline player is **Assets/AI-Tools-DEV/Prefabs/SurvivalPlayer.prefab**. Multiplayer uses **Assets/Game/Prefabs/Player/Network Survivor.prefab**. Editing one does not automatically edit the other.

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
| Hold V | Transmit through a powered Walkie Talkie in your inventory |
| Escape | Mansion pause menu; release cursor in the offline course |
| Tab while spectating | Change spectator target |

Click the Game view if it does not receive your input. Multiplayer pause is local: the enemy and other players continue moving.

## Where everything belongs

All paths below start at Assets.

| Folder | What belongs here |
| --- | --- |
| Game/Scenes | MainMenu and Game; OutdoorsScene is the preserved original scene |
| Game/Scripts | Gameplay code, grouped into Player, Enemy, Items, Interaction, Multiplayer, Procedural Generation, Audio, Objectives, Presentation and UI |
| Game/Prefabs/Player and Game/Prefabs/Enemy | Playable survivor and mansion stalker |
| Game/Prefabs/Items | Flashlights, medkits, objective seals and new pickups |
| Game/Prefabs/Rooms, Hallways and Stairs | Modules used by the mansion generator |
| Game/Prefabs/Props | Furniture and interactive closets |
| Game/Prefabs/Doors | Ordinary doors and the exit |
| Game/Prefabs/Game Management | Session, lobby and round prefabs |
| Game/Data | MansionSettings, visual settings and network-prefab registries |
| Game/Audio | Sound clips and license information |
| Game/Materials, Shaders and Input | Shared visual assets and input actions |
| Game/Resources/Flashlight | Runtime fallback beam and switch sound, loaded by name |
| Game/Tutorials | Beginner guides for playing and expanding the game |
| AI-Tools-DEV/Scenes and Prefabs | Offline controller course, example items and test beam |
| AI-Tools-DEV/Materials and Data | Practice-course materials, exposure profile and previous unused default prefab registry |
| AI-Tools-DEV/Editor | Setup utilities and editor validation commands |
| AI-Tools-DEV/Scripts/Validation | Optional gameplay checks and the multiplayer development probe |

**Permanent organization rule:** anything created solely for AI/MCP/Codex testing, debugging, inspection or development automation belongs under **Assets/AI-Tools-DEV**. Anything required by the playable game belongs under **Assets/Game**. Use the existing categories; create a new folder only when it helps group related files. Keep folders shallow. Leave Unity/package-owned content in its existing location.

To add a pickup, start in **Game/Prefabs/Items** and follow the pickup tutorial. To add a room, hallway or staircase, duplicate a matching prefab in its category, then add it to **Game/Data/MansionSettings.asset**. Moving a prefab into a folder alone does not register it with generation or networking.

## Do I need the development files?

**To play, open Game/Scenes/MainMenu.unity.** You do not need to run checks or open the practice course first. AI-Tools-DEV keeps useful bug-checking and setup tools available without mixing them with game content. The normal build enables MainMenu and Game; the offline course stays available in Unity but is disabled in Build Settings. Enable it deliberately if you want a development build of the course.

Editor scripts do not ship in the player. The multiplayer probe is compiled only for the editor or development builds and runs only when explicitly requested through its command-line option. A development folder name alone does not exclude scripts from builds.

The old MansionFeedbackContent and MansionFeedbackRoundContent builders were removed after their generated content had been saved. Their removal does not remove closets, stairs, medkits or presentation features. The remaining setup tools can modify content, so use the tutorials for normal expansion rather than rerunning initial setup.

There are two beam prefabs. **Game/Resources/Flashlight/FlashlightBeam.prefab** is the game's runtime fallback. **AI-Tools-DEV/Prefabs/Effects/FlashlightBeam.prefab** belongs to the offline course and its example flashlight. Both have working references. Resources is a special Unity folder: moving something inside it also requires updating its Resources.Load path in code.

Move assets in Unity's Project window so their metadata and references move with them. Netcode's generated default registry is configured to stay in **Game/Data/DefaultNetworkPrefabs.asset**. The mansion session uses the authored **Game/Data/NetworkPrefabs.asset**.

Sound sources and license information remain in [Audio/SOURCES.md](Audio/SOURCES.md), with original license texts beside the clips.
