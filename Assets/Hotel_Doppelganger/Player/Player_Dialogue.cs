using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// The player's side of conversations. Click an NPC to talk, pick replies with the mouse, number keys or W/S + E,
// and press Tab to leave. Builds its own UI at runtime and only redraws what changes.
[AddComponentMenu("Game/Player/Player Dialogue")]
public class PlayerDialogue : MonoBehaviour
{
    private enum OptionKind { Choice, Event, Goodbye }

    // One row in the choices box. Picked is worked out when the screen is shown, not every frame.
    private readonly struct Option
    {
        public readonly OptionKind Kind;
        public readonly int Index;
        public readonly bool Picked;

        public Option(OptionKind kind, int index, bool picked)
        {
            Kind = kind;
            Index = index;
            Picked = picked;
        }
    }

    private sealed class ChoiceRow
    {
        public Image Back;
        public Text Label;
        public Shadow Shadow;
    }

    private const string GoodbyeText = "Goodbye.";
    private const string FallbackBackText = "Back.";
    private const string EventReplyIdPrefix = "event:";

    private const float ReopenDelay = 0.2f;
    private const float TypingSkipDelay = 0.25f;
    private const float ChoiceInputDelay = 0.2f;
    private const float FirstLetterDelay = 0.15f;
    private const float MouseMoveThresholdSqr = 1f;
    private const float LineOfSightPadding = 0.02f;
    private const int MaxNumberKeys = 9;
    private const int SortingOrder = 100;
    private const float PortraitSlideTime = 0.25f;
    private const float PortraitSlideDistance = 60f;
    private const float MarkerPixelsPerMeter = 100f;
    private const float MarkerBobSpeed = 3f;
    private const float MarkerBobHeight = 0.06f;
    private const float MarkerFallbackHeight = 2f;

    // A floating ! over one NPC. It sits outside the NPC so hiding or turning the NPC doesn't affect it.
    private sealed class Marker
    {
        public NPCDialogue Npc;
        public Transform Root;
        public Renderer Body;
        public Collider Shape;
        public GameObject Object;
        public Text Label;
        public Shadow Shadow;
        public int SeenVersion = -1;
        public bool HasNew;
        public bool WantsItem;
        public float Phase;
    }

    public static RectOffset NpcBoxPadding => new RectOffset(30, 30, 22, 26);
    public const float NpcBoxSpacing = 8f;
    private static RectOffset ChoicesBoxPadding => new RectOffset(16, 16, 14, 14);
    private static RectOffset RowPadding => new RectOffset(12, 12, 6, 6);

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Camera (optional)")]
    [SerializeField] private Transform cameraTransform;

    [DisplayAs("Player (optional)")]
    [SerializeField] private FirstPersonController player;

    [DisplayAs("Object Grabber (optional)")]
    [SerializeField] private ObjectGrabber grabber;

    [Header("Talking")]
    [DisplayAs("Talk Distance", Min = 0f)]
    [SerializeField] private float talkRange = 3f;

    [DisplayAs("Aim Forgiveness", Min = 0f, Max = 1f)]
    [SerializeField] private float talkRadius = 0.3f;

    [DisplayAs("Talkable Layers")]
    [SerializeField] private LayerMask talkLayers = ~0;

    [DisplayAs("Show Button Prompt")]
    [SerializeField] private bool showKeyPrompt = true;

    [DisplayAs("Hide NPC In The World While Talking")]
    [SerializeField] private bool hideNpcWhileTalking = true;

    [Foldout("Typing Effect")]
    [DisplayAs("Letters Per Second", Min = 1f)]
    [SerializeField] private float lettersPerSecond = 35f;

    [DisplayAs("Pause After Sentences", Min = 0f)]
    [SerializeField] private float sentencePause = 0.35f;

    [DisplayAs("Pause After Commas", Min = 0f)]
    [SerializeField] private float commaPause = 0.12f;

    [DisplayAs("Mouth Flap Time (seconds)", Min = 0.05f, Max = 0.5f)]
    [SerializeField] private float flapInterval = 0.2f;

    [Foldout("Text Look")]
    [DisplayAs("Font (optional)")]
    [SerializeField] private Font font;

    [DisplayAs("Highlighted Words")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.45f, 0.35f);

    [DisplayAs("Show Character Portraits")]
    [SerializeField] private bool showPortraits = true;

    [DisplayAs("NPC Name Color")]
    [SerializeField] private Color nameColor = new Color(0.45f, 0.44f, 0.41f);

    [DisplayAs("NPC Line Color")]
    [SerializeField] private Color lineColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Choice Color")]
    [SerializeField] private Color choiceColor = new Color(0.45f, 0.44f, 0.41f);

    [DisplayAs("Already Picked Choice Color")]
    [SerializeField] private Color saidChoiceColor = new Color(0.34f, 0.33f, 0.31f);

    [DisplayAs("Highlighted Choice Text")]
    [SerializeField] private Color selectedTextColor = Color.black;

