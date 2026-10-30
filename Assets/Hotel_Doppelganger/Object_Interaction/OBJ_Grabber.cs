using UnityEngine;
using UnityEngine.InputSystem;

// SOMA-style physical carrying: hold left mouse to carry, right mouse to rotate, scroll to push or pull.
// Works on any non-kinematic Rigidbody. A Grabbable on the object adds per-object settings.
[AddComponentMenu("Game/Player/Object Grabber")]
public class ObjectGrabber : MonoBehaviour
{
    private const float LineOfSightPadding = 0.02f;
    private const float MinLiftFollow = 0.2f;
    private const float ShakeFrequency = 2f;
    private const float HeavyStuckTimeBonus = 2f;
    private const float RotationDeadZone = 0.01f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Camera (optional)")]
    [SerializeField] private Transform cameraTransform;

    [DisplayAs("Player (optional)")]
    [SerializeField] private FirstPersonController player;

    [Header("Picking Up")]
    [DisplayAs("Reach", Min = 0f)]
    [SerializeField] private float reach = 3f;

    [DisplayAs("Heaviest Liftable Mass", Min = 0f)]
    [SerializeField] private float maxMass = 20f;

    [DisplayAs("Grabbable Layers")]
    [SerializeField] private LayerMask grabLayers = ~0;

    [DisplayAs("Aim Forgiveness", Min = 0f, Max = 1f)]
    [SerializeField] private float grabRadius = 0.2f;

    [DisplayAs("Only Grabbable Objects")]
    [SerializeField] private bool requireGrabbable;

    [Header("Holding")]
    [DisplayAs("Closest Hold Distance", Min = 0f)]
    [SerializeField] private float minHoldDistance = 1f;

    [DisplayAs("Furthest Hold Distance", Min = 0f)]
    [SerializeField] private float maxHoldDistance = 2.5f;

    [DisplayAs("Scroll Step", Min = 0f)]
    [SerializeField] private float scrollStep = 0.25f;

    [Foldout("Carry Feel (advanced)")]
    [DisplayAs("Follow Strength", Min = 0f)]
    [SerializeField] private float followStrength = 15f;

    [DisplayAs("Max Follow Speed", Min = 0f)]
    [SerializeField] private float maxSpeed = 25f;

    [DisplayAs("Drop When This Far Away", Min = 0f)]
    [SerializeField] private float dropDistance = 1.5f;

    [DisplayAs("Drop After Stuck For", Min = 0f)]
    [SerializeField] private float stuckTime = 0.5f;

    [DisplayAs("Rotate Speed", Min = 0f)]
    [SerializeField] private float rotateSpeed = 0.3f;

    [DisplayAs("Rotate Strength", Min = 0f)]
    [SerializeField] private float rotateStrength = 15f;

    // "At Max" values are for objects at Heaviest Liftable Mass and blend out toward Weightless Below Mass
    [Foldout("Heavy Objects (advanced)")]
    [DisplayAs("Weightless Below Mass", Min = 0f)]
    [SerializeField] private float lightMass = 1f;

    [DisplayAs("Follow Strength At Max", Min = 0f, Max = 1f)]
    [SerializeField] private float heavyFollow = 0.25f;

    [DisplayAs("Follow Speed At Max", Min = 0f)]
    [SerializeField] private float heavyMaxSpeed = 6f;

    [DisplayAs("Rotation At Max", Min = 0f, Max = 1f)]
    [SerializeField] private float heavyRotate = 0.25f;

    [DisplayAs("Droop At Max", Min = 0f)]
    [SerializeField] private float heavySag = 0.35f;

    [DisplayAs("Wobble At Max", Min = 0f)]
    [SerializeField] private float heavyShake = 0.04f;

    [DisplayAs("Lift Time At Max", Min = 0f)]
    [SerializeField] private float heavyLiftTime = 1f;

    [DisplayAs("Player Walk Speed At Max", Min = 0f, Max = 1f)]
    [SerializeField] private float heavyPlayerSpeed = 0.45f;

    [DisplayAs("Player Look Speed At Max", Min = 0f, Max = 1f)]
    [SerializeField] private float heavyPlayerLook = 0.5f;

