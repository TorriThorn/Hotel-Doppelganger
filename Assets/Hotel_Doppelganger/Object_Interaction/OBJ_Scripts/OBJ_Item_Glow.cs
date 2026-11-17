using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Marks every inventory item and clue until it's first picked up: a soft glow using the aimed-at glow's
// shader, and a small bobbing arrow above it. Made if there isn't one; add it to any object to change the look
[AddComponentMenu("Game/Interaction/Item Glow")]
[DisallowMultipleComponent]
public class ItemGlow : MonoBehaviour
{
    private sealed class Tracked
    {
        public MeshFilter[] Meshes;
        public Renderer[] Parts;
        public SpriteRenderer Marker;
        public float Phase;
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");

    private const int ArrowSize = 64;
    private const float BobSpeed = 3f;
    private const float BobHeight = 0.04f;

    [Header("Glow")]
    [DisplayAs("Show Glow")]
    [SerializeField] private bool showGlow = true;

    [DisplayAs("Glow Color")]
    [SerializeField] private Color glowColor = new Color(1f, 0.8f, 0.35f, 1f);

    [DisplayAs("Glow Fill", Min = 0f, Max = 1f)]
    [SerializeField] private float fill = 0.12f;

    [DisplayAs("Edge Sharpness", Min = 0.5f, Max = 8f)]
    [SerializeField] private float rimSharpness = 2f;

    [DisplayAs("Glow Pulse", Min = 0f, Max = 1f)]
    [SerializeField] private float pulse = 0.5f;

    [Header("Marker")]
    [DisplayAs("Show Marker")]
    [SerializeField] private bool showMarker = true;

    [DisplayAs("Marker Color", ShowIf = nameof(showMarker))]
    [SerializeField] private Color markerColor = new Color(1f, 0.85f, 0.4f, 1f);

    [DisplayAs("Marker Size (meters)", ShowIf = nameof(showMarker), Min = 0.02f, Max = 1f)]
    [SerializeField] private float markerSize = 0.12f;

    [DisplayAs("Height Above Item (meters)", ShowIf = nameof(showMarker))]
    [SerializeField] private float markerHeight = 0.12f;

    [DisplayAs("Show Within (meters)", ShowIf = nameof(showMarker), Min = 0f)]
    [SerializeField] private float markerRange = 8f;

    [DisplayAs("Marker Image (blank = arrow)", ShowIf = nameof(showMarker))]
    [SerializeField] private Sprite markerSprite;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Glow Shader (blank = Object Grabber's)")]
    [SerializeField] private Shader glowShader;

    [DisplayAs("Camera (optional)")]
    [SerializeField] private Camera viewCamera;

    private static ItemGlow instance;

    private readonly Dictionary<Grabbable, Tracked> tracked = new Dictionary<Grabbable, Tracked>();
    private readonly List<Grabbable> gone = new List<Grabbable>();
    private Material material;
    private RenderParams renderParams;
    private Sprite arrow;
    private Transform markerHolder;
    private PlayerDialogue dialogue;

    public bool ShowGlow { get => showGlow; set => showGlow = value; }
    public bool ShowMarker { get => showMarker; set => showMarker = value; }

    public static ItemGlow Get()
    {
        if (instance == null) instance = FindAnyObjectByType<ItemGlow>();
        if (instance == null) instance = new GameObject("Item Glow").AddComponent<ItemGlow>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;

        dialogue = FindAnyObjectByType<PlayerDialogue>();
        arrow = CreateArrow();
        markerHolder = new GameObject("Item Markers").transform;

        if (glowShader == null)
        {
            ObjectGrabber grabber = FindAnyObjectByType<ObjectGrabber>();
            if (grabber != null) glowShader = grabber.GlowShader;
        }
        if (glowShader == null) glowShader = Shader.Find("Custom/GrabHighlight");
        if (glowShader == null) return;

        material = new Material(glowShader) { name = "Item Glow (Runtime)" };
        renderParams = new RenderParams(material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
        ApplyLook();
    }

    private void OnValidate()
    {
        if (material != null) ApplyLook();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (material != null) Destroy(material);
        if (markerHolder != null) Destroy(markerHolder.gameObject);
        if (arrow != null)
        {
            Destroy(arrow.texture);
            Destroy(arrow);
        }
    }

    // The glow is drawn as an extra pass each frame, so the items' own materials are never touched
    private void LateUpdate()
    {
        foreach (Grabbable item in Grabbable.Active)
        {
            if (item != null && !tracked.ContainsKey(item)) Track(item);
        }

        if (viewCamera == null) viewCamera = Camera.main;
        bool talking = dialogue != null && dialogue.IsTalking;

        gone.Clear();
        foreach (KeyValuePair<Grabbable, Tracked> pair in tracked)
        {
            Grabbable item = pair.Key;
            Tracked entry = pair.Value;
            if (item == null)
            {
                if (entry.Marker != null) Destroy(entry.Marker.gameObject);
                gone.Add(item);
                continue;
            }

            bool wanted = Wanted(item);
            if (wanted && showGlow && material != null) DrawGlow(entry);
            UpdateMarker(entry, wanted && showMarker && !talking);
        }

        foreach (Grabbable item in gone) tracked.Remove(item);
    }

    private void Track(Grabbable item)
    {
        var marker = new GameObject(item.name + " Marker").AddComponent<SpriteRenderer>();
        marker.transform.SetParent(markerHolder, false);
        marker.gameObject.SetActive(false);

        tracked[item] = new Tracked
        {
            Meshes = item.GetComponentsInChildren<MeshFilter>(true),
            Parts = item.GetComponentsInChildren<Renderer>(true),
            Marker = marker,
            Phase = tracked.Count * 1.3f
        };
    }

    // Only things worth finding, until they've been found
    private static bool Wanted(Grabbable item)
    {
        if (!item.isActiveAndEnabled || !item.glowUntilFound || item.WasPickedUp || item.IsHeld || !item.canGrab) return false;
        if (item.TryGetComponent(out Clue clue)) return !clue.Found;
        return item.inventoryItem;
    }

    private void DrawGlow(Tracked entry)
    {
        foreach (MeshFilter meshFilter in entry.Meshes)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null || !meshFilter.gameObject.activeInHierarchy) continue;
            if (!meshFilter.TryGetComponent(out MeshRenderer meshRenderer) || !meshRenderer.enabled) continue;

            Mesh mesh = meshFilter.sharedMesh;
            Matrix4x4 world = meshFilter.transform.localToWorldMatrix;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++) Graphics.RenderMesh(renderParams, mesh, subMesh, world);
        }
    }

