# Closets, gameplay noise, heartbeat, and PSX visuals

[All tutorials](../README.md)

These systems are installed in the multiplayer mansion. The project remains URP. Existing room/objective/medkit counts are preserved.

## Enter and leave a closet

Look at an unoccupied closet and press E at the Hide prompt. Walking into it does not activate hiding. The server claims the closet for you and places your body/camera at its configured hidden positions. Movement, slot changes, dropping, flashlight toggling, and item Primary Use are blocked while hidden. E remains available through the shared interaction system to leave.

The prompt becomes Exit. If another player or obstacle blocks every configured exit, it reports that the exit is blocked and keeps you safely inside. Move the obstruction and try again. No second player can claim the same occupied closet. Disconnect/death/round cleanup releases occupancy.

Voice remains available while Alive or Downed, including while hidden. The closet does not silence you. The enemy can remember seeing you enter, or investigate a sound from inside. On reaching the correct closet with fresh evidence and a clear release position, it pulls you out and uses the normal Downed state. This does not skip revival or immediately turn you into a spectator.

Tables now behave as ordinary furniture. Their tabletop/leg collision remains at ordinary furniture height, but there is no table hiding state or special table detection rule.

## Recipe: add a different wardrobe model

1. Stop Play Mode and duplicate **Assets/Game/Prefabs/Props/Closet.prefab**.
2. Rename it, for example, Carved Wardrobe, and open it in Prefab Mode.
3. Keep its root at unit scale, with NetworkObject and ClosetHideout on the root.
4. Replace visual children with your model. Keep solid colliders enclosing the hidden body and blocking ordinary sight through the closed door. A renderer by itself does not block enemy vision.
5. Preserve or reposition the marker children listed below. Drag replacement markers into the matching ClosetHideout fields if needed.
6. Save, register the new prefab in Data/NetworkPrefabs.asset, then add it to a closet anchor's Variants list.
7. Rebuild both peers and test entry, occupancy, blocked exit, noise, and enemy discovery.

| ClosetHideout field | What to place there |
| --- | --- |
| Entry Point | A clear standing point in front of the door; limits interaction distance |
| Hidden Position | The player's feet inside the closet, fully behind its cover |
| Camera Position | The hidden view position and direction; keep it inside the model |
| Exit Position | Preferred clear standing position outside the door |
| Alternate Exits | Optional fallback positions, all inside the playable room |
| Investigation Point | Walkable place outside where the enemy approaches; leave space around the player exit |
| Noise Multiplier | Multiplier applied to player-sourced AI noises while hidden; default 0.7 |
| Occupant | Runtime server-owned client identity, not an Inspector value to author |

The placeholder has a narrow visual viewing slit, with solid door collision providing closed-door cover. Keep a similar view opening if desired when replacing it. The current first version uses a fixed hidden camera instead of free movement/look. Normal camera behavior returns on exit.

Do not put the enemy's approach exactly on the player's exit: its body could block release. Test with the standing player's full height, even if they entered while crouching. Exit validation checks navigation, supporting floor, physical capsule clearance, and other players. Invalid exits are refused instead of teleporting into geometry.

## Recipe: add a closet spawn location

Closets use the existing **PropSpawnPoint** system. No separate closet randomizer or extra interaction ray is needed.

1. Open a room prefab where a closet makes sense.
2. Create an empty direct child called Closet anchor and add Prop Spawn Point.
3. Place it against a clear wall segment, away from doors, stairs, existing furniture, and item approaches. The prefab's +Z direction faces out of the door.
4. Set Category to Closet for organisation. Category is a label, not automatic filtering.
5. Set Chance, for example 0.45, for a 45% appearance roll at that anchor.
6. Add registered closet prefabs to Variants with positive weights.
7. Leave Random Half Turn off unless both orientations really have usable entry/exit space.
8. Add this marker to the room root's Room Module > Prop Anchors.
9. Save and generate multiple seeds. Check every chosen variant and orientation.

The supplied setup adds anchors to Standard Room, Large Room, and Small Room. Hallways and stairs have no closet anchors by default. Allowed room types are therefore controlled by which prefabs contain these anchors. Appearance chance and weighted variants are per anchor; there is no separate global maximum-closet setting in this version.

The layout includes closet choices in its normal prop manifest. Static props reconstruct on clients, while interactive network props are instantiated/spawned once by the server. Register every new closet variant. The server also checks that the enemy approach connects to the generated navigation.

## Gameplay noise: what the enemy hears

All these actions feed **GameplayNoiseSystem**, and EnemyController subscribes to that same event stream:

| Action | Where to tune AI hearing |
| --- | --- |
| Crouch / walk / sprint | Network Survivor > MovementNoiseEmitter: radii and stride |
| Voice activity | Network Survivor > NetworkPlayer: Voice Noise Radius / Interval |
| Door opening/closing | DoorInteractable: Noise Radius |
| Flashlight on/off click | Flashlight prefab > PlayerFlashlight: Toggle Noise Radius; default 3 |
| Dropped-item collision | Pickup prefab > ImpactNoiseEmitter: Minimum Speed, Reference Speed, Cooldown, Minimum Radius, Maximum Radius |
| Player-sourced noise from a closet | ClosetHideout: Noise Multiplier |

Flashlight noise occurs only when the toggle succeeds, respecting its existing cooldown. A press that cannot toggle does not emit another click. The sound uses the flashlight's replaceable Toggle Sound clip and is relayed to peers as spatial audio.

