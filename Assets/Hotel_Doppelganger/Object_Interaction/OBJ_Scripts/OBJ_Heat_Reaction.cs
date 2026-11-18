using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

// Sizzles and steams near a fire and slowly dries out: the closer it is, the louder, steamier and faster.
// A glowing ring on the floor shows how close it needs to be, and Salem hints at what to do. Once fully dried
// it shrinks and darkens, can rename itself in the inventory, and unlocks new lines for everyone
[AddComponentMenu("Game/Puzzle/Heat Reaction")]
public class HeatReaction : MonoBehaviour
{
    private sealed class Puff
    {
        public SpriteRenderer Sprite;
        public Vector3 Start;
        public Vector3 Drift;
        public float Age = -1f;
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private const int RingTextureSize = 256;
    private const int PuffTextureSize = 32;
    private const int PuffCount = 24;
    private const float PuffsPerSecond = 14f;
    private const float PuffLife = 1.2f;
    private const float PuffRise = 0.35f;
    private const float PuffStartSize = 0.04f;
    private const float PuffEndSize = 0.16f;
    private const float RingShowDistance = 8f;
    private const float RingFadeSpeed = 3f;
    private const float RingPulseSpeed = 2.5f;
    private const float FloorLift = 0.02f;

    [Header("Heat")]
    [DisplayAs("Fire")]
    [SerializeField] private Transform fire;

    [DisplayAs("Feels Heat Within (meters)", Min = 0.1f)]
    [SerializeField] private float heatRange = 2.5f;

    // Off, it ignores the fire until StartReacting is called, e.g. when Carmela drops it
    [DisplayAs("Reacts From The Start")]
    [SerializeField] private bool reactsFromStart;

    [DisplayAs("Seconds To Dry Out (right at the fire)", Min = 0.1f)]
    [SerializeField] private float dryTime = 4f;

    [DisplayAs("Sizzle Sound (looping)")]
    [SerializeField] private AudioClip sizzleSound;

    [DisplayAs("Sizzle Volume", Min = 0f, Max = 1f)]
    [SerializeField] private float sizzleVolume = 1f;

    [Foldout("Hints")]
    [DisplayAs("Salem Thinks When It First Sizzles", Lines = 2)]
    [SerializeField] private string sizzleHint = "Huh? It's sizzling... the heat from the fire is doing something to it.";

    [DisplayAs("Salem Thinks When Picked Up Before It's Dry", Lines = 2)]
    [SerializeField] private string holdHint = "It reacted to the fire. Let me hold it closer to the flames.";

    [DisplayAs("Show Heat Ring On The Floor")]
    [SerializeField] private bool showRing = true;

    [DisplayAs("Ring Color", ShowIf = nameof(showRing))]
    [SerializeField] private Color ringColor = new Color(1f, 0.55f, 0.15f, 0.6f);

    [DisplayAs("Show Steam")]
    [SerializeField] private bool showSteam = true;

    [DisplayAs("Steam Color", ShowIf = nameof(showSteam))]
    [SerializeField] private Color steamColor = new Color(0.9f, 0.9f, 0.9f, 0.5f);

    [Foldout("When Dried")]
    [DisplayAs("Salem Thinks", Lines = 2)]
    [SerializeField] private string driedThought = "Ain't that nifty?";

    [DisplayAs("Size When Dried", Min = 0.1f, Max = 1f)]
    [SerializeField] private float driedSize = 0.75f;

    [DisplayAs("Color When Dried")]
    [SerializeField] private Color driedTint = new Color(0.45f, 0.35f, 0.3f, 1f);

    [DisplayAs("Inventory Name When Dried (blank = keep)")]
    [SerializeField] private string driedName = "";

    [DisplayAs("Description When Dried (blank = keep)", Lines = 2)]
    [SerializeField] private string driedDescription = "";

