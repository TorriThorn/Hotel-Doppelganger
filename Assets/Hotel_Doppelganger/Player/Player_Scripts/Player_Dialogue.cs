using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// The player's side of conversations. Click an NPC or press E to talk, pick replies with the mouse, number keys or W/S + E,
// and press Tab to leave. Conversations play out in a log down the right side of the screen, like Disco Elysium,
// while the camera frames the NPC's face in the space beside it and their sprite swaps between idle and talking.
// Builds its own UI at runtime and only redraws what changes
[AddComponentMenu("Game/Player/Player Dialogue")]
public class PlayerDialogue : MonoBehaviour
{
    private enum OptionKind
    {
        Choice,
        Event,
        Present,
        Goodbye
    }

    // One row in the choices box. Picked is worked out when the screen is shown, not every frame
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

    // A floating ! or ? over one NPC. It sits outside the NPC so hiding or turning the NPC doesn't affect it
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

    public const float NpcBoxSpacing = 8f;

    private const string GoodbyeText = "Goodbye.";
    private const string FallbackBackText = "Back.";
    private const string NeverMindText = "Never mind.";
    private const string WrongItemNodeId = "present:none";
    private const string EventReplyIdPrefix = "event:";

    private const float ReopenDelay = 0.2f;
    private const float TypingSkipDelay = 0.25f;
    private const float ChoiceInputDelay = 0.2f;
    private const float FirstLetterDelay = 0.15f;
    private const float MouseMoveThresholdSqr = 1f;
    private const float LineOfSightPadding = 0.02f;
    private const int MaxNumberKeys = 9;
    private const int SortingOrder = 100;
    private const float MarkerPixelsPerMeter = 100f;
    private const float MarkerBobSpeed = 3f;
    private const float MarkerBobHeight = 0.06f;
    private const float MarkerFallbackHeight = 2f;
    private const float WrappedLineSpacing = 1.2f;
    private const float RevealWallPadding = 0.25f;
    private const float ScreenMargin = 32f;
    private const string NameSeparator = "</color>  \u2014  ";

    // Older lines are reused once the log is this long; by then they've long scrolled off the top
    private const int MaxLogEntries = 40;

    public static RectOffset NpcBoxPadding => new RectOffset(30, 30, 22, 26);
    private static RectOffset RowPadding => new RectOffset(0, 12, 6, 6);
    private static RectOffset BoxPadding => new RectOffset(32, 32, 24, 26);

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

    // Like Ace Attorney's Present: opens the inventory so the player can show the NPC an item
    [DisplayAs("Present Option Text")]
    [SerializeField] private string presentOptionText = "Show them something...";

    // {0} is the item's name
    [DisplayAs("Presenting In The Log")]
    [SerializeField] private string presentLogText = "(Shows {0}.)";

    [Foldout("Conversation Camera")]
    [DisplayAs("Zoom To NPC's Face")]
    [SerializeField] private bool zoomToFace = true;

    [DisplayAs("Distance From Face (meters)", ShowIf = nameof(zoomToFace), Min = 0.3f, Max = 3f)]
    [SerializeField] private float zoomDistance = 1.3f;

    [DisplayAs("Zoomed Field Of View (0 = no change)", ShowIf = nameof(zoomToFace), Min = 0f, Max = 90f)]
    [SerializeField] private float zoomFieldOfView = 45f;

    [DisplayAs("Zoom Time (seconds)", ShowIf = nameof(zoomToFace), Min = 0f, Max = 2f)]
    [SerializeField] private float zoomTime = 0.6f;

    [DisplayAs("Face Below Top Of Head (meters)", ShowIf = nameof(zoomToFace), Min = 0f, Max = 1f)]
    [SerializeField] private float faceBelowHeadTop = 0.2f;

    [DisplayAs("Reveal Zoom Out Time (seconds)", ShowIf = nameof(zoomToFace), Min = 0f, Max = 3f)]
    [SerializeField] private float revealZoomTime = 0.9f;

    // 1 fits the revealed sprite exactly; higher leaves room around it
    [DisplayAs("Space Around Revealed Sprite", ShowIf = nameof(zoomToFace), Min = 1f, Max = 2f)]
    [SerializeField] private float revealMargin = 1.2f;

    // How far up the screen the face sits, from 0 at the bottom to 1 at the top. Sideways it's centered in the
    // space left of the conversation log
    [DisplayAs("Face Height On Screen", ShowIf = nameof(zoomToFace), Min = 0.3f, Max = 0.9f)]
    [SerializeField] private float faceScreenHeight = 0.6f;

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

    [DisplayAs("NPC Name Color")]
    [SerializeField] private Color nameColor = new Color(0.45f, 0.44f, 0.41f);