    [DisplayAs("Highlighted Choice Background")]
    [SerializeField] private Color selectedBackColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Text Shadow Color")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [Foldout("Box Look")]
    [DisplayAs("NPC Box Color")]
    [SerializeField] private Color boxColor = new Color(0f, 0f, 0f, 0.92f);

    [DisplayAs("NPC Box Border")]
    [SerializeField] private Color borderColor = new Color(0.85f, 0.83f, 0.78f, 0.5f);

    [DisplayAs("Choices Box Color")]
    [SerializeField] private Color choicesBoxColor = new Color(0f, 0f, 0f, 0.92f);

    [DisplayAs("Choices Box Border")]
    [SerializeField] private Color choicesBorderColor = new Color(0.85f, 0.83f, 0.78f, 0.5f);

    [DisplayAs("Screen Darkening")]
    [SerializeField] private Color screenDimColor = new Color(0f, 0f, 0f, 0.35f);

    [Foldout("Sizes (as on a 1080p screen)")]
    [DisplayAs("NPC Box Width", Min = 0f)]
    [SerializeField] private float boxWidth = 1100f;

    [DisplayAs("Choices Box Width", Min = 0f)]
    [SerializeField] private float choicesBoxWidth = 700f;

    [DisplayAs("Gap Between Boxes", Min = 0f)]
    [SerializeField] private float boxGap = 16f;

    [DisplayAs("NPC Name Text Size", Min = 1f)]
    [SerializeField] private int nameSize = 22;

    [DisplayAs("NPC Line Text Size", Min = 1f)]
    [SerializeField] private int lineSize = 32;

    [DisplayAs("Choice Text Size", Min = 1f)]
    [SerializeField] private int choiceSize = 28;

    [DisplayAs("Crosshair Name Text Size", Min = 1f)]
    [SerializeField] private int promptSize = 22;

    [DisplayAs("Portrait Height", Min = 100f)]
    [SerializeField] private float portraitHeight = 900f;

    [DisplayAs("Portrait Raise (from bottom)")]
    [SerializeField] private float portraitRaise = 220f;

    [Foldout("New Dialogue Marker")]
    [DisplayAs("Show ! Over NPCs With Something New")]
    [SerializeField] private bool showMarkers = true;

    [DisplayAs("Marker Color", ShowIf = nameof(showMarkers))]
    [SerializeField] private Color markerColor = new Color(1f, 0.82f, 0.2f);

    [DisplayAs("Marker Size", ShowIf = nameof(showMarkers), Min = 0.1f, Max = 3f)]
    [SerializeField] private float markerSize = 1f;

    [DisplayAs("Height Above Head", ShowIf = nameof(showMarkers))]
    [SerializeField] private float markerHeight = 0.25f;

    [DisplayAs("Show Within (meters)", ShowIf = nameof(showMarkers), Min = 0f)]
    [SerializeField] private float markerRange = 20f;

    [DisplayAs("Show-Item Marker", ShowIf = nameof(showMarkers))]
    [SerializeField] private string itemMarkerText = "?";

    [DisplayAs("Show-Item Marker Color", ShowIf = nameof(showMarkers))]
    [SerializeField] private Color itemMarkerColor = new Color(0.45f, 0.85f, 1f);

    private readonly RaycastHit[] rayHits = new RaycastHit[32];
    private readonly RaycastHit[] sphereHits = new RaycastHit[32];
    private readonly List<Option> options = new List<Option>();
    private readonly List<ChoiceRow> rows = new List<ChoiceRow>();
    private readonly List<Image> npcBoxBorders = new List<Image>();
    private readonly List<Image> choicesBoxBorders = new List<Image>();
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private readonly List<Marker> markers = new List<Marker>();
    private readonly TypedLine typedLine = new TypedLine();

    private NPCDialogue lookingAt;
    private NPCDialogue npc;
    private NPCDialogue.Node node;
    private bool nodeIsEventReply;
    private int selected;
    private float openTime;
    private float closeTime = -1f;
    private Vector2 lastMousePosition;

    private int visibleLetters;
    private int renderedLetters = -1;
    private float letterTimer;
    private float choicesShownTime;
    private bool rowsDirty;

    private Canvas canvas;
    private GameObject dialogueRoot;
    private Image dimImage;
    private Image npcBox;
    private Image choicesBox;
    private CanvasGroup choicesGroup;
    private Text promptText;
    private Shadow promptShadow;
    private Image promptBox;
    private readonly List<Image> promptBorders = new List<Image>();
    private bool promptShowsHolding;
    private Text nameText;
    private Shadow nameShadow;
    private Text lineText;
    private Shadow lineShadow;
    private Image portrait;
    private Image timerBar;

    private string highlightTag;
    private Sprite idleSprite;
    private Sprite talkingSprite;
    private bool mouthOpen;
    private float flapTimer;
    private float portraitTime = 1f;

    public bool IsTalking => npc != null;

    // The NPC box's look, so other text boxes (like the intro cutscene's) can match it
    public Font BoxFont => font;
    public Color BoxColor => boxColor;
    public Color BoxBorderColor => borderColor;
    public Color NameColor => nameColor;
    public Color LineColor => lineColor;
    public Color ShadowColor => shadowColor;
    public float BoxWidth => boxWidth;
    public int NameSize => nameSize;
    public int LineSize => lineSize;
    public Color HighlightColor => highlightColor;

