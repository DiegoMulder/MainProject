# Character animation, new maps, enemy kills and radio audio

[Back to all tutorials](../README.md)

## Start with the updated version

This update changes network behaviours and prefab registrations. Build with **Tools > Survival FP > Build Current Multiplayer Game**, then give every player the entire **Builds/Current** folder. Protocol Version is now **3**. Mixing an older build with this version is intentionally rejected.

Your current difficulty values have been preserved. Map and difficulty are independent: choose the map and difficulty in the lobby before pressing Start Game. Only the host can change either setting.

## Where the character animation lives

Open **Game/Prefabs/Player/Network Survivor**. Its **Business_Man** child contains the Animator using **Businessman_Controller**, under Game/Player/business-man-low-polygon-game-character/Animation. Keep that Animator enabled. The owner sees the world through the first-person camera; only that owner's body rendering is hidden. Other players see the animated body normally. The old capsule-shaped Survivor body is no longer the displayed model.

On the prefab root, **Player Animation Driver** points to this Animator. It reads the existing player motor and replicated movement. It does not move the player or create a second movement controller.

| Existing parameter | Type | Meaning |
| --- | --- | --- |
| Blend | Float | Actual horizontal speed divided by the motor's sprint speed; smoothed from idle toward running |
| Crouch | Float | Crouched horizontal speed divided by crouch speed; blends crouch idle and moving clips |
| IsCrouching | Bool | Chooses the crouched locomotion tree |
| Jump | Bool | An actual accepted jump is in progress; cleared by landing |
| IsTalking | Bool | Recent voice activity, from proximity or radio speech |
| IsDown | Bool | Player is currently Downed |
| Down | Float | Actual crawl speed divided by configured downed crawl speed |

**Damping** on Player Animation Driver adjusts how quickly speed floats settle. Smaller values respond faster. Movement speeds themselves still belong to Player Movement; changing animation damping does not change walking speed.

The movement state already travels through NetworkPlayer. PlayerAnimationDriver adds server-written jump and talking values; it derives Animator floats locally from the shared motor state. There is no need to add a second NetworkAnimator and duplicate this traffic. The local owner uses its predicted movement for a responsive view. A momentary grounding loss at a stair edge does not invoke the motor's Jumped event and therefore does not trigger Jump.

### Fixing or adding transitions

Open the Animator window with Businessman_Controller selected. The base layer has the existing standing Blend Tree, crouch Blend Tree, downed Blend Tree and Rig_jump states. Downed entry transitions require IsDown; the previous unconditional transitions have been removed. Crouching and jumping do not wait for a long idle clip to finish. Landing returns from jump, and revive clears IsDown.

If you replace a clip, keep its parameter types and transition conditions. Test idle, walk, run, crouch, jump, landing, crawl and revive. Do not rename a parameter without updating the driver too.

### Downed animation: what to check

The server writes **NetworkPlayer.Life = Downed**. Each peer's PlayerAnimationDriver reads that life state and sets the local Animator's **IsDown** bool. **Down** is a speed blend: zero selects CrawlingIdle, increasing toward one selects Crawling. Owners use predicted motor speed; the server uses its motor simulation; other observers use the replicated horizontal velocity. Reviving changes Life back to Alive and clears IsDown. The Animator, controller, avatar and rig stay enabled and assigned throughout.

To test this yourself, keep another teammate alive so the round does not end immediately. Have the host become downed while the client watches, then reverse the roles. Watch the actual body enter its low pose, stay low while still, crawl while moving, and return to standing after revival. Repeat after switching items. In the Animator window, check both the highlighted downed Blend Tree and the IsDown/Down values.

The current matching-build checks evaluated CrawlingIdle and Crawling and verified shoulder-bone pose changes on both peers, including repeated revivals. The reported static T-pose was not reproduced during those checks; no confirmed disabled Animator, missing controller, invalid avatar or broken clip binding was found. Do not replace the existing animated clips with a static fallback. If it occurs again, record which build each player uses, which player is downed and the remote Animator's active state. Always distribute the entire newly built folder together; protocol 4 rejects older incompatible players.

### Talking and the face

