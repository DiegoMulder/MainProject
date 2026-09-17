# Pickups, objectives, medkits, and new item actions

[All tutorials](../../README.md) · [Player/enemy tuning next](04-Player-Enemies-and-Doors.md)

## Start with the right kind of item

**Offline pickups** are useful in CharacterController_Test. **Network pickups** are required for the multiplayer mansion so everyone agrees who owns them, where they are, and when they disappear.

An item needs three separate things: a saved prefab, a system/location that spawns it, and optional behavior when used. Adding an item to NetworkPrefabs.asset handles network registration only. It does not make that item appear in a room.

## Recipe 1: add a simple offline pickup

1. Open Assets/Scenes/CharacterController_Test.unity, outside Play Mode.
2. In Assets/SurvivalFP/Pickupables, duplicate Cube.prefab. Name it Test Relic.
3. Open the copy. Change Pickup Item > Display Name to Test Relic and Kind to Generic.
4. Replace or recolour its visible mesh. Keep its Rigidbody and a suitably sized solid collider.
5. Save, then drag Test Relic.prefab from Project into the scene. Place it slightly above a floor or table so it falls onto the surface, rather than starting inside it.
6. Save the scene and press Play.
7. Look directly at the collider, press E, switch slots, and press Q to drop it.

A simple collectible does not need a Primary Use action. Kind is just a category: setting it to Flashlight does not create a light or flashlight behavior.

This offline recipe alone does not make a working multiplayer pickup.

## Recipe 2: create a multiplayer collectible

Example: a Brass Key that can be carried and dropped. It will not unlock doors until you implement that additional behavior.

1. Open Assets/SurvivalFP/Mansion/Prefabs.
2. Duplicate Medkit.prefab with Ctrl+D and rename the copy Brass Key.
3. Open the copy in Prefab Mode.
4. On the root, remove the **Medkit Item** component using its component menu. Otherwise this key would still count as a medkit.
5. Set Pickup Item > Display Name to Brass Key, Kind to Generic, and leave Primary Use empty initially.
6. Replace the visible model or material. Preserve the root object and its networking components.
7. Adjust the collider to fit the new model. A Box Collider or Capsule Collider is a good first choice. A dynamic Mesh Collider must be convex.
8. Keep Rigidbody, PickupItem, NetworkObject, NetworkTransform, and NetworkPickup. ImpactNoiseEmitter is optional, but keep it if dropped impacts should attract the enemy.
9. Keep NetworkObject automatic parent synchronization disabled, matching the existing pickup setup. NetworkPickup handles inventory attachment.
10. Save and leave Prefab Mode.
11. Select Mansion/NetworkPrefabs.asset, expand its prefab list, and add an entry.
12. Drag Brass Key.prefab into the new entry's Prefab field. Use an ordinary entry with Override set to None, matching the existing entries; no replacement mapping is needed.
13. Save. Follow Recipe 3 to place it into the generated mansion.

Keep a single NetworkObject on the root. Do not give every visual child another NetworkObject. Duplicate through Unity so the prefab receives its own asset identity; do not copy/edit GUIDs or network IDs by hand.

If you duplicate Objective Seal instead, remove ObjectiveItem for an ordinary collectible. A decorative name does not remove the inherited gameplay capability.

### Held appearance

Pickup Item > Held Euler Angles rotates the model while held. Try small changes outside Play Mode, then pick it up again. The current system applies a shared hand position and a 0.55 held scale factor. There is no exposed per-item hand-position field.

PlayerInventory establishes the hand anchor at startup, so moving that anchor in the prefab alone is not a reliable way to change its runtime position. Per-item position/scale controls would be a script extension. For now, use a well-sized model and its held rotation.

## Recipe 3: spawn your new collectible in generated rooms

**Optional programming extension.** The current built-in surface spawner places objectives and medkits. The following small component adds one optional ordinary pickup at a dedicated marker in each generated copy of a room or prop. It is an example to install yourself; this tutorial has not added it to the game.

This approach does not provide a global loot budget, random item catalog, or automatic reachability/overlap validation. You choose and test a clear location. Do not use it to add required objectives, because it does not update the exit's required-item accounting.

1. In Assets/Scripts/SurvivalFP/Generation, create a C# script named **OptionalPickupSpawnPoint.cs**. The filename must match the class name.
2. Open it in your code editor, replace its contents with the following, and save.
3. Return to Unity and wait for compilation. Resolve any red Console error before continuing.

