using System;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
namespace SurvivalFP
{
    public struct LobbyMember : INetworkSerializable,IEquatable<LobbyMember>
    {
        public ulong clientId; public FixedString128Bytes name;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter {s.SerializeValue(ref clientId);s.SerializeValue(ref name);}
        public bool Equals(LobbyMember other)=>clientId==other.clientId && name.Equals(other.name);
    }
    public sealed class LobbyRoster : NetworkBehaviour
    {
        public static LobbyRoster Instance {get;private set;}
        public NetworkList<LobbyMember> Members;
        public NetworkVariable<bool> Started=new(false);
        void Awake()=>Members=new();
        public static string Sanitize(string name)
        {
            string clean=new string((name??"").Where(c=>char.IsLetterOrDigit(c)||c==' '||c=='_'||c=='-').Take(20).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(clean)?"Survivor":clean;
        }
        public override void OnNetworkSpawn()
        {
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            Instance=this;
            if(IsServer)NetworkManager.OnClientDisconnectCallback+=Remove;
            RegisterRpc(new FixedString128Bytes(Sanitize(GameSession.Instance.PlayerName)));
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        void RegisterRpc(FixedString128Bytes name,RpcParams rpc=default)
        {
            ulong id=rpc.Receive.SenderClientId;
            if(Started.Value || !NetworkManager.ConnectedClients.ContainsKey(id))return;
            for(int i=0;i<Members.Count;i++)if(Members[i].clientId==id)return;
            Members.Add(new LobbyMember{clientId=id,name=new FixedString128Bytes(Sanitize(name.ToString()))});
        }
        void Remove(ulong id){for(int i=Members.Count-1;i>=0;i--)if(Members[i].clientId==id)Members.RemoveAt(i);}
        public string NameOf(ulong id){foreach(var member in Members)if(member.clientId==id)return member.name.ToString();return $"Player {id+1}";}
        public override void OnNetworkDespawn(){if(IsServer)NetworkManager.OnClientDisconnectCallback-=Remove;if(Instance==this)Instance=null;}
    }
}
