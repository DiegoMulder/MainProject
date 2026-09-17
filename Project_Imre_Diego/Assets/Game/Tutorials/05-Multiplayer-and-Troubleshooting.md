# Multiplayer, voice, UI, testing, and troubleshooting

[All tutorials](../README.md)

## How this game's multiplayer is arranged

MainMenu contains the menu and pre-game lobby panel. Game contains the actual gameplay. The host creates a private online lobby/session; players join by code. Start Game loads Game on all peers, then creates the procedural layout, navigation, items, players and enemy. The Multiplayer Session prefab and LobbyRoster persist; scene-local cameras, UI and round content do not. See tutorial 07 for the complete scene and restart flow.

The server owns shared gameplay. Clients receive the layout and reconstruct static geometry, while network-spawned objects synchronize interactive state. You do not need to create a separate scene for each generated mansion floor.

MainMenu has a Session Bootstrap referencing Prefabs/Game Management/Multiplayer Session.prefab. That prefab contains NetworkManager, GameSession and ProximityVoice. Each scene has its own camera and GameUI. GameSession references the lobby and round prefabs. Mansion Round references MansionSettings. NetworkManager references NetworkPrefabs.asset through its network prefab lists.

When an Inspector change seems ignored, follow this reference chain and confirm the live scene uses the asset you edited. Making a second settings asset does not automatically switch the round to it.

## Recipe: test host and client

1. Save your work and open MainMenu.
2. Make a standalone development build using Unity's Build Profiles, with MainMenu as the entry scene and Game also included.
3. Run the editor and one standalone copy, both from the same updated project.
4. In the editor, enter a name and choose Host Game.
5. In the standalone, enter another name and join using the displayed code.
6. Verify both names and have the host start.
7. Check the same rooms, props, doors, and items appear on both peers.
8. Have both players try the same pickup. Only one should obtain that instance.
9. Test doors, drops, objective deposits, downing/revival, spectating, and leaving.
10. Use Back to Lobby as host, then Start Game again without reconnecting. Also test an individual client choosing Main Menu.

A stale build can have old prefabs, different scripts, or an old network behavior layout. Rebuild after adding network components or changing shared content. Two copies of a prefab should not have manually edited identities.

Names are sanitized to at most 20 supported characters (letters, digits, spaces, hyphens, underscores), with Survivor as the empty fallback. They are metadata, not ownership identifiers. The session locks when play begins. The client leaves only its own participation; the host leaving ends the session for everyone.

The old direct-address development methods GameSession.Host() and Join(address) still exist, but the current menu presents online code-based sessions. There is no current menu IP field to fill out.

## Online service setup and diagnosis

The project uses Unity Multiplayer Services for private sessions/Lobby and Relay, NGO for game replication, and Vivox for voice. Keep these pointed at the same intended Unity project.

1. In Unity's project Services settings, confirm the project is linked to the correct organization/project and you are signed in with an account permitted to manage it.
2. In that project's Unity Dashboard, check its Authentication and multiplayer service configuration. The game's login uses anonymous authentication.
3. Check Vivox separately for voice access and configuration.
4. Let the Vivox editor integration retrieve the configuration for that project, then restart the play session/rebuild as needed.
5. Test using the host/join steps above and read the actual Console/service status if a step fails.

Provider dashboard labels can change; these are the services used by this project's code, not a promise about every current dashboard button. Do not paste private signing keys or service-account credentials into scripts or prefabs.

The current project configuration has successfully hosted/joined over Relay and connected the positional Vivox channel. The earlier missing-server initialization error no longer reproduces with this configuration. A connected channel does not prove microphone quality or audible distance falloff: test those with two people using separate devices. If configuration retrieval returns HTTP 403 on another account or project, resolve service access before tuning microphones or distance.

For same-machine development, the editor and standalone use separate authentication profiles. Do not change them to a shared simultaneous identity while diagnosing multi-peer sessions.

## Recipe: tune proximity voice

1. Open Prefabs/Game Management/Multiplayer Session.prefab outside Play Mode.
2. Select its root and find ProximityVoice.
3. Adjust maximum audible distance, conversational distance, falloff strength, and speech threshold as desired.
4. Save the prefab and test with two configured peers, preferably separate machines and headphones.
5. Confirm near conversation, increasing distance, turning/moving, muting, and leaving the round.
6. Check the Options menu's microphone mute and input/output levels on both peers.

Vivox handles microphone capture, encoding, transport, and positional audio. The game supplies position/orientation updates. Alive and Downed players can talk; Dead/Escaped players leave positional voice.

**Voice volume and monster hearing are separate.** On Network Survivor > NetworkPlayer, Voice Noise Radius and Voice Noise Interval control how speech activity alerts the enemy. The server chooses the source position/radius and throttles reports. Changing Vivox's audible distance does not change the monster's hearing radius automatically.

The local speech-activity threshold is a detector setting. Too sensitive can report background noise; too insensitive can miss quiet speech. Test after real voice service connectivity works. A simulated speech-activity event checks gameplay signaling but does not verify a working microphone or real voice attenuation.

## Recipe: change menus or HUD text

The current GameUI draws menus and HUD using C# IMGUI. There is no equivalent Canvas hierarchy full of editable menu buttons to locate.

1. Open Assets/Game/Scripts/UI/GameUI.cs in your code editor.
2. Search for the exact visible label you want to change.
3. Change that text first, keeping the surrounding method calls intact.
4. Save, wait for Unity compilation, and check the Console.
5. Play and visit the affected menu/state. Check text fits at different window sizes.

