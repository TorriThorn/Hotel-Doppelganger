using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Marks an object as carryable by ObjectGrabber and holds its per-object carry settings.
// Also owns the object's glide, used both for returning home and for snapping into a PlacementZone
[AddComponentMenu("Game/Interaction/Grabbable")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class Grabbable : MonoBehaviour
{
    // One recipe: this item combined with another makes a third. It works from either item's side,
    // so it only needs setting up on one of them
    [Serializable]
    public class Combination
    {
        [DisplayAs("Combine With")]
        public Grabbable other;

        // An object waiting hidden in the scene; it's added to the inventory when the two are combined
        [DisplayAs("Makes")]
        public Grabbable result;

        [DisplayAs("Salem Thinks (optional)", Lines = 2)]
        public string salemThinks = "";

        public UnityEvent onCombined;
    }

    private static readonly WaitForFixedUpdate WaitForPhysics = new WaitForFixedUpdate();

    [Header("Basics")]
    [DisplayAs("Can Be Picked Up")]
    public bool canGrab = true;

    [DisplayAs("Can Be Rotated")]
    public bool canRotate = true;

    [DisplayAs("Glow When Aimed At")]
    public bool highlight = true;

    [Header("Weight")]
    [DisplayAs("Weight From Mass")]
    public bool weightFromMass = true;

    [DisplayAs("Custom Weight", HideIf = nameof(weightFromMass), Min = 0f, Max = 1f)]
    public float customWeight;

    [Header("Holding")]
    [DisplayAs("Hold Distance", Min = 0f)]
    public float holdDistance;

    [DisplayAs("Use Fixed Hold Angle")]
    public bool useHoldRotation;

    [DisplayAs("Hold Angle", ShowIf = nameof(useHoldRotation))]
    public Vector3 holdRotation;

    [Header("Return To Start")]
    [DisplayAs("Return When Dropped")]
    public bool returnOnDrop;

    [DisplayAs("Wait Before Returning", ShowIf = nameof(returnOnDrop), Min = 0f)]
    public float returnDelay;

    [DisplayAs("Return Time", ShowIf = nameof(returnOnDrop), Min = 0f)]
    public float returnDuration = 0.4f;

    [DisplayAs("Movement Curve")]
    public AnimationCurve returnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // Items an NPC asks to be shown always go in the inventory; tick this for anything else that should
    [Header("Inventory")]
    [DisplayAs("Goes In Inventory")]
    public bool inventoryItem;

    [DisplayAs("Item Name (blank = object's name)")]
    public string itemName = "";

    [DisplayAs("Item Description", Lines = 3)]
    public string itemDescription = "";

    [DisplayAs("Item Picture (optional)")]
    public Sprite itemPicture;

    public List<Combination> combinations = new List<Combination>();

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

    public bool IsHeld { get; private set; }

    // Item Name, then a clue's inventory name, then the object's own name
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(itemName)) return itemName.Trim();
            return TryGetComponent(out Clue clue) ? clue.InventoryName : name;
        }
    }

    // True while the object is gliding somewhere, home or into a placement zone
    public bool IsGliding => glideRoutine != null;

    // Resolved lazily so a glide requested before Awake (e.g. on an inactive object) still works
    private Rigidbody Body
    {
        get
        {
            if (body == null) body = GetComponent<Rigidbody>();
            return body;
        }
    }

    private void Awake() => body = GetComponent<Rigidbody>();

    // Runs when the component is first added: sensible Rigidbody settings, and a collider if there isn't one
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

    // Finish a glide instantly if the object is switched off, so it's never left stuck kinematic
    private void OnDisable()
    {
        if (glideRoutine == null) return;

        StopCoroutine(glideRoutine);
        CompleteGlide();
    }

    private void Start() => SetHomeHere();

    // For UnityEvents, e.g. letting an item be picked up once a scene is over
    public void SetCanGrab(bool value) => canGrab = value;

    public void SetHomeHere() => SetHome(transform.position, transform.rotation);

    public void SetHome(Vector3 position, Quaternion rotation)
    {
        homePosition = position;
        homeRotation = rotation;
    }

    // Handy from UnityEvents, e.g. a dialogue choice where an NPC hands over something they took.
    // The spot becomes the object's new home, so Return When Dropped sends it back there and not to where it started
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

    // Moves the object smoothly to a pose. With stayKinematic it stays frozen there afterwards
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

    // Stops a glide part way and gives the Rigidbody back, e.g. when the player catches the object
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
        onGrabbed?.Invoke();
    }

    // Put in the inventory rather than dropped, so nothing that listens for a drop reacts
    internal void NotifyPutAway() => IsHeld = false;

    internal void NotifyDropped()
    {
        IsHeld = false;

        int glidesBefore = glideCount;
        onDropped?.Invoke();

        // A listener such as a PlacementZone may have moved the object already, so don't send it home over the top
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

    // Kinematic while gliding so the object doesn't fight physics or shove other props around
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