```csharp
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SurvivalFP
{
    public sealed class OptionalPickupSpawnPoint : MonoBehaviour
    {
        public NetworkPickup prefab;
        [Range(0f, 1f)] public float chance = 1f;

        IEnumerator Start()
        {
            // Generated rooms exist on every peer. Only the server spawns loot.
            var manager = NetworkManager.Singleton;
            if (!manager || !manager.IsServer || !prefab)
                yield break;

            // Wait until the normal round setup has completed successfully.
            var round = RoundManager.Instance;
            while (round && !round.Ready.Value &&
                   round.Phase.Value == RoundPhase.Generating)
                yield return null;

            if (!manager || !manager.IsServer || !round ||
                !round.Ready.Value || round.Phase.Value != RoundPhase.Playing)
                yield break;

            if (chance <= 0f || (chance < 1f && Random.value >= chance))
                yield break;

            var item = Instantiate(prefab, transform.position, transform.rotation);
            item.SafeAnchor = transform.position;
            item.GetComponent<NetworkObject>().Spawn();
        }
    }
}
```

4. Open a room or furniture prefab. Create an empty child called Brass Key Spawn.
5. Add Optional Pickup Spawn Point to that child.
6. Drag the registered Brass Key.prefab into its Prefab field. Set Chance to 1 for the first test.
7. Place the marker above an empty reachable surface with clearance for the key's collider.
8. **Do not add ItemSpawnPoint to this same marker.** Built-in objectives/medkits must not also use it. Keep other surface markers far enough away to prevent overlap.
9. Save. Ensure the containing room/furniture is in its catalog.
10. Generate a round. There should be one key per generated copy containing this marker, subject to Chance. Ten copies with Chance 1 mean ten keys, not one global key.
11. Join with a second player using the same updated build. Confirm both see the same key and only one player can pick up that instance.
12. Test slot switching, dropping, repicking, leaving the round, and starting another round.

The server's Spawn call tells clients to create the registered prefab. Do not also Instantiate that item on every client: that would create extra unowned local copies. No NetworkObject is needed on the marker itself.

For random ordinary loot with fixed totals, extend RoundManager's server-side setup with a separate catalog and a shared pool that reserves positions. The supplied marker example intentionally solves the simpler 'put this new pickup here' case.

## Recipe 4: add more medkits or a new medkit appearance

For **more medkits**, select MansionSettings.asset and increase Medkit Count. Ensure enough reachable item surfaces exist for Objective Count plus Medkit Count. Set Medkit Count to 0 to disable their automatic placement.

For **a different appearance**:

1. Duplicate Mansion/Prefabs/Medkit.prefab and rename it First Aid Bag.
2. Keep MedkitItem this time. Change the visible model, collider, and PickupItem display name.
3. Save and register the copy in NetworkPrefabs.asset.
4. Assign First Aid Bag to MansionSettings > Medkit.
5. Start a new round and test revival with two players.

Carry a medkit in any inventory slot, look at a downed teammate, and press E. Successful revival consumes one medkit. It is currently instant. There is no hold-duration Inspector setting and the medkit does not automatically implement left-click self-healing.

Downing preserves inventory. Full death/disconnect recovers required objectives and removes ordinary carried items. A new ordinary collectible inherits that ordinary-item behavior unless you deliberately extend it.

## Recipe 5: add more objectives or objective names

1. Select MansionSettings.asset.
2. Set Objective Count to the total required, for example 6.
3. Expand Objective Types. Enter names such as Red Seal and Blue Seal.
4. Keep enough eligible item surfaces, plus extra capacity for medkits.
5. Start a new round, collect items, and deposit at the exit with E.

Types cycle across the total. Six items with Red Seal and Blue Seal gives three of each. Five items gives three of the first name and two of the second. The list does not have a separate quantity field per type. Keep names short: network type strings have a fixed capacity.

The current spawner uses **one Objective prefab for every type**. Different type names do not automatically select different models/materials. For a new look for all objectives:

1. Duplicate Objective Seal.prefab and keep ObjectiveItem, PickupItem, and all network/physics components.
2. Replace the model and adjust its collider.
3. Save/register the new prefab and assign MansionSettings > Objective.
4. Retest surface spacing for its new size.

ObjectiveItem receives its type from the round and updates its display name. Merely changing PickupItem's display name is not how you define objective types.

At the exit, the equipped needed objective is preferred. ExitDoor > Search Other Slots allows the exit to find one elsewhere in inventory. Each successful deposit removes that item and frees the slot. All required deposits unlock the exit; another interaction escapes.

Multiple visual objective prefabs, per-type amounts, a special final key, or alternative win conditions require extending the objective catalog/spawner and exit accounting together. Do not add required items through the optional ordinary-loot marker.

## Recipe 6: add a left-click action

