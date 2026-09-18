# Options, difficulty, radios and safe drops

[All tutorials](../README.md)

## Options: what the player can change

Open Options from the main menu, lobby or pause menu. Audio, Controls and Video are separate tabs. Changes apply immediately and save on this computer; they are never sent to other players.

- **Master** changes all sound output, including Vivox voice.
- **Sound effects** changes footsteps, impacts, doors, flashlight clicks, heartbeat and radio cues.
- **Voice / radio** changes incoming proximity and radio speech. It does not make your device quieter to the enemy.
- **Mute microphone** stops your microphone from transmitting through either voice channel.
- **Mouse sensitivity** changes look speed. The displayed multiplier is ten times the underlying degrees-per-mouse-unit value; 1.00 corresponds to the original 0.1 setting.
- **Smooth camera look** and its strength control visual rotation damping. Turn it off or set strength to zero for raw look.
- **Field of view** changes your own gameplay camera. Sprinting can temporarily add its existing FOV increase.
- **PSX visual presentation** allows you to disable the authored retro effect locally.

There is no music volume slider because the game currently has no music system. Avoid adding settings that do nothing.

### Change the starting defaults

1. Stop Play Mode.
2. Select **Assets/Game/Resources/Settings/Local Settings Defaults.asset**.
3. Change the default volumes, sensitivity, FOV or smoothing values in the Inspector.
4. Minimum Fov and Maximum Fov control the slider limits. Keep minimum below maximum.
5. Maximum Smooth Time controls the strongest smoothing. The default is 0.09 seconds; strength scales this value down. Large values will feel delayed.

Saved preferences take priority over these defaults. Changing defaults does not reset an existing player's choices. For a fresh test, remove only the relevant SurvivalFP preference keys, or change the values through Options; do not delete unrelated PlayerPrefs.

**How smoothing works:** PlayerCamera accumulates the intended yaw/pitch immediately. The player's movement yaw follows that target immediately. The visible camera damps toward it and applies the remaining yaw difference locally. This avoids delaying the movement controller. Bob, landing motion and sprint FOV still use their existing systems.

**Adding a setting:** add its default to GameSettingsDefaults, its stored value/load/save/clamping to LocalSettings, and its control to GameUI.DrawOptions. Have the affected local system subscribe to LocalSettings.Changed and unsubscribe when disabled. Keep all preference keys in LocalSettings rather than writing them from UI sliders. These scripts are in Game/Scripts/UI.

### Audio volume groups

The game uses a central SFX bus through **AudioVolumeBus**, rather than a settings menu that searches for AudioSources. Each source registers once using `AudioVolumeBus.RouteSfx(source)`. The bus remembers the source's authored volume and multiplies it by the SFX preference when settings change. Master uses AudioListener.volume. Vivox mixes outside Unity's AudioListener, so ProximityVoice applies Master × Voice to its output device separately, including an actual mute at zero.

When adding a new sound, register its AudioSource with the SFX bus once after creating/configuring it. Use PlayOneShot's volume argument for individual sound strength. Do not overwrite the source's volume every frame, because the bus owns that gain. Optional clips can remain empty. Volume choices affect what you hear, not the radii of GameplayNoise events.

## Difficulty: change the mansion size and objectives

Select **Assets/Game/Data/Difficulty.asset**. Its Profiles list has editable names and values:

| Profile | Target rooms | Required objectives |
| --- | ---: | ---: |
| Easy | 24 | 20 |
| Medium | 60 | 100 |
| Hard | 85 | 140 |
| Extreme | 120 | 200 |

These are starting balance values. Change them in the Inspector without editing scripts. Medium preserves the previous 60-room / 100-objective target. Keep enough item surfaces for objectives, medkits and radios combined. Increasing room count also increases generation and NavMesh work, so test the largest value on your target hardware.

Only the host's lobby arrows can change difficulty. Clients see the synchronized selection. Once Start Game begins, the selection is locked. The server makes a private copy of MansionSettings, applies the chosen profile, and sends the resulting layout and round configuration to clients. It does not modify the saved MansionSettings asset. Returning to the lobby unlocks selection for the next round.

The Difficulty asset is referenced by both **Game/Data/MansionSettings.asset** and **Game/Prefabs/Game Management/Lobby.prefab**. Keep those references pointed at the same asset. All players should use the same game build.

**Add another difficulty value:** add a field to DifficultyProfile and the corresponding generation setting to MansionSettings, then copy it in DifficultyConfig.Apply. The server reads its copied round settings. For a value needed for client presentation, add it to the replicated round configuration as well. Do not scatter checks for names such as Extreme throughout unrelated gameplay scripts. Radio batteries and difficulty-dependent radio behavior have not been added.

## Dropping items safely

Both the offline inventory and server-owned multiplayer drop use **Game/Scripts/Items/SafeItemDrop.cs**. It measures the item's normal collider bounds, uses the player's actual capsule height/radius, and tries the full camera direction first, including up and down. Limited upward/outward corrections keep the item clear of the player and floor. A sphere sweep and overlap test check room for the whole item.

If no candidate is safe, the item stays in your inventory. Move away from the obstruction and press Q again. Crouching itself never disables dropping. The physics check runs only when you drop, not continuously for every item.

