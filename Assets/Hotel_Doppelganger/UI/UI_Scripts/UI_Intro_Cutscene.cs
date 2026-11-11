using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// A 2D visual-novel scene that plays when the game starts: a full-screen background, a talking portrait,
// a name plate and a typed text box, with suspect profile cards along the way.
// Words wrapped in *asterisks* are highlighted. Click, Space, E or Enter moves on, and Tab skips.
// A line can hold the scene until something else (like a Contradiction) calls Continue
[AddComponentMenu("Game/UI/Intro Cutscene")]
public class IntroCutscene : MonoBehaviour
{
    public enum StartMode
    {
        [InspectorName("After Main Menu Start")] AfterMainMenu,
        [InspectorName("When Scene Starts")] OnSceneStart,
        [InspectorName("Only When Play Is Called")] Manually
    }

    public enum Effect
    {
        None,
        Shake,
        Flash,
        [InspectorName("Shake + Flash")] ShakeAndFlash
    }

    private enum State
    {
        Idle,
        FadingIn,
        Reading,
        Waiting,
        Ending
    }

    [Serializable]
    public class Character
    {
        [DisplayAs("Name")]
        public string name;

        [DisplayAs("Portrait")]
        public Sprite portrait;

        [DisplayAs("Talking Portrait (optional)")]
        public Sprite talkingPortrait;

        public Character() { }

        public Character(string name) => this.name = name;
    }

    [Serializable]
    public class Line
    {
        [DisplayAs("Text", Lines = 3)]
        public string text;

        [DisplayAs("Speaker (blank = narration)")]
        public string speaker;

        [DisplayAs("Expression (optional)")]
        public Sprite expression;

        [DisplayAs("New Background (optional)")]
        public Sprite background;

        [DisplayAs("Show Profile (optional)")]
        public string profile;

        [DisplayAs("Effect")]
        public Effect effect;

        // The scene stops after this line and fires On Waiting, then carries on when Continue is called
        [DisplayAs("Wait Here For An Event")]
        public bool waitHere;

        public Line() { }

        public Line(string speaker, string text, string profile = null, Effect effect = Effect.None)
        {
            this.speaker = speaker;
            this.text = text;
            this.profile = profile;
            this.effect = effect;
        }
    }

    [Serializable]
    public class Profile
    {
        [DisplayAs("Name")]
        public string name;

        [DisplayAs("Role")]
        public string role;

        [DisplayAs("Description", Lines = 3)]
        public string description;

        [DisplayAs("Picture")]
        public Sprite picture;

        public Profile() { }

        public Profile(string name, string role, string description)
        {
            this.name = name;
            this.role = role;
            this.description = description;
        }
    }

    // Above the dialogue box and menu, below Game Complete
    private const int SortingOrder = 250;

    private const float FadeInTime = 0.6f;
    private const float FadeOutTime = 0.5f;

    // So the click on Start doesn't also skip the first line
    private const float InputDelay = 0.25f;

    private const float PortraitSlideTime = 0.25f;
    private const float PortraitSlideDistance = 60f;
    private const float CardPopTime = 0.2f;
    private const float ShakeTime = 0.35f;
    private const float ShakeStrength = 18f;
    private const float FlashTime = 0.3f;

    [Header("When To Play")]
    [DisplayAs("Start")]
    [SerializeField] private StartMode startMode = StartMode.AfterMainMenu;

    [DisplayAs("Pause Game While Playing")]
    [SerializeField] private bool pauseGame = true;

    [DisplayAs("Let Players Skip (Tab)")]
    [SerializeField] private bool canSkip = true;

    [Header("Script")]
    [DisplayAs("Starting Background (optional)")]
    [SerializeField] private Sprite startingBackground;

    [SerializeField] private List<Character> cast = DefaultCast();

    [SerializeField] private List<Line> lines = DefaultLines();

    [SerializeField] private List<Profile> profiles = DefaultProfiles();

    [Foldout("Typing")]
    [DisplayAs("Letters Per Second", Min = 1f)]
    [SerializeField] private float lettersPerSecond = 40f;

    [DisplayAs("Pause After Sentences", Min = 0f)]
    [SerializeField] private float sentencePause = 0.3f;

    [DisplayAs("Pause After Commas", Min = 0f)]
    [SerializeField] private float commaPause = 0.1f;

