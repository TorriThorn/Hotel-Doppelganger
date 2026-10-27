using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Shadow = UnityEngine.UI.Shadow;
using Text = UnityEngine.UI.Text;

// Player side of conversations. Click an NPC to talk; choose with the mouse, number keys, or W/S + E; Tab leaves.
// Builds its own screen-space UI at runtime.
[AddComponentMenu("Game/Player/Player Dialogue")]
public class PlayerDialogue : MonoBehaviour
{
    private enum OptionKind { Choice, Event, Goodbye }

    // One row in the choices box: a node choice, an unlocked event dialogue entry, or Goodbye.
    private readonly struct Option
    {
        public readonly OptionKind Kind;
        public readonly int Index;

        public Option(OptionKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }
    }

    private const string GoodbyeText = "Goodbye.";
    private const string FallbackBackText = "Back.";
    private const string EventReplyIdPrefix = "event:";
    private const string InvisibleTextTag = "<color=#00000000>";

    private const float ReopenDelay = 0.2f;
    private const float TypingSkipDelay = 0.25f;
    private const float ChoiceInputDelay = 0.2f;
    private const float FirstLetterDelay = 0.15f;
    private const float MouseMoveThresholdSqr = 1f;
    private const float LineOfSightPadding = 0.02f;
    private const int MaxNumberKeys = 9;

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

    // Conversation state
    private NPCDialogue lookingAt;
    private NPCDialogue npc;
    private NPCDialogue.Node node;
    private bool nodeIsEventReply;
    private readonly List<Option> options = new List<Option>();
    private int selected;
    private float openTime;
    private float closeTime = -1f;
    private Vector2 lastMousePosition;

    // Typewriter
    private int visibleLetters;
    private float letterTimer;
    private float choicesShownTime;

    // Targeting buffers
    private readonly RaycastHit[] rayHits = new RaycastHit[32];
    private readonly RaycastHit[] sphereHits = new RaycastHit[32];

    // UI
    private GameObject canvasObject;
    private GameObject dialogueRoot;
    private Image dimImage;
    private Image boxImage;
    private Image[] boxBorder;
    private Image choicesBoxImage;
    private Image[] choicesBorder;
    private Text promptText;
    private Text nameText;
    private Text lineText;
    private RectTransform choicesParent;
    private CanvasGroup choicesGroup;
    private readonly List<RectTransform> rows = new List<RectTransform>();
    private readonly List<Image> rowBacks = new List<Image>();
    private readonly List<Text> rowTexts = new List<Text>();
    private readonly List<Shadow> rowShadows = new List<Shadow>();

    public bool IsTalking => npc != null;

    // Tunable values, e.g. for the Playtest menu
    public float TalkRange { get => talkRange; set => talkRange = Mathf.Max(0f, value); }
    public float LettersPerSecond { get => lettersPerSecond; set => lettersPerSecond = Mathf.Max(1f, value); }
    private bool IsTyping => node != null && visibleLetters < node.npcText.Length;

    // ---------- Lifecycle ----------

    private void Awake()
    {
        if (player == null) player = GetComponent<FirstPersonController>();
        if (grabber == null) grabber = GetComponent<ObjectGrabber>();

        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        BuildUI();
    }

    private void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    private void Update()
    {
        if (Mouse.current == null || Keyboard.current == null || cameraTransform == null) return;

        if (IsTalking) UpdateTalking();
        else UpdateNotTalking();

        RefreshUI();
    }

    // ---------- Input ----------

    private void UpdateNotTalking()
    {
        bool canTalk = Cursor.lockState == CursorLockMode.Locked && (grabber == null || !grabber.IsHolding);
        lookingAt = canTalk ? FindNPC() : null;

        if (lookingAt != null && Mouse.current.leftButton.wasPressedThisFrame && Time.time > closeTime + ReopenDelay)
            Open(lookingAt);
    }