Drops emit their main noise when the physical object hits something, not when Q is pressed. Slow contacts below Minimum Speed are ignored; stronger contacts scale up to Maximum Radius; Cooldown prevents bounce spam. Impact Sounds accepts any number of clips; Minimum/Maximum Volume and Pitch Range configure audible playback independently of the AI radius. Empty audio arrays are safe and do not stop gameplay noise. See tutorial 07 for a step-by-step setup. The server evaluates the actual collision and sends the audio event to peers.

**AudioClip playback is what people hear. GameplayNoiseSystem is what the enemy hears.** One does not automatically imply the other. A tiny audio volume can still accompany a large AI radius if you configure it that way.

### Recipe: make another action alert the monster

1. Perform/validate the shared gameplay action on the server.
2. Choose its position and a reasonable radius in metres.
3. Call the existing bus, for example:

```csharp
GameplayNoiseSystem.Emit(player.transform.position, 6f,
    NoiseCategory.Other, player.gameObject);
```

4. Use the responsible player's GameObject as Source if the enemy should identify that player. This also lets the shared bus apply the occupied closet's noise multiplier.
5. If players should hear it too, play/replicate an AudioClip separately, as the flashlight/impact systems do.
6. Test inside and outside the configured radius. Add a cooldown for repeatable actions.

Do not add a flashlight-specific or drop-specific branch to enemy hearing. It receives position, radius, category, source, and time. Heartbeat intentionally never calls this bus.

## Heartbeat setup

Open **Prefabs/Player/Network Survivor.prefab** and find **HeartbeatFeedback**.

- Heartbeat Clip: replace the supplied synthesized placeholder with your final AudioClip.
- Maximum Distance: beyond this distance the heartbeat fades away; default 20 metres.
- Intense Distance: at or inside this distance it reaches full intensity; default 3 metres.
- Minimum / Maximum Interval: close/far beat spacing in seconds; smaller means faster.
- Maximum Volume: overall ceiling.
- Volume Curve: maps proximity intensity 0–1 to loudness.
- Pitch Curve: maps intensity 0–1 to playback pitch.
- Smoothing: how quickly intensity follows changing distance.

Only the owning player plays their heartbeat. It uses the nearest spawned enemy and a non-spatial local AudioSource. Alive and Downed players hear it; Dead/Escaped spectators have it disabled, so it never follows the old body's location. It is not networked audio and never alerts the monster.

For a first test, use two players at very different enemy distances. Only the nearby one should have a strong heartbeat. Move closer/farther gradually to check the fade and timing. Keep a short two-pulse clip for clear beat spacing; a long looping music clip is not a suitable direct replacement.

## PSX visual settings

Select **Assets/Game/Data/PsxVisuals.asset**.

| Setting | Effect |
| --- | --- |
| Effect Enabled | Toggle the complete presentation on/off |
| Internal Height | World-rendering height in pixels; width follows the current window aspect ratio |
| Colour Levels | Number of quantization levels per colour channel; lower is more visibly banded |
| Dithering | Strength of the ordered pattern added around colour steps |
| Presentation Shader | The included SurvivalFP/PSX Presentation shader reference |

Default internal height is 360 with 32 colour levels and moderate dithering. Try 240 for chunkier pixels. The world camera actually renders into a smaller RenderTexture using point filtering, then presents it at the current window size. The physical monitor resolution is not fixed. Anti-aliasing is disabled for this target so upscaling stays crisp.

PsxCameraPresentation on the player camera and menu camera references this profile. The same local player camera is retained for downed/spectator views, keeping the treatment consistent. Menus, inventory, prompts, and timers remain full-resolution IMGUI drawn outside the world texture.

During Play Mode, a temporary **PSX screen output** camera presents this texture through a UI canvas. This is expected: the world camera draws the small image, and the output camera displays it on your screen. It prevents Unity's “No cameras rendering” message. Do not add an AudioListener to the output camera. It is created and removed automatically when the effect or its source camera changes.

Disable Effect Enabled to compare with ordinary rendering. Disabling/removing the camera component also restores its prior target and camera settings. Values are local presentation and are not synchronized over the network.

Normal world materials remain URP materials; no PSX shader replacement is required on every prop. This implementation provides low resolution, point upscaling, colour reduction, and dithering. It does not claim to implement affine texture warping, vertex wobble, VHS noise, or CRT scanlines.

## Smooth flashlight aiming and standing on edges

Open the **Flashlight** prefab and look at **PlayerFlashlight > Beam aiming**. **Aim Response** controls how quickly the beam adjusts when your crosshair moves from a close wall to a distant wall. The default is 12; smaller numbers make the adjustment slower. **Minimum Aim Distance** defaults to 2 metres and prevents the offset flashlight from turning sharply sideways against a very close wall. The light still hits nearby walls normally. Only the beam turns; the held model stays in its hand position.

The player keeps a capsule body so stairs, walls and crouching work as before. Ground detection also uses a flat square foot, as wide as the CharacterController's diameter. When part of this foot rests on a flat ledge, the downward ground-stick force no longer pushes the rounded capsule off it. Jumping still leaves the ground immediately, and stepping beyond the foot's reach starts a fall. To check your own room, test its ledges facing straight ahead and diagonally, stand still for several seconds, then jump and walk off.
