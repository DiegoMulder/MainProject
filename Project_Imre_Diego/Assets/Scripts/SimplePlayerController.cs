using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class SimplePlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -22f;
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private Transform viewCamera;

    [Header("Shooting")]
    [SerializeField] private SimpleBullet bulletPrefab;
    [SerializeField] private float bulletSpeed = 35f;
    [SerializeField] private float shotInterval = 0.15f;

    private CharacterController controller;
    private Vector3 spawnPosition;
    private float verticalSpeed;
    private float pitch;
    private float nextShotTime;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        spawnPosition = transform.position;
        if (viewCamera == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null) viewCamera = childCamera.transform;
        }
    }

    private void OnEnable() => SetCursorLocked(true);
    private void OnDisable() => SetCursorLocked(false);

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);

        bool aiming = Cursor.lockState == CursorLockMode.Locked;
        if (!aiming && mouse != null && mouse.leftButton.wasPressedThisFrame)
            SetCursorLocked(true);

        Vector2 movement = Vector2.zero;
        if (aiming && keyboard != null)
        {
            movement.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            movement.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            movement = Vector2.ClampMagnitude(movement, 1f);
        }

        if (aiming && mouse != null && viewCamera != null)
        {
            Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(Vector3.up, look.x);
            pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
            viewCamera.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            if (mouse.leftButton.wasPressedThisFrame) Shoot();
        }

        if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
        if (aiming && keyboard != null && keyboard.spaceKey.wasPressedThisFrame && controller.isGrounded)
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
        verticalSpeed += gravity * Time.deltaTime;
        Vector3 velocity = (transform.right * movement.x + transform.forward * movement.y) * moveSpeed;
        velocity.y = verticalSpeed;
        controller.Move(velocity * Time.deltaTime);

        if (transform.position.y < -15f)
        {
            controller.enabled = false;
            transform.position = spawnPosition;
            verticalSpeed = 0f;
            controller.enabled = true;
        }
    }

    public SimpleBullet Shoot()
    {
        if (bulletPrefab == null || viewCamera == null || Time.time < nextShotTime) return null;
        nextShotTime = Time.time + shotInterval;
        // Start at the camera so nearby walls cannot be skipped by a muzzle offset.
        SimpleBullet bullet = Instantiate(bulletPrefab, viewCamera.position, viewCamera.rotation);
        bullet.Launch(viewCamera.forward * bulletSpeed, controller);
        return bullet;
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) SetCursorLocked(false);
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(12f, 12f, 490f, 30f), "WASD: Move   |   Mouse: Look   |   Space: Jump   |   Left click: Shoot");
        GUI.Label(new Rect(20f, 46f, 360f, 25f), "Esc: Release cursor. Click to resume.");
        if (Cursor.lockState != CursorLockMode.Locked) return;
        float x = Screen.width * 0.5f;
        float y = Screen.height * 0.5f;
        GUI.DrawTexture(new Rect(x - 6f, y - 1f, 12f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 1f, y - 6f, 2f, 12f), Texture2D.whiteTexture);
    }
}