    // Unlocked on every NPC, for Event Dialogue entries that react to the dried version
    [DisplayAs("Unlock Event Id For Everyone")]
    [SerializeField] private string unlockId = "scale_dried";

    [Foldout("Events")]
    [DisplayAs("On Dried")]
    public UnityEvent onDried;

    // Once, the first time the player has it in hand after it's dried, e.g. to end a demo
    [DisplayAs("On In Hand Once Dried")]
    public UnityEvent onHeldDried;

    private readonly List<Renderer> parts = new List<Renderer>();
    private readonly List<Color> startColors = new List<Color>();
    private readonly List<Puff> puffs = new List<Puff>();
    private MaterialPropertyBlock block;
    private AudioSource sizzle;
    private Grabbable grabbable;
    private Transform viewer;
    private SpriteRenderer ring;
    private Transform effects;
    private Vector3 startScale;
    private float dryness;
    private float ringShown;
    private float puffTimer;
    private int nextPuff;
    private bool sizzleHintDone;
    private bool holdHintDone;
    private bool heldDriedDone;

    public bool IsDried { get; private set; }
    public bool IsReacting { get; private set; }

    // 0 = fresh, 1 = fully dried
    public float Dryness => dryness;

    private void Awake()
    {
        startScale = transform.localScale;
        block = new MaterialPropertyBlock();
        IsReacting = reactsFromStart;
        grabbable = GetComponent<Grabbable>();

        foreach (Renderer part in GetComponentsInChildren<Renderer>(true))
        {
            parts.Add(part);
            startColors.Add(StartColor(part));
        }

        BuildEffects();

        if (sizzleSound == null) return;
        sizzle = gameObject.AddComponent<AudioSource>();
        sizzle.clip = sizzleSound;
        sizzle.loop = true;
        sizzle.playOnAwake = false;
        sizzle.spatialBlend = 1f;
        sizzle.volume = 0f;
    }

    private void Start()
    {
        FirstPersonController player = FindAnyObjectByType<FirstPersonController>();
        if (player != null) viewer = player.CameraTransform != null ? player.CameraTransform : player.transform;
    }

    // Everything stops at once when it's put away
    private void OnDisable()
    {
        if (sizzle != null) sizzle.Stop();
        ringShown = 0f;
        if (ring != null) ring.gameObject.SetActive(false);
        foreach (Puff puff in puffs) Retire(puff);
    }

    private void OnDestroy()
    {
        if (effects == null) return;
        if (ring != null && ring.sprite != null) Destroy(ring.sprite.texture);
        if (puffs.Count > 0 && puffs[0].Sprite.sprite != null) Destroy(puffs[0].Sprite.sprite.texture);
        Destroy(effects.gameObject);
    }

    private void Update()
    {
        float heat = IsDried || !IsReacting || fire == null ? 0f : Heat();
        UpdateSizzle(heat);
        UpdateRing();
        UpdateSteam(heat);
        UpdateHints(heat);
        CheckHeldDried();
        if (heat <= 0f) return;

        // Faster the closer it is, so holding it right in the flames dries it quickest
        dryness = Mathf.Min(1f, dryness + heat * heat * Time.deltaTime / dryTime);
        ApplyLook();
        if (dryness >= 1f) Dry();
    }

    // From now on it sizzles, steams and dries near the fire
    public void StartReacting() => IsReacting = true;

    // Dries it straight away, e.g. to skip ahead while testing
    [ContextMenu("Dry Now")]
    public void Dry()
    {
        if (IsDried) return;

        IsDried = true;
        dryness = 1f;
        ApplyLook();
        UpdateSizzle(0f);

        if (grabbable != null)
        {
            if (!string.IsNullOrWhiteSpace(driedName)) grabbable.itemName = driedName;
            if (!string.IsNullOrWhiteSpace(driedDescription)) grabbable.itemDescription = driedDescription;
        }

        if (!string.IsNullOrWhiteSpace(driedThought)) DetectiveThoughts.Get().Say(driedThought);
        NPCDialogue.UnlockEverywhere(unlockId);
        onDried?.Invoke();
    }

