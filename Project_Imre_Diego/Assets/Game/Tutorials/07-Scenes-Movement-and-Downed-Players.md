# Scenes, responsive movement, crawling, and replaying rounds

## Start here

Open **Assets/Game/Scenes/MainMenu.unity**, press Play, enter your name, and host or join. Start Game is the host's button. It loads **Game.unity** for everyone, then generates the mansion. Game is deliberately empty of finished mansion rooms in the editor.

MainMenu contains the menu/lobby screen, menu camera, lighting, and Session Bootstrap. The bootstrap creates **Prefabs/Game Management/Multiplayer Session.prefab** only when a session does not already exist. That prefab holds the NetworkManager, transport, GameSession and voice component. It persists across scene changes. To change its references, open that prefab; do not add another NetworkManager to Game.

Game contains the loading camera, lighting and gameplay UI. Once all connected players finish loading, the host spawns Mansion Round. That component builds the rooms, props and navigation, spawns the players, their flashlights, objectives, medkits and enemy, and controls the exit and win/loss state. These gameplay objects belong to the round.

Lobby is a panel in MainMenu, not a third scene. Keep MainMenu and Game enabled in Build Profiles > Scene List, in that order. The offline controller test scene is optional for a shipping build. Launch MainMenu when testing multiplayer; opening Game directly skips the session bootstrap.

## Finish a round and keep the party

1. Win or lose a round.
2. The host selects **Back to Lobby** on the end screen. Clients see that the host controls this transition.
3. The host removes the old round and its network objects, then uses NGO scene loading to bring everyone back to MainMenu.
4. Names and the existing lobby/session remain. An online lobby keeps its code. The host can press Start Game again.
5. A new round creates new players and inventories, one starting flashlight each, fresh objectives/medkits/enemy/navigation, and zero exit progress. Downed, spectator and closet occupancy state does not carry over.

With Random Seed enabled, each new round gets a new layout seed. With Random Seed disabled, a new round intentionally recreates the configured layout, but all gameplay state still resets.

**Main Menu** means leave the session. A client choosing it leaves individually; other players remain together. If the host leaves, its hosted session ends and clients return to their own menus. Back to Lobby is available for completed, lost or failed rounds, not while the round is still running.

## Why client movement responds immediately

The owning client's PlayerController reads input and looks locally. PlayerPrediction immediately runs the existing PlayerMovement motor at 60 simulation steps per second. It sends recent numbered inputs to the server. The server runs the same motor and checks collision, movement limits, stamina and life-state restrictions. It sends authoritative snapshots back.

If the server disagrees, the client restores that snapshot and replays its unacknowledged inputs. Replayed movement does not replay footstep/landing events. This is called prediction and reconciliation. It does not increase walk speed or remove server authority.

On the owning client, NetworkTransform is disabled so remote interpolation cannot overwrite predicted movement. On other peers it stays enabled, using interpolation for the character's position and yaw. Local camera look, bob and sway are not network interpolated. Remote peers receive only the posture and look information they need.

To tune movement, open **Prefabs/Player/Network Survivor.prefab** and edit PlayerMovement or PlayerStamina. Keep PlayerPrediction and NetworkPlayer attached. Do not change NetworkTransform to owner authority: this implementation uses server authority plus local prediction. Position/yaw synchronization and Interpolate should remain enabled on the saved prefab. The script handles the local-owner exception at runtime.

When testing, compare host and client walking, jumping, crouching, stairs and doorways. Brief correction around changing obstacles can occur; continuous snapping should be investigated. Test actual network delay before shipping, not just two windows on the same computer.

## Downed players and revival

Open **Network Survivor.prefab**, then find **NetworkPlayer > Downed posture**.

- **Downed Crawl Speed** is metres per second; the default is 0.65. Try 0.4 for slower crawling.
- **Bleed Out Duration** is seconds; the default is 300 (five minutes).
- **Visual Body** is the model child. Replace this child with your eventual model/animation while keeping the network root upright.