    // Used for everything in the conversation log, from both the NPC and the player
    [DisplayAs("Log Text Color")]
    [SerializeField] private Color lineColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Player Name In Log")]
    [SerializeField] private string playerName = "YOU";

    [DisplayAs("Player Name Color")]
    [SerializeField] private Color playerNameColor = new Color(0.85f, 0.83f, 0.78f);

    // The replies the player can still pick, set apart from the log
    [DisplayAs("Choice Color")]
    [SerializeField] private Color choiceTextColor = new Color(0.82f, 0.45f, 0.32f);

    [DisplayAs("Already Picked Choice Color")]
    [SerializeField] private Color saidChoiceColor = new Color(0.47f, 0.35f, 0.3f);

    [DisplayAs("Present Option Color")]
    [SerializeField] private Color presentColor = new Color(0.45f, 0.85f, 1f);

    [DisplayAs("Highlighted Choice Text")]
    [SerializeField] private Color selectedTextColor = Color.white;

    [DisplayAs("Highlighted Choice Background")]
    [SerializeField] private Color selectedBackColor = Color.clear;

    [DisplayAs("Text Shadow Color")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [Foldout("Box Look")]
    [DisplayAs("Box Color")]
    [SerializeField] private Color boxColor = new Color(0f, 0f, 0f, 0.92f);

    [DisplayAs("Box Border")]
    [SerializeField] private Color borderColor = new Color(0.85f, 0.83f, 0.78f, 0.5f);

    [DisplayAs("Screen Darkening")]
    [SerializeField] private Color screenDimColor = new Color(0f, 0f, 0f, 0f);

    [Foldout("Sizes (as on a 1080p screen)")]
    [DisplayAs("Box Width", Min = 300f)]
    [SerializeField] private float panelWidth = 640f;

    [DisplayAs("Log Text Size", Min = 1f)]
    [SerializeField] private int lineSize = 26;

    [DisplayAs("Choice Text Size", Min = 1f)]
    [SerializeField] private int choiceSize = 26;

    [DisplayAs("Space Between Log Lines", Min = 0f)]
    [SerializeField] private float logSpacing = 36f;

    [DisplayAs("Space Between Choices", Min = 0f)]
    [SerializeField] private float choiceSpacing = 10f;

    // Split evenly above and below the line between the log and the choices
    [DisplayAs("Space Above Choices", Min = 0f)]
    [SerializeField] private float choicesGap = 32f;

    [DisplayAs("Crosshair Name Text Size", Min = 1f)]
    [SerializeField] private int promptSize = 22;

    // The intro cutscene borrows these when it matches this look
    [DisplayAs("Intro Text Box Width", Min = 0f)]
    [SerializeField] private float boxWidth = 1100f;

    [DisplayAs("Intro Name Text Size", Min = 1f)]
    [SerializeField] private int nameSize = 22;

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
    private readonly List<Image> promptBorders = new List<Image>();
    private readonly List<Marker> markers = new List<Marker>();
    private readonly List<Image> boxBorders = new List<Image>();
    private readonly List<Text> logEntries = new List<Text>();
    private readonly List<Shadow> logShadows = new List<Shadow>();
    private readonly TypedLine typedLine = new TypedLine();
    private readonly TypedLine logLine = new TypedLine();

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
    private RectTransform boxes;
    private Image dialogueBox;
    private VerticalLayoutGroup boxLayout;
    private Image divider;
    private RectTransform logWindow;
    private RectTransform logColumn;
    private WheelScroll logScroll;
    private RectTransform choicesColumn;
    private VerticalLayoutGroup logLayout;
    private VerticalLayoutGroup choicesLayout;
    private CanvasGroup choicesGroup;
    private int usedLogEntries;
    private string namePrefix;
    private Text promptText;
    private Shadow promptShadow;
    private Image promptBox;
    private Text lineText;
    private Shadow lineShadow;
    private Image timerBar;

    private string highlightTag;
    private SpriteRenderer npcSprite;
    private NPCSpriteSwap npcSpriteSwap;
    private NPCFacePlayer npcFacer;
    private Sprite baseSprite;
    private Sprite appliedSprite;
    private Sprite expression;
    private bool mouthOpen;
    private float flapTimer;

    private Camera playerCamera;
    private Vector3 homeLocalPosition;
    private Quaternion homeLocalRotation;
    private float homeFieldOfView;
    private Transform faceAnchor;
    private Vector3 faceOffset;
    private float faceScreenWidth = 0.5f;
    private Vector3 revealOffset;
    private Vector3 revealSize;
    private bool revealMeasured;
    private float revealAmount;
    private Vector3 zoomPosition;
    private Quaternion zoomRotation;
    private float zoomAmount;
    private bool returning;

    private PlayerBody playerBody;
    private int presentedFrame = -1;

    public bool IsTalking => npc != null;

    // The conversation's look, so other text boxes (like the intro cutscene's) can match it
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
    public Color ChoiceColor => choiceTextColor;

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
        if (cameraTransform != null) cameraTransform.TryGetComponent(out playerCamera);
        TryGetComponent(out playerBody);

        BuildUI();
    }

