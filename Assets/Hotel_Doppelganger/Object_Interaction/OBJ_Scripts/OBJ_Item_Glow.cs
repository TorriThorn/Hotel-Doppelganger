using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A soft glow on every inventory item and clue until it's first picked up, using the same shader as the
// aimed-at glow. Made if there isn't one; add it to any object to change the look
[AddComponentMenu("Game/Interaction/Item Glow")]
[DisallowMultipleComponent]
public class ItemGlow : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");

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

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Glow Shader (blank = Object Grabber's)")]
    [SerializeField] private Shader glowShader;

    private static ItemGlow instance;

    private readonly Dictionary<Grabbable, MeshFilter[]> parts = new Dictionary<Grabbable, MeshFilter[]>();
    private Material material;
    private RenderParams renderParams;

    public bool ShowGlow { get => showGlow; set => showGlow = value; }

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
    }

    // Drawn as an extra pass each frame, so the items' own materials are never touched
    private void LateUpdate()
    {
        if (!showGlow || material == null) return;

        foreach (Grabbable item in Grabbable.Active)
        {
            if (!Wanted(item)) continue;

            if (!parts.TryGetValue(item, out MeshFilter[] meshes))
            {
                meshes = item.GetComponentsInChildren<MeshFilter>(true);
                parts[item] = meshes;
            }

            foreach (MeshFilter meshFilter in meshes)
            {
                if (meshFilter == null || meshFilter.sharedMesh == null || !meshFilter.gameObject.activeInHierarchy) continue;
                if (!meshFilter.TryGetComponent(out MeshRenderer meshRenderer) || !meshRenderer.enabled) continue;

                Mesh mesh = meshFilter.sharedMesh;
                Matrix4x4 world = meshFilter.transform.localToWorldMatrix;
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++) Graphics.RenderMesh(renderParams, mesh, subMesh, world);
            }
        }
    }

    // Only things worth finding, until they've been found
    private static bool Wanted(Grabbable item)
    {
        if (item == null || !item.glowUntilFound || item.WasPickedUp || item.IsHeld || !item.canGrab) return false;
        if (item.TryGetComponent(out Clue clue)) return !clue.Found;
        return item.inventoryItem;
    }

    private void ApplyLook()
    {
        material.SetColor(ColorId, glowColor);
        material.SetFloat(FillId, fill);
        material.SetFloat(RimPowerId, rimSharpness);
        material.SetFloat(PulseId, pulse);
    }
}
