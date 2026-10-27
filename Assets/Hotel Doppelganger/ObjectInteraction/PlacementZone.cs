using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using Application = UnityEngine.Application;

// A spot where one specific Grabbable belongs. While the player carries that object,
// a glowing ghost of it marks the spot; releasing the object close enough snaps it into place.
// Place this on an empty GameObject posed where the object's pivot should land.
[AddComponentMenu("Game/Interaction/Placement Zone")]
public class PlacementZone : MonoBehaviour
{
    public enum GhostMode
    {
        [InspectorName("While Carrying The Object")] WhileHoldingObject,
        [InspectorName("Always")] Always,
        [InspectorName("Never")] Never
    }

    private const float ReadyFillMultiplier = 2f;
    private const float ReadyScanlineMultiplier = 1.5f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int ScanlineDensityId = Shader.PropertyToID("_ScanlineDensity");
    private static readonly int ScanlineSpeedId = Shader.PropertyToID("_ScanlineSpeed");
    private static readonly int ScanlineStrengthId = Shader.PropertyToID("_ScanlineStrength");

    private struct GhostPart
    {
        public Mesh Mesh;
        public Matrix4x4 LocalToRoot;
    }

    [Header("The Object")]
    [DisplayAs("Object That Goes Here")]
    [SerializeField] private Grabbable acceptedObject;

    [DisplayAs("Snap Distance", Min = 0f)]
    [SerializeField] private float snapRadius = 0.4f;

    [DisplayAs("Snap To This Rotation")]
    [SerializeField] private bool matchRotation = true;

    [DisplayAs("Snap Time", Min = 0f)]
    [SerializeField] private float snapDuration = 0.25f;

    [DisplayAs("Lock Once Placed")]
    [SerializeField] private bool lockWhenPlaced = true;

    [DisplayAs("Already Placed At Start")]
    [SerializeField] private bool startPlaced;

    [Foldout("Ghost Look")]
    [DisplayAs("Show Ghost")]
    [SerializeField] private GhostMode showGhost = GhostMode.WhileHoldingObject;

    [DisplayAs("Ghost Shader")]
    [SerializeField] private Shader highlightShader;

    [DisplayAs("Ghost Color")]
    [SerializeField] private Color ghostColor = new Color(0.2f, 1f, 0.35f, 0.6f);

    [DisplayAs("Ready To Snap Color")]
    [SerializeField] private Color readyColor = new Color(0.45f, 1f, 0.55f, 1f);

    [DisplayAs("Fill", Min = 0f, Max = 1f)]
    [SerializeField] private float ghostFill = 0.2f;

    [DisplayAs("Edge Sharpness", Min = 0.5f, Max = 8f)]
    [SerializeField] private float ghostRimSharpness = 2f;

    [DisplayAs("Pulse", Min = 0f, Max = 1f)]
    [SerializeField] private float ghostPulse = 0.35f;

    [DisplayAs("Scanline Count", Min = 0f, Max = 100f)]
    [SerializeField] private float scanlineDensity = 40f;

    [DisplayAs("Scanline Speed", Min = -5f, Max = 5f)]
    [SerializeField] private float scanlineSpeed = 0.5f;

    [DisplayAs("Scanline Strength", Min = 0f, Max = 1f)]
    [SerializeField] private float scanlineStrength = 0.25f;

    [Header("Events (optional)")]
    public UnityEvent onPlaced;

    public UnityEvent onRemoved;

    public bool IsFilled { get; private set; }
    public bool ObjectInRange { get; private set; }
    public Grabbable AcceptedObject => acceptedObject;

    private readonly List<GhostPart> ghostParts = new List<GhostPart>();
    private Vector3 objectScale = Vector3.one;
    private Rigidbody acceptedBody;
    private Material ghostMaterial;

    private Quaternion TargetRotation => matchRotation || acceptedObject == null
        ? transform.rotation
        : acceptedObject.transform.rotation;

    // ---------- Lifecycle ----------

    private void Awake()
    {
        if (highlightShader == null) highlightShader = Shader.Find("Custom/PlacementGhost");
        if (highlightShader == null) highlightShader = Shader.Find("Custom/GrabHighlight");
        if (highlightShader != null)
            ghostMaterial = new Material(highlightShader) { name = "Placement Ghost (Runtime)" };

        if (acceptedObject == null) return;

        acceptedBody = acceptedObject.GetComponent<Rigidbody>();
        CacheGhostParts();
    }

    private void Reset() => highlightShader = Shader.Find("Custom/PlacementGhost");

    private void OnEnable()
    {
        if (acceptedObject == null) return;

        acceptedObject.onDropped.AddListener(HandleDropped);
        acceptedObject.onGrabbed.AddListener(HandleGrabbed);
    }

    private void OnDisable()
    {
        if (acceptedObject == null) return;

        acceptedObject.onDropped.RemoveListener(HandleDropped);
        acceptedObject.onGrabbed.RemoveListener(HandleGrabbed);
    }

