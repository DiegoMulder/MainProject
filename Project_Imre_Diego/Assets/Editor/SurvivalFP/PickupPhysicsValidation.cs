using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivalFP.Editor
{
    // Uses real rendered frames and fixed physics steps: a synchronous transform
    // check cannot catch Rigidbody interpolation overwriting a child's world pose.
    [InitializeOnLoad]
    public static class PickupPhysicsValidation
    {
        const string Running = "SurvivalFP.PickupPhysicsRunning";
        const string Finished = "SurvivalFP.PickupPhysicsFinished";
        const string Active = "SurvivalFP.PickupPhysicsActive";
        public const string Report = "Logs/PickupPhysics-validation.txt";

        static PickupPhysicsValidation()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Finished, false))
                {
                    SessionState.SetBool(Finished, false);
                    if (Application.isBatchMode)
                        EditorApplication.Exit(File.ReadAllText(Report).Contains("FAIL:") ? 1 : 0);
                }
            };
        }

        [MenuItem("Tools/Survival FP/Validate Pickup Physics")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(Running, true);
            SessionState.SetBool(Active, true);
            EditorApplication.isPlaying = true;
        }

        static void Update()
        {
            if (SessionState.GetBool(Running, false) && EditorApplication.isPlaying && !EditorApplication.isCompiling)
            {
                SessionState.SetBool(Running, false);
                var scene = SceneManager.CreateScene("Pickup Physics Regression");
                var runner = new GameObject("Pickup Physics Probe");
                SceneManager.MoveGameObjectToScene(runner, scene);
                runner.AddComponent<PickupPhysicsProbe>();
            }
            if (SessionState.GetBool(Active, false) && EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
        }

        public static void Finish(List<string> results)
        {
            Directory.CreateDirectory("Logs");
            var failures = results.Count(r => r.StartsWith("FAIL:"));
            File.WriteAllText(Report, $"PICKUP PHYSICS REGRESSION\n{DateTime.UtcNow:O}\nUnity {Application.unityVersion}\n" +
                $"{results.Count - failures} passed / {failures} failed\n\n" + string.Join("\n", results));
            SessionState.SetBool(Finished, true);
            SessionState.SetBool(Active, false);
            EditorApplication.isPlaying = false;
        }
    }

    public sealed class PickupPhysicsProbe : MonoBehaviour
    {
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();
        static readonly Vector3 Origin = new Vector3(1000f, 100f, 1000f);

        void Check(bool condition, string name) => results.Add((condition ? "PASS: " : "FAIL: ") + name);
        void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message);
        }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            var tests = Test();
            while (true)
            {
                object wait = null;
                bool more;
                try { more = tests.MoveNext(); if (more) wait = tests.Current; }
                catch (Exception ex) { results.Add("FAIL: " + ex); break; }
                if (!more) break;
                yield return wait;
            }
            Application.logMessageReceived -= Log;
            Check(errors.Count == 0, "No runtime errors: " + string.Join("; ", errors));
            PickupPhysicsValidation.Finish(results);
        }

        GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale)
        {
            var obj = GameObject.CreatePrimitive(type);
            SceneManager.MoveGameObjectToScene(obj, gameObject.scene);
            obj.name = name;
            obj.transform.position = position;
            obj.transform.localScale = scale;
            return obj;
        }

        PickupItem Item(string name, Vector3 position, PickupKind kind = PickupKind.Generic)
        {
            var obj = Primitive(name, PrimitiveType.Cube, position, Vector3.one * .3f);
            obj.SetActive(false);
            var pickup = obj.AddComponent<PickupItem>();
            pickup.kind = kind;
            if (kind == PickupKind.Flashlight) obj.AddComponent<PlayerFlashlight>();
            obj.SetActive(true);
            return pickup;
        }

        IEnumerator Test()
        {
            foreach (string name in new[] { "Flashlight", "Cube", "Capsule" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/SurvivalFP/Pickupables/{name}.prefab");
                var rb = prefab ? prefab.GetComponent<Rigidbody>() : null;
                Check(rb && !rb.isKinematic && rb.useGravity, name + " prefab starts with dynamic physics and gravity");
            }

            Primitive("Floor", PrimitiveType.Cube, Origin + Vector3.down * .5f, new Vector3(100f, 1f, 100f));
            var falling = Item("Fresh pickup", Origin + new Vector3(4f, 3f, 0f));
            var fallBody = falling.GetComponent<Rigidbody>();
            float startY = fallBody.position.y;
            Check(!fallBody.isKinematic && fallBody.useGravity && fallBody.detectCollisions, "New PickupItem enables physics in Awake");
            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            Check(fallBody.position.y < startY - .3f && fallBody.linearVelocity.y < -1f, "Untouched pickup falls under gravity before inventory use");

            var player = new GameObject("Test Player");
            SceneManager.MoveGameObjectToScene(player, gameObject.scene);
            player.SetActive(false);
            player.transform.position = Origin;
            var motor = player.AddComponent<PlayerMovement>();
            // Match the serialized references on the real player prefab; camera
            // and motor Awake callbacks have no guaranteed relative order.
            var motorSettings = new SerializedObject(motor);
            motorSettings.FindProperty("capsule").objectReferenceValue = player.GetComponent<CharacterController>();
            motorSettings.FindProperty("stamina").objectReferenceValue = player.GetComponent<PlayerStamina>();
            motorSettings.ApplyModifiedPropertiesWithoutUndo();
            var eye = new GameObject("Player Camera");
            eye.transform.SetParent(player.transform, false);
            eye.AddComponent<Camera>();
            var camera = eye.AddComponent<PlayerCamera>();
            var inventory = player.AddComponent<PlayerInventory>();
            var shortcut = player.AddComponent<PlayerFlashlightShortcut>();
            // Reproduce a broken scene reference: a free-floating hand anchor.
            var strayAnchor = new GameObject("Detached hand anchor").transform;
            SceneManager.MoveGameObjectToScene(strayAnchor.gameObject, gameObject.scene);
            strayAnchor.position = Origin + Vector3.left * 20f;
            var settings = new SerializedObject(inventory);
            settings.FindProperty("handAnchor").objectReferenceValue = strayAnchor;
            settings.ApplyModifiedPropertiesWithoutUndo();
            player.SetActive(true);
            yield return null;
            Check(strayAnchor.parent == eye.transform, "Inventory repairs an anchor outside the camera hierarchy");

            inventory.Tick(new PlayerCommand { Slot1Pressed = true }, eye.transform);
            var item = Item("Equippable cube", eye.transform.position + eye.transform.forward * 2f);
            Physics.SyncTransforms();
            inventory.Tick(new PlayerCommand { PrimaryUsePressed = true }, eye.transform);
            Check(!item.Held, "Left click does not pick up a world item");
            var wall = Primitive("Occluder", PrimitiveType.Cube, eye.transform.position + eye.transform.forward, Vector3.one * .5f);
            Physics.SyncTransforms();
            inventory.Tick(new PlayerCommand { PickupPressed = true }, eye.transform);
            Check(!item.Held, "E cannot pick through the first blocking collider");
            Destroy(wall);
            yield return null;
            item.transform.position = eye.transform.position + eye.transform.forward * 2f + eye.transform.right * .4f;
            item.GetComponent<Rigidbody>().position = item.transform.position;
            Physics.SyncTransforms();
            inventory.Tick(new PlayerCommand { PickupPressed = true }, eye.transform);
            Check(!item.Held, "E misses when the direct ray is beside the collider");
            item.GetComponent<Rigidbody>().position = eye.transform.position + eye.transform.forward * 2f;
            Physics.SyncTransforms();
            inventory.Tick(new PlayerCommand { PickupPressed = true }, eye.transform);
            Check(item.Held && inventory.Current == item && item.GetComponent<Renderer>().enabled, "E equips a direct hit even after selecting an empty slot");
            var heldBody = item.GetComponent<Rigidbody>();
            Check(heldBody.isKinematic && !heldBody.useGravity && !heldBody.detectCollisions && heldBody.interpolation == RigidbodyInterpolation.None,
                "Held body releases transform control and disables physics");

            float maxPositionError = 0f, maxRotationError = 0f;
            Vector3 heldScale = item.transform.localScale;
            for (int i = 0; i < 90; i++)
            {
                motor.Tick(new PlayerCommand { Move = Vector2.up, Crouch = i > 30 && i < 60, JumpPressed = i == 65 }, .02f);
                camera.Look(new Vector2(8f, Mathf.Sin(i * .1f) * 3f), .02f);
                if (i == 45) motor.Teleport(motor.transform.position + Vector3.right * 5f);
                if (i % 3 == 0) yield return new WaitForFixedUpdate();
                yield return null;
                maxPositionError = Mathf.Max(maxPositionError, Vector3.Distance(item.transform.position, strayAnchor.position));
                maxRotationError = Mathf.Max(maxRotationError, Quaternion.Angle(item.transform.rotation, strayAnchor.rotation));
            }
            Check(maxPositionError < .001f && maxRotationError < .1f,
                $"Held pose stays attached across 90 frames of moving, turning, crouching, jumping and teleporting (error {maxPositionError:F6} m, {maxRotationError:F4} deg)");
            for (int i = 0; i < 8; i++)
            {
                inventory.Tick(new PlayerCommand { Slot2Pressed = true }, eye.transform);
                inventory.Tick(new PlayerCommand { Slot1Pressed = true }, eye.transform);
                yield return null;
            }
            Check(Vector3.Distance(heldScale, item.transform.localScale) < .00001f && item.transform.parent == strayAnchor,
                "Repeated slot switching preserves held size and camera parenting");

            var flashlight = Item("Inventory flashlight", eye.transform.position + eye.transform.forward * 2f, PickupKind.Flashlight);
            shortcut.Tick(true);
            Check(!flashlight.GetComponent<PlayerFlashlight>().IsOn, "F does not control a flashlight on the ground");
            Physics.SyncTransforms();
            inventory.Tick(new PlayerCommand { PickupPressed = true }, eye.transform);
            var light = flashlight.GetComponent<PlayerFlashlight>();
            shortcut.Tick(true);
            Check(flashlight.Held && inventory.Current == item && light.IsOn, "F toggles the flashlight with a different item equipped");
            Check(light.Beam.transform != flashlight.transform && Quaternion.Angle(strayAnchor.localRotation, Quaternion.identity) < .01f,
                "Flashlight has an independent beam and the camera hand anchor has no rotation offset");
            var target = Primitive("Crosshair target", PrimitiveType.Cube, eye.transform.position + eye.transform.forward * 5f, Vector3.one * 2f);
            float aimError = 0f;
            bool targetHit = true;
            foreach (float distance in new[] { 2f, 6f, 20f })
            {
                for (int i = 0; i < 10; i++)
                {
                    camera.Look(new Vector2(3f, 1f), .02f);
                    target.transform.position = eye.transform.position + eye.transform.forward * distance;
                    Physics.SyncTransforms();
                    yield return null;
                    var ray = eye.GetComponent<Camera>().ViewportPointToRay(new Vector3(.5f, .5f, 0f));
                    if (target.GetComponent<Collider>().Raycast(ray, out var hit, 40f))
                        aimError = Mathf.Max(aimError, Vector3.Cross(light.Beam.transform.forward, hit.point - light.Beam.transform.position).magnitude);
                    else targetHit = false;
                }
            }
            Check(targetHit && aimError < .01f, $"Beam centres on the crosshair at near, medium and far surfaces while turning (miss {aimError:F6} m)");
            Destroy(target);
            yield return new WaitForSeconds(.25f);
            inventory.Tick(new PlayerCommand { Slot2Pressed = true }, eye.transform);
            inventory.UsePrimary();
            Check(!light.IsOn, "Equipped flashlight also responds to left-click primary use");
            inventory.Tick(new PlayerCommand { Slot1Pressed = true }, eye.transform);
            yield return null;
            Check(Vector3.Distance(flashlight.transform.position, strayAnchor.position) < .001f,
                "Aiming the flashlight does not move its model or the hand anchor");
            inventory.Tick(new PlayerCommand { DropPressed = true }, eye.transform);
            Check(!item.Held && !item.transform.parent && !heldBody.isKinematic && heldBody.useGravity && heldBody.detectCollisions &&
                item.GetComponent<Collider>().enabled && heldBody.interpolation == RigidbodyInterpolation.Interpolate, "Drop restores dynamic physics, colliders and interpolation");
            float dropY = heldBody.position.y;
            for (int i = 0; i < 15; i++) yield return new WaitForFixedUpdate();
            Check(heldBody.position.y < dropY && heldBody.linearVelocity.y < 0f, "Dropped item physically falls after the throw");
            item.GetComponent<Rigidbody>().position = eye.transform.position + eye.transform.forward * 2f;
            Physics.SyncTransforms();
            inventory.Tick(new PlayerCommand { PickupPressed = true }, eye.transform);
            inventory.Tick(new PlayerCommand { Slot1Pressed = true }, eye.transform);
            yield return null;
            Check(item.Held && item.transform.parent == strayAnchor && Vector3.Distance(heldScale, item.transform.localScale) < .00001f,
                "Dropped item can be picked up again without changing size or attachment");
            inventory.Tick(new PlayerCommand { Slot2Pressed = true }, eye.transform);
            inventory.Tick(new PlayerCommand { DropPressed = true }, eye.transform);
            yield return new WaitForSeconds(.25f);
            bool droppedLightState = light.IsOn;
            shortcut.Tick(true);
            Check(light.IsOn == droppedLightState && Quaternion.Angle(light.Beam.transform.localRotation, Quaternion.identity) < .01f,
                "Dropped flashlight stops aiming with the camera and F no longer controls it");
        }
    }
}
