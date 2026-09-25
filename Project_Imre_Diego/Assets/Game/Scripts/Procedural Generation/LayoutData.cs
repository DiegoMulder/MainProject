using System;
using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    public struct RoomPlacement : INetworkSerializable, IEquatable<RoomPlacement>
    {
        public int module, quarter;
        public Vector3 position;
        public Quaternion Rotation => Quaternion.Euler(0, quarter * 90, 0);
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref module); s.SerializeValue(ref quarter); s.SerializeValue(ref position); }
        public bool Equals(RoomPlacement o) => module == o.module && quarter == o.quarter && position == o.position;
    }
    public struct ConnectionPlacement : INetworkSerializable, IEquatable<ConnectionPlacement>
    {
        public int a, ac, b, bc;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref a); s.SerializeValue(ref ac); s.SerializeValue(ref b); s.SerializeValue(ref bc); }
        public bool Equals(ConnectionPlacement o) => a == o.a && ac == o.ac && b == o.b && bc == o.bc;
    }
    public struct PropPlacement : INetworkSerializable, IEquatable<PropPlacement>
    {
        public int room, anchor, variant, halfTurn;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref room); s.SerializeValue(ref anchor); s.SerializeValue(ref variant); s.SerializeValue(ref halfTurn); }
        public bool Equals(PropPlacement o) => room == o.room && anchor == o.anchor && variant == o.variant && halfTurn == o.halfTurn;
    }
    public struct StructurePlacement : INetworkSerializable, IEquatable<StructurePlacement>
    {
        public int room, entry;
        public bool enabled;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref room); s.SerializeValue(ref entry); s.SerializeValue(ref enabled); }
        public bool Equals(StructurePlacement o) => room == o.room && entry == o.entry && enabled == o.enabled;
    }
}