**PickupItem** restores the original size and enables gravity and collisions after placing the body. Dropped objects use Continuous Dynamic collision detection to reduce tunneling. **ImpactNoiseEmitter** still chooses configured impact clips and emits gameplay hearing events on collisions. Change impact clips, pitch, volume and noise radius on the item's prefab. For unusually shaped items, keep colliders close to the actual model: the conservative clearance sphere uses the combined collider bounds.

## Local model visibility and spectators

Open **Game/Prefabs/Player/Network Survivor.prefab**. On NetworkPlayer, **Visual Body** must reference the parent containing the third-person model's renderers. Put additional body meshes under this same parent. Keep held items outside it.

The owning player's body renderers use Shadows Only while alive or downed. Remote clients render the same body normally, including its downed pose. Nothing disables the model globally over the network. The ownership rule is reapplied for added body renderers. Spectators use a short collision-aware view behind the target, so the target's body can remain visible. The spectator camera uses the local saved FOV.

## Exit placement and room authoring

The planner considers unconnected exit-eligible connectors outside the starting room, unless MansionSettings explicitly allows that room. **ExitPlacement** calculates clearance from the exit prefab's BoxColliders over its opening angle, plus an interior approach area. It rejects candidates intersecting another room's reserved bounds, then keeps generated furniture out of the accepted volume.

After constructing the world, the server checks the door swing against actual colliders and verifies a clear, reachable interior approach on the NavMesh. Invalid generation stops with an explanation instead of placing an embedded exit. Candidate selection is bounded by the available connectors.

To make a connector exit-compatible:

1. Use a real opening wide and tall enough for **Game/Prefabs/Doors/Exit Door.prefab**.
2. Set its RoomConnector exit eligibility.
3. Keep space inside the room for the entire door swing and a standing player's approach.
4. Keep the RoomModule bounds accurate so neighboring rooms cannot occupy that area.
5. Test several seeds and all difficulties after changing room geometry.

The exit's hinge is inset 12 cm from the wall plane while the closed leaf stays in its original position. This lets the leaf's thickness clear the jamb when rotating. If you replace the door model, check its hinge, opening angle and BoxCollider shapes together. The current swept check is designed for BoxColliders; extend ExitPlacement before switching to a different collision shape.

## Walkie Talkie setup and use

The ready-made prefab is **Assets/Game/Prefabs/Items/Walkie Talkie.prefab**. MansionSettings has a Walkie Talkie reference and Radio Count; the current count is four world pickups. Radios are registered in Game/Data/NetworkPrefabs.asset like other network items.

1. Pick one up with E. It takes one of the normal three inventory slots.
2. Select its slot.
3. Left-click to toggle power ON/OFF.
4. While it is ON and anywhere in your inventory, hold **V** to transmit. Release V to return to ordinary proximity transmission.
5. Other living players with powered radios can hear radio speech at a distance. Nearby players still use proximity audio, without a second radio copy.

An OFF radio sends and receives no radio speech. A downed player can keep using V if already carrying a powered radio, but cannot regain normal inventory use through this exception. Dead/escaped spectators cannot use living-player radio communication. Dropping the radio removes its permission to transmit from that player.

### Make a radio variant

Duplicate the Walkie Talkie prefab. Change its model and keep PickupItem, Rigidbody, suitable colliders, NetworkObject, NetworkTransform, NetworkPickup, WalkieTalkieUse and ImpactNoiseEmitter. PickupItem forwards primary use to the modular WalkieTalkieUse component; there is no special hidden radio slot. Register a new network prefab in NetworkPrefabs.asset and assign the desired spawn prefab in MansionSettings.

**WalkieTalkieUse Inspector fields:**

- Transmit Noise Radius: how far the enemy may hear the speaker.
- Received Noise Radius: how far the enemy may hear incoming radio sound at a receiver.
- Receiver Noise: disable this only if you intentionally want receivers to be silent to AI.
- Noise Interval: minimum spacing between speech hearing events; default 0.65 seconds.
- Power On/Off, Transmit Start/End and Radio Static: optional AudioClips. Empty references are safe. Radio static is available to call from custom effects and is not looped by default.

PlayerRadio validates ownership, a powered item, round state and life state on the server. ProximityVoice uses Vivox's existing speech detection rather than capturing another microphone. Speaking while holding V emits a throttled RadioVoice GameplayNoise at the speaker and, when enabled, RadioReceiver noise at active distant receivers. EnemyController uses its normal hearing-radius rules, including these noises from downed players. The radio's local volume does not secretly change AI risk.

The input action **Player/RadioTransmit** is in **Game/Input/InputSystem_Actions.inputactions** and defaults to V. Change its binding there to choose another key. The screen's V hint is plain UI text; update that hint too when changing the default binding.

ProximityVoice joins the session's positional channel and a separate radio channel. During PTT it transmits to both, while each listener selects proximity or radio for that speaker. It uses the server-approved PlayerRadio state to suppress invalid radio reception. Testing real voice requires a configured Vivox project, working microphones and at least two connected clients; local direct-IP tests can verify state and AI noise but do not connect Vivox.


For the current camera-directed drop settings, two-way ordinary doors, editable enemy counts and matching-build instructions, continue with [tutorial 09](09-Builds-Drops-Doors-and-Multiple-Enemies.md).