    // Lets color tweaks in the Inspector show up during play without restyling every frame
    private void OnValidate()
    {
        if (canvas == null) return;

        ApplyLook();
        rowsDirty = true;
    }

    // Markers are made in Start so every NPC in the scene exists by then
    private void Start()
    {
        foreach (NPCDialogue target in FindObjectsByType<NPCDialogue>()) markers.Add(CreateMarker(target));
    }

    private void OnDestroy()
    {
        RestoreNpcSprite();
        if (zoomAmount > 0f) PutCameraBack();

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
        if (keyboard == null || mouse == null || cameraTransform == null || returning) return;

        // The inventory takes the input while an item is being picked, and for a frame after so the click isn't reused
        bool presenting = Journal.IsPresenting || Time.frameCount <= presentedFrame + 1;
        if (IsTalking)
        {
            if (!presenting) UpdateTalking(keyboard, mouse);
        }
        else UpdateNotTalking(keyboard, mouse);

        if (IsTalking) RefreshConversation();
    }

    // Runs after animation, so the talking sprite wins over an Animator on the NPC
    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        UpdateCamera();
        if (IsTalking) UpdateNpcSprite();
        UpdateMarkers();
    }

    // Each NPC only rechecks what it has left to say when its dialogue changes; the rest is just following and facing
    private void UpdateMarkers()
    {
        if (markers.Count == 0) return;

        bool hideAll = !showMarkers || IsTalking;
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
                bool wantsItem = WantsCarriedItem(marker.Npc);
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

    // Closes the current conversation, if there is one (e.g. when the game ends)
    public void EndConversation()
    {
        if (IsTalking) Close();
    }

    // Click to talk, or E. E is the only way while carrying something, since a click is for the object
    private void UpdateNotTalking(Keyboard keyboard, Mouse mouse)
    {
        bool holding = grabber != null && grabber.IsHolding;
        bool canTalk = Cursor.lockState == CursorLockMode.Locked;
        SetLookingAt(canTalk ? FindNPC() : null);

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
        // A click on the log's scroll bar is only for scrolling
        bool click = mouse.leftButton.wasPressedThisFrame && !logScroll.IsOver(mouse);

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

    // "Talk to Murray" under the crosshair
    private void UpdatePrompt()
    {
        bool show = lookingAt != null && showKeyPrompt;
        promptBox.gameObject.SetActive(show);
        if (show) promptText.text = "Talk to " + lookingAt.npcName;
    }

    private void Open(NPCDialogue target)
    {
        SetLookingAt(null);
        npc = target;
        openTime = Time.time;

        // Items are shown by presenting them, so anything in hand is let go: an inventory item is put away,
        // anything else drops
        npc.ShownItem = null;
        if (grabber != null) grabber.LetGo();

        // The camera leaves the player's head, so their own body would otherwise be in the shot
        if (playerBody != null) playerBody.HiddenFromView = true;

        if (player != null) player.InputLocked = true;
        FirstPersonController.SetCursorLocked(false);
        lastMousePosition = Mouse.current.position.ReadValue();

        SetPatrolPaused(true);
        npc.MarkTalkedTo();
        GrabNpcSprite();
        StartZoom();
        FaceCamera(true);

        namePrefix = TypedLine.ColorTag(nameColor) + npc.npcName.ToUpper() + NameSeparator;
        ClearLog();
        logScroll.Reset();
        dialogueRoot.SetActive(true);
        GoTo(npc.startNode);
    }

    private void Close()
    {
        SetPatrolPaused(false);
        RestoreNpcSprite();
        FaceCamera(false);

        npc.ShownItem = null;
        npc = null;
        node = null;
        nodeIsEventReply = false;
        options.Clear();
        dialogueRoot.SetActive(false);
        closeTime = Time.time;

        // The player gets control back once the camera has returned to them
        returning = zoomAmount > 0f;
        if (!returning) ReleasePlayer();
    }

    private void ReleasePlayer()
    {
        returning = false;
        if (playerBody != null) playerBody.HiddenFromView = false;

        if (player != null) player.InputLocked = false;
        FirstPersonController.SetCursorLocked(true);
    }

    // NPCs without NPC Face Player get one, so every NPC turns to the camera when talked to
    private void FaceCamera(bool facing)
    {
        if (facing)
        {
            Transform root = NpcRoot(npc);
            npcFacer = root.GetComponentInChildren<NPCFacePlayer>();
            if (npcFacer == null) npcFacer = root.gameObject.AddComponent<NPCFacePlayer>();
        }

        if (npcFacer != null) npcFacer.InConversation = facing;
        if (!facing) npcFacer = null;
    }

    // Walking NPCs hold still for the length of the conversation
    private void SetPatrolPaused(bool paused)
    {
        if (npc == null) return;

        NPCPatrol patrol = npc.GetComponentInParent<NPCPatrol>();
        if (patrol != null) patrol.Paused = paused;
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
        Transform root = NpcRoot(target);

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

    // Anything in the inventory counts, not just what's in the player's hands
    private static bool WantsCarriedItem(NPCDialogue target)
    {
        Journal journal = Journal.Current;
        if (journal == null) return false;

        IReadOnlyList<Grabbable> items = journal.Items;
        for (int i = 0; i < items.Count; i++)
        {
            if (target.WantsItem(items[i])) return true;
        }
        return false;
    }

    // Remembers the player's own view so the camera can return to it exactly
    private void StartZoom()
    {
        homeLocalPosition = cameraTransform.localPosition;
        homeLocalRotation = cameraTransform.localRotation;
        homeFieldOfView = playerCamera != null ? playerCamera.fieldOfView : 60f;

        // Centered in the space left of the log
        float panelPixels = (panelWidth + ScreenMargin) * canvas.scaleFactor;
        faceScreenWidth = Screen.width > 0 ? Mathf.Clamp01(0.5f * (1f - panelPixels / Screen.width)) : 0.5f;

        // Measured once and pinned to the NPC's sprite object, so the shot follows the NPC turning or settling
        // but swapping between idle and talking sprites can't nudge it
        faceAnchor = npcSprite != null ? npcSprite.transform : NpcRoot(npc);
        faceOffset = faceAnchor.InverseTransformPoint(FacePosition());
        revealMeasured = false;
        revealAmount = 0f;
    }

    // Eases between the player's own view and a shot of the NPC's face. Unscaled time, so it still finishes if the game pauses
    private void UpdateCamera()
    {
        if (!IsTalking && !returning) return;

        Vector3 homePosition = HomePosition();
        Quaternion homeRotation = HomeRotation();
        if (IsTalking) AimAtFace(homePosition, homeRotation);

        float target = IsTalking && zoomToFace ? 1f : 0f;
        zoomAmount = zoomTime > 0f ? Mathf.MoveTowards(zoomAmount, target, Time.unscaledDeltaTime / zoomTime) : target;

        float eased = Mathf.SmoothStep(0f, 1f, zoomAmount);
        cameraTransform.SetPositionAndRotation(Vector3.Lerp(homePosition, zoomPosition, eased), Quaternion.Slerp(homeRotation, zoomRotation, eased));
        if (playerCamera != null && zoomFieldOfView > 0f) playerCamera.fieldOfView = Mathf.Lerp(homeFieldOfView, zoomFieldOfView, eased);

        if (returning && zoomAmount <= 0f)
        {
            PutCameraBack();
            ReleasePlayer();
        }
    }

    // Level with the face, on the line between the NPC and the player, so nothing is in the way
    private void AimAtFace(Vector3 homePosition, Quaternion homeRotation)
    {
        Vector3 face = faceAnchor != null ? faceAnchor.TransformPoint(faceOffset) : FacePosition();
        Vector3 away = homePosition - face;
        away.y = 0f;

        // Never further back than the player already is
        float distance = Mathf.Min(zoomDistance, away.magnitude);
        if (away.sqrMagnitude < 0.0001f)
        {
            away = homeRotation * Vector3.back;
            away.y = 0f;
            distance = zoomDistance;
        }

        zoomPosition = face + away.normalized * distance;

        // Turns and tilts just enough to put the face at its spot on screen
        float halfHeight = HalfViewHeight();
        float tilt = Mathf.Atan((2f * faceScreenHeight - 1f) * halfHeight) * Mathf.Rad2Deg;
        zoomRotation = Quaternion.LookRotation(face - zoomPosition, Vector3.up) * Quaternion.Euler(tilt, SideTurn(halfHeight), 0f);

        UpdateReveal();
        if (revealAmount <= 0f) return;

        AimAtWholeSprite(homePosition, homeRotation, out Vector3 revealPosition, out Quaternion revealRotation);
        float eased = Mathf.SmoothStep(0f, 1f, revealAmount);
        zoomPosition = Vector3.Lerp(zoomPosition, revealPosition, eased);
        zoomRotation = Quaternion.Slerp(zoomRotation, revealRotation, eased);
    }

    // A sprite swap during the conversation (like a monster reveal) pulls the camera back, and swapping back
    // returns it to the face. The new sprite is measured the moment it appears, before its pop animation
    private void UpdateReveal()
    {
        bool revealed = npcSpriteSwap != null && npcSpriteSwap.IsSwapped && npcSpriteSwap.ZoomOutOnSwap && npcSprite != null;
        if (revealed && !revealMeasured)
        {
            Bounds bounds = npcSprite.bounds;
            revealOffset = faceAnchor.InverseTransformPoint(bounds.center);
            revealSize = bounds.size;
            revealMeasured = true;
        }

        float step = revealZoomTime > 0f ? Time.unscaledDeltaTime / revealZoomTime : 1f;
        revealAmount = Mathf.MoveTowards(revealAmount, revealed ? 1f : 0f, step);
        if (!revealed && revealAmount <= 0f) revealMeasured = false;
    }

    // Far enough back to fit the whole sprite, tall or wide, in the space beside the log, but never inside a wall
    private void AimAtWholeSprite(Vector3 homePosition, Quaternion homeRotation, out Vector3 position, out Quaternion rotation)
    {
        Vector3 center = faceAnchor.TransformPoint(revealOffset);
        Vector3 away = homePosition - center;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            away = homeRotation * Vector3.back;
            away.y = 0f;
        }
        away = away.normalized;

        float halfHeight = HalfViewHeight();
        float aspect = playerCamera != null ? playerCamera.aspect : 16f / 9f;
        float height = revealSize.y * revealMargin;
        float width = Mathf.Max(revealSize.x, revealSize.z) * revealMargin;
        float fitHeight = height * 0.5f / halfHeight;
        float fitWidth = width / (2f * halfHeight * aspect * Mathf.Max(0.1f, 2f * faceScreenWidth));
        float distance = Mathf.Max(zoomDistance, Mathf.Max(fitHeight, fitWidth));

        if (RaycastIgnoringPlayer(center, away, distance, out RaycastHit wall) && wall.collider.GetComponentInParent<NPCDialogue>() != npc)
            distance = Mathf.Max(zoomDistance, wall.distance - RevealWallPadding);

        position = center + away * distance;
        rotation = Quaternion.LookRotation(center - position, Vector3.up) * Quaternion.Euler(0f, SideTurn(halfHeight), 0f);
    }

    private float HalfViewHeight()
    {
        float fieldOfView = zoomFieldOfView > 0f ? zoomFieldOfView : homeFieldOfView;
        return Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
    }

    // Turns the camera so its subject sits in the middle of the space left of the log
    private float SideTurn(float halfHeight)
    {
        float aspect = playerCamera != null ? playerCamera.aspect : 16f / 9f;
        return -Mathf.Atan((2f * faceScreenWidth - 1f) * halfHeight * aspect) * Mathf.Rad2Deg;
    }

    // Centered on the drawn sprite rather than the NPC's pivot, in case the art sits off to one side
    private Vector3 FacePosition()
    {
        Transform root = NpcRoot(npc);
        Renderer body = npcSprite != null ? npcSprite : root.GetComponentInChildren<Renderer>();
        Vector3 face = npc.HeadPosition(body, MarkerFallbackHeight);
        face.y += npc.faceHeightAdjust - npc.headHeightAdjust - faceBelowHeadTop;

        if (body != null && body.enabled)
        {
            Vector3 center = body.bounds.center;
            face.x = center.x;
            face.z = center.z;
        }
        return face;
    }

    private Vector3 HomePosition()
    {
        Transform parent = cameraTransform.parent;
        return parent != null ? parent.TransformPoint(homeLocalPosition) : homeLocalPosition;
    }

    private Quaternion HomeRotation()
    {
        Transform parent = cameraTransform.parent;
        return parent != null ? parent.rotation * homeLocalRotation : homeLocalRotation;
    }

    private void PutCameraBack()
    {
        zoomAmount = 0f;
        cameraTransform.localPosition = homeLocalPosition;
        cameraTransform.localRotation = homeLocalRotation;
        if (playerCamera != null && zoomFieldOfView > 0f) playerCamera.fieldOfView = homeFieldOfView;
    }

    private void GrabNpcSprite()
    {
        Transform root = NpcRoot(npc);
        npcSprite = root.GetComponentInChildren<SpriteRenderer>();
        npcSpriteSwap = root.GetComponentInChildren<NPCSpriteSwap>();
        if (npcSpriteSwap == null) npcSpriteSwap = npc.GetComponentInParent<NPCSpriteSwap>();

        baseSprite = npcSprite != null ? npcSprite.sprite : null;
        appliedSprite = baseSprite;
        expression = null;
        mouthOpen = false;
    }

    // Anything else that changes the sprite (an Animator, a sprite swap) is picked up as the new base, and the
    // idle and talking sprites step aside while a swapped sprite (like a monster reveal) is showing
    private void UpdateNpcSprite()
    {
        if (npcSprite == null) return;

        Sprite current = npcSprite.sprite;
        if (current != appliedSprite) baseSprite = current;

        Sprite shown = baseSprite;
        bool swapped = npcSpriteSwap != null && npcSpriteSwap.IsSwapped;
        if (expression != null) shown = expression;
        else if (!swapped && mouthOpen && npc.talkingPortrait != null) shown = npc.talkingPortrait;
        else if (!swapped && npc.portrait != null) shown = npc.portrait;

        if (current != shown) npcSprite.sprite = shown;
        appliedSprite = shown;
    }

    // Only undoes the dialogue's own change, so a sprite swap made during the conversation stays
    private void RestoreNpcSprite()
    {
        if (npcSprite != null && npcSprite.sprite == appliedSprite) npcSprite.sprite = baseSprite;
        npcSprite = null;
        npcSpriteSwap = null;
        faceAnchor = null;
    }

    // Flaps between idle and talking while the line types, and rests closed after
    private void AnimateMouth(float dt)
    {
        if (!IsTyping)
        {
            mouthOpen = false;
            return;
        }

        flapTimer += dt;
        if (flapTimer < flapInterval) return;

        flapTimer = 0f;
        mouthOpen = !mouthOpen;
    }

    private static Transform NpcRoot(NPCDialogue target)
    {
        NPCPatrol patrol = target.GetComponentInParent<NPCPatrol>();
        return patrol != null ? patrol.transform : target.transform;
    }

    // Queued now, shown once the conversation closes, since thoughts wait while the dialogue box is up
    private static void Think(string line)
    {
        if (!string.IsNullOrWhiteSpace(line)) DetectiveThoughts.Get().Say(line);
    }

    // A quick-time screen ran out: move on to its timeout node, or end the conversation if it has none
    private void TimeOut()
    {
        LogCurrentLine();
        string next = node.timeoutNode;
        if (string.IsNullOrWhiteSpace(next)) Close();
        else GoTo(next);
    }

    // A Go To Node that doesn't match any node keeps the player on the current screen.
    // Only an unknown First Node ends the conversation, since there's nothing to show
    private void GoTo(string nodeId)
    {
        NPCDialogue.Node target = npc.GetNode(nodeId);

        if (target != null) ShowNode(target, false);
        else if (node != null) ShowNode(node, nodeIsEventReply, true);
        else Close();
    }

    // A screen shown again after a choice that leads nowhere new keeps its line in the log rather than repeating it
    private void ShowNode(NPCDialogue.Node target, bool isEventReply, bool skipTyping = false)
    {
        logScroll.ReturnToStart();
        node = target;
        nodeIsEventReply = isEventReply;
        typedLine.Set(skipTyping ? string.Empty : node.npcText);
        lineText.gameObject.SetActive(typedLine.Length > 0);
        expression = node.expression;

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

        // Unlocked event dialogue sits on its own screen, never on an event's reply screen.
        // Item lines are only reached by presenting the item
        if (!nodeIsEventReply)
        {
            List<NPCDialogue.EventDialogue> events = npc.eventDialogue;
            for (int i = 0; i < events.Count; i++)
            {
                NPCDialogue.EventDialogue entry = events[i];
                if (entry == null || entry.requiredItem != null) continue;
                if (npc.IsShownOn(entry, node) && !string.IsNullOrWhiteSpace(entry.text))
                    options.Add(new Option(OptionKind.Event, i, npc.WasChosen(entry)));
            }
        }

        // Only on the NPC's first screen, like Ace Attorney's Present button outside of testimony
        bool onFirstScreen = !nodeIsEventReply && node == npc.GetNode(npc.startNode);
        if (onFirstScreen && Journal.Get().HasItems) options.Add(new Option(OptionKind.Present, -1, false));
        if (npc.addGoodbye) options.Add(new Option(OptionKind.Goodbye, -1, false));
    }

    private void Choose(Option option)
    {
        if (option.Kind == OptionKind.Present)
        {
            Journal.Get().OpenToPresent(Present);
            return;
        }

        LogCurrentLine();
        AddLogEntry(TypedLine.ColorTag(playerNameColor) + playerName + NameSeparator, OptionText(option));

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

    // The right item plays that item's line; anything else gets the NPC's "that's not relevant" reply
    private void Present(Grabbable item)
    {
        presentedFrame = Time.frameCount;
        if (item == null || !IsTalking) return;

        LogCurrentLine();
        npc.ShownItem = item;

        // What Salem says while showing it goes in the same log line, e.g. "(Shows Geno's Note.) Geno had notes about you."
        int index = npc.FindItemEntry(item);
        string said = presentLogText.Replace("{0}", item.DisplayName);
        if (index >= 0 && !string.IsNullOrWhiteSpace(npc.eventDialogue[index].text)) said += " " + npc.eventDialogue[index].text.Trim();
        AddLogEntry(TypedLine.ColorTag(playerNameColor) + playerName + NameSeparator, said);

        if (index >= 0) ChooseEvent(npc.eventDialogue[index], index);
        else ShowNode(BuildWrongItemReply(), true);
    }

    private NPCDialogue.Node BuildWrongItemReply()
    {
        // Event reply screens have no node of their own to go back to, so those lead back to the first node
        string returnTo = npc.GetNode(node.id) != null ? node.id : npc.startNode;

        return new NPCDialogue.Node
        {
            id = WrongItemNodeId,
            npcText = npc.wrongItemReply,
            choices = new List<NPCDialogue.Choice>
            {
                new NPCDialogue.Choice { text = NeverMindText, nextNode = returnTo, greyAfterChosen = false }
            }
        };
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
            case OptionKind.Present: return presentOptionText;
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
        logScroll.Update(Mouse.current, Time.unscaledDeltaTime);
        if (renderedLetters != visibleLetters) RenderLine();
        AnimateMouth(Time.deltaTime);

        float choicesAlpha = IsTyping ? 0f : 1f;
        if (choicesGroup.alpha != choicesAlpha) choicesGroup.alpha = choicesAlpha;

        bool timed = node != null && node.timeLimit > 0f && !IsTyping;
        if (timerBar.gameObject.activeSelf != timed) timerBar.gameObject.SetActive(timed);
        if (timed)
        {
            float left = 1f - (Time.time - choicesShownTime) / node.timeLimit;
            timerBar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(left), 0.5f);
        }

        if (rowsDirty) DrawRows();
    }

    private void RenderLine()
    {
        renderedLetters = visibleLetters;
        lineText.text = namePrefix + typedLine.Render(visibleLetters, highlightTag);
    }

    // Moves the NPC's current line up into the log, in full
    private void LogCurrentLine()
    {
        if (!lineText.gameObject.activeSelf || typedLine.Length == 0) return;

        AddLogEntry(namePrefix, node.npcText);
        lineText.gameObject.SetActive(false);
    }

    private void AddLogEntry(string prefix, string text)
    {
        Text entry;
        if (usedLogEntries < logEntries.Count) entry = logEntries[usedLogEntries];
        else if (logEntries.Count < MaxLogEntries) entry = CreateLogEntry();
        else
        {
            // The oldest entry is reused as the newest
            entry = logEntries[0];
            logEntries.RemoveAt(0);
            logEntries.Add(entry);
            usedLogEntries--;
        }

        usedLogEntries++;
        logLine.Set(text);
        entry.text = prefix + logLine.Render(logLine.Length, highlightTag);
        entry.gameObject.SetActive(true);

        // Newest entry last, with the NPC's current line under it
        entry.transform.SetAsLastSibling();
        lineText.transform.SetAsLastSibling();
        logScroll.ReturnToStart();
    }

    private void ClearLog()
    {
        foreach (Text entry in logEntries) entry.gameObject.SetActive(false);
        usedLogEntries = 0;
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
            row.Label.color = isSelected ? selectedTextColor : RowColor(options[i]);
            row.Shadow.enabled = !isSelected;
            row.Shadow.effectColor = shadowColor;
        }
    }

    private Color RowColor(Option option)
    {
        if (option.Kind == OptionKind.Present) return presentColor;
        return option.Picked ? saidChoiceColor : choiceTextColor;
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

        BuildBoxes(dialogueRect);

        // The NPC's current line sits at the bottom of the log
        lineText = RuntimeUI.CreateText("Line", logColumn, font, lineSize, lineColor, TextAnchor.UpperLeft, shadowColor, out lineShadow);
        lineText.supportRichText = true;
        lineText.lineSpacing = WrappedLineSpacing;

        highlightTag = TypedLine.ColorTag(highlightColor);
        dialogueRoot.SetActive(false);
    }

    // One box down the right side: the log fills the top, with the choices under a thin line at the bottom
    private void BuildBoxes(Transform parent)
    {
        boxes = RuntimeUI.CreateRect("Boxes", parent);
        boxes.anchorMin = new Vector2(1f, 0f);
        boxes.anchorMax = Vector2.one;
        boxes.pivot = new Vector2(1f, 0.5f);
        boxes.sizeDelta = new Vector2(panelWidth, -2f * ScreenMargin);
        boxes.anchoredPosition = new Vector2(-ScreenMargin, 0f);

        var boxesLayout = boxes.gameObject.AddComponent<VerticalLayoutGroup>();
        boxesLayout.childControlWidth = true;
        boxesLayout.childControlHeight = true;
        boxesLayout.childForceExpandWidth = true;
        boxesLayout.childForceExpandHeight = true;

        dialogueBox = RuntimeUI.CreateBox("Dialogue Box", boxes, panelWidth, boxColor, borderColor, BoxPadding, choicesGap * 0.5f, boxBorders);
        boxLayout = dialogueBox.GetComponent<VerticalLayoutGroup>();

        BuildLog();
        BuildDivider();
        BuildChoices();
    }

    // The log is pinned to the bottom of a clipped window and grows upward, so the oldest lines slide off the top
    private void BuildLog()
    {
        logWindow = RuntimeUI.CreateRect("Window", dialogueBox.transform);
        logWindow.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        logWindow.gameObject.AddComponent<RectMask2D>();

        logColumn = CreateColumn("Log", logWindow, logSpacing);
        logLayout = logColumn.GetComponent<VerticalLayoutGroup>();
        logColumn.anchorMin = Vector2.zero;
        logColumn.anchorMax = new Vector2(1f, 0f);
        logColumn.pivot = new Vector2(0.5f, 0f);
        logColumn.sizeDelta = Vector2.zero;
        logColumn.anchoredPosition = Vector2.zero;
        logColumn.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // The wheel scrolls back through earlier lines; anything new brings the log back down
        logScroll = new WheelScroll(logWindow, logColumn, true, ScrollTrackColor(), lineColor);
    }

    // The quick-time timer drains along this line
    private void BuildDivider()
    {
        divider = RuntimeUI.CreateImage("Divider", dialogueBox.transform, ScrollTrackColor());
        var size = divider.gameObject.AddComponent<LayoutElement>();
        size.minHeight = 1f;
        size.preferredHeight = 1f;

        timerBar = RuntimeUI.CreateImage("Timer", divider.transform, highlightColor);
        RectTransform barRect = timerBar.rectTransform;
        barRect.anchorMin = new Vector2(0f, 0.5f);
        barRect.anchorMax = new Vector2(1f, 0.5f);
        barRect.pivot = new Vector2(0f, 0.5f);
        barRect.sizeDelta = new Vector2(0f, 4f);
        barRect.anchoredPosition = Vector2.zero;
        timerBar.gameObject.SetActive(false);
    }

    // The choices keep their space while a line types, so the log doesn't jump; they only fade in
    private void BuildChoices()
    {
        choicesColumn = CreateColumn("Choices", dialogueBox.transform, choiceSpacing);
        choicesLayout = choicesColumn.GetComponent<VerticalLayoutGroup>();
        choicesGroup = choicesColumn.gameObject.AddComponent<CanvasGroup>();
        choicesGroup.blocksRaycasts = false;
    }

    private Color ScrollTrackColor() => new Color(borderColor.r, borderColor.g, borderColor.b, borderColor.a * 0.35f);

    private static RectTransform CreateColumn(string name, Transform parent, float spacing)
    {
        RectTransform rect = RuntimeUI.CreateRect(name, parent);
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return rect;
    }

    private Text CreateLogEntry()
    {
        Text entry = RuntimeUI.CreateText("Log Entry", logColumn, font, lineSize, lineColor, TextAnchor.UpperLeft, shadowColor, out Shadow shadow);
        entry.supportRichText = true;
        entry.lineSpacing = WrappedLineSpacing;
        logEntries.Add(entry);
        logShadows.Add(shadow);
        return entry;
    }

    private ChoiceRow CreateRow()
    {
        var row = new ChoiceRow { Back = RuntimeUI.CreateRow("Choice", choicesColumn, choiceSize + 8f, RowPadding, 0f) };
        row.Label = RuntimeUI.CreateText("Text", row.Back.transform, font, choiceSize, choiceTextColor, TextAnchor.MiddleLeft, shadowColor, out row.Shadow);
        row.Label.lineSpacing = WrappedLineSpacing;
        RuntimeUI.FillWidth(row.Label);

        rows.Add(row);
        return row;
    }

    private void ApplyLook()
    {
        dimImage.color = screenDimColor;
        dialogueBox.color = boxColor;
        foreach (Image edge in boxBorders) edge.color = borderColor;
        divider.color = ScrollTrackColor();
        boxes.sizeDelta = new Vector2(panelWidth, -2f * ScreenMargin);
        boxLayout.spacing = choicesGap * 0.5f;
        logLayout.spacing = logSpacing;
        choicesLayout.spacing = choiceSpacing;
        logScroll.SetColors(ScrollTrackColor(), lineColor);

        lineText.color = lineColor;
        lineText.fontSize = lineSize;
        foreach (Text entry in logEntries)
        {
            entry.color = lineColor;
            entry.fontSize = lineSize;
        }
        foreach (Shadow shadow in logShadows) shadow.effectColor = shadowColor;
        highlightTag = TypedLine.ColorTag(highlightColor);
        timerBar.color = highlightColor;
        foreach (Marker marker in markers)
        {
            if (marker.Object != null) StyleMarker(marker);
        }
        promptText.color = lineColor;
        promptBox.color = boxColor;
        foreach (Image edge in promptBorders) edge.color = borderColor;
        lineShadow.effectColor = shadowColor;
        promptShadow.effectColor = shadowColor;
    }
}