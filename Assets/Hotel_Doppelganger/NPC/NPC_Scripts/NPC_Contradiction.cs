using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Catching a lie: two characters side by side, each with what they just said. Details in [brackets] are underlined
// in the sentence. Clicking one pins a red string to it, and clicking the detail in the other statement that can't
// also be true pins the other end there and stamps the screen, then fires On Solved (e.g. Intro Cutscene > Continue).
// A wrong pair drops the string and Salem doubts himself
[AddComponentMenu("Game/Puzzle/Contradiction")]
public class Contradiction : MonoBehaviour
{
    [Serializable]
    public class Statement
    {
        [DisplayAs("Speaker")]
        public string speaker;

        [DisplayAs("Portrait (optional)")]
        public Sprite portrait;

        [DisplayAs("Statement (put [brackets] around details to point at)", Lines = 4)]
        public string text;

        // Must match one of the bracketed details; capitals don't matter
        [DisplayAs("The Detail That Gives It Away")]
        public string answer;

        public Statement() { }

        public Statement(string speaker, string text, string answer)
        {
            this.speaker = speaker;
            this.text = text;
            this.answer = answer;
        }
    }

    // One laid out word, or part of one when a detail runs straight into punctuation
    private sealed class Token
    {
        public Text Text;
        public Image Underline;
        public int Detail = -1;
        public Vector2 Home;
    }

    // How one detail moves: it pops in, drifts a little on its own rhythm, and grows when it's pointed at
    private sealed class DetailMotion
    {
        public Vector2 Center;
        public float Rhythm;
        public float Delay;
        public float Scale = 1f;
    }

    private sealed class Side
    {
        public Statement Source;
        public readonly List<string> Details = new List<string>();
        public readonly List<Token> Tokens = new List<Token>();
        public readonly List<DetailMotion> Motions = new List<DetailMotion>();
        public Image Portrait;
        public Text Name;
        public RectTransform Words;
        public LayoutElement WordsSize;
    }

    private enum Phase
    {
        Picking,
        Solved,
        Dropping
    }

    private const int SortingOrder = 260;
    private const string SalemName = "Salem";
    private const float SideWidth = 760f;
    private const float SideGap = 80f;
    private const float WordsWidth = SideWidth - 60f;
    private const float LineSpacing = 1.3f;
    private const float UnderlineThickness = 2f;
    private const float LitUnderlineThickness = 3f;

    private const int StringSegments = 24;
    private const float StringThickness = 4f;
    private const float PinSize = 14f;
    private const float Slack = 0.18f;
    private const float Gravity = 2600f;
    private const float DropTime = 0.7f;

    private const float StampDelay = 0.3f;
    private const float StampTime = 0.16f;
    private const float StampAngle = -8f;
    private const float StampStartScale = 2.6f;
    private const int StampTextSize = 96;
    private const float ContinueDelay = 0.3f;
    private const string ContinueHint = "Click to continue";

    private const float PopDelay = 0.35f;
    private const float PopStagger = 0.09f;
    private const float PopTime = 0.35f;
    private const float HoverScale = 1.18f;
    private const float PickedScale = 1.1f;
    private const float ScaleSpeed = 14f;
    private const float LiftPerScale = 30f;
    private const float DriftSpeed = 1.6f;
    private const float DriftX = 1.5f;
    private const float DriftY = 2.5f;

    private const float FeedbackTime = 4f;
    private const float ShakeTime = 0.35f;
    private const float WrongShake = 12f;
    private const float StampShake = 24f;

    private static RectOffset BoxPadding => new RectOffset(30, 30, 22, 22);

    [Header("Statements")]
    [SerializeField]
    private Statement left = new Statement("Helena",
        "My boyfriend doesn't want to buy me the new private island for sale! When it opened at [7 pm today], I made a fool of myself, screaming for joy [at the bar]. But now [he won't get it]!",
        "at the bar");

    [SerializeField]
    private Statement right = new Statement("James",
        "Helena, you're forgetting that [my money can't be used right now]. And I wasn't going to get it after all the [pool guests] heard your screaming. It was [too embarrassing].",
        "pool guests");

    [Header("Text")]
    [DisplayAs("Title")]
    [SerializeField] private string title = "Something doesn't add up";

