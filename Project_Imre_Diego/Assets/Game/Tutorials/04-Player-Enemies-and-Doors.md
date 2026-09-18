# Player tuning, enemies, doors, materials, and sound

[All tutorials](../README.md) · [Multiplayer and troubleshooting next](05-Multiplayer-and-Troubleshooting.md)

## Recipe: change movement or stamina

1. Stop Play Mode and open **Assets/Game/Prefabs/Player/Network Survivor.prefab**.
2. Select its root. Find Player Movement for speeds, acceleration, jumping, gravity, crouching, and step traversal.
3. Change one field, save, and start a new round. For example, slightly reduce Sprint Speed to make escape harder.
4. Find Player Stamina for capacity, drain, regeneration, delay, and the exhausted restart threshold. Change those independently from speed.
5. Test a full sprint until exhausted, recovery, stairs, crouching under a table, and a jump near an edge.
6. Test on host and client so authority/presentation issues are visible.

Edit **Assets/AI-Tools-DEV/Prefabs/SurvivalPlayer.prefab** separately if you also want the same change in the offline course. The two player prefabs are separate templates.

The values shown on an existing prefab win over initial values written in a script. For example, the script's initial sprint drain is 10, while the imported player's authored drain is 7.5. Changing a field initializer in C# does not normally overwrite already serialized prefab values. Always inspect the prefab you actually spawn.

| Component/field | What changing it does |
| --- | --- |
| PlayerMovement: Walk/Sprint/Crouch Speed | Forward speed for each gait |
| Backwards/Strafe Speed | Directional limits at walking gait |
| Acceleration/Deceleration | How quickly movement starts/stops |
| Air Control | Amount of steering while airborne |
| Standing/Crouching Height | Physical height; recheck hiding and door clearance |
| Jump Height | Desired jump height |
| Gravity | Downward acceleration; existing convention uses a negative value |
| Coyote Time | Brief jump allowance just after leaving an edge |
| Jump Buffer | Remembers a jump pressed shortly before landing |
| Step Height | Height of small ledges the motor can traverse |
| CharacterController: Radius/Slope Limit | Capsule width and climbable slope angle |

Keep the player root at unit scale with its origin at the feet. To change step traversal, use PlayerMovement > Step Height: the motor establishes the controller's step offset at startup. Editing only CharacterController's step offset is not enough. Keep ground probes short rather than enlarging them to disguise gaps or overly tall stairs.

Multiplayer prediction is implemented. Moving-platform carrying and a larger inventory would require additional work rather than simply changing these fields.

## Recipe: reduce camera motion or change the view

1. Open the appropriate player prefab.
2. Find its PlayerCamera component in the camera hierarchy.
3. Set Motion Scale closer to 0 to reduce bob, sway, and landing motion. Zero removes those procedural motion effects.
4. Set your own FOV in Options. Change starting FOV defaults in Game/Resources/Settings/Local Settings Defaults.asset. Sprint Fov Increase remains a separate PlayerCamera field.
5. Save and test walking, sprinting, crouching, looking around, and landing.

The Options menu saves sensitivity, FOV and look smoothing locally. These preferences override the corresponding starting camera settings. Motion Scale still controls bob/sway separately. See [settings and camera smoothing](08-Options-Difficulty-Radios-and-Safe-Drops.md) for the defaults asset and extension instructions.

Keep only the local player's camera and AudioListener active. The network player already handles local/remote presentation; adding another always-enabled camera or listener to a model can break this.

## Recipe: replace the flashlight appearance or tune its beam

1. Duplicate **Prefabs/Items/Flashlight.prefab**.
2. Preserve PickupItem, PlayerFlashlight, its action adapter, network components, light, and required references.
3. Change the mesh/material and collider. Tune the fields on PlayerFlashlight and its light as appropriate.
4. Register the duplicate in NetworkPrefabs.asset and assign MansionSettings > Flashlight.
5. Start a new round; this is the flashlight given to spawned players.
6. Test F from another inventory slot, left-click while equipped, aiming, dropping, and the other player's view.

F only operates a flashlight actually carried in inventory. The held beam aims toward the camera target; a dropped beam follows the item. The authored flashlight intensity differs from the C# initializer, so judge the actual prefab in URP rather than copying an old HDRP intensity value.

## Recipe: tune the enemy

1. Open **Prefabs/Enemy/Mansion Stalker.prefab**.
2. On EnemyPerception, adjust Sight Distance, Field Of View, Hearing Multiplier, and Obstacles.
3. On EnemyController, adjust Roam Speed, Chase Speed, Kill Distance, and timing fields.
4. Change one category at a time and test in a simple layout.
5. Test being unseen, walking versus sprinting nearby, visible chasing, losing sight around a corner, entering a closet, and stair traversal.

| Field | Meaning |
| --- | --- |
| Sight Distance | Maximum visual detection distance |
| Field Of View | Width of the vision cone in degrees |
| Eyes | Sight origin; assign an appropriate child transform for a different model |
| Obstacles | Layers that can obstruct sight; excluding wall layers can permit seeing through them |
| Hearing Multiplier | Scales the enemy's response range to gameplay noise |
| Roam/Chase Speed | Travel speed while wandering/pursuing |
| Kill Distance | Catch-distance field; the current catch normally downs a living player |
| Perception Interval | Time between sensing updates |
| Lost Sight Delay | Brief persistence before leaving the direct chase |
| Investigation/Search/Idle Duration | Time spent in those phases |
| Memory Duration | Duration of remembered player knowledge relevant to a closet |