Primary Use is a capability: a script implements **IPrimaryUse** and its **PrimaryUse()** method. PickupItem forwards equipped left-click to it. If Primary Use is empty, it searches components on the item's root for the first implementation. Explicit assignment is clearest, especially if several scripts implement the interface.

Multiplayer calls the equipped action on the server after inventory validation. A script that changes only a local light or renderer on that server will not automatically show its change on clients. Shared effects need network state or an RPC.

### Example: a bell that attracts the enemy and plays a sound

**Optional multiplayer-only programming example.** This uses a NetworkVariable counter to notify each peer of a ring, while the server emits the AI noise. It does not give the offline course networking; use it on a network pickup in the mansion.

1. Create **Assets/Scripts/SurvivalFP/Items/BellItemUse.cs** with this code.

```csharp
using Unity.Netcode;
using UnityEngine;

namespace SurvivalFP
{
    [RequireComponent(typeof(PickupItem), typeof(AudioSource))]
    public sealed class BellItemUse : NetworkBehaviour, IPrimaryUse
    {
        public AudioClip ringClip;
        [Min(0f)] public float noiseRadius = 16f;
        [Min(0.1f)] public float cooldown = 1f;

        readonly NetworkVariable<int> rings = new(0);
        AudioSource speaker;
        float nextUse;

        public override void OnNetworkSpawn()
        {
            speaker = GetComponent<AudioSource>();
            rings.OnValueChanged += OnRing;
        }

        public override void OnNetworkDespawn()
        {
            rings.OnValueChanged -= OnRing;
        }

        public void PrimaryUse()
        {
            if (!IsServer || !IsSpawned || Time.time < nextUse)
                return;

            var holder = GetComponentInParent<NetworkPlayer>();
            if (!holder || !holder.Alive || !GetComponent<PickupItem>().Held)
                return;

            nextUse = Time.time + cooldown;
            GameplayNoiseSystem.Emit(holder.transform.position, noiseRadius,
                NoiseCategory.Other, holder.gameObject);
            rings.Value++;
        }

        void OnRing(int previous, int current)
        {
            if (speaker && ringClip)
                speaker.PlayOneShot(ringClip);
        }
    }
}
```

2. Make a network pickup called Bell using Recipe 2. Register it and supply a spawn marker using Recipe 3.
3. Add Bell Item Use to the root. Its AudioSource is added automatically if missing.
4. Drag the Bell Item Use component header into Pickup Item > Primary Use.
5. Assign a ring sound to Ring Clip.
6. On AudioSource, disable Play On Awake and Loop. Set Spatial Blend to 1 for 3D sound. Tune its volume and distance falloff to suit the scene.
7. Save and rebuild both peers; they need the same NetworkBehaviour layout.
8. Equip the bell and left-click. Check both players hear it at appropriate distances and the enemy investigates when in hearing range.
9. Check rapid clicking respects cooldown and an unequipped bell cannot be used through ordinary inventory input.

The sound clip and AI hearing radius are independent. Even with Ring Clip empty, the server still emits the AI noise. Playing an AudioSource alone does not automatically alert the enemy. The network counter communicates new ring events; it does not replay old rings to a late observer.

For persistent effects such as a lamp being on/off, use a NetworkVariable<bool> and apply its current value in OnNetworkSpawn as well as OnValueChanged. That lets a newly observing client receive the current state. Also define what happens when the item is unequipped, dropped, consumed, or its owner dies.

## Inventory and interaction limitations to understand

Adding item types does not add carrying capacity. There are three slots, with related input, HUD, and network selection logic. Increasing only the serialized slots array is not a complete larger-inventory feature.

E uses a direct camera ray against colliders. Look at the actual collider; it does not pick the nearest item behind you. Walls block targeting, triggers are ignored for this interaction ray, and distance is configured on PlayerInteraction. The server repeats validation, so changing only client-side targeting is insufficient.

A new custom interaction implements IInteractable: Prompt supplies the text, CanInteract checks availability, and Interact performs it. For multiplayer it must be addressable through a NetworkObject and must make authoritative changes on the server, then replicate them. DoorInteractable and DownedInteractable are working examples.

## Quick item checklist

- A unique saved prefab with visible model, matching collider, and Rigidbody.
- Correct capability markers: MedkitItem only for medkits, ObjectiveItem only for required objective items.
- Network components and registry entry for multiplayer.
- An actual spawn source, not just registration.
- Clear surface and reachable approach, with no overlapping item positions.
- Primary Use assigned if an action is intended.
- Shared effects synchronized, and both peers rebuilt after script/prefab changes.
- Pickup, use, slot switching, drop, recovery, and round exit checked with two players.