The **Mouth Layer** uses **Talking Face.mask**. Only transforms below the Head bone are enabled in that mask; the talking animation cannot move the hips, arms or legs. The driver fades this layer in while speech is detected and fades it back to zero after speech stops. A short 0.25-second release prevents tiny gaps between words from flickering the face.

ProximityVoice uses Vivox's SpeechDetected and AudioEnergy information from the existing microphone session. Muting the microphone stops activity reports. Radio and proximity share one final speaking result, so they cannot set contradictory talking states. Dead, escaped and grabbed characters do not keep a living talking state.

The crouch/crawl clips now include missing baseline facial curves copied from the supplied idle face. The zero-duration crawl-idle pose also has a usable one-second static duration. Normal clips regain control when the talking layer weight reaches zero. If you add a new face bone, include it in the mask and provide an appropriate baseline curve in non-talking clips.

## Make an item sit in the right hand

On the Business_Man rig, expand **Rig > Hips > Spine > Spine 1 > Chest**, then the right arm chain until **Hand_R**. The child named **Right Hand Item Hold** is the remote item anchor. Player Inventory's **Right Hand Anchor** field references it.

This imported rig uses a scale of 100 on the hand hierarchy. The hold point compensates for that scale. Keep the supplied anchor scale unless you change the rig; otherwise a one-metre item could appear 100 metres large.

The owner keeps using **Item Hand Anchor** under the first-person camera. Other clients attach the same replicated item object to Right Hand Item Hold. No extra gameplay pickup is spawned. The bone moves the object automatically during animations.

For a new item:

1. Duplicate a suitable prefab under **Game/Prefabs/Items**.
2. Keep PickupItem, its physics components and the required network components.
3. In **Pickup Item**, adjust **Right Hand Position**, **Right Hand Euler** and **Right Hand Scale**. These values belong to the item, so a flashlight and medkit can use different grips.
4. For the first-person view, use the existing **Held Euler Angles** and **First Person Scale** fields. These are independent of the remote grip.
5. Register the new network prefab once in Game/Data/NetworkPrefabs.asset.
6. Test with a second player. Equip it, walk, crouch, jump and switch slots. Inspect the right hand from the other player's view.

### Why the host could appear offset

Netcode applies a spawn transform after NetworkPickup's spawn callback. The item was already attached at that point, so the later server transform could overwrite the local grip. On the host, that server pose is the owner's first-person camera pose; another survivor uses the animated hand on the server. This made the error appear ownership-dependent. Capturing the original scale only when first equipped also allowed a received presentation scale to become the supposed original size.

PickupItem now captures the authored scale in Awake, before network spawn synchronization. PlayerInventory explicitly selects first-person or third-person presentation. After animation and network updates, the held item applies its absolute local position, rotation and scale to that anchor. Repeated updates reset the same pose; they do not add offsets or create another pickup. Claim also no longer applies the same location twice on the server.

Offsets use the hold point's local axes. Start with small position changes, such as 0.02 metres. The original world size is restored when the item drops.

## Map selection and content

The map assets are:

- **Game/Data/Maps/Mansion/MansionMapDefinition.asset**: map definition pointing to the existing Game/Data/Maps/Mansion/MansionContentSet.asset.
- **Game/Data/Maps/Slaughterhouse/SlaughterhouseMapDefinition.asset**: definition pointing to **SlaughterhouseContentSet.asset** in the same folder.
- **Game/Prefabs/Maps/Slaughterhouse**: separate Rooms, Hallways, Stairs, Props and Doors assets, including its own exit and hiding closet.

Mansion's existing folders now live under **Game/Prefabs/Maps/Mansion**, matching Slaughterhouse. These were moved in Unity with their GUIDs preserved; they were not recreated. No imported third-party character folders were moved. Slaughterhouse currently uses separate placeholder geometry and its own tile material, so you can replace it independently. Its room references and prop references do not point back to Mansion prefabs.

A **Map Definition** has a display name, content settings and an optional ambient colour. The content settings supply the room catalog, doors, exit, items, enemy prefab, floor settings and generation options. Furniture choices live in each map's room Prop Spawn Points. Hallways and stairs are RoomModules in the same weighted catalog; they do not need a second generator.

The lobby's map index is server-written. RoundManager copies the selected map's content, then applies the shared difficulty profile. Clients receive the map index and the same generated layout before building their local geometry. Returning to the lobby destroys the previous world, but leaves the lobby's selected map available to change.