    private float Heat()
    {
        float distance = Vector3.Distance(transform.position, fire.position);
        return Mathf.Clamp01(1f - distance / heatRange);
    }

    // Each hint plays once: when it first starts sizzling, and when the player first picks it up undried
    private void UpdateHints(float heat)
    {
        if (IsDried || !IsReacting) return;

        if (!sizzleHintDone && heat > 0f)
        {
            sizzleHintDone = true;
            Say(sizzleHint);
        }

        if (!holdHintDone && grabbable != null && grabbable.IsHeld)
        {
            holdHintDone = true;
            Say(holdHint);
        }
    }

    private void CheckHeldDried()
    {
        if (heldDriedDone || !IsDried || grabbable == null || !grabbable.IsHeld) return;

        heldDriedDone = true;
        onHeldDried?.Invoke();
    }

    private static void Say(string line)
    {
        if (!string.IsNullOrWhiteSpace(line)) DetectiveThoughts.Get().Say(line);
    }

    private void UpdateSizzle(float heat)
    {
        if (sizzle == null) return;

        sizzle.volume = heat * sizzleVolume;
        sizzle.pitch = 0.9f + heat * 0.3f;
        if (heat > 0f && !sizzle.isPlaying) sizzle.Play();
        else if (heat <= 0f && sizzle.isPlaying) sizzle.Stop();
    }

    // Shows while the scale still needs drying and is in hand or near the fire
    private void UpdateRing()
    {
        if (ring == null || fire == null) return;

        bool held = grabbable != null && grabbable.IsHeld;
        bool near = Vector3.Distance(transform.position, fire.position) <= RingShowDistance;
        bool wanted = showRing && IsReacting && !IsDried && (held || near);

        ringShown = Mathf.MoveTowards(ringShown, wanted ? 1f : 0f, Time.deltaTime * RingFadeSpeed);
        bool visible = ringShown > 0f;
        if (ring.gameObject.activeSelf != visible) ring.gameObject.SetActive(visible);
        if (!visible) return;

        Vector3 floor = fire.position;
        if (Physics.Raycast(fire.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 10f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            floor = hit.point;
        }

        ring.transform.SetPositionAndRotation(floor + Vector3.up * FloorLift, Quaternion.Euler(90f, 0f, 0f));
        ring.transform.localScale = Vector3.one * (heatRange * 2f);

        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * RingPulseSpeed);
        ring.color = new Color(ringColor.r, ringColor.g, ringColor.b, ringColor.a * ringShown * pulse);
    }

    // Puffs rise off the scale, more of them the closer it is to the flames
    private void UpdateSteam(float heat)
    {
        if (puffs.Count == 0) return;

        if (showSteam && heat > 0f)
        {
            puffTimer += Time.deltaTime * PuffsPerSecond * heat;
            while (puffTimer >= 1f)
            {
                puffTimer -= 1f;
                Emit(puffs[nextPuff]);
                nextPuff = (nextPuff + 1) % puffs.Count;
            }
        }

        Quaternion facing = viewer != null ? viewer.rotation : Quaternion.identity;
        foreach (Puff puff in puffs)
        {
            if (puff.Age < 0f) continue;

            puff.Age += Time.deltaTime;
            float t = puff.Age / PuffLife;
            if (t >= 1f)
            {
                Retire(puff);
                continue;
            }

            Transform puffTransform = puff.Sprite.transform;
            puffTransform.SetPositionAndRotation(puff.Start + puff.Drift * t + Vector3.up * (PuffRise * t), facing);
            puffTransform.localScale = Vector3.one * Mathf.Lerp(PuffStartSize, PuffEndSize, t);
            puff.Sprite.color = new Color(steamColor.r, steamColor.g, steamColor.b, steamColor.a * (1f - t) * Mathf.Clamp01(t * 6f));
        }
    }

