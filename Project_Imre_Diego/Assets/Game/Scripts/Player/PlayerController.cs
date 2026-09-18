using UnityEngine;
using UnityEngine.InputSystem;

namespace SurvivalFP
{
    public struct PlayerCommand
    {
        public Vector2 Move;
        public Vector2 Look;
        public bool Sprint;
        public bool Crouch;
        public bool JumpPressed;
        public bool PickupPressed, DropPressed, Slot1Pressed, Slot2Pressed, Slot3Pressed, FlashlightPressed;
        public bool PrimaryUsePressed;
        public float Scroll;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement), typeof(PlayerStamina))]
    [RequireComponent(typeof(PlayerFlashlightShortcut))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Existing project Input System asset")]
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] PlayerMovement movement;
        [SerializeField] PlayerCamera playerCamera;
        [Header("Cursor")]
        [SerializeField] bool lockCursorOnEnable = true;
        InputActionAsset runtimeActions;
        InputAction radio, move, look, sprint, crouch, jump, release, capture, pickup, drop, slot1, slot2, slot3, scroll, primaryUse, flashlight;
        public bool InputBlocked { get; set; }
        public bool ManagePauseExternally { get; set; }
        int ignoreLookFrames;
        PlayerFlashlightShortcut flashlightShortcut;
        public bool Captured => Cursor.lockState == CursorLockMode.Locked;
        public InputActionAsset Actions => runtimeActions;
        public bool RadioHeld=>radio!=null && !InputBlocked && Captured && radio.IsPressed();

        void Awake()
        {
            flashlightShortcut = GetComponent<PlayerFlashlightShortcut>();
            if (!flashlightShortcut) flashlightShortcut = gameObject.AddComponent<PlayerFlashlightShortcut>();
            if (!movement) movement = GetComponent<PlayerMovement>();
            if (!playerCamera) playerCamera = GetComponentInChildren<PlayerCamera>();
            if (!inputActions || !movement || !playerCamera)
            {
                Debug.LogError("PlayerController requires input, movement and camera references.", this);
                enabled = false;
                return;
            }
            // A private action instance avoids disabling other users of the shared project asset.
            // Item interactions are routed through PlayerInventory so item behavior stays self-contained.
            runtimeActions = Instantiate(inputActions);
            move = runtimeActions.FindAction("Player/Move", true);
            look = runtimeActions.FindAction("Player/Look", true);
            sprint = runtimeActions.FindAction("Player/Sprint", true);
            crouch = runtimeActions.FindAction("Player/Crouch", true);
            jump = runtimeActions.FindAction("Player/Jump", true);
            release = runtimeActions.FindAction("Player/ReleaseCursor", true);
            capture = runtimeActions.FindAction("Player/CaptureCursor", true);
            var map = runtimeActions.FindActionMap("Player", true);
            pickup = Ensure(map, "Pickup", "<Keyboard>/e"); drop = Ensure(map, "Drop", "<Keyboard>/q");
            slot1 = Ensure(map, "Slot1", "<Keyboard>/1"); slot2 = Ensure(map, "Slot2", "<Keyboard>/2"); slot3 = Ensure(map, "Slot3", "<Keyboard>/3");
            scroll = Ensure(map, "ItemScroll", "<Mouse>/scroll/y", InputActionType.Value);
            primaryUse = Ensure(map, "PrimaryUse", "<Mouse>/leftButton");
            flashlight = Ensure(map, "Flashlight", "<Keyboard>/f");
            radio = Ensure(map, "RadioTransmit", "<Keyboard>/v");
        }
        static InputAction Ensure(InputActionMap map, string name, string binding, InputActionType type = InputActionType.Button)
        { var a = map.FindAction(name); if (a == null) a = map.AddAction(name, type, binding); return a; }

        void OnEnable()
        {
            if (!runtimeActions) return;
            runtimeActions.FindActionMap("Player", true).Enable();
            if (lockCursorOnEnable) SetCursor(true);
        }

        void OnDisable()
        {
            if (runtimeActions) runtimeActions.Disable();
            if (!ManagePauseExternally) SetCursor(false);
        }
        void OnDestroy() { if (runtimeActions) Destroy(runtimeActions); }
        void OnApplicationFocus(bool focused) { if (!focused) SetCursor(false); }

        void Update()
        {
            if (!runtimeActions) return;
            if (!ManagePauseExternally && release.WasPressedThisFrame()) SetCursor(false);
            else if (!InputBlocked && !Captured && capture.WasPressedThisFrame()) { SetCursor(true); return; }
            // Refocusing the Editor game view can generate a cursor-warp delta.
            if (capture.WasPressedThisFrame()) ignoreLookFrames = 2;
            PlayerCommand command = default;
            if (Captured && !InputBlocked)
            {
                command.Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
                command.Look = ignoreLookFrames > 0 ? Vector2.zero : look.ReadValue<Vector2>();
                // Mouse deltas are already frame displacement; sticks are angular rates.
                if (look.activeControl?.device is Gamepad) command.Look *= 900f * Time.deltaTime;
                command.Sprint = sprint.IsPressed();
                command.Crouch = crouch.IsPressed();
                command.JumpPressed = jump.WasPressedThisFrame();
                command.PickupPressed = pickup.WasPressedThisFrame(); command.DropPressed = drop.WasPressedThisFrame();
                command.Slot1Pressed = slot1.WasPressedThisFrame(); command.Slot2Pressed = slot2.WasPressedThisFrame(); command.Slot3Pressed = slot3.WasPressedThisFrame();
                command.Scroll = scroll.ReadValue<float>();
                command.PrimaryUsePressed = primaryUse.WasPressedThisFrame();
                command.FlashlightPressed = flashlight.WasPressedThisFrame();
            }
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (ignoreLookFrames > 0) ignoreLookFrames--;
            var networkPlayer=GetComponent<NetworkPlayer>();
            if(!networkPlayer || !networkPlayer.IsHidden)playerCamera.Look(command.Look, dt);
            var authority = GetComponent<IPlayerAuthority>();
            if (authority != null && authority.Active) authority.SubmitMovement(command, playerCamera.transform);
            else movement.Tick(command, dt);
            if(networkPlayer && networkPlayer.IsSpawned && !networkPlayer.Alive)return;
            var inventory = GetComponent<PlayerInventory>();
            if (inventory)
            {
                inventory.Tick(command, playerCamera.transform);
                if (command.PrimaryUsePressed) inventory.UsePrimary();
            }
            if (command.FlashlightPressed && authority != null && authority.Active) authority.InventoryAction(3);
            else flashlightShortcut.Tick(command.FlashlightPressed);
        }

        public void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            if (locked) ignoreLookFrames = 2;
        }
    }
}