    private void UpdateTalking()
    {
        Keyboard keyboard = Keyboard.current;
        bool confirm = keyboard.eKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame;
        bool click = Mouse.current.leftButton.wasPressedThisFrame;

        if (keyboard.tabKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (IsTyping)
        {
            bool skip = confirm || keyboard.spaceKey.wasPressedThisFrame || (click && Time.time > openTime + TypingSkipDelay);
            if (skip) FinishTyping();
            else TypeLetters();
            return;
        }

        if (options.Count == 0) return;

        // Stops the click that skipped typing from also picking a choice
        bool ready = Time.time > choicesShownTime + ChoiceInputDelay;

        for (int i = 0; i < options.Count && i < MaxNumberKeys; i++)
        {
            if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
            {
                Choose(options[i]);
                return;
            }
        }

        if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
            selected = (selected - 1 + options.Count) % options.Count;
        if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame)
            selected = (selected + 1) % options.Count;

        // Hover only changes the selection when the mouse moves, so it doesn't fight the keyboard
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        bool mouseMoved = (mousePosition - lastMousePosition).sqrMagnitude > MouseMoveThresholdSqr;
        if (mouseMoved) lastMousePosition = mousePosition;

        for (int i = 0; i < rows.Count; i++)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(rows[i], mousePosition, null)) continue;

