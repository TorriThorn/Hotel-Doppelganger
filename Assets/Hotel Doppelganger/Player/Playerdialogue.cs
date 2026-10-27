using System.Collections.Generic;
using System.Text;
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
    private const string HiddenTextOpen = "<color=#00000000>";
    private const string HiddenTextClose = "</color>";

    private const float ReopenDelay = 0.2f;
    private const float TypingSkipDelay = 0.25f;
    private const float ChoiceInputDelay = 0.2f;
    private const float FirstLetterDelay = 0.15f;
    private const float MouseMoveThresholdSqr = 1f;
    private const float LineOfSightPadding = 0.02f;
    private const int MaxNumberKeys = 9;
    private const int SortingOrder = 100;

    private static RectOffset NpcBoxPadding => new RectOffset(30, 30, 22, 26);
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

    [DisplayAs("Show Name Under Crosshair")]
    [SerializeField] private bool showTalkPrompt;

    [Foldout("Typing Effect")]
    [DisplayAs("Letters Per Second", Min = 1f)]
    [SerializeField] private float lettersPerSecond = 35f;

    [DisplayAs("Pause After Sentences", Min = 0f)]
    [SerializeField] private float sentencePause = 0.35f;

    [DisplayAs("Pause After Commas", Min = 0f)]
    [SerializeField] private float commaPause = 0.12f;

    [Foldout("Text Look")]
    [DisplayAs("Font (optional)")]
    [SerializeField] private Font font;

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

    private readonly RaycastHit[] rayHits = new RaycastHit[32];
    private readonly RaycastHit[] sphereHits = new RaycastHit[32];
    private readonly List<Option> options = new List<Option>();
    private readonly List<ChoiceRow> rows = new List<ChoiceRow>();
    private readonly List<Image> npcBoxBorders = new List<Image>();
    private readonly List<Image> choicesBoxBorders = new List<Image>();
    private readonly StringBuilder lineBuilder = new StringBuilder();

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
    private Text nameText;
    private Shadow nameShadow;
    private Text lineText;
    private Shadow lineShadow;

    public bool IsTalking => npc != null;

    public float TalkRange { get => talkRange; set => talkRange = Mathf.Max(0f, value); }
    public float LettersPerSecond { get => lettersPerSecond; set => lettersPerSecond = Mathf.Max(1f, value); }

    private bool IsTyping => node != null && visibleLetters < node.npcText.Length;

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
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null || cameraTransform == null) return;

        if (IsTalking) UpdateTalking(keyboard, mouse);
        else UpdateNotTalking(mouse);

        if (IsTalking) RefreshConversation();
    }

    // Closes the current conversation, if there is one (e.g. when the game ends)
    public void EndConversation()
    {
        if (IsTalking) Close();
    }

    private void UpdateNotTalking(Mouse mouse)
    {
        bool canTalk = Cursor.lockState == CursorLockMode.Locked && (grabber == null || !grabber.IsHolding);
        SetLookingAt(canTalk ? FindNPC() : null);

        if (lookingAt != null && mouse.leftButton.wasPressedThisFrame && Time.time > closeTime + ReopenDelay) Open(lookingAt);
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

        string text = node.npcText;
        float letterDelay = 1f / Mathf.Max(1f, lettersPerSecond);

        while (letterTimer <= 0f && visibleLetters < text.Length)
        {
            letterTimer += letterDelay + PauseAfter(text[visibleLetters]);
            visibleLetters++;
        }

        if (!IsTyping) choicesShownTime = Time.time;
    }

    private float PauseAfter(char letter)
    {
        switch (letter)
        {
            case '.': case '!': case '?': return sentencePause;
            case ',': case ';': case ':': return commaPause;
            default: return 0f;
        }
    }

    private void FinishTyping()
    {
        visibleLetters = node.npcText.Length;
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
            if (hit.collider.transform.IsChildOf(transform)) continue;

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
            if ((found && hit.distance >= closest.distance) || hit.collider.transform.IsChildOf(transform)) continue;

            closest = hit;
            found = true;
        }

        return found;
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

        bool showPrompt = canTalk && showTalkPrompt;
        promptText.gameObject.SetActive(showPrompt);
        if (showPrompt) promptText.text = target.npcName.ToLower() + "\n[click] talk";
    }

    private void Open(NPCDialogue target)
    {
        SetLookingAt(null);
        npc = target;
        openTime = Time.time;

        if (player != null) player.InputLocked = true;
        FirstPersonController.SetCursorLocked(false);
        lastMousePosition = Mouse.current.position.ReadValue();

        SetPatrolPaused(true);

        nameText.text = npc.npcName.ToUpper();
        dialogueRoot.SetActive(true);
        GoTo(npc.startNode);
    }

    private void Close()
    {
        SetPatrolPaused(false);

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
        if (node.npcText == null) node.npcText = string.Empty;
        nodeIsEventReply = isEventReply;

        // Nothing to type out means the choices can appear straight away
        bool showAtOnce = skipTyping || node.npcText.Length == 0;
        visibleLetters = showAtOnce ? node.npcText.Length : 0;
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

        float choicesAlpha = IsTyping ? 0f : 1f;
        if (choicesGroup.alpha != choicesAlpha) choicesGroup.alpha = choicesAlpha;

        if (rowsDirty) DrawRows();
    }

    // Letters not typed yet are drawn invisible, so line breaks and box size never shift while typing
    private void RenderLine()
    {
        renderedLetters = visibleLetters;
        string text = node.npcText;

        if (visibleLetters >= text.Length)
        {
            lineText.text = text;
            return;
        }

        lineBuilder.Clear();
        lineBuilder.Append(text, 0, visibleLetters)
                   .Append(HiddenTextOpen)
                   .Append(text, visibleLetters, text.Length - visibleLetters)
                   .Append(HiddenTextClose);
        lineText.text = lineBuilder.ToString();
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

        promptText = RuntimeUI.CreateText("Prompt", root, font, promptSize, choiceColor, TextAnchor.UpperCenter, shadowColor, out promptShadow);
        RectTransform promptRect = promptText.rectTransform;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0.5f);
        promptRect.pivot = new Vector2(0.5f, 1f);
        promptRect.anchoredPosition = new Vector2(0f, -40f);
        promptRect.sizeDelta = new Vector2(600f, 100f);
        promptText.gameObject.SetActive(false);

        RectTransform dialogueRect = RuntimeUI.CreateRect("Dialogue", root);
        RuntimeUI.Stretch(dialogueRect);
        dialogueRoot = dialogueRect.gameObject;

        dimImage = RuntimeUI.CreateImage("Dim", dialogueRect, screenDimColor);
        RuntimeUI.Stretch(dimImage.rectTransform);

        Transform stack = BuildBoxStack(dialogueRect);

        npcBox = RuntimeUI.CreateBox("NPC Box", stack, boxWidth, boxColor, borderColor, NpcBoxPadding, 8f, npcBoxBorders);
        nameText = RuntimeUI.CreateText("Name", npcBox.transform, font, nameSize, nameColor, TextAnchor.UpperLeft, shadowColor, out nameShadow);
        lineText = RuntimeUI.CreateText("Line", npcBox.transform, font, lineSize, lineColor, TextAnchor.UpperLeft, shadowColor, out lineShadow);
        lineText.supportRichText = true;

        choicesBox = RuntimeUI.CreateBox("Choices Box", stack, choicesBoxWidth, choicesBoxColor, choicesBorderColor, ChoicesBoxPadding, 2f, choicesBoxBorders);
        choicesGroup = choicesBox.gameObject.AddComponent<CanvasGroup>();
        choicesGroup.blocksRaycasts = false;

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
        promptText.color = choiceColor;
        nameShadow.effectColor = shadowColor;
        lineShadow.effectColor = shadowColor;
        promptShadow.effectColor = shadowColor;
    }
}