### The two files per map are intentional

Think of MapDefinition as the map's label and ContentSet as its box of building pieces. The definition has the display name, ambient colour and one ContentSet reference. Only the ContentSet lists rooms, doors, exit and generation settings. You never maintain the same room list in both files. Both maps use the same two asset types; ContentSet's existing C# type is named MansionSettings for compatibility, but it configures either map.

The layout is now:

```text
Assets/Game/Data/Maps/
  Mansion/
    MansionMapDefinition.asset
    MansionContentSet.asset
  Slaughterhouse/
    SlaughterhouseMapDefinition.asset
    SlaughterhouseContentSet.asset

Assets/Game/Prefabs/Maps/
  Mansion/
    Rooms/  Hallways/  Stairs/  Props/  Doors/
  Slaughterhouse/
    Rooms/  Hallways/  Stairs/  Props/  Doors/
```

Closets belong to each map's Props folder; the exit belongs to Doors. Shared scripts, player, enemy and pickups stay outside these folders.

### What happened to Mansion Round?

It is now **Game/Prefabs/Game Management/Round Runtime.prefab**, the same asset with the same network identity. It contains NetworkObject, RoundManager and the shared procedural world builder. It has no Mansion-only rooms, lights or scene children, so a duplicate Slaughterhouse round is unnecessary. The selected MapDefinition provides the content and ambient colour. Lobby selection -> Game scene -> shared Round Runtime -> selected content -> generated world. Return to Lobby cleans that world before the next selection.

### Create a third map

1. Create **Game/Prefabs/Maps/YourMap** with the categories you need.
2. Duplicate Slaughterhouse or Mansion's room, hallway, staircase, furniture, door, exit and closet prefabs into your folder. Use Unity's Project window so every new asset gets its own identity.
3. Open each copied room. Replace every Prop Spawn Point variant with your copied furniture/closet assets. Copying a room alone does not automatically remap its furniture references.
4. Create **Game/Data/Maps/YourMap**. Duplicate a ContentSet there and name it **YourMapContentSet**.
5. Replace its entire Rooms catalog with your map's modules. Assign your own door and exit. Keep shared player/item/enemy prefabs where intended. Check the floor count and staircase compatibility.
6. Right-click in the Project window and choose **Create > Survival FP > Map Definition**. Save it beside the content as **YourMapMapDefinition**. Set its display name and assign YourMapContentSet.
7. Open **Game/Prefabs/Game Management/Lobby** and append the definition to its **Maps** array.
8. Open **Round Runtime** in the same folder and append it to its **Maps** array in exactly the same order. The index must mean the same map on both components.
9. Register new networked doors, exits and closets once in **NetworkPrefabs.asset**. Plain room meshes do not need network registration because peers reconstruct them from the shared layout.
10. Build, join with a second client, choose your map and play. Return to the lobby and switch back to verify cleanup.

Difficulty remains shared. The profile controls room, objective and enemy counts after the map is selected. Do not duplicate Difficulty.asset just to add a map.

## Enemy Kill sequence

Open **Game/Prefabs/Enemy/Mansion Stalker**. The **Enemy Kill Sequence** component references the enemy Animator and **KillCameraPoint**, parented to the existing animated head bone (CATRigHub002Bone001). This rig uses generic bone names rather than a humanoid Head assignment.

The existing **Kill trigger** enters the **Kill state**, using the supplied four-second Attack(3) clip. The state has no automatic exit that could cut the sequence short. The server watches that state's normalized animation progress; once it reaches the end, it resolves the victim and exits the state. Changing Animator speed changes the completion time naturally.

The flow is:

1. Server verifies the victim is Alive, connected, not already grabbed and close enough.
2. Enemy stops navigation, faces the victim and enters Kill. The victim cannot move or use inventory.
3. Victim's local camera follows the animated KillCameraPoint. Other players retain their own cameras.
4. The full Kill animation completes.
5. Victim becomes Downed. Kill-camera control is released back to the existing downed or spectator presentation.
6. Enemy enters **Stagger** for **Post Kill Stagger Duration**, default two seconds.
7. Normal AI resumes.