    public float TalkRange { get => talkRange; set => talkRange = Mathf.Max(0f, value); }
    public float LettersPerSecond { get => lettersPerSecond; set => lettersPerSecond = Mathf.Max(1f, value); }
    public float TalkAimForgiveness { get => talkRadius; set => talkRadius = Mathf.Max(0f, value); }
    public float SentencePause { get => sentencePause; set => sentencePause = Mathf.Max(0f, value); }

    public bool ShowTalkPrompt
    {
        get => showKeyPrompt;
        set
        {
            showKeyPrompt = value;
            NPCDialogue target = lookingAt;
            lookingAt = null;
            if (promptText != null) SetLookingAt(target);
        }
    }

    private bool IsTyping => node != null && visibleLetters < typedLine.Length;

    // Markers are made in Start so every NPC in the scene exists by then
    private void Start()
    {
        foreach (NPCDialogue target in FindObjectsByType<NPCDialogue>()) markers.Add(CreateMarker(target));
    }

    private void Awake()
    {
        if (player == null) TryGetComponent(out player);
        if (grabber == null) TryGetComponent(out grabber);

        if (cameraTransform == null && player != null) cameraTransform = player.CameraTransform;
        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        BuildUI();
    }

    // Lets color tweaks in the Inspector show up during play without restyling every frame
    private void OnValidate()
    {
        if (canvas == null) return;

        ApplyLook();
        rowsDirty = true;
    }

    private void OnDestroy()
    {
        ShowHiddenNpc();
        foreach (Marker marker in markers)
        {
            if (marker.Object != null) Destroy(marker.Object);
        }
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null || cameraTransform == null) return;

        if (IsTalking) UpdateTalking(keyboard, mouse);
        else UpdateNotTalking(keyboard, mouse);

