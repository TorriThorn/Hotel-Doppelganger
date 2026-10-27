using UnityEngine;
using UnityEngine.InputSystem;

// First-person movement and mouse look.
// WASD moves, Shift sprints, Esc releases the cursor and a click recaptures it.
// Interaction systems drive the crosshair and can lock movement or look through the public properties.
[AddComponentMenu("Game/Player/First Person Controller")]
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public enum CrosshairState { Dot, OpenHand, ClosedHand }

    private const float GroundedStickVelocity = -2f;
    private const int DotTextureSize = 32;

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

    // Icon requested by the interaction systems. Falls back to the dot if its texture is unset.
    public CrosshairState Crosshair { get; set; }

    // Shows the talk icon when no hand icon is active.
    public bool CanTalk { get; set; }

    // Stops the camera turning, e.g. while rotating a held object.
    public bool LookLocked { get; set; }

    // Freezes movement, look and cursor handling, e.g. during dialogue.
    public bool InputLocked { get; set; }

    public float SpeedMultiplier { get; set; } = 1f;
    public float LookMultiplier { get; set; } = 1f;

    // Tunable values, e.g. for the Playtest menu
    public float WalkSpeed { get => walkSpeed; set => walkSpeed = Mathf.Max(0f, value); }
    public float SprintSpeed { get => sprintSpeed; set => sprintSpeed = Mathf.Max(0f, value); }
    public float MouseSensitivity { get => mouseSensitivity; set => mouseSensitivity = Mathf.Max(0f, value); }
    public bool InvertY { get => invertY; set => invertY = value; }

    private CharacterController controller;
    private Texture2D dotTexture;
    private float verticalVelocity;
    private float cameraPitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }
    }

    private void Start() => SetCursorLocked(true);

    private void OnDestroy()
    {
        if (dotTexture != null) Destroy(dotTexture);
    }

    private void Update()
    {
        if (Keyboard.current == null || Mouse.current == null) return;

        if (!InputLocked)
        {
            HandleCursor();
            HandleLook();
        }

        HandleMovement();
    }

    private void HandleCursor()
    {
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);
        else if (Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            SetCursorLocked(true);
    }

    private void HandleLook()
    {
        if (LookLocked || cameraTransform == null || Cursor.lockState != CursorLockMode.Locked) return;

        Vector2 delta = Mouse.current.delta.ReadValue() * (mouseSensitivity * LookMultiplier);
        float pitchDelta = invertY ? delta.y : -delta.y;

        transform.Rotate(Vector3.up, delta.x);
        cameraPitch = Mathf.Clamp(cameraPitch + pitchDelta, -maxLookAngle, maxLookAngle);
        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        // A small constant downward push keeps CharacterController.isGrounded reliable
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = GroundedStickVelocity;

        Vector3 input = InputLocked ? Vector3.zero : ReadMoveInput();
        float speed = (Keyboard.current.leftShiftKey.isPressed ? sprintSpeed : walkSpeed) * SpeedMultiplier;

        Vector3 velocity = transform.TransformDirection(input) * speed;
        verticalVelocity += gravity * Time.deltaTime;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    private static Vector3 ReadMoveInput()
    {
        Keyboard kb = Keyboard.current;
        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        return Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
    }

    private void OnGUI()
    {
        if (InputLocked) return;

        if (dotTexture == null) dotTexture = CreateDotTexture(DotTextureSize);

        Texture2D icon = GetCrosshairIcon();
        bool useDot = icon == null;
        float size = useDot ? crosshairSize : handSize;
        Rect rect = new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size);

        GUI.color = useDot ? crosshairColor : Color.white;
        GUI.DrawTexture(rect, useDot ? dotTexture : icon);
        GUI.color = Color.white;
    }

    // Returns the icon for the current state, or null to fall back to the dot.
    private Texture2D GetCrosshairIcon()
    {
        if (Crosshair == CrosshairState.OpenHand && openHandTexture != null) return openHandTexture;
        if (Crosshair == CrosshairState.ClosedHand && closedHandTexture != null) return closedHandTexture;
        if (CanTalk && talkTexture != null) return talkTexture;
        return null;
    }

    private static Texture2D CreateDotTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size * 0.5f;
        var center = new Vector2(radius, radius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance)));
            }
        }

        texture.Apply();
        return texture;
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}