The states are Idle, Roam, Investigate, Chase, and Search. Seeing a living player starts pursuit. Losing sight leads toward the last known position and then a bounded search. Hearing creates investigation behavior. Closet evidence uses the same state machine; ordinary tables have no special hiding behavior.

## Recipe: create another enemy appearance

1. Duplicate Mansion Stalker and rename it, for example, Butler Stalker.
2. Replace visual children while retaining EnemyController, EnemyPerception, NavMeshAgent, NetworkObject, and network movement components.
3. Reposition Eyes and size the collider/agent to suit the model. Keep the agent disabled in the saved prefab, matching the original; server spawning enables it after navigation exists.
4. Save/register the new network prefab.
5. Assign it to MansionSettings > Enemy.
6. Test doors, stairs, narrow paths, and catching distance.

The current round spawns **one enemy from the Enemy reference**. Registering multiple variants does not randomly choose or spawn them all. Multiple enemies or a weighted enemy catalog requires modifying RoundManager's server-side spawning. A new animated mesh also needs its own Animator/controller; the supplied movement code does not automatically provide animation clips for any imported model.

## Downing, death, and revival tuning

Open Network Survivor and find NetworkPlayer > Bleed Out Duration. It defaults to 300 seconds. This is the server-owned time a downed player has before full death, not a revival-channel duration.

A downed player retains inventory and can crawl slowly, but cannot sprint, jump, stand or use items. NetworkPlayer > Downed Crawl Speed controls crawl speed. Enemy vision/hearing ignores downed players. A living teammate carrying a medkit can revive them with E. Full death/disconnect makes required objectives recoverable. Dead/escaped players spectate living players.

If nobody Alive remains, there is no possible reviver. The round ends without waiting out every downed timer. If someone escaped it can resolve as a win; otherwise it resolves as a loss. Increasing Bleed Out Duration does not change that rule.

If replacing the player model, preserve the DownedInteractable setup and its assigned collider. Its enabled state is managed by life state; do not leave a large always-active target collider around every living player.

## Recipe: create a door variant

1. Duplicate **Prefabs/Doors/Ordinary Door.prefab**.
2. Open it and inspect DoorInteractable's Hinge reference. That transform is the pivot that rotates.
3. Replace the visual leaf while keeping its pivot at the hinge edge. A centre pivot makes it spin around the middle.
4. Keep the leaf collider beneath the interactable hierarchy, sized to the new leaf.
5. Set Open Angle and Speed. Check that it swings into clear space.
6. Keep Interactable enabled for player use. Set Start Open only if desired.
7. Assign Audio Source and Sound if the door should play a clip; Noise Radius separately controls AI hearing.
8. Preserve the supplied exclusion of the swinging leaf from static navigation. A leaf baked as a permanent obstacle can block the route even when open.
9. Save/register the new prefab and assign MansionSettings > Door.
10. Test E from both sides and let the enemy approach it. The server enemy can open ordinary doors.

The exit uses **Exit Door** and MansionSettings > Exit separately. Duplicate that prefab when changing exit visuals, and preserve ExitDoor's objective/deposit behavior. An ordinary door renamed Exit does not become an objective gate.

The exit's PlayerEscaped event is an extension point for progression/presentation. Persistent rewards would need deliberate storage and server-side integration; they are not automatically saved by that event.

## Sound you hear versus sound the enemy hears

These are independent systems:

- **AudioSource/PlayerAudio:** audible clips for the people playing.
- **GameplayNoiseSystem:** gameplay events used by enemy hearing.

Turning down a footstep clip does not automatically make footsteps quieter to the monster. Raising a noise radius does not turn up speakers.

On Network Survivor > MovementNoiseEmitter, tune Crouch Radius, Walk Radius, Sprint Radius, and Stride. Noise is emitted from actual movement. The supplied radii are 2, 7, and 14 metres, so crouching is quieter than sprinting.

On a pickup > ImpactNoiseEmitter, Minimum Speed ignores gentle contacts, Cooldown prevents rapid repeated impact events, Reference Speed sets the strongest impact, and Minimum/Maximum Radius control AI hearing. Impact Sounds is a configurable array; Minimum/Maximum Volume and Pitch Range control audio. A very low threshold can make resting objects overly noisy.

To replace player clips, inspect PlayerAudio on the appropriate player prefab and assign new AudioClip assets to its clip fields. Keep a few variations for footsteps. Then test volume and timing separately from enemy hearing. Sources/licenses for bundled audio remain in the Audio folder.

Custom server-side noises call GameplayNoiseSystem.Emit(position, radius, category, source). Supply the responsible player's GameObject when the sound should identify a hidden player. See the bell example in the pickup tutorial for a complete use action.

## Importing a new model into this URP project

1. Import the model and textures into a clearly named Assets subfolder.
2. Create or assign materials using a URP-compatible shader, such as Universal Render Pipeline/Lit.
3. Assign textures to the appropriate material fields, then apply the material to the mesh renderer.
4. Put the model as a visual child of a working room/item/enemy prefab when possible. Preserve the root gameplay components.
5. Check scale against the approximately 2-metre standing player and adjust visual children/colliders together.
6. Test in the actual mansion lighting.

Pink materials usually indicate an unsupported or missing shader. Inspect that material before changing gameplay scripts. The project is URP; installing HDRP is not part of adding an item or room. For glowing materials, emission makes the surface look bright, while a Light component provides illumination of nearby geometry.

See [the current noise/presentation tutorial](06-Closets-Noise-and-Presentation.md) for flashlight clicks, impact AudioClips, closet hearing, heartbeat distances, and PSX configuration.
