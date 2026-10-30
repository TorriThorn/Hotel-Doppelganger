using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// First-person movement and mouse look. WASD moves, Shift sprints, Esc frees the cursor and a click captures it again.
// Other systems drive the crosshair and can freeze the player through the public properties.
[AddComponentMenu("Game/Player/First Person Controller")]
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public enum CrosshairState { Dot, OpenHand, ClosedHand }

    // A small constant push down keeps CharacterController.isGrounded from flickering
    private const float GroundedStickVelocity = -2f;
    private const int DotTextureSize = 32;
    private const int CrosshairSortingOrder = 50;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Camera (optional)")]
    [SerializeField] private Transform cameraTransform;

    [Header("Movement")]
    [DisplayAs("Walk Speed", Min = 0f)]
    [SerializeField] private float walkSpeed = 5f;

    [DisplayAs("Sprint Speed", Min = 0f)]
    [SerializeField] private float sprintSpeed = 8f;

    [DisplayAs("Gravity")]
    [SerializeField] private float gravity = -19.62f;

    [Header("Mouse Look")]
    [DisplayAs("Mouse Sensitivity", Min = 0f)]
    [SerializeField] private float mouseSensitivity = 0.1f;

    [DisplayAs("Look Up/Down Limit", Min = 0f, Max = 90f)]
    [SerializeField] private float maxLookAngle = 85f;

    [DisplayAs("Invert Mouse Up/Down")]
    [SerializeField] private bool invertY;

    [Foldout("Crosshair Icons")]
    [DisplayAs("Dot Size", Min = 0f)]
    [SerializeField] private float crosshairSize = 6f;

    [DisplayAs("Dot Color")]
    [SerializeField] private Color crosshairColor = Color.white;

    [DisplayAs("Can Grab Icon")]
    [SerializeField] private Texture2D openHandTexture;

    [DisplayAs("Holding Icon")]
    [SerializeField] private Texture2D closedHandTexture;

    [DisplayAs("Can Talk Icon")]
    [SerializeField] private Texture2D talkTexture;

    [DisplayAs("Icon Size", Min = 0f)]
    [SerializeField] private float handSize = 32f;

    private CharacterController controller;
    private Camera playerCamera;
    private float verticalVelocity;
    private float cameraPitch;

    private Canvas crosshairCanvas;
    private RawImage crosshairImage;
    private Texture2D dotTexture;
    private Texture shownTexture;

    // Requested by the interaction systems. Falls back to the dot when that icon isn't assigned.
    public CrosshairState Crosshair { get; set; }

    // Shows the talk icon when no hand icon is active
    public bool CanTalk { get; set; }

    // Stops the camera turning, e.g. while rotating a held object
    public bool LookLocked { get; set; }

    // Freezes movement, look and cursor handling, e.g. during dialogue or menus
    public bool InputLocked { get; set; }

    public float SpeedMultiplier { get; set; } = 1f;
    public float LookMultiplier { get; set; } = 1f;

    public Transform CameraTransform => cameraTransform;

    // Up/down look angle in degrees. Positive is looking down.
    public float Pitch => cameraPitch;

    public float WalkSpeed { get => walkSpeed; set => walkSpeed = Mathf.Max(0f, value); }
    public float SprintSpeed { get => sprintSpeed; set => sprintSpeed = Mathf.Max(0f, value); }
    public float MouseSensitivity { get => mouseSensitivity; set => mouseSensitivity = Mathf.Max(0f, value); }
    public bool InvertY { get => invertY; set => invertY = value; }
    public float LookLimit { get => maxLookAngle; set => maxLookAngle = Mathf.Clamp(value, 0f, 90f); }
    public float Gravity { get => gravity; set => gravity = value; }
    public float DotSize { get => crosshairSize; set { crosshairSize = Mathf.Max(0f, value); shownTexture = null; } }
    public float IconSize { get => handSize; set { handSize = Mathf.Max(0f, value); shownTexture = null; } }

    // Reads 60 when there's no camera, so the Playtest slider still has something to show
    public float FieldOfView
    {
        get => playerCamera != null ? playerCamera.fieldOfView : 60f;
        set { if (playerCamera != null) playerCamera.fieldOfView = Mathf.Clamp(value, 1f, 179f); }
    }

    // Only touches the cursor when it actually needs to change, since menus call this every frame
    public static void SetCursorLocked(bool locked)
    {
        CursorLockMode mode = locked ? CursorLockMode.Locked : CursorLockMode.None;
        if (Cursor.lockState != mode) Cursor.lockState = mode;
        if (Cursor.visible == locked) Cursor.visible = !locked;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        if (cameraTransform != null) cameraTransform.TryGetComponent(out playerCamera);
        BuildCrosshair();
    }

    private void Start() => SetCursorLocked(true);

    private void OnValidate() => shownTexture = null;

    private void OnDestroy()
    {
        if (dotTexture != null) Destroy(dotTexture);
        if (crosshairCanvas != null) Destroy(crosshairCanvas.gameObject);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        if (!InputLocked)
        {
            HandleCursor(keyboard, mouse);
            HandleLook(mouse);
        }

        HandleMovement(keyboard);
    }

    private void LateUpdate() => UpdateCrosshair();

    private static void HandleCursor(Keyboard keyboard, Mouse mouse)
    {
        if (keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
        else if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);
    }

    private void HandleLook(Mouse mouse)
    {
        if (LookLocked || cameraTransform == null || Cursor.lockState != CursorLockMode.Locked) return;

        Vector2 delta = mouse.delta.ReadValue() * (mouseSensitivity * LookMultiplier);
        float pitchDelta = invertY ? delta.y : -delta.y;

        transform.Rotate(Vector3.up, delta.x);
        cameraPitch = Mathf.Clamp(cameraPitch + pitchDelta, -maxLookAngle, maxLookAngle);
        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    private void HandleMovement(Keyboard keyboard)
    {
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = GroundedStickVelocity;

        Vector3 input = InputLocked ? Vector3.zero : ReadMoveInput(keyboard);
        float speed = (keyboard.leftShiftKey.isPressed ? sprintSpeed : walkSpeed) * SpeedMultiplier;

        Vector3 velocity = transform.TransformDirection(input) * speed;
        verticalVelocity += gravity * Time.deltaTime;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    private static Vector3 ReadMoveInput(Keyboard keyboard)
    {
        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        return Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
    }

    // Unscaled canvas, so the dot and icons stay at their pixel sizes on any resolution
    private void BuildCrosshair()
    {
        dotTexture = CreateDotTexture(DotTextureSize);
        crosshairCanvas = RuntimeUI.CreateCanvas("Crosshair UI", CrosshairSortingOrder, false);

        RectTransform rect = RuntimeUI.CreateCentered("Crosshair", crosshairCanvas.transform, Vector2.zero);
        crosshairImage = rect.gameObject.AddComponent<RawImage>();
        crosshairImage.raycastTarget = false;
    }

    private void UpdateCrosshair()
    {
        bool visible = !InputLocked;
        if (crosshairCanvas.enabled != visible) crosshairCanvas.enabled = visible;
        if (!visible) return;

        Texture icon = CurrentIcon();
        bool isDot = icon == null;
        Texture texture = isDot ? dotTexture : icon;
        if (texture == shownTexture) return;

        shownTexture = texture;
        float size = isDot ? crosshairSize : handSize;
        crosshairImage.texture = texture;
        crosshairImage.color = isDot ? crosshairColor : Color.white;
        crosshairImage.rectTransform.sizeDelta = new Vector2(size, size);
    }

    private Texture CurrentIcon()
    {
        if (Crosshair == CrosshairState.OpenHand && openHandTexture != null) return openHandTexture;
        if (Crosshair == CrosshairState.ClosedHand && closedHandTexture != null) return closedHandTexture;
        if (CanTalk && talkTexture != null) return talkTexture;
        return null;
    }

    // Soft-edged white circle, tinted by Dot Color
    private static Texture2D CreateDotTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}