    private void Start()
    {
        if (startPlaced) Place(true);
    }

    private void OnDestroy()
    {
        if (ghostMaterial != null) Destroy(ghostMaterial);
    }

    private void Update()
    {
        if (acceptedObject == null) return;

        ObjectInRange = !IsFilled && IsObjectInRange();

        if (ShouldShowGhost())
            DrawGhost(ObjectInRange && acceptedObject.IsHeld);
    }

    // ---------- Public API (UnityEvent friendly) ----------

    // Puts the accepted object into the zone from wherever it is.
    public void Place(bool instant = false)
    {
        if (acceptedObject == null || IsFilled) return;

        IsFilled = true;
        if (lockWhenPlaced) acceptedObject.canGrab = false;

        Vector3 position = transform.position;
        Quaternion rotation = TargetRotation;

        // An object with Return On Drop now treats this spot as home
        acceptedObject.SetHome(position, rotation);
        acceptedObject.GlideTo(position, rotation, instant ? 0f : snapDuration, lockWhenPlaced);

        onPlaced?.Invoke();
    }

    // Lets a locked object be picked up again.
    public void Unlock()
    {
        if (acceptedObject == null) return;

        acceptedObject.canGrab = true;
        if (acceptedBody != null && IsFilled && !acceptedObject.IsGliding)
            acceptedBody.isKinematic = false;
    }

    // ---------- Object events ----------

    private void HandleDropped()
    {
        if (!IsFilled && IsObjectInRange()) Place();
    }

    private void HandleGrabbed()
    {
        if (!IsFilled) return;

        IsFilled = false;
        onRemoved?.Invoke();
    }

    // Compares centres of mass so the check feels right even when the pivot is at the object's base
    private bool IsObjectInRange()
    {
        if (acceptedBody == null) return false;

        Vector3 targetCenter = transform.position + TargetRotation * acceptedBody.centerOfMass;
        return (acceptedBody.worldCenterOfMass - targetCenter).sqrMagnitude <= snapRadius * snapRadius;
    }

    // ---------- Ghost rendering ----------

    private bool ShouldShowGhost()
    {
        if (IsFilled || ghostMaterial == null) return false;

        switch (showGhost)
        {
            case GhostMode.Always: return true;
            case GhostMode.WhileHoldingObject: return acceptedObject.IsHeld;
            default: return false;
        }
    }

    private void CacheGhostParts()
    {
        Transform root = acceptedObject.transform;
        objectScale = root.lossyScale;
        ghostParts.Clear();

        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>())
        {
            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            if (meshFilter.sharedMesh == null || meshRenderer == null || !meshRenderer.enabled) continue;

            ghostParts.Add(new GhostPart
            {
                Mesh = meshFilter.sharedMesh,
                LocalToRoot = root.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix
            });
        }
    }

    private void DrawGhost(bool ready)
    {
        ghostMaterial.SetColor(ColorId, ready ? readyColor : ghostColor);
        ghostMaterial.SetFloat(FillId, ready ? Mathf.Min(1f, ghostFill * ReadyFillMultiplier) : ghostFill);
        ghostMaterial.SetFloat(RimPowerId, ghostRimSharpness);
        ghostMaterial.SetFloat(PulseId, ghostPulse);
        ghostMaterial.SetFloat(ScanlineDensityId, scanlineDensity);
        ghostMaterial.SetFloat(ScanlineSpeedId, scanlineSpeed);
        ghostMaterial.SetFloat(ScanlineStrengthId,
            ready ? Mathf.Min(1f, scanlineStrength * ReadyScanlineMultiplier) : scanlineStrength);

        var renderParams = new RenderParams(ghostMaterial)
        {
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            layer = gameObject.layer
        };

        Matrix4x4 rootMatrix = Matrix4x4.TRS(transform.position, TargetRotation, objectScale);
        foreach (GhostPart part in ghostParts)
        {
            Matrix4x4 world = rootMatrix * part.LocalToRoot;
            for (int subMesh = 0; subMesh < part.Mesh.subMeshCount; subMesh++)
                Graphics.RenderMesh(renderParams, part.Mesh, subMesh, world);
        }
    }

    // ---------- Editor ----------

#if UNITY_EDITOR
    // Wireframe preview of the placed object while editing the scene
    private void OnDrawGizmos()
    {
        if (acceptedObject == null || Application.isPlaying) return;

        Transform root = acceptedObject.transform;
        Matrix4x4 rootMatrix = Matrix4x4.TRS(transform.position, transform.rotation, root.lossyScale);

        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.8f);
        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (meshFilter.sharedMesh == null) continue;

            Gizmos.matrix = rootMatrix * (root.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix);
            Gizmos.DrawWireMesh(meshFilter.sharedMesh);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;
        Rigidbody rb = acceptedObject != null ? acceptedObject.GetComponent<Rigidbody>() : null;
        if (rb != null) center += transform.rotation * rb.centerOfMass;

        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.35f);
        Gizmos.DrawWireSphere(center, snapRadius);
    }
#endif
}