    [Foldout("Glow")]
    [DisplayAs("Glow On Aimed Object")]
    [SerializeField] private bool highlightTarget = true;

    [DisplayAs("Keep Glowing While Held", ShowIf = nameof(highlightTarget))]
    [SerializeField] private bool highlightWhileHolding;

    [DisplayAs("Glow Shader", ShowIf = nameof(highlightTarget))]
    [SerializeField] private Shader highlightShader;

    [DisplayAs("Glow Color", ShowIf = nameof(highlightTarget))]
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.4f, 1f);

    [DisplayAs("Glow Fill", ShowIf = nameof(highlightTarget), Min = 0f, Max = 1f)]
    [SerializeField] private float highlightFill = 0.15f;

    [DisplayAs("Edge Sharpness", ShowIf = nameof(highlightTarget), Min = 0.5f, Max = 8f)]
    [SerializeField] private float highlightRimSharpness = 2.5f;

    [DisplayAs("Glow Pulse", ShowIf = nameof(highlightTarget), Min = 0f, Max = 1f)]
    [SerializeField] private float highlightPulse = 0.3f;

    private readonly RaycastHit[] rayHits = new RaycastHit[16];
    private readonly RaycastHit[] sphereHits = new RaycastHit[32];

    private Collider playerCollider;

    private Rigidbody held;
    private Grabbable heldGrabbable;
    private Collider[] heldColliders;
    private bool heldUsedGravity;
    private RigidbodyInterpolation heldInterpolation;
    private Quaternion holdRotation;
    private float holdDistance;
    private float weight;
    private float liftProgress;
    private float stuckTimer;

    private Material highlightMaterial;
    private Rigidbody highlighted;
    private Renderer[] highlightedRenderers;
    private Material[][] originalMaterials;

    public bool IsHolding { get; private set; }
    public Rigidbody HeldObject => held;

    public float Reach { get => reach; set => reach = Mathf.Max(0f, value); }
    public float AimForgiveness { get => grabRadius; set => grabRadius = Mathf.Max(0f, value); }
    public float MaxMass { get => maxMass; set => maxMass = Mathf.Max(0f, value); }
    public float ClosestHold { get => minHoldDistance; set => minHoldDistance = Mathf.Clamp(value, 0.1f, maxHoldDistance); }
    public float FarthestHold { get => maxHoldDistance; set => maxHoldDistance = Mathf.Max(value, minHoldDistance); }
    public float ScrollStep { get => scrollStep; set => scrollStep = Mathf.Max(0f, value); }
    public float RotateSpeed { get => rotateSpeed; set => rotateSpeed = Mathf.Max(0f, value); }
    public float FollowStrength { get => followStrength; set => followStrength = Mathf.Max(0f, value); }
    public bool ShowGlow { get => highlightTarget; set => highlightTarget = value; }

    private void Awake()
    {
        if (player == null) TryGetComponent(out player);
        TryGetComponent(out CharacterController body);
        playerCollider = body;

        if (cameraTransform == null && player != null) cameraTransform = player.CameraTransform;
        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        if (highlightShader == null) highlightShader = Shader.Find("Custom/GrabHighlight");
        if (highlightShader != null)
        {
            highlightMaterial = new Material(highlightShader) { name = "Grab Highlight (Runtime)" };
            ApplyHighlightLook();
        }
    }

    private void Reset() => highlightShader = Shader.Find("Custom/GrabHighlight");

    private void OnValidate()
    {
        if (highlightMaterial != null) ApplyHighlightLook();
    }

    private void OnDisable()
    {
        if (IsHolding) Drop();
        ClearHighlight();
    }

    private void OnDestroy()
    {
        ClearHighlight();
        if (highlightMaterial != null) Destroy(highlightMaterial);
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || cameraTransform == null) return;

        if (player != null && player.InputLocked)
        {
            if (IsHolding) Drop();
            ClearHighlight();
            return;
        }

        if (IsHolding) UpdateHolding(mouse);
        else UpdateSearching(mouse);
    }

    private void FixedUpdate()
    {
        if (!IsHolding) return;

        if (!HeldStillValid())
        {
            Drop();
            return;
        }

        float liftTime = heavyLiftTime * weight;
        liftProgress = liftTime > 0f ? Mathf.MoveTowards(liftProgress, 1f, Time.fixedDeltaTime / liftTime) : 1f;

        // Aim for the center of mass so props with offset pivots still sit in the middle of the view
        Vector3 offset = HoldPoint() - held.worldCenterOfMass;

        if (StuckTooLong(offset.magnitude))
        {
            Drop();
            return;
        }

        float follow = followStrength * Mathf.Lerp(1f, heavyFollow, weight) * Mathf.Lerp(MinLiftFollow, 1f, liftProgress);
        held.linearVelocity = Vector3.ClampMagnitude(offset * follow, Mathf.Lerp(maxSpeed, heavyMaxSpeed, weight));

        RotateTowardHoldRotation();
    }

    private void UpdateSearching(Mouse mouse)
    {
        Rigidbody target = Cursor.lockState == CursorLockMode.Locked ? FindGrabbable() : null;

        SetHighlight(target);
        SetCrosshair(target != null ? FirstPersonController.CrosshairState.OpenHand : FirstPersonController.CrosshairState.Dot);

        if (target != null && mouse.leftButton.wasPressedThisFrame) Grab(target);
    }

    private void UpdateHolding(Mouse mouse)
    {
        if (!HeldStillValid() || !mouse.leftButton.isPressed)
        {
            Drop();
            return;
        }

        SetCrosshair(FirstPersonController.CrosshairState.ClosedHand);
        SetHighlight(highlightWhileHolding ? held : null);

        float scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0f)
            holdDistance = Mathf.Clamp(holdDistance + Mathf.Sign(scroll) * scrollStep, minHoldDistance, maxHoldDistance);

        // Right mouse turns the object instead of the camera
        bool rotating = mouse.rightButton.isPressed && (heldGrabbable == null || heldGrabbable.canRotate);
        if (player != null) player.LookLocked = rotating;
        if (rotating) RotateHoldWithMouse(mouse);
    }

    private Vector3 HoldPoint()
    {
        Vector3 point = cameraTransform.position + cameraTransform.forward * holdDistance;
        if (weight <= 0f) return point;

        float t = Time.time * ShakeFrequency;
        var shake = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f);

        return point
               + Vector3.down * (heavySag * weight)
               + cameraTransform.rotation * shake * (heavyShake * weight * 2f);
    }

    // Heavier objects get longer to catch up before they're dropped
    private bool StuckTooLong(float distanceFromHoldPoint)
    {
        if (distanceFromHoldPoint <= dropDistance)
        {
            stuckTimer = 0f;
            return false;
        }

        stuckTimer += Time.fixedDeltaTime;
        return stuckTimer >= stuckTime * (1f + weight * HeavyStuckTimeBonus);
    }

    private void RotateTowardHoldRotation()
    {
        Quaternion difference = cameraTransform.rotation * holdRotation * Quaternion.Inverse(held.rotation);
        difference.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        if (Mathf.Abs(angle) <= RotationDeadZone || float.IsInfinity(axis.x))
        {
            held.angularVelocity = Vector3.zero;
            return;
        }

        float strength = rotateStrength * Mathf.Lerp(1f, heavyRotate, weight);
        held.angularVelocity = axis * (angle * Mathf.Deg2Rad * strength);
    }

    private void RotateHoldWithMouse(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue() * (rotateSpeed * Mathf.Lerp(1f, heavyRotate, weight));

        Quaternion cameraRotation = cameraTransform.rotation;
        Quaternion worldRotation = Quaternion.AngleAxis(-delta.x, cameraTransform.up)
                                   * Quaternion.AngleAxis(delta.y, cameraTransform.right)
                                   * cameraRotation * holdRotation;

        holdRotation = Quaternion.Inverse(cameraRotation) * worldRotation;
    }

    private Rigidbody FindGrabbable()
    {
        Vector3 origin = cameraTransform.position;
        Vector3 direction = cameraTransform.forward;

        // Exact aim always wins
        if (RaycastIgnoringPlayer(origin, direction, reach, grabLayers, out RaycastHit direct))
        {
            if (CanGrab(direct.rigidbody)) return direct.rigidbody;

            // Looking straight at an NPC leaves the click for dialogue instead of grabbing a prop next to them
            if (direct.collider.GetComponentInParent<NPCDialogue>() != null) return null;
        }

        if (grabRadius <= 0f) return null;

        // Otherwise sweep a fatter ray and take whatever sits closest to the crosshair
        int count = Physics.SphereCastNonAlloc(origin, grabRadius, direction, sphereHits, reach, grabLayers, QueryTriggerInteraction.Ignore);
        Rigidbody best = null;
        float bestScore = float.MaxValue;
        float reachSqr = reach * reach;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = sphereHits[i];
            Rigidbody candidate = hit.rigidbody;
            if (candidate == best || !CanGrab(candidate)) continue;

            // Colliders already inside the sphere when the cast starts report no hit point
            Vector3 point = hit.distance > 0f ? hit.point : ClosestPointOn(hit.collider, origin);
            Vector3 toPoint = point - origin;
            if (toPoint.sqrMagnitude > reachSqr) continue;

            float score = Vector3.Cross(direction, toPoint).sqrMagnitude;
            if (score >= bestScore || !HasLineOfSight(origin, toPoint, candidate)) continue;

            bestScore = score;
            best = candidate;
        }

        return best;
    }

    private bool CanGrab(Rigidbody rb)
    {
        if (rb == null || rb.transform.IsChildOf(transform)) return false;

        bool hasGrabbable = rb.TryGetComponent(out Grabbable grabbable);

        // Kinematic bodies can't be carried, except a Grabbable mid-glide, which the player can catch
        if (rb.isKinematic && (!hasGrabbable || !grabbable.IsGliding)) return false;

        if (!hasGrabbable) return !requireGrabbable && rb.mass <= maxMass;
        if (!grabbable.enabled || !grabbable.canGrab) return false;

        // A custom weight ignores the mass limit
        return !grabbable.weightFromMass || rb.mass <= maxMass;
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 toPoint, Rigidbody target)
    {
        float distance = toPoint.magnitude - LineOfSightPadding;
        if (distance <= 0f) return true;

        return !RaycastIgnoringPlayer(origin, toPoint.normalized, distance, Physics.DefaultRaycastLayers, out RaycastHit blocker)
               || blocker.rigidbody == target;
    }

    // Skips the player's own colliders, which would otherwise block the ray from inside the head
    private bool RaycastIgnoringPlayer(Vector3 origin, Vector3 direction, float distance, int mask, out RaycastHit closest)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, rayHits, distance, mask, QueryTriggerInteraction.Ignore);

        closest = default;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayHits[i];
            if ((found && hit.distance >= closest.distance) || hit.collider.transform.IsChildOf(transform)) continue;

            closest = hit;
            found = true;
        }

        return found;
    }

    // Collider.ClosestPoint doesn't work on concave mesh colliders, so use their bounds instead
    private static Vector3 ClosestPointOn(Collider hitCollider, Vector3 position) =>
        hitCollider is MeshCollider mesh && !mesh.convex
            ? hitCollider.bounds.ClosestPoint(position)
            : hitCollider.ClosestPoint(position);

    // The held object can be destroyed, disabled, made kinematic or locked by something else at any time
    private bool HeldStillValid()
    {
        if (held == null || held.isKinematic || !held.gameObject.activeInHierarchy) return false;
        return heldGrabbable == null || (heldGrabbable.enabled && heldGrabbable.canGrab);
    }

    private void Grab(Rigidbody rb)
    {
        held = rb;
        rb.TryGetComponent(out heldGrabbable);

        // Cancel any glide first so the Rigidbody state saved below is the real one
        if (heldGrabbable != null) heldGrabbable.CancelGlide();

        IsHolding = true;
        heldColliders = rb.GetComponentsInChildren<Collider>();
        stuckTimer = 0f;
        liftProgress = 0f;

        bool customWeight = heldGrabbable != null && !heldGrabbable.weightFromMass;
        weight = customWeight ? heldGrabbable.customWeight : Mathf.InverseLerp(lightMass, maxMass, rb.mass);

        if (player != null)
        {
            player.SpeedMultiplier = Mathf.Lerp(1f, heavyPlayerSpeed, weight);
            player.LookMultiplier = Mathf.Lerp(1f, heavyPlayerLook, weight);
        }

        heldUsedGravity = rb.useGravity;
        heldInterpolation = rb.interpolation;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        float startDistance = heldGrabbable != null && heldGrabbable.holdDistance > 0f
            ? heldGrabbable.holdDistance
            : Vector3.Distance(cameraTransform.position, rb.worldCenterOfMass);
        holdDistance = Mathf.Clamp(startDistance, minHoldDistance, maxHoldDistance);

        holdRotation = heldGrabbable != null && heldGrabbable.useHoldRotation
            ? Quaternion.Euler(heldGrabbable.holdRotation)
            : Quaternion.Inverse(cameraTransform.rotation) * rb.rotation;

        SetPlayerCollision(false);

        if (heldGrabbable != null) heldGrabbable.NotifyGrabbed();
    }

    private void Drop()
    {
        if (held != null)
        {
            held.useGravity = heldUsedGravity;
            held.interpolation = heldInterpolation;
        }

        SetPlayerCollision(true);

        // Clear our state before notifying, in case a listener reacts by grabbing or moving the object
        Grabbable dropped = heldGrabbable;
        IsHolding = false;
        held = null;
        heldGrabbable = null;
        heldColliders = null;

        if (player != null)
        {
            player.LookLocked = false;
            player.SpeedMultiplier = 1f;
            player.LookMultiplier = 1f;
        }

        SetCrosshair(FirstPersonController.CrosshairState.Dot);

        if (dropped != null) dropped.NotifyDropped();
    }

    private void SetPlayerCollision(bool collide)
    {
        if (playerCollider == null || heldColliders == null) return;

        foreach (Collider heldCollider in heldColliders)
        {
            if (heldCollider != null) Physics.IgnoreCollision(heldCollider, playerCollider, !collide);
        }
    }

    private void SetCrosshair(FirstPersonController.CrosshairState state)
    {
        if (player != null) player.Crosshair = state;
    }

    private void ApplyHighlightLook()
    {
        highlightMaterial.SetColor(ColorId, highlightColor);
        highlightMaterial.SetFloat(FillId, highlightFill);
        highlightMaterial.SetFloat(RimPowerId, highlightRimSharpness);
        highlightMaterial.SetFloat(PulseId, highlightPulse);
    }

    // The glow is an extra material slot on the target's renderers, removed again when the target changes
    private void SetHighlight(Rigidbody target)
    {
        if (target != null && !ShouldHighlight(target)) target = null;
        if (target == highlighted) return;

        ClearHighlight();
        if (target == null) return;

        highlighted = target;
        highlightedRenderers = target.GetComponentsInChildren<Renderer>();
        originalMaterials = new Material[highlightedRenderers.Length][];

        for (int i = 0; i < highlightedRenderers.Length; i++)
        {
            Renderer targetRenderer = highlightedRenderers[i];
            if (!(targetRenderer is MeshRenderer) && !(targetRenderer is SkinnedMeshRenderer)) continue;

            Material[] original = targetRenderer.sharedMaterials;
            originalMaterials[i] = original;

            var withGlow = new Material[original.Length + 1];
            original.CopyTo(withGlow, 0);
            withGlow[original.Length] = highlightMaterial;
            targetRenderer.sharedMaterials = withGlow;
        }
    }

    private bool ShouldHighlight(Rigidbody target)
    {
        if (!highlightTarget || highlightMaterial == null) return false;
        return !target.TryGetComponent(out Grabbable grabbable) || grabbable.highlight;
    }

    private void ClearHighlight()
    {
        if (highlightedRenderers != null)
        {
            for (int i = 0; i < highlightedRenderers.Length; i++)
            {
                if (highlightedRenderers[i] != null && originalMaterials[i] != null)
                    highlightedRenderers[i].sharedMaterials = originalMaterials[i];
            }
        }

        highlighted = null;
        highlightedRenderers = null;
        originalMaterials = null;
    }
}