using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Rendering;
namespace SurvivalFP
{
    public enum PlayerLife { Alive, Dead, Escaped, Downed }
    [DefaultExecutionOrder(100), RequireComponent(typeof(PlayerController), typeof(PlayerInteraction),typeof(PlayerPrediction))]
    public sealed class NetworkPlayer : NetworkBehaviour, IPlayerAuthority
    {
        public static readonly List<NetworkPlayer> Players = new();
        public static NetworkPlayer Local => Players.FirstOrDefault(p => p && p.IsOwner);
        public NetworkVariable<PlayerLife> Life = new(PlayerLife.Alive);
        public NetworkVariable<ulong> GrabbedBy=new(ulong.MaxValue);
        public bool IsGrabbed=>GrabbedBy.Value!=ulong.MaxValue;
        public NetworkVariable<double> BleedOutAt = new(0), BleedOutStartedAt = new(0);
        public float BleedOutProgress=>Life.Value==PlayerLife.Downed?Mathf.Clamp01(1-BleedOutRemaining/Mathf.Max(.001f,(float)(BleedOutAt.Value-BleedOutStartedAt.Value))):0;
        public NetworkVariable<ulong> HiddenClosetId=new(ClosetHideout.Empty);
        public bool IsHidden=>HiddenClosetId.Value!=ClosetHideout.Empty;
        public ClosetHideout HiddenCloset=>ClosetHideout.All.Find(c=>c && c.IsSpawned && c.NetworkObjectId==HiddenClosetId.Value);
        Vector3 savedViewPosition;
        Quaternion savedViewRotation;
        bool hidingPresented;
        [Min(1)] public float bleedOutDuration = 300f;
        public string DisplayName => LobbyRoster.Instance ? LobbyRoster.Instance.NameOf(OwnerClientId) : $"Player {OwnerClientId+1}";
        public float BleedOutRemaining => IsSpawned ? Mathf.Max(0,(float)(BleedOutAt.Value-NetworkManager.ServerTime.Time)) : 0;
        [Header("Voice hearing")]
        [Min(0)] public float voiceNoiseRadius=16;
        [Min(.2f)] public float voiceNoiseInterval=.65f;
        float nextVoiceNoise, nextVoiceReport;
        public NetworkVariable<float> Stamina = new(100f), Height = new(2f), Pitch = new(0f), Yaw = new(0f);
        public NetworkVariable<Vector3> Velocity = new(Vector3.zero);
        public NetworkVariable<int> Selection = new(-1), Gait = new(0);
        public NetworkVariable<bool> Grounded = new(true);
        public bool Active => IsSpawned;
        public bool CanMove=>IsSpawned && (Life.Value==PlayerLife.Alive || Life.Value==PlayerLife.Downed) && !IsHidden && !IsGrabbed
            && RoundManager.Instance && RoundManager.Instance.Phase.Value==RoundPhase.Playing;
        [Header("Downed posture")]
        [Min(0)] public float downedCrawlSpeed=.65f;
        public Transform visualBody;
        Vector3 bodyPosition;Quaternion bodyRotation;
        PlayerPrediction prediction;
        public bool Alive => IsSpawned && Life.Value == PlayerLife.Alive;
        public PlayerInventory Inventory { get; private set; }
        public PlayerInteraction Interaction { get; private set; }
        public Transform View { get; private set; }
        public PlayerController Controller { get; private set; }
        public PlayerMovement Motor { get; private set; }
        public PlayerCamera CameraMotion { get; private set; }
        PlayerCommand pending;
        float lastInput, lastAction, snapshotTime;
        float inputYaw, inputPitch;
        bool paused;
        public bool Paused => paused;
        void Awake()
        {
            prediction=GetComponent<PlayerPrediction>();
            if(!visualBody)visualBody=transform.Find("Survivor body");
            if(visualBody){bodyPosition=visualBody.localPosition;bodyRotation=visualBody.localRotation;}
            Inventory = GetComponent<PlayerInventory>(); Interaction = GetComponent<PlayerInteraction>();
            Controller = GetComponent<PlayerController>(); Motor = GetComponent<PlayerMovement>();
            CameraMotion = GetComponentInChildren<PlayerCamera>(true); View = CameraMotion.transform;
            Controller.ManagePauseExternally = true; Controller.enabled = false;
            CameraMotion.enabled = false;
            View.GetComponent<Camera>().enabled = false;
            View.GetComponent<AudioListener>().enabled = false;
            GetComponent<PlayerAudio>().enabled = false;
        }
        public override void OnNetworkSpawn()
        {
            Players.Add(this);
            Controller.enabled = IsOwner; CameraMotion.enabled = IsOwner;
            View.GetComponent<Camera>().enabled = IsOwner;
            View.GetComponent<AudioListener>().enabled = IsOwner;
            // Remote capsules must participate in local collision prediction too.
            // Only the owner/server ticks a motor; remote transforms remain network-driven.
            GetComponent<CharacterController>().enabled = true;
            if(IsOwner && !IsServer)GetComponent<NetworkTransform>().enabled=false;
            GetComponent<PlayerAudio>().enabled = IsOwner;
            ApplyBodyVisibility();
            Life.OnValueChanged += OnLife; Selection.OnValueChanged += OnSelection; HiddenClosetId.OnValueChanged+=OnHiding;
            if (IsOwner) { CameraMotion.ApplySettings(); Controller.SetCursor(true); }
            OnLife(Life.Value, Life.Value);
        }
        public override void OnNetworkDespawn()
        {
            if(IsServer && HiddenCloset)HiddenCloset.Forget(this);
            HiddenClosetId.OnValueChanged-=OnHiding;
            if (IsServer && RoundManager.Instance && !(GameSession.Instance && GameSession.Instance.Transitioning)) RoundManager.Instance.ReleaseItems(this);
            if(IsServer)foreach(var enemy in EnemyController.Enemies.ToArray())if(enemy)enemy.InvalidatePlayer(this);
            Players.Remove(this); Life.OnValueChanged -= OnLife; Selection.OnValueChanged -= OnSelection;
            if (IsOwner) Controller.SetCursor(false);
            if (IsServer && RoundManager.Instance && !(GameSession.Instance && GameSession.Instance.Transitioning)) RoundManager.Instance.EvaluateRound();
        }
        void OnSelection(int old, int value) { if (value >= 0) Inventory.ApplySelection(value); }
        void OnLife(PlayerLife old, PlayerLife value)
        {
            bool alive = value == PlayerLife.Alive;
            bool downed=value==PlayerLife.Downed;pending=default;
            if(IsServer&&!alive)foreach(var enemy in EnemyController.Enemies.ToArray())if(enemy)enemy.InvalidatePlayer(this);
            Motor.SetDowned(downed,downedCrawlSpeed);
            if(visualBody && !GetComponent<PlayerAnimationDriver>()){visualBody.localPosition=downed?new Vector3(0,.3f,0):bodyPosition;
                visualBody.localRotation=downed?Quaternion.Euler(90,0,0)*bodyRotation:bodyRotation;}
            Controller.InputBlocked = (!alive && !downed) || paused;
            GetComponent<CharacterController>().enabled = alive || downed;
            foreach (var c in GetComponentsInChildren<Collider>()) if (!(c is CharacterController) && !c.GetComponentInParent<PickupItem>()) c.enabled = alive;
            var revive = GetComponent<DownedInteractable>();
            if(revive) revive.SetDowned(value==PlayerLife.Downed);
            if (IsOwner) { CameraMotion.enabled = alive || downed; GetComponent<PlayerAudio>().enabled = alive; Controller.SetCursor((alive || downed) && !paused); }
            ApplyHiding();
            if(IsServer)prediction.ForceState();
        }
        public void SetHiding(ClosetHideout closet)
        {
            if(!IsServer)return;
            pending=default;Velocity.Value=Vector3.zero;Gait.Value=(int)MovementState.Idle;
            HiddenClosetId.Value=closet?closet.NetworkObjectId:ClosetHideout.Empty;
            ApplyHiding();
            prediction.ForceState();
        }
        void OnHiding(ulong old,ulong value)=>ApplyHiding();
        void ApplyHiding()
        {
            if(IsHidden && !hidingPresented){savedViewPosition=View.localPosition;savedViewRotation=View.localRotation;}
            if(!IsHidden && hidingPresented){View.localPosition=savedViewPosition;View.localRotation=savedViewRotation;}
            hidingPresented=IsHidden;
            GetComponent<CharacterController>().enabled=(Alive || Life.Value==PlayerLife.Downed) && !IsHidden;
            if(IsOwner){CameraMotion.enabled=(Alive || Life.Value==PlayerLife.Downed) && !IsHidden;GetComponent<PlayerAudio>().enabled=Alive && !IsHidden;}
            ApplyBodyVisibility();
        }
        public void ApplyBodyVisibility()
        {
            bool visible=Life.Value==PlayerLife.Alive||Life.Value==PlayerLife.Downed;
            var driver=GetComponent<PlayerAnimationDriver>();if(driver&&driver.animator)driver.animator.enabled=visible;
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=!visible||IsHidden;
            if(!visualBody)return;
            foreach(var renderer in visualBody.GetComponentsInChildren<Renderer>(true))
                if(!renderer.GetComponentInParent<PickupItem>())renderer.shadowCastingMode=IsOwner && (Alive || Life.Value==PlayerLife.Downed)?ShadowCastingMode.ShadowsOnly:ShadowCastingMode.On;
        }
        void LateUpdate()
        {
            ApplyBodyVisibility();
            var closet=HiddenCloset;
            if(!closet)return;
            transform.position=closet.hiddenPosition.position;
            View.SetPositionAndRotation(closet.cameraPosition.position,closet.cameraPosition.rotation);
        }
        public void SetPaused(bool value)
        {
            if (!IsOwner) return;
            paused = value; Controller.InputBlocked = value || (!Alive && Life.Value!=PlayerLife.Downed); Controller.SetCursor(!value && (Alive || Life.Value==PlayerLife.Downed));
        }
        public void SubmitMovement(PlayerCommand command, Transform view)
        {
            if (!IsOwner || !CanMove) return;
            if (paused) command = default;
            if(!IsServer){prediction.Predict(command,transform.eulerAngles.y,Mathf.DeltaAngle(0,view.localEulerAngles.x),Mathf.Min(Time.deltaTime,.05f));return;}
            MoveRpc(command.Move, command.Sprint, command.Crouch, command.JumpPressed, transform.eulerAngles.y, Mathf.DeltaAngle(0,view.localEulerAngles.x));
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        void MoveRpc(Vector2 move, bool sprint, bool crouch, bool jump, float yaw, float pitch)
        {
            if (!CanMove || !float.IsFinite(move.x) || !float.IsFinite(move.y) || !float.IsFinite(yaw) || !float.IsFinite(pitch)) return;
            pending.Move = Vector2.ClampMagnitude(move,1); pending.Sprint = sprint; pending.Crouch = crouch; pending.JumpPressed |= jump;
            inputYaw = yaw % 360f; inputPitch = Mathf.Clamp(pitch,-85,85); lastInput = Time.time;
        }
        public void SetNetworkLook(float yaw,float pitch){inputYaw=yaw;inputPitch=pitch;}
        void Update()
        {
            if (!IsSpawned) return;
            if(IsServer && Life.Value==PlayerLife.Downed && NetworkManager.ServerTime.Time>=BleedOutAt.Value) Kill();
            if (IsServer && CanMove)
            {
                if (Time.time - lastInput > .3f) pending = default;
                if(IsOwner){Motor.Tick(pending, Mathf.Min(Time.deltaTime,.05f));pending.JumpPressed=false;}
                if (!IsOwner) { View.localPosition = Vector3.up * (Motor.Height-.18f); View.localRotation = Quaternion.Euler(inputPitch,0,0); }
                if (Time.time >= snapshotTime)
                {
                    snapshotTime = Time.time + .05f;
                    Stamina.Value = GetComponent<PlayerStamina>().Current; Height.Value = Motor.Height;
                    Velocity.Value = Motor.HorizontalVelocity + Vector3.up * Motor.VerticalSpeed;
                    Gait.Value = (int)Motor.State; Grounded.Value = Motor.IsGrounded; Pitch.Value = inputPitch; Yaw.Value = inputYaw;
                    Selection.Value = Inventory.CurrentSlot;
                }
                if (transform.position.y < -8) Motor.Teleport(RoundManager.Instance.World.SpawnPosition);
            }
            if (!IsServer && !IsOwner && !IsHidden)
            {
                Motor.ApplyNetworkPresentation((MovementState)Gait.Value,Height.Value,Velocity.Value,Grounded.Value,Time.deltaTime);
                if (!IsOwner) { View.localPosition = Vector3.up*(Height.Value-.18f); View.localRotation = Quaternion.Euler(Pitch.Value,0,0); }
            }
        }
        public void RequestInteract(MonoBehaviour target, Vector3 direction)
        {
            var networkObject = target.GetComponentInParent<NetworkObject>();
            if (IsOwner && Alive && !paused && networkObject) InteractRpc(new NetworkObjectReference(networkObject), direction);
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void InteractRpc(NetworkObjectReference reference, Vector3 direction)
        {
            if (!CanAct() || !float.IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude < .5f || !reference.TryGet(out var target)) return;
            if(IsHidden)
            {
                var closet=HiddenCloset;
                if(closet && target==closet.NetworkObject)closet.Interact(Interaction);
                return;
            }
            Physics.SyncTransforms();
            var hit = Interaction.FindTarget(transform.position + Vector3.up*(Motor.Height-.18f),direction.normalized);
            if (!hit || hit.GetComponentInParent<NetworkObject>() != target || !(hit is IInteractable interactable) || !interactable.CanInteract(Interaction)) return;
            interactable.Interact(Interaction);
        }
        public bool CanUseItems=>CanAct()&&!IsHidden;
        bool CanAct() => Alive && !IsGrabbed && RoundManager.Instance && RoundManager.Instance.Phase.Value == RoundPhase.Playing;
        public void InventoryAction(int action, int slot = -1) { if (IsOwner && CanUseItems && !paused) InventoryRpc(action,slot); }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void InventoryRpc(int action, int slot)
        {
            if (!CanUseItems) return;
            if (action == 0) { Inventory.ApplySelection(slot); Selection.Value = Inventory.CurrentSlot; return; }
            if (Time.time - lastAction < .08f) return; lastAction = Time.time;
            if (action == 1 && Inventory.Current)
            {
                var item=Inventory.Current.GetComponent<NetworkPickup>();
                if(item && SafeItemDrop.TryFind(Inventory.Current,transform,View,out var position,out var rotation))
                    item.Release(position,rotation,Inventory.DropVelocity(View));
            }
            else if (action == 2 && Inventory.Current) Inventory.Current.UsePrimary();
            else if (action == 3) GetComponent<PlayerFlashlightShortcut>().Tick(true);
            Selection.Value = Inventory.CurrentSlot;
        }
        public void Down()
        {
            if(!IsServer || !Alive) return;
            if(HiddenCloset && !HiddenCloset.TryExit(this))return;
            BleedOutStartedAt.Value=NetworkManager.ServerTime.Time;
            BleedOutAt.Value=BleedOutStartedAt.Value+bleedOutDuration;
            pending=default; Life.Value=PlayerLife.Downed; RoundManager.Instance.EvaluateRound();
        }
        public void ReportSpeech()
        {
            if(!IsOwner || !Alive || Time.unscaledTime<nextVoiceReport)return;
            nextVoiceReport=Time.unscaledTime+voiceNoiseInterval;SpeechRpc();
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner)]
        void SpeechRpc()
        {
            if(!EnemyTargetRules.CanTarget(this) || Time.time<nextVoiceNoise || !RoundManager.Instance || RoundManager.Instance.Phase.Value!=RoundPhase.Playing)return;
            nextVoiceNoise=Time.time+Mathf.Max(.2f,voiceNoiseInterval);
            GameplayNoiseSystem.Emit(transform.position,voiceNoiseRadius,NoiseCategory.Voice,gameObject);
        }
        public bool Revive()
        {
            if(!IsServer || Life.Value!=PlayerLife.Downed || BleedOutRemaining<=0 || RoundManager.Instance.Phase.Value!=RoundPhase.Playing) return false;
            pending=default; BleedOutAt.Value=0; Life.Value=PlayerLife.Alive; return true;
        }
        public void Kill()
        {
            if (!IsServer || !IsSpawned || (Life.Value!=PlayerLife.Alive && Life.Value!=PlayerLife.Downed)) return;
            if(HiddenCloset)HiddenCloset.Forget(this);
            RoundManager.Instance.ReleaseItems(this); Life.Value = PlayerLife.Dead; pending = default; RoundManager.Instance.EvaluateRound();
        }
    }
}
