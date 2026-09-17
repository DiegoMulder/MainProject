using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace SurvivalFP.Editor
{
    // Runtime validation for the integrated controller.
    /// <summary>Repeatable Play Mode integration checks using the real scene colliders and motor.</summary>
    [InitializeOnLoad]
    public static class ControllerValidation
    {
        const string Request = "Library/SurvivalFP.validate-request";
        const string Running = "SurvivalFP.ValidationRunning";
        const string Report = "Logs/SurvivalFP-validation.txt";
        const float Dt = 1f / 60f;
        static readonly List<string> results = new List<string>();
        static readonly List<string> runtimeErrors = new List<string>();
        static int frames;
        static bool executed;
        static PlayerMovement motor;
        static PlayerStamina stamina;
        static PlayerCamera camera;
        static PlayerFlashlight torch;
        static PlayerController controller;
        static Vector3 spawn = new Vector3(0f, 0.1f, -14f); // inventory validation pass

        static ControllerValidation()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += ModeChanged;
        }

        [MenuItem("Tools/Survival FP/Run Play Mode Validation")]
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene before validation.");
            EditorSceneManager.OpenScene(ControllerTestSceneBuilder.ScenePath);
            SessionState.SetBool(Running, true);
            SessionState.SetBool("SurvivalFP.ControllerBatchValidation", Application.isBatchMode);
            EditorApplication.isPlaying = true;
        }

        static void Update()
        {
            if (File.Exists(Request) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(Request);
                try { Start(); } catch (Exception ex) { File.WriteAllText(Report, ex.ToString()); }
            }
            if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying) return;
            EditorApplication.QueuePlayerLoopUpdate();
            frames++;
            if (frames == 30 && !executed)
            {
                executed = true;
                try { RunChecks(); } catch (Exception ex) { results.Add("FAIL: Test harness: " + ex); }
            }
            if (executed && frames >= 90)
            {
                Check(runtimeErrors.Count == 0, "No controller runtime errors during Play Mode", string.Join("\n", runtimeErrors));
                int failures = results.Count(r => r.StartsWith("FAIL"));
                File.WriteAllText(Report, $"SURVIVAL FP PLAY MODE VALIDATION\nUnity {Application.unityVersion}\n{DateTime.UtcNow:O}\n" +
                    $"{results.Count - failures} passed / {failures} failed\n\n" + string.Join("\n", results));
                Debug.Log($"Survival FP validation finished: {results.Count - failures} passed, {failures} failed. {Report}");
                SessionState.SetBool(Running, false);
                EditorApplication.isPlaying = false;
            }
        }

        static void ModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("SurvivalFP.ControllerBatchValidation", false))
            {
                SessionState.SetBool("SurvivalFP.ControllerBatchValidation", false);
                EditorApplication.Exit(File.Exists(Report) && !File.ReadAllText(Report).Contains("FAIL:") ? 0 : 1);
            }
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
            {
                frames = 0; executed = false; results.Clear(); runtimeErrors.Clear();
                Application.logMessageReceived += CaptureError;
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Application.logMessageReceived -= CaptureError;
                SessionState.SetBool(Running, false);
            }
        }

        static void CaptureError(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeErrors.Add(message);
        }

        static void Check(bool condition, string name, string evidence = "") =>
            results.Add((condition ? "PASS: " : "FAIL: ") + name + (string.IsNullOrEmpty(evidence) ? "" : " | " + evidence));

        static void Step(PlayerCommand command, int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Physics.SyncTransforms();
                motor.Tick(command, Dt);
                camera.Look(command.Look, Dt);
                camera.TickEffects(Dt);
                torch.Tick(false, Dt);
            }
        }

        static void ResetAt(Vector3 position)
        {
            motor.Teleport(position);
            stamina.Restore();
            Physics.SyncTransforms();
            Step(default, 90);
        }

        static void RunChecks()
        {
            motor = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
            stamina = motor.GetComponent<PlayerStamina>();
            controller = motor.GetComponent<PlayerController>();
            camera = motor.GetComponentInChildren<PlayerCamera>();
            torch = UnityEngine.Object.FindObjectsByType<PlayerFlashlight>(FindObjectsInactive.Include)
                .FirstOrDefault(f => f.isActiveAndEnabled && f.GetComponent<PickupItem>() && f.GetComponent<PickupItem>().kind == PickupKind.Flashlight);
            var playerAudio = motor.GetComponent<PlayerAudio>();
            var inventoryForItems = motor.GetComponent<PlayerInventory>();
            var staminaSettings = new SerializedObject(stamina);
            float sprintDrain = staminaSettings.FindProperty("sprintDrain").floatValue;
            float regeneration = staminaSettings.FindProperty("regeneration").floatValue;
            float regenerationDelay = staminaSettings.FindProperty("regenerationDelay").floatValue;
            float flashlightIntensity = new SerializedObject(torch).FindProperty("onIntensity").floatValue;
            Check(controller.enabled, "PlayerController Awake completed with valid required references");
            Check(UnityEngine.Object.FindObjectsByType<Camera>().Count(c => c.enabled) == 1, "Exactly one active camera");
            Check(UnityEngine.Object.FindObjectsByType<AudioListener>().Count(c => c.enabled) == 1, "Exactly one audio listener");
            Check(playerAudio != null, "PlayerAudio component assigned");
            Check(inventoryForItems != null && inventoryForItems.Slots.Length == 3, "Three-slot item inventory assigned");
            Check(UnityEngine.Object.FindObjectsByType<PickupItem>().Length >= 3, "Temporary flashlight, cube and capsule pickups present");
            var beamTemplate = Resources.Load<GameObject>("Flashlight/FlashlightBeam");
            Check(beamTemplate && beamTemplate.GetComponentInChildren<Light>(true), "Flashlight fallback prefab has a serialized Light");
            Check(torch != null, "World flashlight pickup owns a PlayerFlashlight component");
            foreach (var component in new MonoBehaviour[] {motor, stamina, controller, camera, torch, playerAudio, UnityEngine.Object.FindAnyObjectByType<PlayerHUD>()})
            {
                if (!component)
                {
                    Check(false, "Serialized reference scan has no missing component");
                    continue;
                }
                var so = new SerializedObject(component);
                var p = so.GetIterator();
                bool valid = true;
                while (p.NextVisible(true))
                    if (p.propertyType == SerializedPropertyType.ObjectReference && p.name != "toggleSound" && p.objectReferenceValue == null) valid = false;
                Check(valid, component.GetType().Name + " serialized references assigned");
            }
            // Synthetic devices must reach the game in a batch editor without a focused Game view.
            // Use a temporary settings instance so the user's input preferences are preserved.
            var originalInputSettings = InputSystem.settings;
            var testInputSettings = UnityEngine.Object.Instantiate(originalInputSettings);
            var originalFlags = originalInputSettings.hideFlags;
            // Input System destroys its old default settings when HideAndDontSave is set.
            originalInputSettings.hideFlags = HideFlags.None;
            try
            {
                testInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                testInputSettings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                InputSystem.settings = testInputSettings;
                TestControllerInput();
                controller.enabled = false; // Isolate the motor from live devices.
                TestInput();
            }
            finally
            {
                InputSystem.settings = originalInputSettings;
                originalInputSettings.hideFlags = originalFlags;
                if (testInputSettings) UnityEngine.Object.DestroyImmediate(testInputSettings);
            }
            ResetAt(spawn);
            Check(motor.IsGrounded && motor.State == MovementState.Idle, "Flat ground resolves Idle");
            Step(new PlayerCommand { Move = Vector2.up }, 90);
            float walkSpeed = motor.ActualSpeed;
            Check(motor.State == MovementState.Walking && Mathf.Abs(walkSpeed - 4.2f) < 0.1f, "Walk reaches configured speed", walkSpeed.ToString("F3"));
            float stopStart = motor.transform.position.z;
            Step(default, 12);
            Check(motor.ActualSpeed < 0.01f && motor.transform.position.z - stopStart < 0.35f, "Responsive stop without ice sliding", $"distance={motor.transform.position.z - stopStart:F3}m");
            ResetAt(spawn);
            Step(new PlayerCommand { Move = Vector2.one }, 60);
            Check(motor.ActualSpeed <= walkSpeed + 0.02f, "Diagonal speed never exceeds forward speed", motor.ActualSpeed.ToString("F3"));
            ResetAt(spawn);
            Step(new PlayerCommand { Move = Vector2.down, Sprint = true }, 30);
            Check(motor.State == MovementState.Walking && Mathf.Abs(motor.ActualSpeed - 3f) < 0.1f, "Backwards speed and sprint exclusion", motor.ActualSpeed.ToString("F3"));
            ResetAt(spawn);
            Step(new PlayerCommand { Move = Vector2.right }, 45);
            Check(Mathf.Abs(motor.ActualSpeed - 3.8f) < 0.1f, "Independent strafe speed", motor.ActualSpeed.ToString("F3"));
            ResetAt(spawn);
            Step(new PlayerCommand { Move = Vector2.up, Sprint = true }, 120);
            Check(motor.State == MovementState.Sprinting && Mathf.Abs(motor.ActualSpeed - 7f) < 0.1f, "Forward sprint reaches 7 m/s");
            float afterSprint = stamina.Maximum - sprintDrain * 120 * Dt;
            Check(Mathf.Abs(stamina.Current - afterSprint) < 0.3f, "Sprint drain matches the configured rate", stamina.Current.ToString("F2"));
            Check(camera.GetComponent<Camera>().fieldOfView > 80.9f, "Sprint FOV smoothly reaches 81 degrees");
            Step(default, 45);
            Check(Mathf.Abs(stamina.Current - (afterSprint + regeneration * Mathf.Max(0f, 45 * Dt - regenerationDelay))) < 0.3f, "Regeneration delay respected");
            Step(default, 105);
            Check(Mathf.Abs(stamina.Current - Mathf.Min(stamina.Maximum, afterSprint + regeneration * Mathf.Max(0f, 150 * Dt - regenerationDelay))) < 0.4f, "Stamina regenerates at the configured rate after delay", stamina.Current.ToString("F2"));
            Check(Mathf.Abs(camera.GetComponent<Camera>().fieldOfView - 75f) < 0.05f && camera.BobOffset.magnitude < 0.002f, "FOV and head bob settle at rest");
            ResetAt(spawn);
            Step(new PlayerCommand { Move = new Vector2(0.3f, 1f), Sprint = true }, 30);
            Check(motor.State == MovementState.Sprinting, "Slight diagonal forward sprint allowed");
            ResetAt(new Vector3(0f, 0.1f, -20f));
            int exhaustionFrames = Mathf.CeilToInt(stamina.Maximum / Mathf.Max(sprintDrain, .01f) / Dt) + 120;
            for (int i = 0; i < exhaustionFrames && !stamina.Exhausted; i++)
            {
                // Lower drain settings require a run longer than the physical lane.
                // Return to its start without restoring stamina so walls cannot stop the drain.
                if (i > 0 && i % 180 == 0) motor.Teleport(new Vector3(0f, 0.1f, -20f));
                Step(new PlayerCommand { Move = Vector2.up, Sprint = true });
            }
            Step(new PlayerCommand { Move = Vector2.up, Sprint = true });
            Check(stamina.Exhausted && !stamina.CanSprint && motor.State != MovementState.Sprinting, "Exhaustion automatically ends sprint", stamina.Current.ToString("F2"));
            Step(new PlayerCommand { JumpPressed = true });
            Check(motor.VerticalSpeed <= 0f, "Insufficient stamina prevents a paid jump");
            Step(default, 300);
            Check(stamina.CanSprint, "Sprint unlocks after meaningful stamina recovery");

            ResetAt(spawn);
            Step(new PlayerCommand { Crouch = true, Move = Vector2.up, Sprint = true }, 60);
            Check(motor.State == MovementState.Crouching && Mathf.Abs(motor.Height - 1f) < 0.01f && motor.ActualSpeed < 2.1f, "Smooth crouch and sprint exclusion");
            motor.Teleport(new Vector3(-10f, 0.05f, 7f));
            Step(new PlayerCommand { Crouch = true }, 30);
            Step(default, 60);
            Check(motor.CeilingBlocked && motor.IsCrouching && motor.Height < 1.05f, "Cannot stand inside 1.25 m tunnel");
            Check(camera.transform.localPosition.y < 1.1f, "Crouched eye remains below ceiling");
            motor.Teleport(new Vector3(-10f, 0.05f, 14f));
            Step(default, 90);
            Check(!motor.CeilingBlocked && !motor.IsCrouching && Mathf.Abs(motor.Height - 2f) < 0.01f, "Stands up after leaving tunnel");

            ResetAt(spawn);
            float takeoff = motor.transform.position.y;
            float landingSpeed = 0f;
            Action<float> landing = v => landingSpeed = Mathf.Max(landingSpeed, v);
            motor.Landed += landing;
            Step(new PlayerCommand { Move = Vector2.up, JumpPressed = true });
            Check(motor.State == MovementState.Airborne && motor.VerticalSpeed > 0f, "Walking jump enters Airborne");
            float apex = motor.transform.position.y;
            float maxDip = 0f;
            for (int i = 0; i < 110; i++) { Step(default); apex = Mathf.Max(apex, motor.transform.position.y); maxDip = Mathf.Min(maxDip, camera.LandingOffset); }
            Check(apex - takeoff > 1.15f && apex - takeoff < 1.35f, "Jump height matches 1.2 m", $"apex={apex - takeoff:F3}m");
            Check(motor.IsGrounded && landingSpeed > 4f && maxDip < -0.005f && maxDip >= -0.101f, "Landing event and bounded camera spring", $"impact={landingSpeed:F2} dip={maxDip:F3}");
            motor.Landed -= landing;

            ResetAt(spawn);
            Step(new PlayerCommand { Move = Vector2.up, Sprint = true }, 40);
            Step(new PlayerCommand { Move = Vector2.up, Sprint = true, JumpPressed = true });
            Check(motor.VerticalSpeed > 0f && motor.HorizontalVelocity.magnitude > 6f, "Sprinting jump preserves momentum");
            ResetAt(new Vector3(-10f, 3.1f, 31.7f));
            int guard = 0;
            while (motor.IsGrounded && guard++ < 120) Step(new PlayerCommand { Move = Vector2.up });
            Step(new PlayerCommand { Move = Vector2.up }, 2);
            Step(new PlayerCommand { Move = Vector2.up, JumpPressed = true });
            Check(guard < 120 && motor.VerticalSpeed > 0f, "Coyote jump succeeds just after platform edge");

            ResetAt(spawn);
            motor.Teleport(new Vector3(0f, 2f, -14f));
            guard = 0;
            while (motor.transform.position.y > 0.5f && guard++ < 120) Step(default);
            Step(new PlayerCommand { JumpPressed = true });
            bool buffered = false;
            for (int i = 0; i < 14; i++) { Step(default); if (motor.VerticalSpeed > 1f) buffered = true; }
            Check(buffered, "Jump buffered before touchdown launches on landing");

            ResetAt(new Vector3(-10f, 0.1f, 19.2f));
            float highest = 0f;
            for (int i = 0; i < 160; i++) { Step(new PlayerCommand { Move = Vector2.up }); highest = Mathf.Max(highest, motor.transform.position.y); }
            Check(highest > 2.8f, "Walkable ramp reaches raised platform", $"height={highest:F3} position={motor.transform.position}");
            Step(default, 60);
            Vector3 slopeStop = motor.transform.position;
            Step(default, 60);
            Check(Vector3.Distance(slopeStop, motor.transform.position) < 0.05f, "Stationary player does not drift on supported ground");
            ResetAt(new Vector3(-10f, 2f, 24f));
            Vector3 rampStop = motor.transform.position;
            Step(default, 60);
            Check(rampStop.y > 0.3f && Vector3.Distance(rampStop, motor.transform.position) < 0.05f,
                "Stationary player holds position on the 22 degree ramp", $"drift={Vector3.Distance(rampStop, motor.transform.position):F4}m");
            motor.Teleport(new Vector3(-16f, 1.25f, 24f));
            stamina.Restore();
            Physics.SyncTransforms();
            Step(default, 1);
            highest = 0f;
            Vector3 slideStart = motor.transform.position;
            float maxSlideSpeed = 0f;
            bool sawSlide = motor.State == MovementState.Sliding;
            for (int i = 0; i < 90; i++) { Step(new PlayerCommand { Move = Vector2.up, Sprint = true }); highest = Mathf.Max(highest, motor.transform.position.y); maxSlideSpeed = Mathf.Max(maxSlideSpeed, motor.SlideSpeed); sawSlide |= motor.State == MovementState.Sliding; }
            Check(sawSlide && (maxSlideSpeed > 0.5f || Vector3.Distance(slideStart, motor.transform.position) > 0.05f), "60 degree slope slides downhill", $"height={highest:F3} maxSpeed={maxSlideSpeed:F2}");

            torch.Tick(true, Dt);
            Check(torch.IsOn && torch.Intensity > 0f && torch.Intensity < flashlightIntensity * .5f, "Flashlight toggles on with a smooth fade");
            torch.Tick(true, Dt);
            Check(torch.IsOn, "Flashlight cooldown ignores rapid second toggle");
            for (int i = 0; i < 60; i++) torch.Tick(false, Dt);
            Check(Mathf.Abs(torch.Intensity - flashlightIntensity) < flashlightIntensity * .002f, "Flashlight reaches configured intensity");
            torch.Tick(true, Dt);
            for (int i = 0; i < 90; i++) torch.Tick(false, Dt);
            Check(!torch.IsOn && torch.Intensity == 0f && torch.gameObject.activeSelf, "Flashlight fades out while GameObject stays active");
            Check(playerAudio != null, "Player audio event system is present");

            motor.transform.rotation = Quaternion.identity;
            camera.transform.localRotation = Quaternion.identity;
            ResetAt(spawn);
            // World pickups now fall as soon as Play starts. Place the fixture
            // under the ray at test time rather than assuming its initial height.
            var worldBody = torch.GetComponent<Rigidbody>();
            Check(worldBody && !worldBody.isKinematic && worldBody.useGravity, "World pickup starts with active physics");
            worldBody.position = camera.transform.position + camera.transform.forward * 2f;
            worldBody.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            inventoryForItems.Tick(new PlayerCommand { PrimaryUsePressed = true }, camera.transform);
            Check(!inventoryForItems.Current, "Left click alone never picks up a ground item");
            camera.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
            inventoryForItems.Tick(new PlayerCommand { PickupPressed = true }, camera.transform);
            Check(!inventoryForItems.Current, "E only picks up an item under the direct camera ray");
            camera.transform.localRotation = Quaternion.identity;
            inventoryForItems.Tick(new PlayerCommand { PickupPressed = true }, camera.transform);
            Check(inventoryForItems.Current && inventoryForItems.Current.kind == PickupKind.Flashlight, "E pickup finds the flashlight in front of the player");
            var heldBody = torch.GetComponent<Rigidbody>();
            Check(heldBody && heldBody.isKinematic && !heldBody.detectCollisions && heldBody.interpolation == RigidbodyInterpolation.None,
                "Held item disables physics and interpolation");
            var heldScale = torch.transform.localScale;
            torch.GetComponent<PickupItem>().SetHeld(torch.transform.parent);
            Check((torch.transform.localScale - heldScale).sqrMagnitude < 0.000001f && torch.transform.localPosition.sqrMagnitude < 0.000001f,
                "Repeated hand assignment keeps the item transform stable");
            inventoryForItems.UsePrimary();
            var aimWall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            aimWall.transform.position=camera.transform.position+camera.transform.forward;
            aimWall.transform.localScale=new Vector3(2,2,.1f);Physics.SyncTransforms();
            var modelPosition=torch.transform.localPosition;var modelRotation=torch.transform.localRotation;
            torch.Beam.transform.localRotation=Quaternion.identity;
            torch.AimAtView(Dt);var firstAim=torch.Beam.transform.localRotation;
            for(int i=0;i<120;i++)torch.AimAtView(Dt);
            float fullAim=Quaternion.Angle(Quaternion.identity,torch.Beam.transform.localRotation);
            Check(Quaternion.Angle(Quaternion.identity,firstAim)>0 && Quaternion.Angle(Quaternion.identity,firstAim)<fullAim*.4f,
                "Flashlight approaches a nearby surface gradually instead of snapping");
            var nearAim=torch.Beam.transform.localRotation;
            UnityEngine.Object.DestroyImmediate(aimWall);Physics.SyncTransforms();
            torch.AimAtView(Dt);var firstFarAim=torch.Beam.transform.localRotation;
            for(int i=0;i<120;i++)torch.AimAtView(Dt);
            Check(Quaternion.Angle(nearAim,firstFarAim)<Quaternion.Angle(nearAim,torch.Beam.transform.localRotation)*.4f,
                "Flashlight smoothly crosses a near-to-far depth discontinuity");
            Check(torch.transform.localPosition==modelPosition && Quaternion.Angle(torch.transform.localRotation,modelRotation)<.01f,
                "Beam aiming preserves the held model pose");
            Step(default, 20);
            Check(torch.IsOn, "Equipped flashlight responds to primary use");
            inventoryForItems.Tick(new PlayerCommand { Slot2Pressed = true }, camera.transform);
            bool flashlightBeforeShortcut = torch.IsOn;
            motor.GetComponent<PlayerFlashlightShortcut>().Tick(true);
            Check(torch.IsOn != flashlightBeforeShortcut, "F toggles a flashlight in a non-equipped inventory slot");
            inventoryForItems.Tick(new PlayerCommand { Slot1Pressed = true }, camera.transform);
            inventoryForItems.Tick(new PlayerCommand { DropPressed = true }, camera.transform);
            var droppedBody = torch.GetComponent<Rigidbody>();
            Check(droppedBody && !droppedBody.isKinematic && droppedBody.useGravity && droppedBody.linearVelocity.sqrMagnitude > 1f,
                "Dropping an item restores Rigidbody physics", droppedBody ? $"velocity={droppedBody.linearVelocity.magnitude:F2}m/s" : "missing Rigidbody");
            torch.Toggle();

            float initialYaw = motor.transform.eulerAngles.y;
            camera.Look(new Vector2(30f, 100000f), Dt);
            Step(default, 60);
            Check(Mathf.Abs(Mathf.DeltaAngle(0f, camera.transform.localEulerAngles.x) + 85f) < 0.1f &&
                Mathf.Abs(Mathf.DeltaAngle(initialYaw, motor.transform.eulerAngles.y) - 3f) < 0.1f,
                "Mouse look clamps pitch and rotates the player in yaw");
            camera.Look(new Vector2(-30f, -850f), Dt);
            Step(default, 60);

            ResetAt(spawn);
            controller.enabled = true;
            controller.SetCursor(false);
        }

        static void TestInput()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var actions = InputActionAsset.FromJson(File.ReadAllText("Assets/Game/Input/InputSystem_Actions.inputactions"));
            try
            {
                actions.devices = new InputDevice[] {keyboard};
                actions.FindActionMap("Player").Enable();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D, Key.LeftShift, Key.LeftCtrl, Key.Space, Key.F));
                InputSystem.Update();
                Vector2 move = actions.FindAction("Player/Move").ReadValue<Vector2>();
                Check(move.magnitude <= 1.001f && move.x > 0.5f && move.y > 0.5f, "Input System WASD composite normalizes diagonals");
                foreach (string action in new[] {"Sprint", "Crouch", "Jump", "Flashlight"})
                    Check(actions.FindAction("Player/" + action).IsPressed(), "Input binding resolves " + action);
            }
            finally { actions.Disable(); UnityEngine.Object.DestroyImmediate(actions); InputSystem.RemoveDevice(keyboard); }
        }

        static void TestControllerInput()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var previousDevices = controller.Actions.devices;
            try
            {
                controller.Actions.devices = new InputDevice[] {keyboard};
                controller.SetCursor(true);
                motor.Teleport(spawn);
                Physics.SyncTransforms();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift, Key.F));
                InputSystem.Update();
                if (Application.isBatchMode)
                {
                    // Batch Unity has no native cursor to lock. Check the real bindings
                    // and shortcut routing here; the motor and gameplay actions have
                    // independent scene/physics tests below.
                    motor.GetComponent<PlayerFlashlightShortcut>().Tick(controller.Actions.FindAction("Player/Flashlight").WasPressedThisFrame());
                    Check(!torch.IsOn && controller.Actions.FindAction("Player/Move").ReadValue<Vector2>().y > .5f,
                        "Controller input bindings resolve movement and F leaves an unowned flashlight off (batch)");
                }
                else
                {
                    controller.SendMessage("Update");
                    Check(!torch.IsOn && motor.HorizontalVelocity.z > 0f, "Full controller input pipeline moves while flashlight remains item-controlled");
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                InputSystem.Update();
                if (Application.isBatchMode)
                    Check(controller.Actions.FindAction("Player/ReleaseCursor").WasPressedThisFrame(), "Escape resolves ReleaseCursor input (batch)");
                else
                {
                    controller.SendMessage("Update");
                    Check(!controller.Captured, "Escape releases the cursor through controller input");
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                for (int i = 0; i < 60; i++) torch.Tick(false, Dt);
                if (torch.IsOn) torch.Tick(true, Dt);
                for (int i = 0; i < 90; i++) torch.Tick(false, Dt);
            }
            finally { controller.Actions.devices = previousDevices; InputSystem.RemoveDevice(keyboard); }
        }
    }
}





