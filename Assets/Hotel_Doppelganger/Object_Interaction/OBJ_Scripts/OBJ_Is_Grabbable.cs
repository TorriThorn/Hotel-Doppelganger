using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Per-object carry settings for ObjectGrabber, plus the glide used for returning home and placement zones
[AddComponentMenu("Game/Interaction/Grabbable")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class Grabbable : MonoBehaviour
{
    // One recipe. It works from either item's side, so it only needs setting up on one
    [Serializable]
    public class Combination
    {
        [DisplayAs("Combine With")]
        public Grabbable other;

        // A hidden object in the scene, added to the inventory when the two are combined
        [DisplayAs("Makes")]
        public Grabbable result;

        [DisplayAs("Salem Thinks (optional)", Lines = 2)]
        public string salemThinks = "";

        public UnityEvent onCombined;
    }

    private static readonly WaitForFixedUpdate WaitForPhysics = new WaitForFixedUpdate();
    private static readonly List<Grabbable> active = new List<Grabbable>();

    [Header("Basics")]
    [DisplayAs("Can Be Picked Up")]
    public bool canGrab = true;

    [DisplayAs("Can Be Rotated")]
    public bool canRotate = true;

    [DisplayAs("Glow When Aimed At")]
    public bool highlight = true;

    // Only inventory items and clues glow
    [DisplayAs("Glow Until Found")]
    public bool glowUntilFound = true;

    // Items an NPC asks to be shown always go in the inventory
    [Header("Inventory")]
    [DisplayAs("Goes In Inventory")]
    public bool inventoryItem;

    [DisplayAs("Item Name (blank = object's name)")]
    public string itemName = "";

    [DisplayAs("Item Description", Lines = 3)]
    public string itemDescription = "";

    [DisplayAs("Item Picture (optional)")]
    public Sprite itemPicture;

    [Foldout("Combining")]
    public List<Combination> combinations = new List<Combination>();

    [Foldout("Weight")]
    [DisplayAs("Weight From Mass")]
    public bool weightFromMass = true;

    [DisplayAs("Custom Weight", HideIf = nameof(weightFromMass), Min = 0f, Max = 1f)]
    public float customWeight;

    [Foldout("Holding")]
    [DisplayAs("Hold Distance (0 = where it was picked up)", Min = 0f)]
    public float holdDistance;

    [DisplayAs("Use Fixed Hold Angle")]
    public bool useHoldRotation;

    [DisplayAs("Hold Angle", ShowIf = nameof(useHoldRotation))]
    public Vector3 holdRotation;

    [Foldout("Return To Start")]
    [DisplayAs("Return When Dropped")]
    public bool returnOnDrop;

    [DisplayAs("Wait Before Returning", ShowIf = nameof(returnOnDrop), Min = 0f)]
    public float returnDelay;

    [DisplayAs("Return Time", ShowIf = nameof(returnOnDrop), Min = 0f)]
    public float returnDuration = 0.4f;

    [DisplayAs("Movement Curve")]
    public AnimationCurve returnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Foldout("Events")]
    public UnityEvent onGrabbed;

    public UnityEvent onDropped;

    public UnityEvent onReturned;

    private Rigidbody body;
    private Vector3 homePosition;
    private Quaternion homeRotation;

    private Coroutine glideRoutine;
    private Vector3 glidePosition;
    private Quaternion glideRotation;
    private bool glideStaysKinematic;
    private Action glideCompleted;
    private int glideCount;
    private bool kinematicSetByGlide;
    private bool kinematicBeforeGlide;

    public static IReadOnlyList<Grabbable> Active => active;

    public bool IsHeld { get; private set; }
    public bool WasPickedUp { get; private set; }

    // Item Name, then a clue's inventory name, then the object's name
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(itemName)) return itemName.Trim();
            return TryGetComponent(out Clue clue) ? clue.InventoryName : name;
        }
    }

    public bool IsGliding => glideRoutine != null;

    // Lazy, so a glide requested before Awake still works
    private Rigidbody Body
    {
        get
        {
            if (body == null) body = GetComponent<Rigidbody>();
            return body;
        }
    }

    private void Awake() => body = GetComponent<Rigidbody>();

    // Sensible Rigidbody settings, and a collider if there isn't one
    private void Reset()
    {
        if (TryGetComponent(out Rigidbody rb))
        {
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        if (GetComponentInChildren<Collider>() != null) return;

        if (TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
            gameObject.AddComponent<MeshCollider>().convex = true;
        else
            gameObject.AddComponent<BoxCollider>();
    }

    private void OnEnable() => active.Add(this);

    // So it's never left stuck kinematic
    private void OnDisable()
    {
        active.Remove(this);
        if (glideRoutine == null) return;

        StopCoroutine(glideRoutine);
        CompleteGlide();
    }

    private void Start()
    {
        SetHomeHere();
        ItemGlow.Get();
    }

    public void SetCanGrab(bool value) => canGrab = value;

    public void SetHomeHere() => SetHome(transform.position, transform.rotation);

    public void SetHome(Vector3 position, Quaternion rotation)
    {
        homePosition = position;
        homeRotation = rotation;
    }

    // The spot becomes the object's new home, so Return When Dropped sends it back there
    public void MoveTo(Transform spot)
    {
        if (spot == null) return;
        Show();
        SetHome(spot.position, spot.rotation);
        GlideTo(spot.position, spot.rotation, 0f);
    }

    public void GlideToSpot(Transform spot)
    {
        if (spot == null) return;
        Show();
        SetHome(spot.position, spot.rotation);
        GlideTo(spot.position, spot.rotation, returnDuration);
    }

    public void Hide() => gameObject.SetActive(false);
    public void Show() => gameObject.SetActive(true);

    public void ReturnHome() =>
        GlideTo(homePosition, homeRotation, returnDuration, false, () => onReturned?.Invoke(), returnDelay);

    // With stayKinematic it stays frozen there afterwards
    public void GlideTo(Vector3 position, Quaternion rotation, float duration,
                        bool stayKinematic = false, Action onArrived = null, float delay = 0f)
    {
        CancelGlide();
        glideCount++;

        glidePosition = position;
        glideRotation = rotation;
        glideStaysKinematic = stayKinematic;
        glideCompleted = onArrived;

        if (isActiveAndEnabled && (duration > 0f || delay > 0f))
        {
            glideRoutine = StartCoroutine(GlideRoutine(duration, delay));
            return;
        }

        BeginKinematic();
        CompleteGlide();
    }

    // Gives the Rigidbody back, e.g. when the player catches the object
    public void CancelGlide()
    {
        if (glideRoutine == null) return;

        StopCoroutine(glideRoutine);
        glideRoutine = null;
        glideCompleted = null;
        RestoreKinematic();
    }

    internal void NotifyGrabbed()
    {
        CancelGlide();
        IsHeld = true;
        WasPickedUp = true;
        onGrabbed?.Invoke();
    }

    // Not a drop, so nothing that listens for one reacts
    internal void NotifyPutAway() => IsHeld = false;

    internal void NotifyDropped()
    {
        IsHeld = false;

        int glidesBefore = glideCount;
        onDropped?.Invoke();

        // A listener such as a PlacementZone may have moved it already
        if (returnOnDrop && glideCount == glidesBefore) ReturnHome();
    }

    private IEnumerator GlideRoutine(float duration, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        BeginKinematic();

        if (duration > 0f)
        {
            Rigidbody rb = Body;
            Vector3 startPosition = rb.position;
            Quaternion startRotation = rb.rotation;

            for (float t = 0f; t < 1f;)
            {
                yield return WaitForPhysics;

                t = Mathf.Min(1f, t + Time.fixedDeltaTime / duration);
                float eased = returnCurve.Evaluate(t);
                rb.MovePosition(Vector3.LerpUnclamped(startPosition, glidePosition, eased));
                rb.MoveRotation(Quaternion.SlerpUnclamped(startRotation, glideRotation, eased));
            }
        }

        CompleteGlide();
    }

    private void BeginKinematic()
    {
        if (kinematicSetByGlide) return;

        Rigidbody rb = Body;
        kinematicBeforeGlide = rb.isKinematic;
        if (!rb.isKinematic) StopMotion(rb);

        rb.isKinematic = true;
        kinematicSetByGlide = true;
    }

    private void RestoreKinematic()
    {
        if (!kinematicSetByGlide) return;

        Body.isKinematic = kinematicBeforeGlide;
        kinematicSetByGlide = false;
    }

    private void CompleteGlide()
    {
        Rigidbody rb = Body;
        transform.SetPositionAndRotation(glidePosition, glideRotation);
        rb.position = glidePosition;
        rb.rotation = glideRotation;

        if (glideStaysKinematic)
        {
            rb.isKinematic = true;
            kinematicSetByGlide = false;
        }
        else
        {
            RestoreKinematic();
        }

        if (!rb.isKinematic) StopMotion(rb);

        glideRoutine = null;
        Action completed = glideCompleted;
        glideCompleted = null;
        completed?.Invoke();
    }

    private static void StopMotion(Rigidbody rb)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!UnityEngine.Application.isPlaying || !returnOnDrop) return;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(homePosition, 0.08f);
        Gizmos.DrawLine(transform.position, homePosition);
    }
#endif
}