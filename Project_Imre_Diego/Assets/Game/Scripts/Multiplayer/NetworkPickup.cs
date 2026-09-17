using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
namespace SurvivalFP
{
    [Serializable]
    public struct ItemLocation : INetworkSerializable, IEquatable<ItemLocation>
    {
        public ulong carrier;
        public int slot;
        public Vector3 position;
        public Quaternion rotation;
        public bool Held => slot >= 0;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref carrier); s.SerializeValue(ref slot); s.SerializeValue(ref position); s.SerializeValue(ref rotation); }
        public bool Equals(ItemLocation o) => carrier == o.carrier && slot == o.slot && position == o.position && rotation == o.rotation;
    }
    [RequireComponent(typeof(PickupItem),typeof(NetworkObject),typeof(NetworkTransform))]
    public sealed class NetworkPickup : NetworkBehaviour
    {
        public NetworkVariable<ItemLocation> Location = new(new ItemLocation { carrier = ulong.MaxValue, slot = -1, rotation = Quaternion.identity });
        public NetworkVariable<bool> LightOn = new(false);
        public Vector3 SafeAnchor { get; set; }
        PickupItem item;
        PlayerFlashlight flashlight;
        NetworkPlayer bound;
        public override void OnNetworkSpawn()
        {
            item = GetComponent<PickupItem>(); flashlight = GetComponent<PlayerFlashlight>();
            NetworkObject.AutoObjectParentSync = false;
            Location.OnValueChanged += Changed; LightOn.OnValueChanged += LightChanged;
            ConfigureWorldPhysics(); ApplyLocation(); LightChanged(false,LightOn.Value);
        }
        public override void OnNetworkDespawn()
        {
            Location.OnValueChanged -= Changed; LightOn.OnValueChanged -= LightChanged;
            if (bound) bound.Inventory.RemoveItem(item);
            transform.SetParent(null,true);
        }
        void Changed(ItemLocation old, ItemLocation value) => ApplyLocation();
        public void PlaySwitchSound() { if (IsServer) SwitchSoundRpc(); }
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SwitchSoundRpc() { if (flashlight) flashlight.PlaySwitchSound(); }
        public void PlayImpactSound(float strength,int clip,float pitch) { if (IsServer) ImpactSoundRpc(strength,clip,pitch); }
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ImpactSoundRpc(float strength,int clip,float pitch) { GetComponent<ImpactNoiseEmitter>()?.PlayImpact(strength,clip,pitch); }
        void LightChanged(bool old, bool value) { if (flashlight && !IsServer) flashlight.ApplyNetworkState(value); }
        void ConfigureWorldPhysics()
        {
            var body = GetComponent<Rigidbody>(); body.isKinematic = !IsServer; body.useGravity = IsServer;
        }
        void Update()
        {
            if (!IsSpawned) return;
            if (Location.Value.Held && !bound) ApplyLocation();
            if (IsServer && flashlight) LightOn.Value = flashlight.IsOn;
            if (IsServer && !Location.Value.Held && transform.position.y < -5) Release(SafeAnchor,Quaternion.identity,Vector3.zero);
        }
        void ApplyLocation()
        {
            var location = Location.Value;
            if (bound && (!location.Held || bound.OwnerClientId != location.carrier)) { bound.Inventory.RemoveItem(item); bound = null; }
            GetComponent<NetworkTransform>().enabled = !location.Held;
            if (location.Held)
            {
                bound = NetworkPlayer.Players.Find(p => p && p.OwnerClientId == location.carrier);
                if (bound) { bound.Inventory.ApplyItem(location.slot,item); bound.Inventory.ApplySelection(bound.Selection.Value); }
            }
            else if (item.Held)
            {
                item.Drop(location.position,location.rotation,Vector3.zero);
                if (!IsServer) ConfigureWorldPhysics();
            }
        }
        public bool Claim(NetworkPlayer player)
        {
            if (!IsServer || !player.Alive || Location.Value.Held || !player.Inventory.HasSpace) return false;
            int slot = Array.IndexOf(player.Inventory.Slots,null);
            Location.Value = new ItemLocation { carrier = player.OwnerClientId, slot = slot };
            ApplyLocation(); player.Selection.Value = player.Inventory.CurrentSlot; return true;
        }
        public void Release(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            if (!IsServer) return;
            if (bound) { bound.Inventory.RemoveItem(item); bound.Selection.Value = bound.Inventory.CurrentSlot; bound = null; }
            item.Drop(position,rotation,velocity);
            Location.Value = new ItemLocation { carrier = ulong.MaxValue, slot = -1, position = position, rotation = rotation };
            GetComponent<NetworkTransform>().enabled = true;
            GetComponent<NetworkTransform>().Teleport(position,rotation,transform.localScale);
            var body = GetComponent<Rigidbody>(); body.linearVelocity = velocity;
        }
    }
}
