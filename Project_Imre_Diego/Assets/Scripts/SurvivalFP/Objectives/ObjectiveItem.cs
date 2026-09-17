using Unity.Collections;
using Unity.Netcode;
namespace SurvivalFP
{
    public sealed class ObjectiveItem : NetworkBehaviour
    {
        public NetworkVariable<FixedString64Bytes> Kind = new(new FixedString64Bytes("Seal"));
        public NetworkVariable<bool> Deposited = new(false);
        public override void OnNetworkSpawn() { Kind.OnValueChanged+=Changed; Changed(default,Kind.Value); }
        public override void OnNetworkDespawn() => Kind.OnValueChanged-=Changed;
        void Changed(FixedString64Bytes old,FixedString64Bytes value) => GetComponent<PickupItem>().displayName=value.ToString();
    }
}
