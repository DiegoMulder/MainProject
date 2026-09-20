# Downed players, radios, the red screen effect and smooth movement

## What each life state allows

| State | Move | Use items | Transmit radio | Proximity voice | Enemy target/noise |
|---|---|---|---|---|---|
| Alive | Normal movement | Yes | Equipped walkie, ON, speaking | Yes | Yes |
| Downed | Crawl | No | No | Teammates still hear you | No |
| Dead / Escaped | Spectator | No | No | Existing spectator rules | No |

An Alive player can receive radio speech from a powered radio in any inventory slot. To **send**, select the powered walkie so it is actually in your hand. There is no push-to-talk key. Selecting a different item stops transmission. Downing stops it immediately, even if the radio remains ON in the inventory. When revived, select the radio again if necessary and speak normally.

Standing beside a teammate with both radios ON intentionally produces both clean proximity voice and filtered radio voice. Switch your receiver OFF to hear proximity only. The Voice and Master volume controls still apply, and received radio noise can still alert enemies. Turning your computer's volume down does not reduce the enemy's hearing radius.

## Why enemies stop following a downed player

`EnemyTargetRules.CanTarget` is the shared server-side eligibility check. Vision, hearing, chase and kill logic use it. `GameplayNoiseSystem.Emit` also rejects player-sourced events when that player is ineligible. Down/death/disconnect immediately invalidates the enemy's remembered player and target, rather than waiting for a hearing or vision timer.

When adding a new player noise, pass the player's GameObject (or an object beneath that player) as the source. This lets the common check identify the player. Do not pass null to disguise a player's sound as an environmental event. Truly independent sounds such as a released object hitting the floor or a door moving still make noise normally.

This AI rule does not mute the human's microphone: downed proximity speech remains available to teammates. Audio playback and AI hearing are separate systems.

## Adding an item that respects downing

Follow tutorial 03 to add the prefab and its `IPrimaryUse` action. Route ordinary primary input through `PickupItem.UsePrimary`; it checks `PickupItem.CanUse`. Network inventory commands and the flashlight shortcut also check `NetworkPlayer.CanUseItems`.

If you add a separate shortcut or server RPC for an item, check `CanUseItems` on the server before changing shared state. Also check that the caller owns the player and carries the required item. A local input check helps responsiveness, but it cannot replace the server check. Do not change `Life` locally to make an action work.

## Adjusting the red bleed-out effect without coding

1. Stop Play mode so your edits will be saved.
2. Open **Assets/Game/Prefabs/Player/Network Survivor.prefab**.
3. Find the **Downed Vignette** component on the root object.
4. **Minimum Opacity** is the red effect at the start of downing. **Maximum Opacity** is its strength near death. Defaults are 0.12 and 0.8; 0 is transparent and 1 is opaque at the outer edge.
5. **Minimum / Maximum Intensity** control how far the vignette reaches inward. Leave a useful clear center so the player can still see where to crawl.
6. **Colour** changes the tint. **Progression** is a curve from 0 at the start of the timer to 1 at its end. A curve that rises near the right-hand side makes the last part of bleed-out more dramatic.
7. **Fade Time** controls how gently the effect changes and fades after revival. The default is 0.25 seconds. Full death clears it immediately so it does not cover the spectator view.
8. Save the prefab and test with two players. Keep one Alive so the round does not end immediately when the other becomes Downed.

To change the amount of time available, change **Network Player > Bleed Out Duration**, on the same prefab. The server sends the start and end times. The effect calculates progress from those times, so it does not depend on the client's frame rate or an independent local countdown.

The vignette draws over the final image and beneath the normal HUD. It works with the existing PSX world render texture and does not need an extra camera, HDRP or a new URP renderer feature. Only the owning downed player sees it. Teammates do not get a red screen just because someone else is down.

## Downed bodies versus fully dead bodies

Downed players keep their crawling body visible to teammates. When fully Dead, body renderers and held visuals are hidden on every peer and the Animator is disabled. The network player object remains because the session and spectator camera still need it. Required objectives are recovered through the existing item-release rules.

When adding body meshes, keep them beneath the player's visual body. Do not delete the network player or disable its entire root GameObject to hide a corpse: that also disables unrelated networking and spectator behavior.

## Making an enemy remember sight briefly

1. Open **Assets/Game/Prefabs/Enemy/Mansion Stalker.prefab**.
2. Find **Enemy Controller > Lost Sight Chase Duration**. The default is **2 seconds**.
3. During that period the enemy continues pursuing an eligible player after losing sight. Seeing them again refreshes the timer.
4. After expiry it travels to the last known position and searches, then returns to roaming. It cannot attack through an obstructing wall merely because the timer is active.
5. Set the value to 0 for immediate loss of moving-target pursuit. Larger values make corners less effective for escaping.
6. Test around a solid wall. Also down the pursued player during the grace period: the enemy must forget them immediately. Life-state eligibility always overrides this timer.

## What changed in movement presentation

The client predicts movement in fixed 1/60-second steps. At 120 rendered frames per second, a baseline walk produced 166 movement frames, with 83 frames showing no new root movement and no server corrections. The camera was directly following those simulation steps, producing visible judder even on a healthy connection.

`PlayerPrediction.RenderOffset` now interpolates the local camera between the last two prediction positions. This adds at most one simulation step of presentation delay, about 16.7 milliseconds. The CharacterController still uses the original positions and collisions. Server corrections and forced teleports still take effect; the change does not conceal them with a long smoothing animation. The host and remote avatars keep their existing movement paths.

Mouse smoothing is a separate setting: it smooths looking around. Turning it OFF should not bring back the fixed-step camera judder. Head bob, crouch eye height, FOV and landing effects continue to work.

Keep the existing **PlayerPrediction**, **NetworkPlayer** and **NetworkTransform** arrangement on the survivor. The joining player's own NetworkTransform is disabled at runtime because local prediction controls that root; other players' NetworkTransforms keep interpolation enabled. Enabling both writers for the owner can create competing position updates. The server still validates movement and replays acknowledged input. Keep Animator root motion disabled so animation does not become another movement writer.

A second inconsistency was found in teammate collisions: the server had solid player capsules, but a joining client disabled other players' capsules. It could therefore predict walking through a teammate and then be pulled back by the server. Alive/downed remote capsules now stay enabled, and their height follows the replicated posture. Hidden/dead players still disable collision. Do not disable a remote capsule merely to stop its input; ownership already prevents its motor from running locally. Network delay can still require corrections around moving objects or players, so this is not a promise of zero corrections under every connection condition.

When reporting a remaining snap, include whether you hosted or joined, the frame rate, your mouse-smoothing setting, what you hit or stepped on, and whether it happened near a teleport/revive. A collision correction, a slow frame and a mouse-look issue can feel similar but need different fixes. Development measurements are opt-in and live under **Assets/AI-Tools-DEV**; normal play does not print movement messages every frame.

## Quick two-player check after making changes

Build the whole game folder and give both people the same build. This version uses network protocol **4** because synchronized player state changed. Do not mix an older EXE with the current data folder.

Check a nearby and distant radio conversation, switch away from the transmitting radio, switch a receiver OFF, down the speaker, revive them, and let one player bleed out while the other survives. Check that only the downed player sees red, the dead body disappears, and the dead player can spectate. Walk, sprint, crouch and jump at different frame rates, including beside doors, furniture, stairs and ledges.

Automated state checks do not replace two people listening to real microphone audio. Use that listening check to judge radio distortion, volume and the intentional nearby overlap.