    private void Emit(Puff puff)
    {
        puff.Age = 0f;
        puff.Start = transform.position + new Vector3(Random.Range(-0.03f, 0.03f), 0.02f, Random.Range(-0.03f, 0.03f));
        puff.Drift = new Vector3(Random.Range(-0.08f, 0.08f), 0f, Random.Range(-0.08f, 0.08f));
        puff.Sprite.gameObject.SetActive(true);
    }

    private static void Retire(Puff puff)
    {
        puff.Age = -1f;
        if (puff.Sprite != null) puff.Sprite.gameObject.SetActive(false);
    }

    private void ApplyLook()
    {
        transform.localScale = startScale * Mathf.Lerp(1f, driedSize, dryness);

        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] == null) continue;

            Color tinted = Color.Lerp(startColors[i], startColors[i] * driedTint, dryness);
            parts[i].GetPropertyBlock(block);
            block.SetColor(BaseColorId, tinted);
            block.SetColor(ColorId, tinted);
            parts[i].SetPropertyBlock(block);
        }
    }

    // Kept outside the scale, so they don't shrink with it or vanish with it into the inventory too early
    private void BuildEffects()
    {
        effects = new GameObject(name + " Heat Effects").transform;

        ring = new GameObject("Heat Ring").AddComponent<SpriteRenderer>();
        ring.transform.SetParent(effects, false);
        ring.sprite = CreateRing();
        ring.gameObject.SetActive(false);

        Sprite puffSprite = CreatePuff();
        for (int i = 0; i < PuffCount; i++)
        {
            var puff = new Puff { Sprite = new GameObject("Steam").AddComponent<SpriteRenderer>() };
            puff.Sprite.transform.SetParent(effects, false);
            puff.Sprite.sprite = puffSprite;
            puff.Sprite.gameObject.SetActive(false);
            puffs.Add(puff);
        }
    }

    // A thin glowing band at the edge with a faint warm fill inside, 1 meter across before scaling
    private static Sprite CreateRing()
    {
        var pixels = new Color32[RingTextureSize * RingTextureSize];
        for (int y = 0; y < RingTextureSize; y++)
        {
            for (int x = 0; x < RingTextureSize; x++)
            {
                float u = (x + 0.5f) / RingTextureSize * 2f - 1f;
                float v = (y + 0.5f) / RingTextureSize * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);

                float band = Mathf.Clamp01(1f - Mathf.Abs(d - 0.96f) / 0.03f);
                float fill = d < 0.96f ? 0.12f * d : 0f;
                float alpha = Mathf.Clamp01(band + fill);
                pixels[y * RingTextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        return MakeSprite(pixels, RingTextureSize, "Heat Ring");
    }

    private static Sprite CreatePuff()
    {
        var pixels = new Color32[PuffTextureSize * PuffTextureSize];
        for (int y = 0; y < PuffTextureSize; y++)
        {
            for (int x = 0; x < PuffTextureSize; x++)
            {
                float u = (x + 0.5f) / PuffTextureSize * 2f - 1f;
                float v = (y + 0.5f) / PuffTextureSize * 2f - 1f;
                float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                pixels[y * PuffTextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * alpha * 255f));
            }
        }

        return MakeSprite(pixels, PuffTextureSize, "Steam Puff");
    }

    private static Sprite MakeSprite(Color32[] pixels, int size, string label)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = label, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Color StartColor(Renderer part)
    {
        Material material = part.sharedMaterial;
        if (material == null) return Color.white;
        if (material.HasProperty(BaseColorId)) return material.GetColor(BaseColorId);
        if (material.HasProperty(ColorId)) return material.GetColor(ColorId);
        return Color.white;
    }

    private void OnDrawGizmosSelected()
    {
        if (fire == null) return;
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(fire.position, heatRange);
    }
}