            if (mouseMoved) selected = i;
            if (click && ready)
            {
                Choose(options[i]);
                return;
            }
        }

        if (confirm && ready) Choose(options[selected]);
    }

    // ---------- Typewriter ----------

    private void TypeLetters()
    {
        letterTimer -= Time.deltaTime;

        while (letterTimer <= 0f && IsTyping)
        {
            char letter = node.npcText[visibleLetters];
            visibleLetters++;
            letterTimer += 1f / Mathf.Max(1f, lettersPerSecond) + PauseAfter(letter);
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

    // ---------- Targeting ----------

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

        // Otherwise sweep a wider ray and pick the NPC closest to the crosshair
        int count = Physics.SphereCastNonAlloc(origin, talkRadius, direction, sphereHits, talkRange,
                                               talkLayers, QueryTriggerInteraction.Ignore);
        NPCDialogue best = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = sphereHits[i];
            if (hit.collider.transform.IsChildOf(transform)) continue;

            NPCDialogue candidate = hit.collider.GetComponentInParent<NPCDialogue>();
            if (candidate == null || candidate == best) continue;

            // Colliders already inside the sphere at the start report no hit point
            Vector3 point = hit.distance > 0f ? hit.point : ClosestPointOn(hit.collider, origin);
            Vector3 toPoint = point - origin;
            if (toPoint.magnitude > talkRange) continue;
            if (!HasLineOfSight(origin, toPoint, candidate)) continue;

            float distanceFromCrosshair = Vector3.Cross(direction, toPoint).magnitude;
            if (distanceFromCrosshair < bestScore)
            {
                bestScore = distanceFromCrosshair;
                best = candidate;
            }
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

    // Skips every collider on the player rig, which would otherwise block the ray
    private bool RaycastIgnoringPlayer(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, rayHits, distance, talkLayers, QueryTriggerInteraction.Ignore);

        closest = default;
        float closestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closestDistance) continue;

            closest = hit;
            closestDistance = hit.distance;
            found = true;
        }

        return found;
    }

    // Collider.ClosestPoint doesn't support concave mesh colliders, so fall back to their bounds
    private static Vector3 ClosestPointOn(Collider hitCollider, Vector3 position)
    {
        if (hitCollider is MeshCollider meshCollider && !meshCollider.convex)
            return hitCollider.bounds.ClosestPoint(position);

        return hitCollider.ClosestPoint(position);
    }

    // ---------- Conversation flow ----------

    private void Open(NPCDialogue target)
    {
        npc = target;
        openTime = Time.time;
        lookingAt = null;

        if (player != null) player.InputLocked = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        lastMousePosition = Mouse.current.position.ReadValue();

        SetPatrolPaused(true);

        nameText.text = npc.npcName.ToUpper();
        GoTo(npc.startNode);
    }

    private void Close()
    {
        SetPatrolPaused(false);

        npc = null;
        node = null;
        nodeIsEventReply = false;
        options.Clear();
        ClearRows();
        closeTime = Time.time;

        if (player != null) player.InputLocked = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Closes the current conversation, if there is one (e.g. when the game ends).
    public void EndConversation()
    {
        if (IsTalking) Close();
    }

    // Walking NPCs hold still for the length of the conversation
    private void SetPatrolPaused(bool paused)
    {
        if (npc == null) return;

        NPCPatrol patrol = npc.GetComponentInParent<NPCPatrol>();
        if (patrol != null) patrol.Paused = paused;
    }

    private void GoTo(string nodeId)
    {
        NPCDialogue.Node target = npc.GetNode(nodeId);
        if (target != null)
        {
            ShowNode(target, false);
            return;
        }

        // A Go To Node that doesn't match any node keeps the player on the current screen
        // instead of ending the conversation. Only an unknown First Node closes it.
        if (node != null) ShowNode(node, nodeIsEventReply, true);
        else Close();
    }

    private void ShowNode(NPCDialogue.Node target, bool isEventReply, bool skipTyping = false)
    {
        node = target;
        if (node.npcText == null) node.npcText = string.Empty;
        nodeIsEventReply = isEventReply;

        // Nothing to type out: the choices appear straight away
        bool showAtOnce = skipTyping || node.npcText.Length == 0;
        visibleLetters = showAtOnce ? node.npcText.Length : 0;
        letterTimer = FirstLetterDelay;
        if (showAtOnce) choicesShownTime = Time.time;

        BuildOptions();
        selected = 0;

        ClearRows();
        for (int i = 0; i < options.Count; i++)
            MakeRow($"{i + 1}.  {GetOptionText(options[i])}");
    }

    private void BuildOptions()
    {
        options.Clear();

        for (int i = 0; i < node.choices.Count; i++)
        {
            NPCDialogue.Choice choice = node.choices[i];

            // Hidden until its event happens, or blank: never takes up a row
            if (!npc.IsAvailable(choice) || string.IsNullOrWhiteSpace(choice.text)) continue;

            options.Add(new Option(OptionKind.Choice, i));
        }

        // Event dialogue shows on the screen it's assigned to (the first node by default), once unlocked
        if (!nodeIsEventReply)
        {
            for (int i = 0; i < npc.eventDialogue.Count; i++)
            {
                NPCDialogue.EventDialogue entry = npc.eventDialogue[i];
                string screen = string.IsNullOrWhiteSpace(entry.showInNode) ? npc.startNode : entry.showInNode.Trim();

                bool onThisScreen = string.Equals(node.id, screen, StringComparison.OrdinalIgnoreCase);
                if (onThisScreen && npc.IsAvailable(entry) && !string.IsNullOrWhiteSpace(entry.text))
                    options.Add(new Option(OptionKind.Event, i));
            }
        }

        if (npc.addGoodbye) options.Add(new Option(OptionKind.Goodbye, -1));
    }

    private void Choose(Option option)
    {
        if (npc == null) return;

        switch (option.Kind)
        {
            case OptionKind.Goodbye: Close(); break;
            case OptionKind.Event: ChooseEvent(option.Index); break;
            default: ChooseNodeChoice(option.Index); break;
        }
    }

    private void ChooseNodeChoice(int index)
    {
        NPCDialogue.Choice choice = node.choices[index];
        if (choice.greyAfterChosen) npc.MarkSaid(node, index);
        choice.onChosen?.Invoke();
        if (!IsTalking) return;

        if (choice.endConversation) Close();
        else if (string.IsNullOrWhiteSpace(choice.nextNode)) ShowNode(node, nodeIsEventReply, true);   // Stay on this screen
        else GoTo(choice.nextNode);
    }

    private void ChooseEvent(int index)
    {
        NPCDialogue.EventDialogue entry = npc.eventDialogue[index];
        if (entry.greyAfterChosen) npc.MarkEventSaid(index);
        entry.onChosen?.Invoke();
        if (!IsTalking) return;

        if (entry.endConversation)
        {
            Close();
            return;
        }

        // Go To Node is used only if it names a real node; otherwise the entry's own NPC Says reply is shown
        NPCDialogue.Node target = string.IsNullOrWhiteSpace(entry.nextNode) ? null : npc.GetNode(entry.nextNode);
        if (target != null)
        {
            ShowNode(target, false);
            return;
        }

        ShowNode(BuildEventReply(entry, index), true);
    }

    // The reply screen: the entry's own choices, plus a way back to the screen it was picked from
    private NPCDialogue.Node BuildEventReply(NPCDialogue.EventDialogue entry, int index)
    {
        var reply = new NPCDialogue.Node
        {
            id = EventReplyIdPrefix + index,
            npcText = entry.npcReply,
            choices = new List<NPCDialogue.Choice>(entry.choices)
        };

        bool hasBackText = !string.IsNullOrWhiteSpace(entry.backText);
        if (hasBackText || !npc.addGoodbye)   // Never leave the player without an option
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

    private string GetOptionText(Option option)
    {
        switch (option.Kind)
        {
            case OptionKind.Goodbye: return GoodbyeText;
            case OptionKind.Event: return npc.eventDialogue[option.Index].text;
            default: return node.choices[option.Index].text;
        }
    }

    private bool WasChosen(Option option)
    {
        switch (option.Kind)
        {
            case OptionKind.Choice: return npc.WasSaid(node, option.Index);
            case OptionKind.Event: return npc.WasEventSaid(option.Index);
            default: return false;
        }
    }

    // ---------- UI refresh ----------

    private void RefreshUI()
    {
        bool canTalk = !IsTalking && lookingAt != null;
        if (player != null) player.CanTalk = canTalk;

        promptText.gameObject.SetActive(canTalk && showTalkPrompt);
        if (canTalk && showTalkPrompt) promptText.text = lookingAt.npcName.ToLower() + "\n[click] talk";
        promptText.color = choiceColor;
        promptText.GetComponent<Shadow>().effectColor = shadowColor;

        dialogueRoot.SetActive(IsTalking);
        if (!IsTalking || node == null) return;

        ApplyColors();

        // Untyped letters are drawn invisible so the box size and line breaks never shift while typing
        string fullText = node.npcText;
        string typed = fullText.Substring(0, visibleLetters);
        string remaining = fullText.Substring(visibleLetters);
        lineText.text = remaining.Length > 0 ? typed + InvisibleTextTag + remaining + "</color>" : typed;

        choicesGroup.alpha = IsTyping ? 0f : 1f;

        for (int i = 0; i < rows.Count; i++)
        {
            bool isSelected = i == selected;
            bool wasChosen = WasChosen(options[i]);

            rowBacks[i].color = isSelected ? selectedBackColor : Color.clear;
            rowTexts[i].color = isSelected ? selectedTextColor : (wasChosen ? saidChoiceColor : choiceColor);
            rowShadows[i].enabled = !isSelected;
            rowShadows[i].effectColor = shadowColor;
        }
    }

    // Applied every frame so Inspector tweaks show up live
    private void ApplyColors()
    {
        dimImage.color = screenDimColor;
        boxImage.color = boxColor;
        choicesBoxImage.color = choicesBoxColor;
        foreach (Image edge in boxBorder) edge.color = borderColor;
        foreach (Image edge in choicesBorder) edge.color = choicesBorderColor;

        nameText.color = nameColor;
        lineText.color = lineColor;
        nameText.GetComponent<Shadow>().effectColor = shadowColor;
        lineText.GetComponent<Shadow>().effectColor = shadowColor;
    }

    // ---------- UI construction ----------

    private void BuildUI()
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        canvasObject = new GameObject("Dialogue UI", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        promptText = MakeText(canvasObject.transform, "Prompt", promptSize, choiceColor, TextAnchor.UpperCenter);
        RectTransform promptRect = promptText.rectTransform;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0.5f);
        promptRect.pivot = new Vector2(0.5f, 1f);
        promptRect.anchoredPosition = new Vector2(0f, -40f);
        promptRect.sizeDelta = new Vector2(600f, 100f);
        promptText.gameObject.SetActive(false);

        dialogueRoot = new GameObject("Dialogue", typeof(RectTransform));
        dialogueRoot.transform.SetParent(canvasObject.transform, false);
        Stretch((RectTransform)dialogueRoot.transform);

        dimImage = MakeImage(dialogueRoot.transform, "Dim", screenDimColor);
        Stretch(dimImage.rectTransform);

        Transform stack = BuildBoxStack(dialogueRoot.transform);

        boxImage = MakeBox(stack, "NPC Box", boxWidth, boxColor, borderColor, new RectOffset(30, 30, 22, 26), 8f, out boxBorder);
        nameText = MakeText(boxImage.transform, "Name", nameSize, nameColor, TextAnchor.UpperLeft);
        lineText = MakeText(boxImage.transform, "Line", lineSize, lineColor, TextAnchor.UpperLeft);
        lineText.supportRichText = true;

        choicesBoxImage = MakeBox(stack, "Choices Box", choicesBoxWidth, choicesBoxColor, choicesBorderColor, new RectOffset(16, 16, 14, 14), 2f, out choicesBorder);
        choicesParent = choicesBoxImage.rectTransform;
        choicesGroup = choicesBoxImage.gameObject.AddComponent<CanvasGroup>();
        choicesGroup.blocksRaycasts = false;

        dialogueRoot.SetActive(false);
    }

    // Bottom-centred column holding the NPC box above the choices box
    private Transform BuildBoxStack(Transform parent)
    {
        var stack = new GameObject("Boxes", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        stack.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)stack.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 40f);
        rect.sizeDelta = new Vector2(Mathf.Max(boxWidth, choicesBoxWidth), 0f);

        VerticalLayoutGroup layout = stack.GetComponent<VerticalLayoutGroup>();
        layout.spacing = boxGap;
        layout.childAlignment = TextAnchor.LowerCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        stack.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return stack.transform;
    }

    private Image MakeBox(Transform parent, string objectName, float width, Color color, Color border,
                          RectOffset padding, float spacing, out Image[] edges)
    {
        Image box = MakeImage(parent, objectName, color);
        box.rectTransform.sizeDelta = new Vector2(width, 0f);

        const float thickness = 2f;
        edges = new[]
        {
            MakeEdge(box.transform, "Border Top",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, thickness), border),
            MakeEdge(box.transform, "Border Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, thickness), border),
            MakeEdge(box.transform, "Border Left",   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(thickness, 0f), border),
            MakeEdge(box.transform, "Border Right",  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(thickness, 0f), border),
        };

        VerticalLayoutGroup layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = padding;
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return box;
    }

    private static Image MakeEdge(Transform box, string objectName, Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 pivot, Vector2 size, Color color)
    {
        Image edge = MakeImage(box, objectName, color);
        edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        RectTransform rect = edge.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        return edge;
    }

    private void MakeRow(string label)
    {
        var row = new GameObject("Choice", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(choicesParent, false);

        Image back = row.GetComponent<Image>();
        back.color = Color.clear;
        back.raycastTarget = false;

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 6, 6);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        row.GetComponent<LayoutElement>().minHeight = choiceSize + 16f;

        Text text = MakeText(row.transform, "Text", choiceSize, choiceColor, TextAnchor.MiddleLeft);
        text.text = label;

        rows.Add((RectTransform)row.transform);
        rowBacks.Add(back);
        rowTexts.Add(text);
        rowShadows.Add(text.GetComponent<Shadow>());
    }

    private void ClearRows()
    {
        foreach (RectTransform row in rows)
        {
            if (row != null) Destroy(row.gameObject);
        }

        rows.Clear();
        rowBacks.Clear();
        rowTexts.Clear();
        rowShadows.Clear();
    }

    private Text MakeText(Transform parent, string objectName, int size, Color color, TextAnchor alignment)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text), typeof(Shadow));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;

        Shadow shadow = textObject.GetComponent<Shadow>();
        shadow.effectColor = shadowColor;
        shadow.effectDistance = new Vector2(2f, -2f);

        return text;
    }

    private static Image MakeImage(Transform parent, string objectName, Color color)
    {
        var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}