    [DisplayAs("Mouth Flap Time (seconds)", Min = 0.05f, Max = 0.5f)]
    [SerializeField] private float flapInterval = 0.2f;

    [Foldout("Events")]
    public UnityEvent onFinished;

    // Fires when a Wait Here line is reached, e.g. to open a Contradiction
    public UnityEvent onWaiting;

    [Foldout("Look")]
    [DisplayAs("Match NPC Dialogue Box")]
    [SerializeField] private bool matchDialogueBox = true;

    [DisplayAs("Font (optional)", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Font font;

    [DisplayAs("Text Box Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color boxColor = new Color(0f, 0f, 0f, 0.92f);

    [DisplayAs("Text Box Border", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color borderColor = new Color(0.85f, 0.83f, 0.78f, 0.5f);

    [DisplayAs("Name Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color nameColor = new Color(0.45f, 0.44f, 0.41f);

    [DisplayAs("Text Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color textColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Text Shadow Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [DisplayAs("Narration Color")]
    [SerializeField] private Color narrationColor = new Color(0.6f, 0.58f, 0.54f);

    [DisplayAs("Highlighted Words", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color highlightColor = new Color(1f, 0.45f, 0.35f);

    [DisplayAs("Background Color (no picture)")]
    [SerializeField] private Color backgroundColor = new Color(0.1f, 0.08f, 0.12f);

    [Foldout("Sizes (as on a 1080p screen)")]
    [DisplayAs("Text Box Width", Min = 100f, HideIf = nameof(matchDialogueBox))]
    [SerializeField] private float boxWidth = 1100f;

    [DisplayAs("Name Size", Min = 1f, HideIf = nameof(matchDialogueBox))]
    [SerializeField] private int nameSize = 22;

    [DisplayAs("Text Size", Min = 1f, HideIf = nameof(matchDialogueBox))]
    [SerializeField] private int textSize = 32;

    [DisplayAs("Portrait Height", Min = 100f)]
    [SerializeField] private float portraitHeight = 900f;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Player (optional)")]
    [SerializeField] private FirstPersonController player;

    [DisplayAs("Main Menu (optional)")]
    [SerializeField] private MainMenu menu;

    [DisplayAs("Player Dialogue (optional)")]
    [SerializeField] private PlayerDialogue dialogue;

    private readonly TypedLine typedLine = new TypedLine();
    private readonly List<Image> borders = new List<Image>();
    private readonly List<Shadow> shadows = new List<Shadow>();

    private Canvas canvas;
    private RectTransform stage;
    private Image background;
    private Image portrait;
    private CanvasGroup card;
    private Image cardPicture;
    private Text cardName;
    private Text cardRole;
    private Text cardDescription;
    private Text cardLabel;
    private Image box;
    private Text nameText;
    private Text lineText;
    private Image nextArrow;
    private Text hintText;
    private Image flash;
    private Image fade;

    private State state;
    private int lineIndex;
    private int visibleLetters;
    private int renderedLetters = -1;
    private float letterTimer;
    private float readyTime;
    private float previousTimeScale = 1f;
    private string highlightTag;
    private Color activeTextColor;
    private Color activeNameColor;
    private Color activeBorderColor;

    private Character speaker;
    private string shownSpeaker;
    private Sprite idleSprite;
    private Sprite talkingSprite;
    private bool mouthOpen;
    private float flapTimer;
    private float portraitTime = 1f;
    private float cardTime = 1f;
    private float shakeLeft;
    private float flashLeft;

    public static bool IsPlaying { get; private set; }

    private bool IsTyping => visibleLetters < typedLine.Length;

    private void Awake()
    {
        IsPlaying = false;
        if (player == null) player = FindAnyObjectByType<FirstPersonController>();
        if (menu == null) menu = FindAnyObjectByType<MainMenu>();
        if (dialogue == null) dialogue = FindAnyObjectByType<PlayerDialogue>();

        BuildUI();
    }

    private void OnValidate()
    {
        if (canvas != null) ApplyLook();
    }

    private void Start()
    {
        if (startMode == StartMode.AfterMainMenu && menu != null) menu.GameStarted += Play;
        else if (startMode != StartMode.Manually) Play();
    }

    private void OnDestroy()
    {
        if (menu != null) menu.GameStarted -= Play;
        if (state != State.Idle)
        {
            Time.timeScale = previousTimeScale;
            IsPlaying = false;
        }
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        if (state == State.Idle) return;

        HoldGameplay();
        float dt = Time.unscaledDeltaTime;
        UpdateEffects(dt);

        if (state != State.Reading) return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null) return;

        if (canSkip && keyboard.tabKey.wasPressedThisFrame)
        {
            SkipToWait();
            return;
        }

        bool next = keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame
                    || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame
                    || (mouse != null && mouse.leftButton.wasPressedThisFrame);

        if (next && Time.unscaledTime >= readyTime)
        {
            if (IsTyping) visibleLetters = typedLine.Length;
            else Advance();
        }

        if (state == State.Reading) UpdateLine(dt);
    }

    public void Play()
    {
        if (state != State.Idle || lines.Count == 0) return;
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        state = State.FadingIn;
        IsPlaying = true;
        previousTimeScale = Time.timeScale;
        if (pauseGame) Time.timeScale = 0f;
        HoldGameplay();

        ApplyLook();
        SetBackground(startingBackground);
        shownSpeaker = null;
        stage.gameObject.SetActive(true);
        canvas.gameObject.SetActive(true);

        SetFade(1f);
        lineIndex = 0;
        ShowLine(lines[0]);
        yield return Animate(FadeInTime, t => SetFade(1f - t));

        readyTime = Time.unscaledTime + InputDelay;
        state = State.Reading;
        while (state == State.Reading || state == State.Waiting) yield return null;

        // Fade to black, swap the cutscene out for the game, then fade back in on the lounge
        yield return Animate(FadeOutTime, SetFade);
        stage.gameObject.SetActive(false);
        Time.timeScale = previousTimeScale;
        yield return Animate(FadeOutTime, t => SetFade(1f - t));

        canvas.gameObject.SetActive(false);
        state = State.Idle;
        IsPlaying = false;
        onFinished?.Invoke();

        // A frame's wait so the final click isn't also read as a grab or a talk
        yield return null;
        if (player != null) player.InputLocked = false;
        FirstPersonController.SetCursorLocked(true);
    }

    // Picks up after a Wait Here line, e.g. once a Contradiction has been solved
    public void Continue()
    {
        if (state != State.Waiting) return;

        state = State.Reading;
        readyTime = Time.unscaledTime + InputDelay;
        lineIndex++;
        if (lineIndex >= lines.Count) state = State.Ending;
        else ShowLine(lines[lineIndex]);
    }

    // Lets something shown over the cutscene, like a Contradiction, take the screen without the text box showing
    // through. The portrait comes back with the next line
    public void HideTextBox(bool hide)
    {
        box.gameObject.SetActive(!hide);
        hintText.gameObject.SetActive(!hide);
        if (hide) portrait.gameObject.SetActive(false);
    }

    private void Advance()
    {
        if (lines[lineIndex].waitHere)
        {
            BeginWaiting();
            return;
        }

        lineIndex++;
        if (lineIndex >= lines.Count) state = State.Ending;
        else ShowLine(lines[lineIndex]);
    }

    private void BeginWaiting()
    {
        state = State.Waiting;
        nextArrow.gameObject.SetActive(false);
        onWaiting?.Invoke();
    }

    // Skipping stops at the next Wait Here line, so nothing the story needs is skipped past
    private void SkipToWait()
    {
        for (int i = lineIndex; i < lines.Count; i++)
        {
            if (!lines[i].waitHere) continue;

            if (i != lineIndex)
            {
                lineIndex = i;
                ShowLine(lines[i]);
            }
            visibleLetters = typedLine.Length;
            RenderLine();
            BeginWaiting();
            return;
        }

        state = State.Ending;
    }

    private void ShowLine(Line line)
    {
        typedLine.Set(line.text);
        visibleLetters = 0;
        renderedLetters = -1;
        letterTimer = 0f;

        if (line.background != null) SetBackground(line.background);

        bool narration = string.IsNullOrWhiteSpace(line.speaker);
        speaker = narration ? null : FindCharacter(line.speaker);

        // Same as the NPC box: the speaker's name in small capitals above the line
        nameText.gameObject.SetActive(!narration);
        if (!narration) nameText.text = (speaker != null ? speaker.name : line.speaker.Trim()).ToUpper();
        lineText.color = narration ? narrationColor : activeTextColor;

        // Narration clears the stage; a speaker keeps their portrait between lines and slides in when they change
        idleSprite = line.expression != null ? line.expression : speaker != null ? speaker.portrait : null;
        talkingSprite = line.expression == null && speaker != null ? speaker.talkingPortrait : null;
        portrait.gameObject.SetActive(idleSprite != null && !narration);
        portrait.sprite = idleSprite;
        mouthOpen = false;

        string speakerKey = narration ? null : line.speaker.Trim();
        if (!string.Equals(speakerKey, shownSpeaker, StringComparison.OrdinalIgnoreCase)) portraitTime = 0f;
        shownSpeaker = speakerKey;

        ShowProfile(FindProfile(line.profile));

        if (line.effect == Effect.Shake || line.effect == Effect.ShakeAndFlash) shakeLeft = ShakeTime;
        if (line.effect == Effect.Flash || line.effect == Effect.ShakeAndFlash) flashLeft = FlashTime;

        nextArrow.gameObject.SetActive(false);
        RenderLine();
    }

    private void UpdateLine(float dt)
    {
        if (IsTyping) TypeLetters(dt);
        if (visibleLetters != renderedLetters) RenderLine();

        // Mouth flaps between the two portraits while the line is typing, and rests closed after
        if (talkingSprite != null)
        {
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

        bool done = !IsTyping;
        if (nextArrow.gameObject.activeSelf != done) nextArrow.gameObject.SetActive(done);
        if (done) nextArrow.rectTransform.anchoredPosition = new Vector2(-24f, 22f + Mathf.Sin(Time.unscaledTime * 6f) * 3f);
    }

    private void TypeLetters(float dt)
    {
        letterTimer -= dt;
        float delay = 1f / Mathf.Max(1f, lettersPerSecond);

        while (letterTimer <= 0f && visibleLetters < typedLine.Length)
        {
            letterTimer += delay + typedLine.PauseAfter(visibleLetters, sentencePause, commaPause);
            visibleLetters++;
        }
    }

    private void RenderLine()
    {
        renderedLetters = visibleLetters;
        lineText.text = typedLine.Render(visibleLetters, highlightTag);
    }

    private void ShowProfile(Profile profile)
    {
        bool show = profile != null;
        if (card.gameObject.activeSelf != show) card.gameObject.SetActive(show);
        if (!show) return;

        cardName.text = profile.name;
        cardRole.text = profile.role;
        cardDescription.text = profile.description;
        cardPicture.sprite = profile.picture;
        cardPicture.gameObject.SetActive(profile.picture != null);
        cardTime = 0f;
    }

    private void UpdateEffects(float dt)
    {
        if (portraitTime < 1f)
        {
            portraitTime = Mathf.Min(1f, portraitTime + dt / PortraitSlideTime);
            float eased = Mathf.SmoothStep(0f, 1f, portraitTime);
            portrait.color = new Color(1f, 1f, 1f, eased);
            portrait.rectTransform.anchoredPosition = new Vector2((1f - eased) * PortraitSlideDistance, 0f);
        }

        if (cardTime < 1f)
        {
            cardTime = Mathf.Min(1f, cardTime + dt / CardPopTime);
            float eased = Mathf.SmoothStep(0f, 1f, cardTime);
            card.alpha = eased;
            card.transform.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, eased);
        }

        if (shakeLeft > 0f)
        {
            shakeLeft = Mathf.Max(0f, shakeLeft - dt);
            float strength = ShakeStrength * (shakeLeft / ShakeTime);
            float t = Time.unscaledTime * 40f;
            stage.anchoredPosition = new Vector2(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f) * (2f * strength);
        }

        if (flashLeft > 0f)
        {
            flashLeft = Mathf.Max(0f, flashLeft - dt);
            flash.color = new Color(1f, 1f, 1f, flashLeft / FlashTime);
        }
    }

    private void HoldGameplay()
    {
        if (player != null) player.InputLocked = true;
        FirstPersonController.SetCursorLocked(false);
    }

    private void SetBackground(Sprite sprite)
    {
        background.sprite = sprite;
        background.color = sprite != null ? Color.white : backgroundColor;
    }

    private void SetFade(float amount) => fade.color = new Color(0f, 0f, 0f, amount);

    private Character FindCharacter(string name)
    {
        string key = name.Trim();
        foreach (Character character in cast)
        {
            if (character != null && string.Equals(character.name?.Trim(), key, StringComparison.OrdinalIgnoreCase)) return character;
        }
        return null;
    }

    private Profile FindProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        string key = name.Trim();
        foreach (Profile profile in profiles)
        {
            if (profile != null && string.Equals(profile.name?.Trim(), key, StringComparison.OrdinalIgnoreCase)) return profile;
        }
        return null;
    }

    // Eases 0 to 1 on real time, so it runs while the game is paused
    private static IEnumerator Animate(float duration, Action<float> apply)
    {
        for (float time = 0f; time < duration;)
        {
            time += Time.unscaledDeltaTime;
            apply(Mathf.SmoothStep(0f, 1f, time / duration));
            yield return null;
        }
        apply(1f);
    }

    private void BuildUI()
    {
        canvas = RuntimeUI.CreateCanvas("Intro Cutscene UI", SortingOrder);

        // Everything on the stage shakes together
        stage = RuntimeUI.CreateRect("Stage", canvas.transform);
        RuntimeUI.Stretch(stage);

        background = RuntimeUI.CreateImage("Background", stage, backgroundColor);
        RuntimeUI.Stretch(background.rectTransform);

        // Stands at the bottom of the screen so the text box covers the lower half, like a courtroom shot
        portrait = RuntimeUI.CreateImage("Portrait", stage, Color.white);
        portrait.preserveAspect = true;
        RectTransform portraitRect = portrait.rectTransform;
        portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0.5f, 0f);
        portraitRect.pivot = new Vector2(0.5f, 0f);

        BuildProfileCard();
        BuildTextBox();

        hintText = CreateText("Hint", canvas.transform, 24, TextAnchor.UpperRight);
        RectTransform hintRect = hintText.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = Vector2.one;
        hintRect.anchoredPosition = new Vector2(-32f, -24f);
        hintRect.sizeDelta = new Vector2(800f, 40f);

        flash = RuntimeUI.CreateImage("Flash", canvas.transform, Color.clear);
        RuntimeUI.Stretch(flash.rectTransform);

        fade = RuntimeUI.CreateImage("Fade", canvas.transform, Color.black);
        RuntimeUI.Stretch(fade.rectTransform);

        ApplyLook();
        canvas.gameObject.SetActive(false);
    }

    private void BuildProfileCard()
    {
        Image cardBox = RuntimeUI.CreateBox("Profile Card", stage, 1100f, boxColor, borderColor, new RectOffset(36, 36, 28, 32), 14f, borders);
        RectTransform cardRect = cardBox.rectTransform;
        cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = new Vector2(0f, 150f);
        cardBox.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        card = cardBox.gameObject.AddComponent<CanvasGroup>();
        card.blocksRaycasts = false;

        cardLabel = CreateText("Label", cardBox.transform, 24, TextAnchor.UpperLeft);
        cardLabel.text = "PROFILE";

        Image row = RuntimeUI.CreateRow("Row", cardBox.transform, 300f, new RectOffset(0, 0, 0, 0), 32f);

        cardPicture = RuntimeUI.CreateImage("Picture", row.transform, Color.white);
        cardPicture.preserveAspect = true;
        LayoutElement pictureSize = RuntimeUI.FixedWidth(cardPicture, 300f);
        pictureSize.minHeight = 300f;
        pictureSize.preferredHeight = 300f;

        RectTransform info = RuntimeUI.CreateRect("Info", row.transform);
        RuntimeUI.FillWidth(info);
        var column = info.gameObject.AddComponent<VerticalLayoutGroup>();
        column.spacing = 10f;
        column.childAlignment = TextAnchor.MiddleLeft;
        column.childControlWidth = true;
        column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;

        cardName = CreateText("Name", info, 52, TextAnchor.UpperLeft);
        cardRole = CreateText("Role", info, 30, TextAnchor.UpperLeft);
        cardDescription = CreateText("Description", info, 30, TextAnchor.UpperLeft);
        cardDescription.lineSpacing = 1.1f;

        card.gameObject.SetActive(false);
    }

    // Built the same way as the NPC dialogue box: bottom-centered, sized to its text, name above the line
    private void BuildTextBox()
    {
        box = RuntimeUI.CreateBox("Text Box", stage, boxWidth, boxColor, borderColor, PlayerDialogue.NpcBoxPadding, PlayerDialogue.NpcBoxSpacing, borders);
        RectTransform boxRect = box.rectTransform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0.5f, 0f);
        boxRect.pivot = new Vector2(0.5f, 0f);
        boxRect.anchoredPosition = new Vector2(0f, 40f);
        box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        nameText = CreateText("Name", box.transform, nameSize, TextAnchor.UpperLeft);
        lineText = CreateText("Line", box.transform, textSize, TextAnchor.UpperLeft);
        lineText.supportRichText = true;

        // A small diamond that bobs in the corner once the line has finished typing
        nextArrow = RuntimeUI.CreateImage("Next", box.transform, Color.white);
        nextArrow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        RectTransform arrowRect = nextArrow.rectTransform;
        arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(1f, 0f);
        arrowRect.sizeDelta = new Vector2(12f, 12f);
        arrowRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    private Text CreateText(string name, Transform parent, int size, TextAnchor alignment)
    {
        Text text = RuntimeUI.CreateText(name, parent, font, size, textColor, alignment, shadowColor, out Shadow shadow);
        shadows.Add(shadow);
        return text;
    }

    // Pushes the look onto the built UI: the Player Dialogue's box when matching it, otherwise this component's own values.
    // Runs after building, at the start of each play, and whenever a value is edited
    private void ApplyLook()
    {
        bool match = matchDialogueBox && dialogue != null;
        Font activeFont = match ? dialogue.BoxFont : font;
        if (activeFont == null) activeFont = RuntimeUI.DefaultFont;

        Color activeBox = match ? dialogue.BoxColor : boxColor;
        Color activeShadow = match ? dialogue.ShadowColor : shadowColor;
        activeBorderColor = match ? dialogue.BoxBorderColor : borderColor;
        activeNameColor = match ? dialogue.NameColor : nameColor;
        activeTextColor = match ? dialogue.LineColor : textColor;

        Color activeHighlight = match ? dialogue.HighlightColor : highlightColor;
        highlightTag = TypedLine.ColorTag(activeHighlight);

        if (state == State.Idle) SetBackground(startingBackground);
        portrait.rectTransform.sizeDelta = new Vector2(portraitHeight * 1.2f, portraitHeight);

        box.color = activeBox;
        box.rectTransform.sizeDelta = new Vector2(match ? dialogue.BoxWidth : boxWidth, 0f);
        card.GetComponent<Image>().color = activeBox;
        foreach (Image border in borders) border.color = activeBorderColor;
        foreach (Shadow shadow in shadows) shadow.effectColor = activeShadow;

        foreach (Text text in new[] { nameText, lineText, cardLabel, cardName, cardRole, cardDescription, hintText }) text.font = activeFont;
        nameText.fontSize = match ? dialogue.NameSize : nameSize;
        lineText.fontSize = match ? dialogue.LineSize : textSize;
        nameText.color = activeNameColor;
        lineText.color = activeTextColor;
        nextArrow.color = activeNameColor;

        cardName.color = activeHighlight;
        cardRole.color = activeNameColor;
        cardDescription.color = activeTextColor;
        cardLabel.color = activeNameColor;

        hintText.color = activeNameColor;
        hintText.text = canSkip ? "Click / Space: Next      Tab: Skip" : "Click / Space: Next";

        renderedLetters = -1;
    }

#if UNITY_EDITOR
    // Adds Level 0's opening after the existing lines: Helena and James's argument, the pause for catching
    // their lie, and Salem heading in. Helena and James are added to the cast if they aren't there yet
    [ContextMenu("Add Level 0 Opening Lines")]
    private void AddLevelZeroOpening()
    {
        UnityEditor.Undo.RecordObject(this, "Add Level 0 Opening Lines");
        foreach (string name in new[] { "Helena", "James", "Salem" })
        {
            if (FindCharacter(name) == null) cast.Add(new Character(name));
        }

        lines.AddRange(LevelZeroLines());
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private static List<Line> LevelZeroLines() => new List<Line>
    {
        new Line("", "Before Salem can take a step into the lobby, a woman rushes up to him in tears."),
        new Line("Helena", "Detective! You must help me!", null, Effect.Shake),
        new Line("Salem", "What's wrong?"),
        new Line("Helena", "My boyfriend doesn't want to buy me the new private island for sale! When it opened at 7 pm today, I made a fool of myself, screaming for joy at the bar. But now he won't get it!"),
        new Line("James", "Helena, you're forgetting that my money can't be used right now. And I wasn't going to get it after all the pool guests heard your screaming. It was too embarrassing."),
        new Line("Helena", "Do you see, Detective? He's so mean to me!"),
        new Line("James", "A private estate in the city's high suburbs should be fine!"),
        new Line("Salem", "(Wait a minute...)") { waitHere = true },
        new Line("Salem", "(There's a murder case happening; I'm here to investigate, and one of them has already *lied* about something.)"),
        new Line("Helena", "Detective?"),
        new Line("Salem", "Sorry about your private island, ma'am. But move back, will you? Where's the body?"),
        new Line("", "Helena goes back to crying while James points to the elevator in a huff."),
        new Line("Salem", "(I doubt catching their lies will be that easy going forward.)")
    };
#endif

    private static List<Character> DefaultCast() => new List<Character>
    {
        new Character("Police Chief"),
        new Character("Salem")
    };

    private static List<Line> DefaultLines() => new List<Line>
    {
        new Line("", "Hotel Eclipse. Late Thursday night."),
        new Line("Police Chief", "Salem. Good, you're here. We have another one."),
        new Line("Police Chief", "The victim is *Geno Jones*. Found right here in the lounge, nearly split in two.", null, Effect.Shake),
        new Line("Police Chief", "Geno was a *master inventor of supernatural tools*. Half the protective charms in this city came out of his workshop."),
        new Line("Police Chief", "Not exactly a saint, though. He sold to whoever paid and made plenty of enemies doing it. *Morally grey*, let's say."),
        new Line("Police Chief", "Time of death was *10:00 pm*. The wounds point to *large claws*. Whatever did this wasn't human."),
        new Line("Police Chief", "As for witnesses... the staff are acting like zombies. Glassy eyes, flat voices. Half of them can barely remember their own names."),
        new Line("Police Chief", "I pieced together what I could from *multiple staff accounts*. Nobody saw the attack itself."),
        new Line("Police Chief", "But they all agree on one thing. At 10:00, *six guests* were in this lounge, and none of them were in that daze."),
        new Line("Salem", "So whoever did this is still in the room."),
        new Line("Police Chief", "Exactly. Six suspects. Here's what I have on each of them."),
        new Line("Police Chief", "*Helena*. James's girlfriend. She hasn't stopped crying since we got here.", "Helena"),
        new Line("Police Chief", "*James*. Tech billionaire, in town for a business meeting. Cold as ice, and not happy we're keeping him here.", "James"),
        new Line("Police Chief", "*Carmela*. A certified witch who brews healing potions. Says she was called here to deliver a sobering tonic. Jumpy.", "Carmela"),
        new Line("Police Chief", "*Zain*. Been drinking since before we arrived. Used to work for James's company, which is interesting.", "Zain"),
        new Line("Police Chief", "*Murray*. Says he's a businessman. Sells fancy caviar. Seems very interested in where Zain is at all times.", "Murray"),
        new Line("Police Chief", "*Yaroslava*. Sells potions and amulets. Not certified, and she'll tell you what she thinks of witches who are.", "Yaroslava"),
        new Line("Police Chief", "One more thing. Every one of them is *lying* about something. And from the early reports, there may be *more than one monster*.", null, Effect.Flash),
        new Line("Police Chief", "Talk to them, find the evidence, and only accuse someone when you're sure. *One wrong guess*, and you'll end up like Geno.", null, Effect.Shake),
        new Line("Police Chief", "I'll be outside. Good luck, Detective."),
        new Line("Salem", "Time to get to work.")
    };

    private static List<Profile> DefaultProfiles() => new List<Profile>
    {
        new Profile("Helena", "James's girlfriend  ·  Socialite",
            "Here with James on his business trip. Dramatic and tearful. Wants James to buy her a penthouse in the city."),
        new Profile("James", "Tech billionaire  ·  Helena's boyfriend",
            "In town for an important business meeting. Cold, logical and dismissive. Geno was one of his loudest critics."),
        new Profile("Carmela", "Certified witch  ·  Potion maker",
            "Called to the hotel to deliver a sobering tonic to a drunk guest. Paranoid and avoids confrontation."),
        new Profile("Zain", "Hotel guest",
            "Drinking heavily. Lost his job at James's company and seems to be running from someone."),
        new Profile("Murray", "Businessman  ·  Caviar distributor",
            "Ships expensive caviar in signature tins. Keeps a close eye on Zain."),
        new Profile("Yaroslava", "Potion and amulet seller",
            "Uncertified magic practitioner. Bitter toward certified witches, especially Carmela.")
    };
}