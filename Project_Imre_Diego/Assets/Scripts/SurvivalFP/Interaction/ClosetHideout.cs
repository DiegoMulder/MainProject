using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ClosetHideout : NetworkBehaviour, IInteractable
    {
        public const ulong Empty=ulong.MaxValue;
        public static readonly List<ClosetHideout> All=new();
        public static event Action<NetworkPlayer,ClosetHideout> Entering;
        public Transform entryPoint, hiddenPosition, cameraPosition, exitPosition, investigationPoint;
        public Transform[] alternateExits=Array.Empty<Transform>();
        [Range(.05f,1)] public float noiseMultiplier=.7f;
        public NetworkVariable<ulong> Occupant=new(Empty);
        public NetworkVariable<bool> ExitBlocked=new(false);
        float nextExitCheck;
        void Update()
        {
            if(!IsServer || Occupant.Value==Empty || Time.time<nextExitCheck)return;
            nextExitCheck=Time.time+.2f;
            var player=NetworkPlayer.Players.Find(p=>p && p.OwnerClientId==Occupant.Value);
            ExitBlocked.Value=player && !HasClearExit(player);
        }
        public Vector3 InvestigationPosition=>investigationPoint?investigationPoint.position:exitPosition.position;
        public override void OnNetworkSpawn() { All.Add(this); NetworkObject.AutoObjectParentSync=false; }
        public override void OnNetworkDespawn()
        {
            if(IsServer)
            {
                var player=NetworkPlayer.Players.Find(p=>p && p.OwnerClientId==Occupant.Value);
                if(player) { player.SetHiding(null); if(RoundManager.Instance && RoundManager.Instance.World.Built)player.Motor.Teleport(RoundManager.Instance.World.SpawnPosition); }
            }
            All.Remove(this);
        }
        public string Prompt(PlayerInteraction interaction)
        {
            var player=interaction.GetComponent<NetworkPlayer>();
            if(player && player.HiddenCloset==this) return !ExitBlocked.Value?"E  Exit closet":"Exit blocked — wait for space";
            return Occupant.Value==Empty?"E  Hide in closet":"Closet occupied";
        }
        public bool CanInteract(PlayerInteraction interaction)
        {
            var player=interaction.GetComponent<NetworkPlayer>();
            return player && player.Alive && (player.HiddenCloset==this ||
                (!player.IsHidden && Occupant.Value==Empty && entryPoint &&
                 Vector3.Distance(player.transform.position,entryPoint.position)<=interaction.distance));
        }
        public void Interact(PlayerInteraction interaction)
        {
            if(!IsServer || !CanInteract(interaction))return;
            var player=interaction.GetComponent<NetworkPlayer>();
            if(player.HiddenCloset==this)TryExit(player);else TryEnter(player);
        }
        public bool TryEnter(NetworkPlayer player)
        {
            if(!IsServer || !IsSpawned || !player || !CanInteract(player.Interaction) || player.IsHidden ||
                !hiddenPosition || !cameraPosition || !exitPosition || !HasClearExit(player)) return false;
            // Let ordinary perception observe the player BEFORE they move behind the door.
            Entering?.Invoke(player,this);
            Occupant.Value=player.OwnerClientId;
            player.SetHiding(this);
            player.Motor.Teleport(hiddenPosition.position);
            Physics.SyncTransforms();
            return true;
        }
        public bool TryExit(NetworkPlayer player)
        {
            if(!IsServer || !player || Occupant.Value!=player.OwnerClientId || !TryExitPosition(player,out var point))return false;
            player.Motor.Teleport(point);
            Occupant.Value=Empty;player.SetHiding(null);
            Physics.SyncTransforms();return true;
        }
        public void Forget(NetworkPlayer player)
        {
            if(IsServer && player && Occupant.Value==player.OwnerClientId) { Occupant.Value=Empty;player.SetHiding(null); }
        }
        public bool HasClearExit(NetworkPlayer player)=>TryExitPosition(player,out _);
        bool TryExitPosition(NetworkPlayer player,out Vector3 point)
        {
            point=default;
            if(exitPosition && IsSafeExit(player,exitPosition.position)) { point=exitPosition.position;return true; }
            foreach(var exit in alternateExits)
                if(exit && IsSafeExit(player,exit.position)) {point=exit.position;return true;}
            return false;
        }
        public bool IsSafeExit(NetworkPlayer player,Vector3 point)
        {
            if(!player || !NavMesh.SamplePosition(point,out var floor,.3f,NavMesh.AllAreas) ||
                Mathf.Abs(floor.position.y-point.y)>.2f)return false;
            float radius=player.GetComponent<CharacterController>().radius;
            float height=player.Motor.StandingHeight;
            var bottom=point+Vector3.up*(radius+.05f);var top=point+Vector3.up*(height-radius);
            if(Physics.OverlapCapsule(bottom,top,radius,~0,QueryTriggerInteraction.Ignore)
                .Any(c=>!c.transform.IsChildOf(player.transform)))return false;
            // Remote player capsules may be disabled locally; check replicated bodies too.
            foreach(var other in NetworkPlayer.Players)
                if(other && other!=player && !other.IsHidden &&
                   (other.Alive || other.Life.Value==PlayerLife.Downed) &&
                   Mathf.Abs(other.transform.position.y-point.y)<height &&
                   Vector2.Distance(new Vector2(point.x,point.z),new Vector2(other.transform.position.x,other.transform.position.z))<radius+.45f)return false;
            return Physics.Raycast(point+Vector3.up*.25f,Vector3.down,.6f,~0,QueryTriggerInteraction.Ignore);
        }
        public bool Catch(NetworkPlayer player)
        {
            if(!IsServer || !player || !player.Alive || player.HiddenCloset!=this || !TryExit(player))return false;
            player.Down();return true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics(){All.Clear();Entering=null;}
    }
}