    [DisplayAs("Instructions", Lines = 2)]
    [SerializeField] private string howTo = "Click a detail to pin the string to it, then click the detail in the other statement that can't also be true.";

    [DisplayAs("Stamp")]
    [SerializeField] private string stampText = "CONTRADICTION";

    [DisplayAs("Salem Realises", Lines = 2)]
    [SerializeField] private string realisation = "(She was screaming at the bar... so how did the guests at the pool hear her?)";

    // Used in turn after each wrong pair. {0} and {1} become the two details that were picked
    [DisplayAs("Salem Doubts Himself")]
    [SerializeField]
    private List<string> doubts = new List<string>
    {
        "(\"{0}\"... and \"{1}\"? No. Both of those could be true.)",
        "(That doesn't prove anything. Think, Salem.)",
        "(No... those fit together fine. Something else doesn't.)"
    };

    [Foldout("Events")]
    public UnityEvent onSolved;

    public UnityEvent onWrong;

    [Foldout("Look")]
    [DisplayAs("Match NPC Dialogue Box")]
    [SerializeField] private bool matchDialogueBox = true;

    [DisplayAs("Font (optional)", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Font font;

    [DisplayAs("Box Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color boxColor = new Color(0f, 0f, 0f, 0.92f);

    [DisplayAs("Box Border", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color borderColor = new Color(0.85f, 0.83f, 0.78f, 0.5f);

    [DisplayAs("Name Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color nameColor = new Color(0.45f, 0.44f, 0.41f);

    [DisplayAs("Text Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color textColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Detail Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color detailColor = new Color(0.82f, 0.45f, 0.32f);

    [DisplayAs("Picked Detail Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color pickedColor = new Color(1f, 0.45f, 0.35f);

    [DisplayAs("Text Shadow Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [DisplayAs("Screen Shade")]
    [SerializeField] private Color shadeColor = new Color(0f, 0f, 0f, 0.6f);

    [DisplayAs("String And Stamp Color")]
    [SerializeField] private Color stringColor = new Color(0.78f, 0.1f, 0.1f);

    [Foldout("Sizes (as on a 1080p screen)")]
    [DisplayAs("Portrait Height", Min = 100f)]
    [SerializeField] private float portraitHeight = 440f;

    [DisplayAs("Statement Text Size", Min = 1f)]
    [SerializeField] private int textSize = 26;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Player (optional)")]
    [SerializeField] private FirstPersonController player;

    [DisplayAs("Player Dialogue (optional)")]
    [SerializeField] private PlayerDialogue dialogue;

    private readonly Side leftSide = new Side();
    private readonly Side rightSide = new Side();
    private readonly List<Image> borders = new List<Image>();
    private readonly List<Image> stampBorders = new List<Image>();
    private readonly List<Shadow> shadows = new List<Shadow>();
    private readonly List<Image> segments = new List<Image>();
    private readonly Vector3[] corners = new Vector3[4];

    private Canvas canvas;
    private RectTransform content;
    private Image shade;
    private Text titleText;
    private Text instructionsText;
    private Text feedbackText;
    private Text measure;
    private RectTransform stringLayer;
    private Image startPin;
    private Image endPin;
    private RectTransform stamp;
    private Text stampLabel;

    private IntroCutscene coveredCutscene;
    private Font activeFont;
    private Color wordColor;
    private Color activeDetailColor;
    private Color activePickedColor;
    private Color activeShadowColor;

    private Phase phase;
    private Side pinnedSide;
    private int pinnedDetail = -1;
    private Side hoverSide;
    private int hoverDetail = -1;
    private Side targetSide;
    private int targetDetail = -1;
    private Vector2 dropStart;
    private Vector2 dropEnd;
    private Vector2 dropVelocity;
    private float phaseTime;
    private float openTime;
    private bool stampLanded;
    private int doubtIndex;
    private float feedbackLeft;
    private float shakeLeft;
    private float shakeStrength;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (player == null) player = FindAnyObjectByType<FirstPersonController>();
        if (dialogue == null) dialogue = FindAnyObjectByType<PlayerDialogue>();
        BuildUI();
    }

    private void OnValidate()
    {
        if (canvas == null) return;

        ApplyLook();
        Recolor();
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        if (!IsOpen) return;

        HoldGameplay();
        float dt = Time.unscaledDeltaTime;
        phaseTime += dt;
        openTime += dt;
        UpdateEffects(dt);
        AnimateDetails(dt);

        switch (phase)
        {
            case Phase.Solved:
                UpdateSolved();
                break;
            case Phase.Dropping:
                UpdateDrop(dt);
                break;
            default:
                UpdatePicking(Mouse.current);
                break;
        }
    }

    // Shows the two statements with nothing picked. Hook it to Intro Cutscene > On Waiting, or any other event
    public void Open()
    {
        if (IsOpen) return;

        canvas.gameObject.SetActive(true);
        IsOpen = true;
        ApplyLook();

        // Inside the intro cutscene, Salem's thoughts go in its text box, which starts empty instead of
        // still showing the line it stopped on
        coveredCutscene = IntroCutscene.IsPlaying ? FindAnyObjectByType<IntroCutscene>() : null;
        if (coveredCutscene != null) coveredCutscene.ShowAside(SalemName, string.Empty);

        phase = Phase.Picking;
        pinnedSide = hoverSide = targetSide = null;
        pinnedDetail = hoverDetail = targetDetail = -1;
        doubtIndex = 0;
        openTime = 0f;
        feedbackText.text = string.Empty;
        feedbackLeft = 0f;
        shakeLeft = 0f;
        stamp.gameObject.SetActive(false);
        ShowString(false);

        Prepare(leftSide, left);
        Prepare(rightSide, right);
        SetUpMotions();
        Recolor();
        AnimateDetails(0f);
    }

    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        canvas.gameObject.SetActive(false);

        if (coveredCutscene != null) coveredCutscene.EndAside();
        coveredCutscene = null;

        // On its own (not inside the intro cutscene) it hands the controls back itself
        if (IntroCutscene.IsPlaying) return;
        if (player != null) player.InputLocked = false;
        FirstPersonController.SetCursorLocked(true);
    }

    private void Finish()
    {
        Close();
        onSolved?.Invoke();
    }

    private void UpdatePicking(Mouse mouse)
    {
        if (mouse == null) return;

        Vector2 point = mouse.position.ReadValue();
        FindDetailAt(point, out Side side, out int detail);
        if (side != hoverSide || detail != hoverDetail)
        {
            hoverSide = side;
            hoverDetail = detail;
            Recolor();
        }

        if (mouse.leftButton.wasPressedThisFrame) Click(side, detail);
        else if (mouse.rightButton.wasPressedThisFrame) Unpin();

        if (phase != Phase.Picking || pinnedSide == null) return;

        Vector2 from = DetailPoint(pinnedSide, pinnedDetail);
        Vector2 to = ToLocal(point);
        DrawString(from, to, Vector2.Distance(from, to) * Slack, 1f);
    }

    // Clicking a detail pins the string to it, clicking the same one again lets go, and a detail on the other side is the guess
    private void Click(Side side, int detail)
    {
        if (side == null)
        {
            Unpin();
            return;
        }

        if (pinnedSide != null && pinnedSide != side)
        {
            Judge(side, detail);
            return;
        }

        if (pinnedSide == side && pinnedDetail == detail) Unpin();
        else Pin(side, detail);
    }

    private void Pin(Side side, int detail)
    {
        pinnedSide = side;
        pinnedDetail = detail;
        ShowString(true);
        Recolor();
    }

    private void Unpin()
    {
        pinnedSide = null;
        pinnedDetail = -1;
        ShowString(false);
        Recolor();
    }

    private void Judge(Side side, int detail)
    {
        targetSide = side;
        targetDetail = detail;
        hoverSide = null;
        hoverDetail = -1;
        phaseTime = 0f;

        if (IsAnswer(side, detail) && IsAnswer(pinnedSide, pinnedDetail))
        {
            phase = Phase.Solved;
            stampLanded = false;
            Recolor();
            return;
        }

        string leftPick = side == leftSide ? side.Details[detail] : pinnedSide.Details[pinnedDetail];
        string rightPick = side == rightSide ? side.Details[detail] : pinnedSide.Details[pinnedDetail];
        SayDoubt(leftPick.Trim(), rightPick.Trim());
        Shake(WrongShake);
        onWrong?.Invoke();

        // The far end comes loose and falls, then the string is gone
        dropStart = DetailPoint(pinnedSide, pinnedDetail);
        dropEnd = DetailPoint(side, detail);
        dropVelocity = Vector2.zero;
        phase = Phase.Dropping;
        pinnedSide = null;
        pinnedDetail = -1;
        Recolor();
    }

    // The string stays pinned between the two details while the stamp slams down and Salem says what he's worked out
    private void UpdateSolved()
    {
        Vector2 from = DetailPoint(pinnedSide, pinnedDetail);
        Vector2 to = DetailPoint(targetSide, targetDetail);
        DrawString(from, to, Vector2.Distance(from, to) * Slack, 1f);

        float stampProgress = (phaseTime - StampDelay) / StampTime;
        if (stampProgress >= 0f)
        {
            float t = Mathf.Clamp01(stampProgress);
            stamp.gameObject.SetActive(true);
            stamp.localScale = Vector3.one * Mathf.Lerp(StampStartScale, 1f, t * t);
            SetStampAlpha(t);

            if (t >= 1f && !stampLanded)
            {
                stampLanded = true;
                Shake(StampShake);
                Say(realisation, true);
            }
        }

        // Waits for a click like the rest of the intro; the first click finishes Salem's line if it's still typing
        if (!stampLanded || phaseTime < StampDelay + StampTime + ContinueDelay || !ContinuePressed()) return;

        if (coveredCutscene != null && coveredCutscene.AsideTyping) coveredCutscene.FinishAsideTyping();
        else Finish();
    }

    private void UpdateDrop(float dt)
    {
        dropVelocity += Vector2.down * (Gravity * dt);
        dropEnd += dropVelocity * dt;
        float fade = 1f - Mathf.Clamp01(phaseTime / DropTime);
        DrawString(dropStart, dropEnd, Vector2.Distance(dropStart, dropEnd) * Slack, fade);

        if (phaseTime < DropTime) return;

        ShowString(false);
        phase = Phase.Picking;
    }

    private bool IsAnswer(Side side, int detail) =>
        side != null && detail >= 0 && detail < side.Details.Count
        && string.Equals(side.Details[detail].Trim(), side.Source.answer?.Trim(), StringComparison.OrdinalIgnoreCase);

    private void SayDoubt(string leftPick, string rightPick)
    {
        if (doubts == null || doubts.Count == 0) return;

        string line = doubts[doubtIndex % doubts.Count] ?? string.Empty;
        doubtIndex++;
        Say(line.Replace("{0}", leftPick).Replace("{1}", rightPick));
    }

    // Salem's line for the solve stays up until the player clicks on
    private void Say(string line, bool waitForClick = false)
    {
        if (waitForClick && coveredCutscene == null) instructionsText.text = ContinueHint;
        if (string.IsNullOrWhiteSpace(line)) return;

        if (coveredCutscene != null)
        {
            coveredCutscene.ShowAside(SalemName, line, waitForClick);
            return;
        }

        feedbackText.text = line;
        feedbackLeft = waitForClick ? float.MaxValue : FeedbackTime;
    }

    private static bool ContinuePressed()
    {
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        return (mouse != null && mouse.leftButton.wasPressedThisFrame)
               || (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame
                                        || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame));
    }

    private void Shake(float strength)
    {
        shakeStrength = strength;
        shakeLeft = ShakeTime;
    }

    private void UpdateEffects(float dt)
    {
        if (feedbackLeft > 0f)
        {
            feedbackLeft -= dt;
            Color color = feedbackText.color;
            color.a = Mathf.Clamp01(feedbackLeft / 0.4f);
            feedbackText.color = color;
        }

        Vector2 offset = Vector2.zero;
        if (shakeLeft > 0f)
        {
            shakeLeft = Mathf.Max(0f, shakeLeft - dt);
            float strength = shakeStrength * (shakeLeft / ShakeTime);
            float t = Time.unscaledTime * 40f;
            offset = new Vector2(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f) * (2f * strength);
        }
        content.anchoredPosition = offset;
    }

    private void HoldGameplay()
    {
        if (player != null) player.InputLocked = true;
        FirstPersonController.SetCursorLocked(false);
    }

    // Splits out the bracketed details, then lays the statement out word by word so each detail can be clicked where it is
    private void Prepare(Side side, Statement statement)
    {
        side.Source = statement;
        side.Details.Clear();

        var plain = new System.Text.StringBuilder();
        var owners = new List<int>();
        int owner = -1;
        foreach (char letter in statement.text ?? string.Empty)
        {
            if (letter == '[')
            {
                owner = side.Details.Count;
                side.Details.Add(string.Empty);
                continue;
            }
            if (letter == ']')
            {
                owner = -1;
                continue;
            }

            plain.Append(letter);
            owners.Add(owner);
            if (owner >= 0) side.Details[owner] += letter;
        }

        LayoutWords(side, plain.ToString(), owners);

        side.Portrait.sprite = statement.portrait;
        side.Portrait.gameObject.SetActive(statement.portrait != null);
        side.Name.text = (statement.speaker ?? string.Empty).ToUpper();
    }

    // Wraps by whole words. A word can hold several tokens, e.g. "bar" (a detail) and the full stop after it
    private void LayoutWords(Side side, string plain, List<int> owners)
    {
        foreach (Token old in side.Tokens)
        {
            old.Text.gameObject.SetActive(false);
            Destroy(old.Text.gameObject);
        }
        side.Tokens.Clear();

        float space = Measure("a a") - Measure("aa");
        float lineHeight = Mathf.Ceil(MeasureHeight("Ay") * LineSpacing);
        float x = 0f;
        float y = 0f;
        Token last = null;
        var parts = new List<int>();

        int start = 0;
        while (start < plain.Length)
        {
            if (char.IsWhiteSpace(plain[start]))
            {
                start++;
                continue;
            }

            int end = start;
            while (end < plain.Length && !char.IsWhiteSpace(plain[end])) end++;

            // Where the owner changes inside the word, a new token starts
            parts.Clear();
            parts.Add(start);
            for (int i = start + 1; i < end; i++)
            {
                if (owners[i] != owners[i - 1]) parts.Add(i);
            }
            parts.Add(end);

            float width = 0f;
            for (int i = 0; i < parts.Count - 1; i++) width += Measure(plain.Substring(parts[i], parts[i + 1] - parts[i]));

            if (x > 0f && x + space + width > WordsWidth)
            {
                x = 0f;
                y += lineHeight;
                last = null;
            }
            else if (x > 0f)
            {
                // A detail of several words gets one unbroken underline
                if (last != null && last.Detail >= 0 && last.Detail == owners[start]) Widen(last, space);
                x += space;
            }

            for (int i = 0; i < parts.Count - 1; i++)
            {
                string piece = plain.Substring(parts[i], parts[i + 1] - parts[i]);
                float pieceWidth = Measure(piece);
                last = CreateToken(side, piece, owners[parts[i]], x, y, pieceWidth, lineHeight);
                x += pieceWidth;
            }

            start = end;
        }

        measure.text = string.Empty;
        side.WordsSize.minHeight = y + lineHeight;
        side.WordsSize.preferredHeight = y + lineHeight;
    }

    private Token CreateToken(Side side, string piece, int detail, float x, float y, float width, float height)
    {
        Text label = RuntimeUI.CreateText("Word", side.Words, activeFont, textSize, wordColor, TextAnchor.MiddleLeft, activeShadowColor, out _);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.text = piece;

        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width + 1f, height);

        var token = new Token { Text = label, Detail = detail, Home = rect.anchoredPosition };
        if (detail >= 0)
        {
            token.Underline = RuntimeUI.CreateImage("Underline", rect, activeDetailColor);
            RectTransform line = token.Underline.rectTransform;
            line.anchorMin = line.anchorMax = line.pivot = Vector2.zero;
            line.anchoredPosition = new Vector2(0f, 3f);
            line.sizeDelta = new Vector2(width, UnderlineThickness);
        }

        side.Tokens.Add(token);
        return token;
    }

    private static void Widen(Token token, float extra)
    {
        RectTransform line = token.Underline.rectTransform;
        line.sizeDelta = new Vector2(line.sizeDelta.x + extra, line.sizeDelta.y);
    }

    private float Measure(string piece)
    {
        measure.text = piece;
        return measure.preferredWidth;
    }

    private float MeasureHeight(string piece)
    {
        measure.text = piece;
        return measure.preferredHeight;
    }

    // Each detail scales around its own middle, and they pop in one after another, left statement first
    private void SetUpMotions()
    {
        int order = 0;
        foreach (Side side in new[] { leftSide, rightSide })
        {
            side.Motions.Clear();
            for (int detail = 0; detail < side.Details.Count; detail++)
            {
                var motion = new DetailMotion
                {
                    Rhythm = detail * 1.7f + (side == rightSide ? 0.9f : 0f),
                    Delay = PopDelay + order * PopStagger
                };
                order++;

                float minX = float.MaxValue;
                float maxX = float.MinValue;
                float top = float.MinValue;
                float bottom = float.MaxValue;
                foreach (Token token in side.Tokens)
                {
                    if (token.Detail != detail) continue;

                    Vector2 size = token.Text.rectTransform.sizeDelta;
                    minX = Mathf.Min(minX, token.Home.x);
                    maxX = Mathf.Max(maxX, token.Home.x + size.x);
                    top = Mathf.Max(top, token.Home.y);
                    bottom = Mathf.Min(bottom, token.Home.y - size.y);
                }

                if (minX <= maxX) motion.Center = new Vector2((minX + maxX) * 0.5f, (top + bottom) * 0.5f);
                side.Motions.Add(motion);
            }
        }
    }

    private void AnimateDetails(float dt)
    {
        float blend = 1f - Mathf.Exp(-ScaleSpeed * dt);
        foreach (Side side in new[] { leftSide, rightSide })
        {
            for (int detail = 0; detail < side.Motions.Count; detail++)
            {
                DetailMotion motion = side.Motions[detail];
                float goal = side == hoverSide && detail == hoverDetail ? HoverScale : IsLit(side, detail) ? PickedScale : 1f;
                motion.Scale = Mathf.Lerp(motion.Scale, goal, blend);
            }

            foreach (Token token in side.Tokens)
            {
                if (token.Detail < 0 || token.Detail >= side.Motions.Count) continue;

                DetailMotion motion = side.Motions[token.Detail];
                float scale = motion.Scale * PopCurve((openTime - motion.Delay) / PopTime);
                float time = Time.unscaledTime * DriftSpeed + motion.Rhythm;
                Vector2 drift = new Vector2(Mathf.Cos(time * 0.8f) * DriftX, Mathf.Sin(time) * DriftY + (motion.Scale - 1f) * LiftPerScale);

                RectTransform rect = token.Text.rectTransform;
                rect.localScale = Vector3.one * scale;
                rect.anchoredPosition = motion.Center + (token.Home - motion.Center) * scale + drift;
            }
        }
    }

    // Grows past full size and settles back, so each detail pops
    private static float PopCurve(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;

        const float overshoot = 1.70158f;
        float u = t - 1f;
        return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
    }

    private void FindDetailAt(Vector2 point, out Side side, out int detail)
    {
        foreach (Side candidate in new[] { leftSide, rightSide })
        {
            foreach (Token token in candidate.Tokens)
            {
                if (token.Detail < 0 || !RuntimeUI.Contains(token.Text.rectTransform, point)) continue;

                side = candidate;
                detail = token.Detail;
                return;
            }
        }

        side = null;
        detail = -1;
    }

    // Under the middle of a detail, which may run over two lines, in the string layer's space
    private Vector2 DetailPoint(Side side, int detail)
    {
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float bottom = float.MaxValue;

        foreach (Token token in side.Tokens)
        {
            if (token.Detail != detail) continue;

            token.Text.rectTransform.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 local = stringLayer.InverseTransformPoint(corner);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                bottom = Mathf.Min(bottom, local.y);
            }
        }

        return minX > maxX ? Vector2.zero : new Vector2((minX + maxX) * 0.5f, bottom + 2f);
    }

    // The canvas draws over the screen, so screen points are its world points
    private Vector2 ToLocal(Vector2 screenPoint) => stringLayer.InverseTransformPoint(new Vector3(screenPoint.x, screenPoint.y, 0f));

    private void ShowString(bool show) => stringLayer.gameObject.SetActive(show);

    // A sagging curve built from short straight pieces, with a pin at each end
    private void DrawString(Vector2 from, Vector2 to, float sag, float alpha)
    {
        ShowString(true);
        Color color = new Color(stringColor.r, stringColor.g, stringColor.b, stringColor.a * alpha);

        Vector2 previous = from;
        for (int i = 0; i < segments.Count; i++)
        {
            float t = (i + 1f) / segments.Count;
            Vector2 next = Vector2.Lerp(from, to, t) + Vector2.down * (sag * 4f * t * (1f - t));
            Vector2 step = next - previous;

            RectTransform rect = segments[i].rectTransform;
            rect.anchoredPosition = previous;
            rect.sizeDelta = new Vector2(step.magnitude + 1f, StringThickness);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(step.y, step.x) * Mathf.Rad2Deg);
            segments[i].color = color;
            previous = next;
        }

        Color pinColor = new Color(stringColor.r * 0.6f, stringColor.g * 0.6f, stringColor.b * 0.6f, alpha);
        startPin.rectTransform.anchoredPosition = from;
        endPin.rectTransform.anchoredPosition = to;
        startPin.color = pinColor;
        endPin.color = pinColor;
    }

    private void SetStampAlpha(float alpha)
    {
        Color text = stampLabel.color;
        text.a = alpha;
        stampLabel.color = text;
        foreach (Image edge in stampBorders) edge.color = new Color(stringColor.r, stringColor.g, stringColor.b, alpha);
    }

    // Details are tinted and underlined, and the one under the mouse or pinned stands out
    private void Recolor()
    {
        foreach (Side side in new[] { leftSide, rightSide })
        {
            foreach (Token token in side.Tokens)
            {
                if (token.Detail < 0)
                {
                    token.Text.color = wordColor;
                    continue;
                }

                bool lit = IsLit(side, token.Detail);
                Color color = lit ? activePickedColor : activeDetailColor;
                token.Text.color = color;
                token.Underline.color = color;

                RectTransform line = token.Underline.rectTransform;
                line.sizeDelta = new Vector2(line.sizeDelta.x, lit ? LitUnderlineThickness : UnderlineThickness);
            }
        }
    }

    private bool IsLit(Side side, int detail) =>
        (side == pinnedSide && detail == pinnedDetail)
        || (side == hoverSide && detail == hoverDetail)
        || (phase == Phase.Solved && side == targetSide && detail == targetDetail);

    private void BuildUI()
    {
        canvas = RuntimeUI.CreateCanvas("Contradiction UI", SortingOrder);

        shade = RuntimeUI.CreateImage("Shade", canvas.transform, shadeColor);
        RuntimeUI.Stretch(shade.rectTransform);

        // Everything that shakes
        content = RuntimeUI.CreateRect("Content", canvas.transform);
        RuntimeUI.Stretch(content);

        titleText = CreateText("Title", content, 40, TextAnchor.MiddleCenter);
        PlaceAcross(titleText.rectTransform, 1f, -70f);
        instructionsText = CreateText("Instructions", content, 24, TextAnchor.MiddleCenter);
        PlaceAcross(instructionsText.rectTransform, 1f, -122f);
        feedbackText = CreateText("Salem", content, 30, TextAnchor.MiddleCenter);
        feedbackText.fontStyle = FontStyle.Italic;
        PlaceAcross(feedbackText.rectTransform, 0f, 70f);

        // Only used to measure words, so it's never seen
        measure = CreateText("Measure", content, textSize, TextAnchor.MiddleLeft);
        measure.horizontalOverflow = HorizontalWrapMode.Overflow;
        measure.color = Color.clear;

        Image row = RuntimeUI.CreateRow("Sides", content, 0f, new RectOffset(0, 0, 0, 0), SideGap);
        row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
        RectTransform rowRect = row.rectTransform;
        rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(0f, 10f);
        var fit = row.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildSide(leftSide, row.transform);
        BuildSide(rightSide, row.transform);
        BuildString();
        BuildStamp();

        ApplyLook();
        canvas.gameObject.SetActive(false);
    }

    // A portrait with a box under it holding the name and the statement
    private void BuildSide(Side side, Transform parent)
    {
        RectTransform column = RuntimeUI.CreateRect("Side", parent);
        RuntimeUI.FixedWidth(column, SideWidth);
        var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.LowerCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        side.Portrait = RuntimeUI.CreateImage("Portrait", column, Color.white);
        side.Portrait.preserveAspect = true;
        var portraitSize = side.Portrait.gameObject.AddComponent<LayoutElement>();
        portraitSize.minHeight = portraitHeight;
        portraitSize.preferredHeight = portraitHeight;

        Image box = RuntimeUI.CreateBox("Statement", column, SideWidth, boxColor, borderColor, BoxPadding, 10f, borders);
        side.Name = CreateText("Name", box.transform, 22, TextAnchor.UpperLeft);
        side.Words = RuntimeUI.CreateRect("Words", box.transform);
        side.WordsSize = side.Words.gameObject.AddComponent<LayoutElement>();
    }

    // Drawn above the statements, centered so its points line up with the words' positions
    private void BuildString()
    {
        stringLayer = RuntimeUI.CreateRect("String", content);
        RuntimeUI.Stretch(stringLayer);

        for (int i = 0; i < StringSegments; i++)
        {
            Image segment = RuntimeUI.CreateImage("Piece", stringLayer, stringColor);
            RectTransform rect = segment.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            segments.Add(segment);
        }

        startPin = CreatePin("Start Pin");
        endPin = CreatePin("End Pin");
        stringLayer.gameObject.SetActive(false);
    }

    private Image CreatePin(string name)
    {
        Image pin = RuntimeUI.CreateImage(name, stringLayer, stringColor);
        RectTransform rect = pin.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(PinSize, PinSize);
        rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        return pin;
    }

    // A tilted, framed word in the middle of the screen, like a rubber stamp
    private void BuildStamp()
    {
        Image frame = RuntimeUI.CreateBox("Stamp", content, 0f, Color.clear, stringColor, new RectOffset(40, 40, 8, 8), 0f, stampBorders);
        stamp = frame.rectTransform;
        stamp.anchorMin = stamp.anchorMax = stamp.pivot = new Vector2(0.5f, 0.5f);
        stamp.anchoredPosition = new Vector2(0f, 40f);
        stamp.localRotation = Quaternion.Euler(0f, 0f, StampAngle);
        var fit = frame.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        stampLabel = CreateText("Word", stamp, StampTextSize, TextAnchor.MiddleCenter);
        stampLabel.fontStyle = FontStyle.Bold;
        stampLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        stamp.gameObject.SetActive(false);
    }

    private static void PlaceAcross(RectTransform rect, float anchorY, float y)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, anchorY);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(1600f, 50f);
    }

    private Text CreateText(string name, Transform parent, int size, TextAnchor alignment)
    {
        Text text = RuntimeUI.CreateText(name, parent, font, size, textColor, alignment, shadowColor, out Shadow shadow);
        shadows.Add(shadow);
        return text;
    }

    // The NPC dialogue box's look when matching it, otherwise this component's own values
    private void ApplyLook()
    {
        bool match = matchDialogueBox && dialogue != null;
        activeFont = match ? dialogue.BoxFont : font;
        if (activeFont == null) activeFont = RuntimeUI.DefaultFont;

        Color box = match ? dialogue.BoxColor : boxColor;
        Color border = match ? dialogue.BoxBorderColor : borderColor;
        Color names = match ? dialogue.NameColor : nameColor;
        wordColor = match ? dialogue.LineColor : textColor;
        activeDetailColor = match ? dialogue.ChoiceColor : detailColor;
        activePickedColor = match ? dialogue.HighlightColor : pickedColor;
        activeShadowColor = match ? dialogue.ShadowColor : shadowColor;

        shade.color = shadeColor;
        foreach (Image edge in borders) edge.color = border;
        foreach (Shadow shadow in shadows) shadow.effectColor = activeShadowColor;

        titleText.text = title.ToUpper();
        titleText.color = activePickedColor;
        instructionsText.text = howTo;
        instructionsText.color = names;
        feedbackText.color = new Color(wordColor.r, wordColor.g, wordColor.b, feedbackText.color.a);
        measure.fontSize = textSize;
        stampLabel.text = stampText;
        stampLabel.color = new Color(stringColor.r, stringColor.g, stringColor.b, stampLabel.color.a);

        foreach (Side side in new[] { leftSide, rightSide })
        {
            side.Words.parent.GetComponent<Image>().color = box;
            side.Name.color = names;
            side.Name.font = activeFont;
            foreach (Token token in side.Tokens)
            {
                token.Text.font = activeFont;
                token.Text.GetComponent<Shadow>().effectColor = activeShadowColor;
            }
        }

        foreach (Text text in new[] { titleText, instructionsText, feedbackText, measure, stampLabel }) text.font = activeFont;
    }
}