On being downed, the player gets a low capsule and a temporary prone model pose. WASD moves slowly and mouse look still works. Sprint, jump, standing, item pickup, inventory actions and ordinary interactions are blocked. The enemy ignores the downed player as a vision, hearing and chase target. Networking, voice, the bleed-out timer and revival remain active.

A living teammate can look at the downed body. Without a medkit the prompt reads **Needs Medkit to Revive**. With a medkit in any slot it offers **Revive**. Press E to revive instantly and consume one medkit. Looking at an alive, dead or escaped player never offers revival. The server rechecks the target's life state and medkit ownership before consuming anything.

Reviving restores the normal movement mode, visual orientation, camera effects, interaction and enemy detectability. Standing height returns through the normal posture system; a low ceiling still prevents standing into solid geometry. If nobody alive remains, the round ends because no reviver remains; the game does not wait out five minutes unnecessarily.

## Put several impact sounds on an item

1. Import your WAV/OGG clips into **Assets/Game/Audio**.
2. Open the item's multiplayer prefab under **Prefabs/Items**.
3. Find **ImpactNoiseEmitter**. Expand **Impact Sounds**.
4. Increase the array size, for example to 4. Drag a clip into each Element field. Add as many as you need.
5. Save the prefab, pick it up, and drop it several times onto solid floor.

Each qualifying collision chooses a random non-empty entry. The server chooses the clip and pitch and relays that choice to other players. Empty slots are skipped. An entirely empty array makes the impact silent, but still sends an appropriate gameplay-noise event to the enemy.

| Inspector field | What it changes |
| --- | --- |
| Minimum Speed | Contacts slower than this do not make impact noise |
| Reference Speed | Speed that reaches the maximum volume and hearing radius |
| Minimum / Maximum Volume | Audible loudness range, from 0 to 1 |
| Minimum / Maximum Radius | Enemy hearing distance range in metres |
| Cooldown | Minimum time between impact events, preventing bounce spam |
| Pitch Range | Random pitch bounds; set both to 1 for no pitch variation |

Harder impacts interpolate toward the maximum settings. Audible sound and enemy hearing are separate: deleting every clip must not disable AI hearing. Test with an enemy inside the maximum radius, then outside it. Do not add a different impact script for every item type; reuse this component.

## Where new content belongs

Use **Assets/Game** for actual game content and **Assets/AI-Tools-DEV** for AI-assisted tests and development tools. This is the project rule for future additions too.

| Under Assets/Game | Put this here |
| --- | --- |
| Scripts | Gameplay code grouped by responsibility |
| Scenes | MainMenu, Game and the preserved original OutdoorsScene |
| Prefabs/Player and Prefabs/Enemy | Player and enemy templates |
| Prefabs/Items | Pickups, medkits and objective items |
| Prefabs/Rooms, Prefabs/Hallways and Prefabs/Stairs | Procedural building blocks |
| Prefabs/Props and Prefabs/Doors | Furniture, closets and doors |
| Prefabs/Game Management | Session, lobby and round templates |
| Data | Mansion settings, visual settings and network-prefab registries |
| Audio | Sound clips and source/license information |
| Materials, Shaders and Input | Visual assets and the shared input action asset |
| Resources/Flashlight | Runtime beam and switch sound loaded by resource path |
| Tutorials | Beginner instructions |

**Assets/AI-Tools-DEV** contains the practice course in Scenes, examples in Prefabs, test materials/settings in Materials and Data, editor utilities in Editor, and opt-in checks in Scripts/Validation. Normal gameplay must not depend on these test assets. Keep useful checks here instead of deleting them. Only create categories you actually need.

Unity's rendering Settings, template TutorialInfo and external packages remain in their existing locations. Move assets through Unity's Project window so GUID references survive. To add a network pickup or closet, follow its tutorial and register the prefab; putting it in a folder does not add it to generation. See the [folder reference](../README.md) for the complete practical rundown.