        if (IsTalking) RefreshConversation();
    }

    // Closes the current conversation, if there is one (e.g. when the game ends)
    public void EndConversation()
    {
        if (IsTalking) Close();
    }

    // Click to talk, or E. E is the only way while carrying something, since the left button is busy holding it.
    private void UpdateNotTalking(Keyboard keyboard, Mouse mouse)
    {
        bool holding = grabber != null && grabber.IsHolding;
        bool canTalk = Cursor.lockState == CursorLockMode.Locked;
        SetLookingAt(canTalk ? FindNPC() : null);
        if (lookingAt != null && holding != promptShowsHolding) UpdatePrompt();

        bool pressed = keyboard.eKey.wasPressedThisFrame || (!holding && mouse.leftButton.wasPressedThisFrame);
        if (lookingAt != null && pressed && Time.time > closeTime + ReopenDelay) Open(lookingAt);
    }

    private void UpdateTalking(Keyboard keyboard, Mouse mouse)
    {
        if (keyboard.tabKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        bool confirm = keyboard.eKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame;
        bool click = mouse.leftButton.wasPressedThisFrame;

        if (IsTyping)
        {
            bool skip = confirm || keyboard.spaceKey.wasPressedThisFrame || (click && Time.time > openTime + TypingSkipDelay);
            if (skip) FinishTyping();
            else TypeLetters();
            return;
        }

        if (node.timeLimit > 0f && Time.time - choicesShownTime >= node.timeLimit)
        {
            TimeOut();
            return;
        }

        if (options.Count == 0) return;

        // A short delay so the click that skipped the typing doesn't also pick a choice
        bool ready = Time.time > choicesShownTime + ChoiceInputDelay;

        int numbered = Mathf.Min(options.Count, MaxNumberKeys);
        for (int i = 0; i < numbered; i++)
        {
            if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
            {
                Choose(options[i]);
                return;
            }
        }

        if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame) Select((selected - 1 + options.Count) % options.Count);
        if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame) Select((selected + 1) % options.Count);

        // Hover only moves the selection when the mouse actually moves, so it doesn't fight the keyboard
        Vector2 mousePosition = mouse.position.ReadValue();
        bool mouseMoved = (mousePosition - lastMousePosition).sqrMagnitude > MouseMoveThresholdSqr;
        if (mouseMoved) lastMousePosition = mousePosition;

        int hovered = RowAt(mousePosition);
        if (hovered >= 0)
        {
            if (mouseMoved) Select(hovered);
            if (click && ready)
            {
                Choose(options[hovered]);
                return;
            }
        }

        if (confirm && ready) Choose(options[selected]);
    }

    private void TypeLetters()
    {
        letterTimer -= Time.deltaTime;

        float letterDelay = 1f / Mathf.Max(1f, lettersPerSecond);

        while (letterTimer <= 0f && visibleLetters < typedLine.Length)
        {
            letterTimer += letterDelay + typedLine.PauseAfter(visibleLetters, sentencePause, commaPause);
            visibleLetters++;
        }

        if (!IsTyping) choicesShownTime = Time.time;
    }

    private void FinishTyping()
    {
        visibleLetters = typedLine.Length;
        choicesShownTime = Time.time;
    }

    private NPCDialogue FindNPC()
    {
        Vector3 origin = cameraTransform.position;
        Vector3 direction = cameraTransform.forward;

        // Exact aim always wins
        if (RaycastIgnoringPlayer(origin, direction, talkRange, out RaycastHit direct))
        {
            NPCDialogue target = direct.collider.GetComponentInParent<NPCDialogue>();
            if (target != null) return target;
        }

        if (talkRadius <= 0f) return null;

        // Otherwise sweep a fatter ray and take the NPC closest to the crosshair
        int count = Physics.SphereCastNonAlloc(origin, talkRadius, direction, sphereHits, talkRange, talkLayers, QueryTriggerInteraction.Ignore);
        NPCDialogue best = null;
        float bestScore = float.MaxValue;
        float rangeSqr = talkRange * talkRange;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = sphereHits[i];
            if (IsOwnOrHeld(hit)) continue;

            NPCDialogue candidate = hit.collider.GetComponentInParent<NPCDialogue>();
            if (candidate == null || candidate == best) continue;

            // Colliders already inside the sphere when the cast starts report no hit point
            Vector3 point = hit.distance > 0f ? hit.point : ClosestPointOn(hit.collider, origin);
            Vector3 toPoint = point - origin;
            if (toPoint.sqrMagnitude > rangeSqr) continue;

            float score = Vector3.Cross(direction, toPoint).sqrMagnitude;
            if (score >= bestScore || !HasLineOfSight(origin, toPoint, candidate)) continue;

            bestScore = score;
            best = candidate;
        }

        return best;
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 toPoint, NPCDialogue target)
    {
        float distance = toPoint.magnitude - LineOfSightPadding;
        if (distance <= 0f) return true;

        return !RaycastIgnoringPlayer(origin, toPoint.normalized, distance, out RaycastHit blocker)
               || blocker.collider.GetComponentInParent<NPCDialogue>() == target;
    }

    // Skips the player's own colliders, which would otherwise block the ray from inside the head
    private bool RaycastIgnoringPlayer(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, rayHits, distance, talkLayers, QueryTriggerInteraction.Ignore);

        closest = default;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayHits[i];
            if ((found && hit.distance >= closest.distance) || IsOwnOrHeld(hit)) continue;

            closest = hit;
            found = true;
        }

        return found;
    }

    // The player's own colliders and whatever they're carrying would otherwise block the view of the NPC
    private bool IsOwnOrHeld(RaycastHit hit)
    {
        if (hit.collider.transform.IsChildOf(transform)) return true;
        return grabber != null && grabber.HeldObject != null && hit.rigidbody == grabber.HeldObject;
    }

    // Collider.ClosestPoint doesn't work on concave mesh colliders, so use their bounds instead
    private static Vector3 ClosestPointOn(Collider hitCollider, Vector3 position) =>
        hitCollider is MeshCollider mesh && !mesh.convex
            ? hitCollider.bounds.ClosestPoint(position)
            : hitCollider.ClosestPoint(position);

    private void SetLookingAt(NPCDialogue target)
    {
        if (target == lookingAt) return;

        lookingAt = target;
        bool canTalk = target != null;
        if (player != null) player.CanTalk = canTalk;

        UpdatePrompt();
    }

    // "[E]  Talk to Murray" under the crosshair, or "[E]  Show to Murray" while carrying something
    private void UpdatePrompt()
    {
        bool show = lookingAt != null && showKeyPrompt;
        promptBox.gameObject.SetActive(show);
        if (!show) return;

        promptShowsHolding = grabber != null && grabber.IsHolding;
        string action = promptShowsHolding ? "Show to " : "Talk to ";
        promptText.text = highlightTag + "[E]</color>   " + action + lookingAt.npcName;
    }

    private void Open(NPCDialogue target)
    {
        SetLookingAt(null);
        npc = target;
        openTime = Time.time;

        // Whatever the player is carrying stays in hand and unlocks any "show the item" lines
        Rigidbody held = grabber != null && grabber.IsHolding ? grabber.HeldObject : null;
        npc.ShownItem = held != null ? held.GetComponent<Grabbable>() : null;
        if (grabber != null) grabber.HoldThroughLock = true;

        if (player != null) player.InputLocked = true;
        FirstPersonController.SetCursorLocked(false);
        lastMousePosition = Mouse.current.position.ReadValue();

        SetPatrolPaused(true);
        npc.MarkTalkedTo();
        HideNpc();

        nameText.text = npc.npcName.ToUpper();
        portraitTime = 0f;
        dialogueRoot.SetActive(true);
        GoTo(npc.startNode);
    }

    private void Close()
    {
        SetPatrolPaused(false);
        ShowHiddenNpc();

        npc.ShownItem = null;
        if (grabber != null) grabber.HoldThroughLock = false;
        npc = null;
        node = null;
        nodeIsEventReply = false;
        options.Clear();
        dialogueRoot.SetActive(false);
        closeTime = Time.time;

        if (player != null) player.InputLocked = false;
        FirstPersonController.SetCursorLocked(true);
    }

    // Walking NPCs hold still for the length of the conversation
    private void SetPatrolPaused(bool paused)
    {
        if (npc == null) return;

        NPCPatrol patrol = npc.GetComponentInParent<NPCPatrol>();
        if (patrol != null) patrol.Paused = paused;
    }

    // Each NPC only rechecks what it has left to say when its dialogue changes; the rest is just following and facing
    private void LateUpdate()
    {
        if (markers.Count == 0 || cameraTransform == null) return;

        bool hideAll = !showMarkers || IsTalking;
        Grabbable held = HeldGrabbable();
        Vector3 cameraPosition = cameraTransform.position;
        Quaternion facing = cameraTransform.rotation;
        float rangeSqr = markerRange * markerRange;
        float bob = Time.time * MarkerBobSpeed;

        foreach (Marker marker in markers)
        {
            if (marker.Object == null) continue;

            bool show = false;
            Vector3 top = Vector3.zero;

            if (!hideAll && marker.Npc != null && marker.Npc.isActiveAndEnabled)
            {
                if (marker.SeenVersion != marker.Npc.Version)
                {
                    marker.SeenVersion = marker.Npc.Version;
                    marker.HasNew = marker.Npc.HasNewDialogue();
                }

                // "Show me that" beats "something new", since it's the more useful thing to know right now
                bool wantsItem = marker.Npc.WantsItem(held);
                if (wantsItem != marker.WantsItem)
                {
                    marker.WantsItem = wantsItem;
                    StyleMarker(marker);
                }

                if (marker.HasNew || wantsItem)
                {
                    top = MarkerAnchor(marker);
                    show = (top - cameraPosition).sqrMagnitude <= rangeSqr;
                }
            }

            if (marker.Object.activeSelf != show) marker.Object.SetActive(show);
            if (!show) continue;

            Vector3 position = top + Vector3.up * (markerHeight + Mathf.Sin(bob + marker.Phase) * MarkerBobHeight);
            marker.Object.transform.SetPositionAndRotation(position, facing);
        }
    }

    // The top of the NPC's sprite, or its collider if it has no visible sprite
    private static Vector3 MarkerAnchor(Marker marker)
    {
        if (marker.Body != null && marker.Body.enabled) return marker.Npc.HeadPosition(marker.Body, MarkerFallbackHeight);

        Vector3 root = marker.Root.position;
        float top = marker.Shape != null && marker.Shape.enabled ? marker.Shape.bounds.max.y : root.y + MarkerFallbackHeight;
        return new Vector3(root.x, top + marker.Npc.headHeightAdjust, root.z);
    }

    private Marker CreateMarker(NPCDialogue target)
    {
        NPCPatrol patrol = target.GetComponentInParent<NPCPatrol>();
        Transform root = patrol != null ? patrol.transform : target.transform;

        var marker = new Marker
        {
            Npc = target,
            Root = root,
            Body = root.GetComponentInChildren<SpriteRenderer>(),
            Shape = root.GetComponentInChildren<Collider>(),
            Phase = markers.Count * 1.3f
        };
        if (marker.Body == null) marker.Body = root.GetComponentInChildren<Renderer>();

        // A small world-space canvas, so the ! uses the same font and shadow as the rest of the UI
        var canvas = new GameObject(target.npcName + " New Dialogue Marker").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rect = canvas.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(100f, 140f);
        rect.pivot = new Vector2(0.5f, 0f);

        marker.Label = RuntimeUI.CreateText("!", canvas.transform, font, 120, markerColor, TextAnchor.LowerCenter, shadowColor, out marker.Shadow);
        RuntimeUI.Stretch(marker.Label.rectTransform);
        marker.Label.text = "!";
        marker.Label.fontStyle = FontStyle.Bold;
        marker.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
        marker.Label.verticalOverflow = VerticalWrapMode.Overflow;

        marker.Object = canvas.gameObject;
        StyleMarker(marker);
        marker.Object.SetActive(false);
        return marker;
    }

    private void StyleMarker(Marker marker)
    {
        marker.Object.transform.localScale = Vector3.one * (markerSize / MarkerPixelsPerMeter);
        marker.Label.text = marker.WantsItem ? itemMarkerText : "!";
        marker.Label.color = marker.WantsItem ? itemMarkerColor : markerColor;
        marker.Shadow.effectColor = shadowColor;
    }

    // The Grabbable in the player's hands, looked up only when what they're holding changes
    private Rigidbody heldBody;
    private Grabbable heldGrabbable;

    private Grabbable HeldGrabbable()
    {
        Rigidbody body = grabber != null && grabber.IsHolding ? grabber.HeldObject : null;
        if (body != heldBody)
        {
            heldBody = body;
            heldGrabbable = body != null ? body.GetComponent<Grabbable>() : null;
        }
        return heldGrabbable;
    }

    // Only the visuals are switched off, so the NPC keeps its collider and spot in the room.
    // The portrait still reads the hidden sprite, so a sprite swap mid-conversation shows up.
    private void HideNpc()
    {
        if (!hideNpcWhileTalking) return;

        NPCPatrol patrol = npc.GetComponentInParent<NPCPatrol>();
        Transform root = patrol != null ? patrol.transform : npc.transform;

        foreach (Renderer part in root.GetComponentsInChildren<Renderer>())
        {
            if (!part.enabled) continue;
            part.enabled = false;
            hiddenRenderers.Add(part);
        }
    }

    private void ShowHiddenNpc()
    {
        foreach (Renderer part in hiddenRenderers)
        {
            if (part != null) part.enabled = true;
        }
        hiddenRenderers.Clear();
    }

    // Queued now, shown once the conversation closes, since thoughts wait while the dialogue box is up
    private static void Think(string line)
    {
        if (!string.IsNullOrWhiteSpace(line)) DetectiveThoughts.Get().Say(line);
    }

    // A quick-time screen ran out: move on to its timeout node, or end the conversation if it has none
    private void TimeOut()
    {
        string next = node.timeoutNode;
        if (string.IsNullOrWhiteSpace(next)) Close();
        else GoTo(next);
    }

    // A Go To Node that doesn't match any node keeps the player on the current screen.
    // Only an unknown First Node ends the conversation, since there's nothing to show.
    private void GoTo(string nodeId)
    {
        NPCDialogue.Node target = npc.GetNode(nodeId);

        if (target != null) ShowNode(target, false);
        else if (node != null) ShowNode(node, nodeIsEventReply, true);
        else Close();
    }

    private void ShowNode(NPCDialogue.Node target, bool isEventReply, bool skipTyping = false)
    {
        node = target;
        nodeIsEventReply = isEventReply;
        typedLine.Set(node.npcText);
        ShowPortrait(node.expression);

        // Nothing to type out means the choices can appear straight away
        bool showAtOnce = skipTyping || typedLine.Length == 0;
        visibleLetters = showAtOnce ? typedLine.Length : 0;
        renderedLetters = -1;
        letterTimer = FirstLetterDelay;
        if (showAtOnce) choicesShownTime = Time.time;

        BuildOptions();
        selected = 0;
        BindRows();
    }

    private void BuildOptions()
    {
        options.Clear();

        List<NPCDialogue.Choice> choices = node.choices;
        for (int i = 0; i < choices.Count; i++)
        {
            NPCDialogue.Choice choice = choices[i];

            // Locked or blank choices never take up a row
            if (npc.IsAvailable(choice) && !string.IsNullOrWhiteSpace(choice.text))
                options.Add(new Option(OptionKind.Choice, i, npc.WasChosen(choice)));
        }

        // Unlocked event dialogue sits on its own screen, never on an event's reply screen
        if (!nodeIsEventReply)
        {
            List<NPCDialogue.EventDialogue> events = npc.eventDialogue;
            for (int i = 0; i < events.Count; i++)
            {
                NPCDialogue.EventDialogue entry = events[i];
                if (npc.IsShownOn(entry, node) && !string.IsNullOrWhiteSpace(entry.text))
                    options.Add(new Option(OptionKind.Event, i, npc.WasChosen(entry)));
            }
        }

        if (npc.addGoodbye) options.Add(new Option(OptionKind.Goodbye, -1, false));
    }

    private void Choose(Option option)
    {
        switch (option.Kind)
        {
            case OptionKind.Goodbye: Close(); break;
            case OptionKind.Event: ChooseEvent(npc.eventDialogue[option.Index], option.Index); break;
            default: ChooseNodeChoice(node.choices[option.Index]); break;
        }
    }

    private void ChooseNodeChoice(NPCDialogue.Choice choice)
    {
        npc.MarkVisited(choice);
        Think(choice.salemThinks);
        if (choice.greyAfterChosen) npc.MarkChosen(choice);
        choice.onChosen?.Invoke();

        // The choice's own events may have ended the conversation (e.g. the game ending)
        if (!IsTalking) return;

        if (choice.endConversation) Close();
        else if (string.IsNullOrWhiteSpace(choice.nextNode)) ShowNode(node, nodeIsEventReply, true);
        else GoTo(choice.nextNode);
    }

    private void ChooseEvent(NPCDialogue.EventDialogue entry, int index)
    {
        npc.MarkVisited(entry);
        Think(entry.salemThinks);
        if (entry.greyAfterChosen) npc.MarkChosen(entry);
        entry.onChosen?.Invoke();
        if (!IsTalking) return;

        if (entry.endConversation)
        {
            Close();
            return;
        }

        // Go To Node only counts if it names a real node; otherwise the entry's own reply is used
        NPCDialogue.Node target = npc.GetNode(entry.nextNode);
        ShowNode(target ?? BuildEventReply(entry, index), target == null);
    }

    // The entry's reply screen: its own choices, plus a way back to the screen it was picked from
    private NPCDialogue.Node BuildEventReply(NPCDialogue.EventDialogue entry, int index)
    {
        var reply = new NPCDialogue.Node
        {
            id = EventReplyIdPrefix + index,
            npcText = entry.npcReply,
            expression = entry.expression,
            choices = new List<NPCDialogue.Choice>(entry.choices)
        };

        // Always leave the player at least one way out
        bool hasBackText = !string.IsNullOrWhiteSpace(entry.backText);
        if (hasBackText || !npc.addGoodbye)
        {
            reply.choices.Add(new NPCDialogue.Choice
            {
                text = hasBackText ? entry.backText : FallbackBackText,
                nextNode = node.id,
                greyAfterChosen = false
            });
        }

        return reply;
    }

    private string OptionText(Option option)
    {
        switch (option.Kind)
        {
            case OptionKind.Goodbye: return GoodbyeText;
            case OptionKind.Event: return npc.eventDialogue[option.Index].text;
            default: return node.choices[option.Index].text;
        }
    }

    private void Select(int index)
    {
        if (index == selected) return;
        selected = index;
        rowsDirty = true;
    }

    private int RowAt(Vector2 screenPoint)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (RuntimeUI.Contains(rows[i].Back.rectTransform, screenPoint)) return i;
        }

        return -1;
    }

    private void RefreshConversation()
    {
        if (renderedLetters != visibleLetters) RenderLine();
        AnimatePortrait(Time.deltaTime);

        float choicesAlpha = IsTyping ? 0f : 1f;
        if (choicesGroup.alpha != choicesAlpha) choicesGroup.alpha = choicesAlpha;

        bool timed = node != null && node.timeLimit > 0f && !IsTyping;
        if (timerBar.gameObject.activeSelf != timed) timerBar.gameObject.SetActive(timed);
        if (timed)
        {
            float left = 1f - (Time.time - choicesShownTime) / node.timeLimit;
            timerBar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(left), 1f);
        }

        if (rowsDirty) DrawRows();
    }

    private void RenderLine()
    {
        renderedLetters = visibleLetters;
        lineText.text = typedLine.Render(visibleLetters, highlightTag);
    }

    // An expression on the node wins, then the NPC's portrait, then the sprite the NPC shows in the world
    private void ShowPortrait(Sprite expression)
    {
        idleSprite = null;
        talkingSprite = null;

        if (showPortraits)
        {
            idleSprite = expression != null ? expression : npc.portrait != null ? npc.portrait : WorldSprite(npc);
            talkingSprite = expression == null ? npc.talkingPortrait : null;
        }

        portrait.sprite = idleSprite;
        portrait.gameObject.SetActive(idleSprite != null);
        mouthOpen = false;
    }

    private static Sprite WorldSprite(NPCDialogue target)
    {
        SpriteRenderer sprite = target.GetComponentInChildren<SpriteRenderer>();
        if (sprite == null) sprite = target.GetComponentInParent<SpriteRenderer>();
        return sprite != null ? sprite.sprite : null;
    }

    // Slides the portrait in when a conversation opens, and flaps its mouth while the line types
    private void AnimatePortrait(float dt)
    {
        if (idleSprite == null) return;

        if (portraitTime < 1f)
        {
            portraitTime = Mathf.Min(1f, portraitTime + dt / PortraitSlideTime);
            float eased = Mathf.SmoothStep(0f, 1f, portraitTime);
            portrait.color = new Color(1f, 1f, 1f, eased);
            portrait.rectTransform.anchoredPosition = new Vector2((1f - eased) * PortraitSlideDistance, portraitRaise);
        }

        if (talkingSprite == null) return;

        bool open = false;
        if (IsTyping)
        {
            flapTimer += dt;
            if (flapTimer >= flapInterval)
            {
                flapTimer = 0f;
                mouthOpen = !mouthOpen;
            }
            open = mouthOpen;
        }
        portrait.sprite = open ? talkingSprite : idleSprite;
    }

    // Rows are pooled and reused between screens instead of being destroyed and rebuilt
    private void BindRows()
    {
        for (int i = 0; i < options.Count; i++)
        {
            ChoiceRow row = i < rows.Count ? rows[i] : CreateRow();
            row.Back.gameObject.SetActive(true);
            row.Label.text = $"{i + 1}.  {OptionText(options[i])}";
        }

        for (int i = options.Count; i < rows.Count; i++) rows[i].Back.gameObject.SetActive(false);

        rowsDirty = true;
    }

    private void DrawRows()
    {
        rowsDirty = false;

        for (int i = 0; i < options.Count; i++)
        {
            ChoiceRow row = rows[i];
            bool isSelected = i == selected;

            row.Back.color = isSelected ? selectedBackColor : Color.clear;
            row.Label.color = isSelected ? selectedTextColor : options[i].Picked ? saidChoiceColor : choiceColor;
            row.Shadow.enabled = !isSelected;
            row.Shadow.effectColor = shadowColor;
        }
    }

    private void BuildUI()
    {
        canvas = RuntimeUI.CreateCanvas("Dialogue UI", SortingOrder);
        Transform root = canvas.transform;

        // A small box under the crosshair that grows to fit its text
        promptBox = RuntimeUI.CreateBox("Prompt", root, 0f, boxColor, borderColor, new RectOffset(16, 16, 8, 10), 0f, promptBorders);
        RectTransform promptRect = promptBox.rectTransform;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0.5f);
        promptRect.pivot = new Vector2(0.5f, 1f);
        promptRect.anchoredPosition = new Vector2(0f, -48f);
        var promptFit = promptBox.gameObject.AddComponent<ContentSizeFitter>();
        promptFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        promptFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        promptText = RuntimeUI.CreateText("Text", promptBox.transform, font, promptSize, lineColor, TextAnchor.MiddleCenter, shadowColor, out promptShadow);
        promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
        promptText.supportRichText = true;
        promptBox.gameObject.SetActive(false);

        RectTransform dialogueRect = RuntimeUI.CreateRect("Dialogue", root);
        RuntimeUI.Stretch(dialogueRect);
        dialogueRoot = dialogueRect.gameObject;

        dimImage = RuntimeUI.CreateImage("Dim", dialogueRect, screenDimColor);
        RuntimeUI.Stretch(dimImage.rectTransform);

        // Stands behind the boxes at the bottom of the screen, like the intro cutscene
        portrait = RuntimeUI.CreateImage("Portrait", dialogueRect, Color.white);
        portrait.preserveAspect = true;
        RectTransform portraitRect = portrait.rectTransform;
        portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0.5f, 0f);
        portraitRect.pivot = new Vector2(0.5f, 0f);
        portraitRect.sizeDelta = new Vector2(portraitHeight * 1.2f, portraitHeight);
        portraitRect.anchoredPosition = new Vector2(0f, portraitRaise);
        portrait.gameObject.SetActive(false);

        Transform stack = BuildBoxStack(dialogueRect);

        npcBox = RuntimeUI.CreateBox("NPC Box", stack, boxWidth, boxColor, borderColor, NpcBoxPadding, NpcBoxSpacing, npcBoxBorders);
        nameText = RuntimeUI.CreateText("Name", npcBox.transform, font, nameSize, nameColor, TextAnchor.UpperLeft, shadowColor, out nameShadow);
        lineText = RuntimeUI.CreateText("Line", npcBox.transform, font, lineSize, lineColor, TextAnchor.UpperLeft, shadowColor, out lineShadow);
        lineText.supportRichText = true;

        choicesBox = RuntimeUI.CreateBox("Choices Box", stack, choicesBoxWidth, choicesBoxColor, choicesBorderColor, ChoicesBoxPadding, 2f, choicesBoxBorders);
        choicesGroup = choicesBox.gameObject.AddComponent<CanvasGroup>();
        choicesGroup.blocksRaycasts = false;

        // A thin bar along the top of the choices box that drains on quick-time screens
        timerBar = RuntimeUI.CreateImage("Timer", choicesBox.transform, highlightColor);
        timerBar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        RectTransform barRect = timerBar.rectTransform;
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = Vector2.one;
        barRect.pivot = new Vector2(0f, 1f);
        barRect.sizeDelta = new Vector2(0f, 6f);
        barRect.anchoredPosition = Vector2.zero;
        timerBar.gameObject.SetActive(false);

        highlightTag = TypedLine.ColorTag(highlightColor);
        dialogueRoot.SetActive(false);
    }

    // Bottom-centered column with the NPC box above the choices box
    private Transform BuildBoxStack(Transform parent)
    {
        RectTransform rect = RuntimeUI.CreateRect("Boxes", parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 40f);
        rect.sizeDelta = new Vector2(Mathf.Max(boxWidth, choicesBoxWidth), 0f);

        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = boxGap;
        layout.childAlignment = TextAnchor.LowerCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        rect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rect;
    }

    private ChoiceRow CreateRow()
    {
        var row = new ChoiceRow { Back = RuntimeUI.CreateRow("Choice", choicesBox.transform, choiceSize + 16f, RowPadding, 0f) };
        row.Label = RuntimeUI.CreateText("Text", row.Back.transform, font, choiceSize, choiceColor, TextAnchor.MiddleLeft, shadowColor, out row.Shadow);
        RuntimeUI.FillWidth(row.Label);

        rows.Add(row);
        return row;
    }

    private void ApplyLook()
    {
        dimImage.color = screenDimColor;
        npcBox.color = boxColor;
        choicesBox.color = choicesBoxColor;
        foreach (Image edge in npcBoxBorders) edge.color = borderColor;
        foreach (Image edge in choicesBoxBorders) edge.color = choicesBorderColor;

        nameText.color = nameColor;
        lineText.color = lineColor;
        highlightTag = TypedLine.ColorTag(highlightColor);
        timerBar.color = highlightColor;
        foreach (Marker marker in markers)
        {
            if (marker.Object != null) StyleMarker(marker);
        }
        portrait.rectTransform.sizeDelta = new Vector2(portraitHeight * 1.2f, portraitHeight);
        if (portraitTime >= 1f) portrait.rectTransform.anchoredPosition = new Vector2(0f, portraitRaise);
        promptText.color = lineColor;
        promptBox.color = boxColor;
        foreach (Image edge in promptBorders) edge.color = borderColor;
        nameShadow.effectColor = shadowColor;
        lineShadow.effectColor = shadowColor;
        promptShadow.effectColor = shadowColor;
    }
}