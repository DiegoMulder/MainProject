# Audio, modelled closets and menu hover states

## Where to change a sound

Almost every sound lives in one asset: **Assets/Game/Resources/Audio/Sound Library**. Select it and the Inspector shows headed sections:

MIX · PLAYER AUDIO · DOOR AUDIO · CLOSET AUDIO · ITEM AUDIO · FLASHLIGHT AUDIO · WALKIE-TALKIE AUDIO · ENEMY AUDIO · IMPORTANT CUES · UI AUDIO

Each slot holds a **Sound Event** asset (the events themselves are in **Assets/Game/Audio/Events/…**). Select a Sound Event to edit it:

| Field | What it does |
|---|---|
| Clips | One is chosen at random each play. Add as many variations as you like; empty = silent (safe). |
| Avoid Repeat | Never the same clip twice in a row. |
| Volume / Volume Variation | Loudness, and how much quieter a single play may randomly be. |
| Pitch / Pitch Variation | Base pitch (below 1 = heavier, older wood), and a subtle random offset. |
| Bus | Mixing layer (see below). |
| Spatial Blend, Min/Max Distance | 3D placement: full volume inside Min, silent beyond Max. |
| Reverb Mix | How much the room reverb (if any reverb zone exists) colours it. |
| Alerts Enemy, Hearing Radius, Hearing Category | Whether the enemy hears it, how far, and what kind of noise it counts as. |

To give one prefab a different sound, assign a Sound Event directly on its component (Door → DOOR AUDIO, Closet Hideout → CLOSET AUDIO, flashlight, walkie, pickup, Impact Noise Emitter, Player Audio, Enemy Audio). An empty field falls back to the Sound Library.

Create a new event with **Assets → Create → Survival FP → Sound Event**.

## Mixing layers

Quietest to loudest: **Environment < Player < Interaction < Enemy < Cue**. UI is separate. The MIX section scales each bus, and the player's *Sound effects* option scales everything on top. Important sounds (enemy, heartbeat, chase start, downed) have a higher voice priority, so they are never cut off to make room for a footstep.

## Playback versus enemy hearing

Playing a sound and alerting the enemy are separate on purpose:

- `soundEvent.Play(position, follow)` only makes sound on this machine (every client plays its own copy).
- `soundEvent.EmitHearing(position, sourceObject)` only tells the enemies, and only on the server, and only when *Alerts Enemy* is ticked.
- UI sounds are played only through `UIKit.UISound` and never call EmitHearing, so a menu click, even the pause menu mid-round, can never alert the monster.

How each sound reaches the enemy:

| Sound | Enemy hearing |
|---|---|
| Footsteps | `MovementNoiseEmitter` on the player (crouch 2 m / walk 7 m / sprint 14 m). Changing a footstep clip never changes this. |
| Jump / landing | Jump and Land events (3 m / 5 m soft / 11 m hard, scaled by fall speed). |
| Doors | Door Open (6 m) and Door Close (7 m). Untick *Alerts Enemy* to use the door's own Noise Radius instead. |
| Closets | Closet Enter / Exit (5 m), reported while the player is still outside (entering) or already out (leaving): the enemy investigates the closet front but is not told who is inside. A player hidden in a closet still has their other noise (voice) muffled by the closet's Noise Multiplier. |
| Item drops | `ImpactNoiseEmitter` Min/Max Radius, stronger for harder hits. The clip is picked from the item's own Impact Event, otherwise Item Drop Heavy/Light by Rigidbody mass (≥ Heavy Item Mass = heavy). |
| Item pickup | Not heard (tick *Alerts Enemy* on Item Pickup to change that). |
| Flashlight | Flashlight On/Off (3 m). |
| Walkie | Power on/off (4 m), plus the existing transmit/receive radii on the walkie. |
| Enemy's own sounds, UI | Never. |

## Player sounds

`PlayerAudio` now runs on every client: your own character from its motor, teammates from their replicated movement. So you hear a teammate walking, sprinting, jumping and landing near you. Walk, sprint and crouch use separate sets. Sprint steps are heavier (lower pitch, louder, further). A first step comes quickly after standing still, and cloth rustles now and then. Stride lengths (Player Audio → Stride distance) match the walk and run cycles; the enemy's footsteps come from its animation events and land exactly on its feet. *Breathing* is empty until a breath clip is added (it then plays for your own character while sprinting or winded).

## Radio

The Vivox voice stays filtered like a real radio (tutorial 10). The radio's own sounds are placed on the radio in the world: the talker's radio clicks on key-down and squelches on release. Every receiving radio squelches in, hisses quietly while someone talks (`Generated/radio_static_loop.wav`), and squelches out. Anyone near a receiving radio hears both the voice and the radio.

## Enemy voices

Each enemy prefab has **Enemy Audio** with its own voice: the Butcher uses its breaths for investigate, search, chase start, stagger and roaming. The Ghost Woman whispers while roaming. The chainsaw loops, footsteps, breathing and kill sound stay on the model (`EnemyRandomSounds`, animation-driven). The Butcher's and Ghost's AudioSources now follow the Sound effects volume.

## Performance

All Sound Events play through one pooled set of AudioSources (`SoundPlayer`: 24, growing to at most 48) created once for the session. Nothing is created or destroyed per sound. When every voice is busy, the least important, oldest sound gives way.

## Modelled closets become real closets

The two closets in the Grand Hall were part of the room's baked shell mesh: they looked like closets but had no Closet Hideout, so they could not be used. The Room Furniture Splitter now finds any closet modelled into a room (by matching the Closet prefab's size), cuts it out of the shell and adds a *Modelled Closet N Setup* group whose spawn marker has **Keep Authored Pose** ticked, so the real, networked closet appears exactly where the modelled one stood. Closets remain the only hiding spots. After editing a room model, run the splitter again; it reports "N modelled closets made real".

## Menu hover states

Unity's default theme gives buttons, fields and toggles a light fill on hover, focus and press. Our words have no box, so that fill appeared behind light text. **GameUI.uss → State guard** removes it for every state. Every control now reads the same way:

- **Normal:** muted word, no box.
- **Hover / focus:** ivory word with a faint dark-crimson glow, eased right, the thin crimson line drawn under it, the ◂ mark.
- **Pressed:** stays ivory, settles back slightly, line thickens.
- **Selected** (current options tab): line and mark stay.
- **Disabled:** dim, flat and still legible.

Links, stepper arrows (map, difficulty, enemies), switches, sliders and text fields follow the same rules. No control ever gains an opaque panel.
