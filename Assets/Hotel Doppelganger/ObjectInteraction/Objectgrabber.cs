using UnityEngine;
using UnityEngine.InputSystem;

// SOMA-style physical carrying. Hold left mouse to carry, right mouse to rotate, scroll to change distance.
// Works on any non-kinematic Rigidbody; a Grabbable adds per-object settings.
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

    // "At Max" values apply to the heaviest objects and fade out for lighter ones
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

    // Held object
    private bool isHolding;
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

    // Highlight
    private Material highlightMaterial;
    private Rigidbody highlighted;
    private Renderer[] highlightedRenderers;
    private Material[][] originalMaterials;

    public bool IsHolding => isHolding;
    public Rigidbody HeldObject => held;

    // Tunable values, e.g. for the Playtest menu
    public float Reach { get => reach; set => reach = Mathf.Max(0f, value); }
    public float AimForgiveness { get => grabRadius; set => grabRadius = Mathf.Max(0f, value); }
    public float MaxMass { get => maxMass; set => maxMass = Mathf.Max(0f, value); }

    // ---------- Lifecycle ----------

    private void Awake()
    {
        if (player == null) player = GetComponent<FirstPersonController>();
        playerCollider = GetComponent<CharacterController>();

        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        if (highlightShader == null) highlightShader = Shader.Find("Custom/GrabHighlight");
        if (highlightShader != null)
            highlightMaterial = new Material(highlightShader) { name = "Grab Highlight (Runtime)" };
    }

    private void Reset() => highlightShader = Shader.Find("Custom/GrabHighlight");

    private void OnDisable()
    {
        if (isHolding) Drop();
        ClearHighlight();
    }

    private void OnDestroy()
    {
        ClearHighlight();
        if (highlightMaterial != null) Destroy(highlightMaterial);
    }

    private void Update()
    {
        if (Mouse.current == null || cameraTransform == null) return;

        if (player != null && player.InputLocked)
        {
            if (isHolding) Drop();
            ClearHighlight();
            return;
        }

        if (isHolding) UpdateHolding();
        else UpdateSearching();
    }

    private void FixedUpdate()
    {
        if (!isHolding) return;

        if (!HeldStillValid())
        {
            Drop();
            return;
        }

        float liftTime = heavyLiftTime * weight;
        liftProgress = liftTime > 0f ? Mathf.MoveTowards(liftProgress, 1f, Time.fixedDeltaTime / liftTime) : 1f;

        // Center of mass, so objects with off-centre pivots still sit in the middle of the view
        Vector3 offset = GetHoldPoint() - held.worldCenterOfMass;

        if (HasBeenStuckTooLong(offset.magnitude))
        {
            Drop();
            return;
        }

        MoveTowardHoldPoint(offset);
        RotateTowardHoldRotation();
    }

    // ---------- Frame logic ----------

    private void UpdateSearching()
    {
        Rigidbody target = Cursor.lockState == CursorLockMode.Locked ? FindGrabbable() : null;

        SetHighlight(target);
        SetCrosshair(target != null ? FirstPersonController.CrosshairState.OpenHand : FirstPersonController.CrosshairState.Dot);

        if (target != null && Mouse.current.leftButton.wasPressedThisFrame)
            Grab(target);
    }

    private void UpdateHolding()
    {
        if (!HeldStillValid() || !Mouse.current.leftButton.isPressed)
        {
            Drop();
            return;
        }

        SetCrosshair(FirstPersonController.CrosshairState.ClosedHand);
        SetHighlight(highlightWhileHolding ? held : null);

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (scroll != 0f)
            holdDistance = Mathf.Clamp(holdDistance + Mathf.Sign(scroll) * scrollStep, minHoldDistance, maxHoldDistance);

        // Right mouse turns the object instead of the camera
        bool rotating = Mouse.current.rightButton.isPressed && (heldGrabbable == null || heldGrabbable.canRotate);
        if (player != null) player.LookLocked = rotating;
        if (rotating) RotateHoldWithMouse();
    }

    // ---------- Holding physics ----------

    private Vector3 GetHoldPoint()
    {
        Vector3 point = cameraTransform.position + cameraTransform.forward * holdDistance;
        point += Vector3.down * (heavySag * weight);

        float t = Time.time * ShakeFrequency;
        Vector3 shake = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f);
        point += cameraTransform.rotation * shake * (heavyShake * weight * 2f);

        return point;
    }

    // Heavy objects get longer to catch up before being dropped
    private bool HasBeenStuckTooLong(float distanceFromHoldPoint)
    {
        if (distanceFromHoldPoint <= dropDistance)
        {
            stuckTimer = 0f;
            return false;
        }

        stuckTimer += Time.fixedDeltaTime;
        return stuckTimer >= stuckTime * (1f + weight * HeavyStuckTimeBonus);
    }

    private void MoveTowardHoldPoint(Vector3 offset)
    {
        float follow = followStrength
                       * Mathf.Lerp(1f, heavyFollow, weight)
                       * Mathf.Lerp(MinLiftFollow, 1f, liftProgress);
        float topSpeed = Mathf.Lerp(maxSpeed, heavyMaxSpeed, weight);

        held.linearVelocity = Vector3.ClampMagnitude(offset * follow, topSpeed);
    }

    private void RotateTowardHoldRotation()
    {
        Quaternion targetRotation = cameraTransform.rotation * holdRotation;
        Quaternion difference = targetRotation * Quaternion.Inverse(held.rotation);
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

    private void RotateHoldWithMouse()
    {
        float speed = rotateSpeed * Mathf.Lerp(1f, heavyRotate, weight);
        Vector2 delta = Mouse.current.delta.ReadValue() * speed;

        Quaternion worldRotation = cameraTransform.rotation * holdRotation;
        worldRotation = Quaternion.AngleAxis(-delta.x, cameraTransform.up)
                      * Quaternion.AngleAxis(delta.y, cameraTransform.right)
                      * worldRotation;

        holdRotation = Quaternion.Inverse(cameraTransform.rotation) * worldRotation;
    }

    // ---------- Targeting ----------

    private Rigidbody FindGrabbable()
    {
        Vector3 origin = cameraTransform.position;
        Vector3 direction = cameraTransform.forward;

        // Exact aim always wins
        if (RaycastIgnoringPlayer(origin, direction, reach, grabLayers, out RaycastHit direct))
        {
            if (CanGrab(direct.rigidbody)) return direct.rigidbody;

            // Looking straight at an NPC: leave the click for dialogue rather than grabbing a nearby prop
            if (direct.collider.GetComponentInParent<NPCDialogue>() != null) return null;
        }

        if (grabRadius <= 0f) return null;

        // Otherwise sweep a wider ray and pick the candidate closest to the crosshair
        int count = Physics.SphereCastNonAlloc(origin, grabRadius, direction, sphereHits, reach,
                                               grabLayers, QueryTriggerInteraction.Ignore);
        Rigidbody best = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = sphereHits[i];
            Rigidbody candidate = hit.rigidbody;
            if (candidate == best || !CanGrab(candidate)) continue;

            // Colliders already inside the sphere at the start report no hit point
            Vector3 point = hit.distance > 0f ? hit.point : ClosestPointOn(hit.collider, origin);
            Vector3 toPoint = point - origin;

            if (toPoint.magnitude > reach) continue;
            if (!HasLineOfSight(origin, point, candidate)) continue;

            float distanceFromCrosshair = Vector3.Cross(direction, toPoint).magnitude;
            if (distanceFromCrosshair < bestScore)
            {
                bestScore = distanceFromCrosshair;
                best = candidate;
            }
        }

        return best;
    }

    private bool CanGrab(Rigidbody rb)
    {
        if (rb == null || rb.transform.IsChildOf(transform)) return false;

        Grabbable grabbable = rb.GetComponent<Grabbable>();

        // Kinematic bodies can't be carried, except a Grabbable mid-glide (the player can catch it)
        if (rb.isKinematic && (grabbable == null || !grabbable.IsGliding)) return false;

        if (grabbable != null)
        {
            if (!grabbable.enabled || !grabbable.canGrab) return false;
            if (!grabbable.weightFromMass) return true;   // Custom weight ignores Max Mass
        }
        else if (requireGrabbable)
        {
            return false;
        }

        return rb.mass <= maxMass;
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 point, Rigidbody target)
    {
        Vector3 toPoint = point - origin;
        float distance = toPoint.magnitude - LineOfSightPadding;
        if (distance <= 0f) return true;

        if (!RaycastIgnoringPlayer(origin, toPoint.normalized, distance, Physics.DefaultRaycastLayers, out RaycastHit blocker))
            return true;

        return blocker.rigidbody == target;
    }

    // Skips every collider on the player rig, which would otherwise block the ray
    private bool RaycastIgnoringPlayer(Vector3 origin, Vector3 direction, float distance, int mask, out RaycastHit closest)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, rayHits, distance, mask, QueryTriggerInteraction.Ignore);

        closest = default;
        float closestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closestDistance) continue;

            closest = hit;
            closestDistance = hit.distance;
            found = true;
        }

        return found;
    }

    // Collider.ClosestPoint doesn't support concave mesh colliders, so fall back to their bounds
    private static Vector3 ClosestPointOn(Collider hitCollider, Vector3 position)
    {
        if (hitCollider is MeshCollider meshCollider && !meshCollider.convex)
            return hitCollider.bounds.ClosestPoint(position);

        return hitCollider.ClosestPoint(position);
    }

    // ---------- Grab / drop ----------

    // The held object can be destroyed, disabled, made kinematic or made ungrabbable at any time
    private bool HeldStillValid()
    {
        if (held == null || !held.gameObject.activeInHierarchy || held.isKinematic) return false;
        if (heldGrabbable != null && (!heldGrabbable.enabled || !heldGrabbable.canGrab)) return false;
        return true;
    }

    private void Grab(Rigidbody rb)
    {
        isHolding = true;
        held = rb;
        heldGrabbable = rb.GetComponent<Grabbable>();
        if (heldGrabbable != null) heldGrabbable.CancelGlide();   // Before saving physics state

        heldColliders = held.GetComponentsInChildren<Collider>();
        stuckTimer = 0f;
        liftProgress = 0f;

        weight = heldGrabbable != null && !heldGrabbable.weightFromMass
            ? heldGrabbable.customWeight
            : Mathf.InverseLerp(lightMass, maxMass, held.mass);

        if (player != null)
        {
            player.SpeedMultiplier = Mathf.Lerp(1f, heavyPlayerSpeed, weight);
            player.LookMultiplier = Mathf.Lerp(1f, heavyPlayerLook, weight);
        }

        heldUsedGravity = held.useGravity;
        heldInterpolation = held.interpolation;
        held.useGravity = false;
        held.interpolation = RigidbodyInterpolation.Interpolate;

        float startDistance = heldGrabbable != null && heldGrabbable.holdDistance > 0f
            ? heldGrabbable.holdDistance
            : Vector3.Distance(cameraTransform.position, held.worldCenterOfMass);
        holdDistance = Mathf.Clamp(startDistance, minHoldDistance, maxHoldDistance);

        holdRotation = heldGrabbable != null && heldGrabbable.useHoldRotation
            ? Quaternion.Euler(heldGrabbable.holdRotation)
            : Quaternion.Inverse(cameraTransform.rotation) * held.rotation;

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

        if (heldGrabbable != null) heldGrabbable.NotifyDropped();

        isHolding = false;
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
    }

    private void SetPlayerCollision(bool collide)
    {
        if (playerCollider == null || heldColliders == null) return;

        foreach (Collider heldCollider in heldColliders)
        {
            if (heldCollider != null) Physics.IgnoreCollision(heldCollider, playerCollider, !collide);
        }
    }

    // ---------- Feedback ----------

    private void SetCrosshair(FirstPersonController.CrosshairState state)
    {
        if (player != null) player.Crosshair = state;
    }

    // Adds the glow as an extra material on the target's renderers, and restores them afterwards
    private void SetHighlight(Rigidbody target)
    {
        if (target != null && !ShouldHighlight(target)) target = null;
        if (target == highlighted) return;

        ClearHighlight();
        if (target == null) return;

        highlightMaterial.SetColor(ColorId, highlightColor);
        highlightMaterial.SetFloat(FillId, highlightFill);
        highlightMaterial.SetFloat(RimPowerId, highlightRimSharpness);
        highlightMaterial.SetFloat(PulseId, highlightPulse);

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

        Grabbable grabbable = target.GetComponent<Grabbable>();
        return grabbable == null || grabbable.highlight;
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