**Maximum Grab Distance** is a final server check; ordinary catch attempts still use EnemyController's Kill Distance and line of sight. The victim is not dramatically teleported into a fixed pose. Position the camera anchor in front of the head and point it toward the face if you replace the model. Keep root motion off unless you deliberately integrate animation-driven motion with navigation.

Kill and Stagger suppress perception, noise responses and new attacks for that enemy. Other monsters keep their own independent state. One victim cannot be grabbed by two enemies simultaneously.

### Delay the final defeat screen

Open **Game/Prefabs/Game Management/Round Runtime**. **Final Kill Game Over Delay** defaults to five seconds. It starts after the final kill animation completes, not when the grab begins. RoundManager tracks active kill sequences and existing team-failure rules. The end screen cannot appear while a teammate is still Alive or a kill presentation is still active.

The ordinary end-screen, Back to Lobby and Main Menu controls remain part of the existing GameUI/session flow.

### Why downed players are ignored

**Game/Scripts/Enemy/EnemyTargetRules.cs** contains the central CanTarget check. Vision, maintained target memory and new kill attempts use it. It requires an Alive, connected, spawned player who is not already grabbed. A Downed player crawling in front of an enemy remains ineligible. Completing a kill also clears the enemy's remembered player/closet references.

Downed players produce no AI hearing events, including proximity speech, radio speech and receiver noise. Teammates can still hear their proximity voice. Down/death/disconnect immediately clears the enemy's target and player memory, including during the lost-sight grace period. Independent environmental events such as a dropped object impact or a door still make noise.

## Open-mic radio use

Pick up a walkie, select its slot, and left-click to switch it ON. Speak normally: there is no V key or separate transmit action. Keep the powered radio equipped to transmit. Selecting another slot stops transmission immediately, although an Alive player can still receive through a powered radio carried in another slot. OFF stops both transmission and reception. Nearby listeners intentionally hear both clean proximity speech and filtered radio speech when their receiver is ON.

The session's **Proximity Voice** component uses the existing Vivox speech detector and **Speech Threshold**. **Speech Release Delay** (0.2 seconds) bridges small gaps between words. Only active speech refreshes the server's transmission state; stopping sends one stop event. If reports stop unexpectedly, the server expires transmission after 0.5 seconds. AI hearing events have their own slower **Noise Interval**. Power cues and their small noise radius are unchanged.

## Radio sound and received voice processing

On **Game/Prefabs/Game Management/Multiplayer Session**, find **Proximity Voice**:

- **Radio High Pass** removes low frequencies; default 350 Hz.
- **Radio Low Pass** removes high frequencies; default 3000 Hz.
- **Radio Distortion** adds mild grit; default 0.18.

Only the radio participant's received audio uses this effect chain. A Vivox participant audio tap silences that participant in the normal radio mix and sends its audio through a Unity AudioSource with high-pass, low-pass and distortion filters. Proximity audio retains its existing clean positional path. Both paths remain audible to close listeners with an ON receiver; there is no proximity-distance suppression of radio audio.

The radio AudioSource uses Voice volume, with Master applied by the listener. Power cues use the existing SFX volume bus and Master. Audio filtering does not determine AI hearing range.

On **Game/Prefabs/Items/Walkie Talkie**, find **Walkie Talkie Use**:

- **Power On Sounds / Power Off Sounds**: arrays of clips. The provided short tones make toggles audible immediately. Add variants; the server picks a variant and sends the same choice to peers. Empty arrays remain safe, with the older optional single-clip fields as fallbacks.
- **Power Noise Radius**: small AI hearing radius for a power toggle, default four metres.
- **Transmit Noise Radius**, **Received Noise Radius**, **Noise Interval** and **Receiver Noise**: retain the existing transmitter/receiver AI hearing settings.

Radio voice processing is presentation; GameplayNoise is information for enemies. Turning your SFX slider down does not make toggling a physical radio silent to AI.

Actual microphone quality and perceived radio intelligibility should be checked by two people speaking on separate clients. Automated speech-state checks verify networking and animation timing, but do not replace a listening test.

## Development helpers

All configuration utilities and validation code are under **AI-Tools-DEV**. Normal play does not run the integration setup utility or test scripts automatically. Do not rerun MultiplayerIntegrationSetup to make routine art changes: it is an authoring helper that rewrites the known controller/prefab setup. Use the Inspector steps above instead.
