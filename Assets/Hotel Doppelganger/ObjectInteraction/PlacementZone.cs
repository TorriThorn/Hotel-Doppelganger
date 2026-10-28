using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using Application = UnityEngine.Application;

// A spot where one particular Grabbable belongs. While the player carries it, a glowing ghost marks the spot,
// and letting go close enough snaps the object in. Pose this GameObject where the object's pivot should end up.
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

    private readonly List<GhostPart> ghostParts = new List<GhostPart>();
    private Vector3 objectScale = Vector3.one;
    private Rigidbody acceptedBody;
    private Material ghostMaterial;
    private RenderParams ghostParams;
    private bool ghostShowsReady;

    public bool IsFilled { get; private set; }
    public bool ObjectInRange => !IsFilled && IsObjectInRange();
    public Grabbable AcceptedObject => acceptedObject;

    private Quaternion TargetRotation => matchRotation || acceptedObject == null
        ? transform.rotation
        : acceptedObject.transform.rotation;

    private void Awake()
    {
        if (highlightShader == null) highlightShader = Shader.Find("Custom/PlacementGhost");
        if (highlightShader == null) highlightShader = Shader.Find("Custom/GrabHighlight");

        if (highlightShader != null)
        {
            ghostMaterial = new Material(highlightShader) { name = "Placement Ghost (Runtime)" };
            ghostParams = new RenderParams(ghostMaterial)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer
            };
            ApplyGhostLook(false);
        }

        if (acceptedObject == null) return;

        acceptedObject.TryGetComponent(out acceptedBody);
        CacheGhostParts();
    }

    private void Reset() => highlightShader = Shader.Find("Custom/PlacementGhost");

    private void OnValidate()
    {
        if (ghostMaterial != null) ApplyGhostLook(ghostShowsReady);
    }

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
        if (!ShouldShowGhost()) return;

        bool ready = acceptedObject.IsHeld && IsObjectInRange();
        if (ready != ghostShowsReady) ApplyGhostLook(ready);

        DrawGhost();
    }

    // Puts the accepted object into the zone from wherever it currently is
    public void Place(bool instant = false)
    {
        if (acceptedObject == null || IsFilled) return;

        IsFilled = true;
        if (lockWhenPlaced) acceptedObject.canGrab = false;

        Vector3 position = transform.position;
        Quaternion rotation = TargetRotation;

        // An object with Return When Dropped now treats this spot as home
        acceptedObject.SetHome(position, rotation);
        acceptedObject.GlideTo(position, rotation, instant ? 0f : snapDuration, lockWhenPlaced);

        onPlaced?.Invoke();
    }

    // Lets a locked object be picked up again
    public void Unlock()
    {
        if (acceptedObject == null) return;

        acceptedObject.canGrab = true;
        if (acceptedBody != null && IsFilled && !acceptedObject.IsGliding) acceptedBody.isKinematic = false;
    }

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

    // Compares centers of mass, which feels right even when the pivot sits at the base of the object
    private bool IsObjectInRange()
    {
        if (acceptedBody == null) return false;

        Vector3 targetCenter = transform.position + TargetRotation * acceptedBody.centerOfMass;
        return (acceptedBody.worldCenterOfMass - targetCenter).sqrMagnitude <= snapRadius * snapRadius;
    }

    private bool ShouldShowGhost()
    {
        if (IsFilled || ghostMaterial == null || acceptedObject == null) return false;

        switch (showGhost)
        {
            case GhostMode.Always: return true;
            case GhostMode.WhileHoldingObject: return acceptedObject.IsHeld;
            default: return false;
        }
    }

    private void ApplyGhostLook(bool ready)
    {
        ghostShowsReady = ready;
        ghostMaterial.SetColor(ColorId, ready ? readyColor : ghostColor);
        ghostMaterial.SetFloat(FillId, ready ? Mathf.Min(1f, ghostFill * ReadyFillMultiplier) : ghostFill);
        ghostMaterial.SetFloat(RimPowerId, ghostRimSharpness);
        ghostMaterial.SetFloat(PulseId, ghostPulse);
        ghostMaterial.SetFloat(ScanlineDensityId, scanlineDensity);
        ghostMaterial.SetFloat(ScanlineSpeedId, scanlineSpeed);
        ghostMaterial.SetFloat(ScanlineStrengthId, ready ? Mathf.Min(1f, scanlineStrength * ReadyScanlineMultiplier) : scanlineStrength);
    }

    // Mesh offsets are recorded relative to the object's root, so the ghost can be drawn at this zone's pose
    private void CacheGhostParts()
    {
        Transform root = acceptedObject.transform;
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;
        objectScale = root.lossyScale;
        ghostParts.Clear();

        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (meshFilter.sharedMesh == null) continue;
            if (!meshFilter.TryGetComponent(out MeshRenderer meshRenderer) || !meshRenderer.enabled) continue;

            ghostParts.Add(new GhostPart
            {
                Mesh = meshFilter.sharedMesh,
                LocalToRoot = worldToRoot * meshFilter.transform.localToWorldMatrix
            });
        }
    }

    private void DrawGhost()
    {
        Matrix4x4 rootMatrix = Matrix4x4.TRS(transform.position, TargetRotation, objectScale);

        foreach (GhostPart part in ghostParts)
        {
            Matrix4x4 world = rootMatrix * part.LocalToRoot;
            for (int subMesh = 0; subMesh < part.Mesh.subMeshCount; subMesh++)
                Graphics.RenderMesh(ghostParams, part.Mesh, subMesh, world);
        }
    }

#if UNITY_EDITOR
    // Wireframe preview of the placed object while editing the scene
    private void OnDrawGizmos()
    {
        if (acceptedObject == null || UnityEngine.Application.isPlaying) return;

        Transform root = acceptedObject.transform;
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;
        Matrix4x4 rootMatrix = Matrix4x4.TRS(transform.position, transform.rotation, root.lossyScale);

        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.8f);
        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (meshFilter.sharedMesh == null) continue;

            Gizmos.matrix = rootMatrix * (worldToRoot * meshFilter.transform.localToWorldMatrix);
            Gizmos.DrawWireMesh(meshFilter.sharedMesh);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;
        if (acceptedObject != null && acceptedObject.TryGetComponent(out Rigidbody rb))
            center += transform.rotation * rb.centerOfMass;

        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.35f);
        Gizmos.DrawWireSphere(center, snapRadius);
    }
#endif
}