Main menu, lobby, pause/options, downed timer, spectator, round failure, and completion screens are managed here. Keep the scene's menu-camera reference assigned. The pause menu must remain local; setting Time.timeScale to 0 would not be an appropriate way to pause a live shared round.

For a visual UI workflow, replacing GameUI with a Canvas/UI Toolkit interface is a code integration task: new buttons must call the existing session/actions and display network state. Creating visual buttons alone does not replace the existing UI logic.

For a replacement stamina display, PlayerStamina exposes Current, Maximum, Normalized, and Changed. PlayerMovement exposes State/StateChanged and Landed for animation/presentation. Subscribe/unsubscribe events appropriately so repeated rounds do not accumulate listeners.

## A useful test order after adding content

1. **Compile:** no new red C# errors before pressing Play.
2. **Check references:** no missing prefab, component, hinge, light, or clip references required by the feature.
3. **Repeatable layout:** disable Random Seed and record the seed.
4. **Single-host behavior:** walk the route and exercise the new feature.
5. **Variation:** try multiple seeds and rotations; one successful layout is not enough for a generator change.
6. **Second peer:** compare geometry and interactive state, especially contested items.
7. **Lifecycle:** use/drop/consume, down/revive/die, leave, and start again as relevant.

You do not need to test every unrelated system after changing a text label. Match the test to what changed, but test shared gameplay on more than one peer.

For movement changes, save scene edits before using **Tools > Survival FP > Run Play Mode Validation**. For pickup changes, use **Tools > Survival FP > Validate Pickup Physics**. These exercise the original course and write results under the project's Logs folder. They do not by themselves prove a new multiplayer feature works.

Advanced developer utilities live in Assets/AI-Tools-DEV/Editor/Validation (including MansionGeometryValidation) and Assets/AI-Tools-DEV/Scripts/Validation (including MansionFeedbackAcceptance). Some are code-driven integration tools rather than menu commands. They verify gameplay; you do not need to run them before playing. Start with the manual workflow above unless you are intentionally editing the test harness.

## Common problems, with concrete fixes to check

| Problem | First checks |
| --- | --- |
| Changes vanish when stopping Play | Edit the saved prefab/scene outside Play Mode, not a generated instance |
| Prefab exists but never appears | Add room to Rooms, furniture to Variants, or item to an actual spawner; registration alone does not spawn |
| Changed settings have no effect | Follow MainMenu > Session Bootstrap > Multiplayer Session prefab > Mansion Round > settings |
| New network item only appears on host | Registry entry, matching builds, NetworkObject.Spawn from server, required components |
| Item appears twice | Both peers may be locally instantiating it; spawn network items once on the server |
| Item falls through floor | Floor/item collider, spawn clearance, scale, convex dynamic mesh collider |
| E gives no pickup prompt | Actual collider under crosshair, range, layer mask, solid rather than trigger-only target, free inventory slot |
| Item has a name but left-click does nothing | IPrimaryUse component and Primary Use reference; selected slot; no action is valid for ordinary collectibles |
| Clients miss an item's action | Action changed server-local visuals without replicated state/event |
| Key revives a teammate | MedkitItem remained on a duplicated medkit prefab |
| 'Not enough reachable surfaces' | Objective + medkit capacity after excluding starting room, absent props, and rejected approaches |
| Navigation failure | Solid route, accurate room bounds, clear anchors/doorway approaches, compatible stairs |
| Enemy sees through walls | Wall colliders and EnemyPerception obstacle layers |
| Hiding never works | Closet occupancy/positions, solid door colliders, and whether enemy saw entry or heard the occupant |
| Everyone downed ends round immediately | Expected: no living reviver remains |
| Host/join fails | Actual online error, project link, service access, network connectivity, same project/build |
| Lobby works but voice does not | Vivox access/configuration first, then mute/devices/levels; check the actual service status/error |
| Pink materials | Missing/unsupported shader; use URP-compatible materials |
| Several AudioListener warnings | Extra enabled camera/listener on a model or remote player |
| Mouse sensitivity seems unchanged | Locally saved Options preference may override the prefab value |

## When a feature needs code

Trace the responsibility instead of adding all logic to the player controller:

| Responsibility | Starting point |
| --- | --- |
| Room selection/placement | Procedural Generation/MansionLayoutPlanner.cs |
| Constructed geometry and navigation | Procedural Generation/MansionWorld.cs |
| Shared spawning and round lifecycle | Multiplayer/RoundManager.cs |
| Lobby/session entry and leaving | Multiplayer/GameSession.cs |
| Player authority/life state | Multiplayer/NetworkPlayer.cs |
| World/held item state | Multiplayer/NetworkPickup.cs |
| Inventory slots and hand attachment | Player/PlayerInventory.cs |
| Item use capability | Items/PickupItem.cs and IPrimaryUse |
| Interaction ray/prompt | Interaction/PlayerInteraction.cs and IInteractable |
| Objective deposit/escape | Objectives/ExitDoor.cs and ObjectiveItem.cs |
| Enemy decisions and vision | Enemy/EnemyController.cs and EnemyPerception.cs |
| Gameplay hearing events | Audio/GameplayNoise.cs |
| Menus/HUD | UI/GameUI.cs |

Paths in this table are relative to Assets/Game/Scripts. Extend one responsibility at a time and preserve server authority for shared state. Start by making a small working variant, then expand its rules once both peers agree on the simple version.