    // Faces the camera and bobs just above the top of the item
    private void UpdateMarker(Tracked entry, bool wanted)
    {
        SpriteRenderer marker = entry.Marker;
        Vector3 top = TopOf(entry);
        bool show = wanted && viewCamera != null
                    && (top - viewCamera.transform.position).sqrMagnitude <= markerRange * markerRange;

        if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
        if (!show) return;

        marker.sprite = markerSprite != null ? markerSprite : arrow;
        marker.color = markerColor;

        float bob = Mathf.Sin(Time.time * BobSpeed + entry.Phase) * BobHeight;
        Vector3 position = top + Vector3.up * (markerHeight + markerSize * 0.5f + bob);
        marker.transform.SetPositionAndRotation(position, viewCamera.transform.rotation);

        // Sized in meters whatever the image's own resolution
        float spriteHeight = Mathf.Max(0.0001f, marker.sprite.bounds.size.y);
        marker.transform.localScale = Vector3.one * (markerSize / spriteHeight);
    }

    private static Vector3 TopOf(Tracked entry)
    {
        bool found = false;
        var bounds = new Bounds();
        foreach (Renderer part in entry.Parts)
        {
            if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) continue;
            if (found) bounds.Encapsulate(part.bounds);
            else bounds = part.bounds;
            found = true;
        }

        return found ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) : Vector3.zero;
    }

    private void ApplyLook()
    {
        material.SetColor(ColorId, glowColor);
        material.SetFloat(FillId, fill);
        material.SetFloat(RimPowerId, rimSharpness);
        material.SetFloat(PulseId, pulse);
    }

    // A soft-edged arrow pointing down at the item, drawn once
    private static Sprite CreateArrow()
    {
        var texture = new Texture2D(ArrowSize, ArrowSize, TextureFormat.RGBA32, false)
        {
            name = "Item Marker Arrow",
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[ArrowSize * ArrowSize];
        for (int y = 0; y < ArrowSize; y++)
        {
            for (int x = 0; x < ArrowSize; x++)
            {
                // u across from the middle, v from the bottom tip (0) to the top (1)
                float u = Mathf.Abs((x + 0.5f) / ArrowSize * 2f - 1f);
                float v = (y + 0.5f) / ArrowSize;

                // A triangle whose sides widen from the tip, with a soft outer edge
                float inside = v * 0.9f - u;
                float alpha = Mathf.Clamp01(inside * 12f) * Mathf.Clamp01((0.92f - v) * 12f);
                pixels[y * ArrowSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, ArrowSize, ArrowSize), new Vector2(0.5f, 0.5f), ArrowSize);
    }
}