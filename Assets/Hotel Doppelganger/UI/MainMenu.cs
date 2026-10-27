using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Title screen with Start, How To Play, and a Playtest page for changing settings while testing.
// Playtest settings are grouped into sections, each on its own page. It can also be opened mid-game with F1.
// Builds its own UI at runtime.
[AddComponentMenu("Game/UI/Main Menu")]
public class MainMenu : MonoBehaviour
{
    private enum Page { Main, Tutorial, Playtest, Section }

    private sealed class SettingsSection
    {
        public string Title;
        public readonly List<MenuSetting> Settings = new List<MenuSetting>();
        public GameObject Root;
        public MenuList List;
        public Text TitleText;
    }

    private static readonly int[] FrameRates = { 30, 60, 120, 144, -1 };
    private static readonly string[] FrameRateNames = { "30", "60", "120", "144", "Unlimited" };

    private const int SortingOrder = 200;   // above the dialogue box

    private static RectOffset ButtonsBoxPadding => new RectOffset(16, 16, 14, 14);
    private static RectOffset TutorialBoxPadding => new RectOffset(36, 36, 28, 24);
    private static RectOffset PlaytestBoxPadding => new RectOffset(28, 28, 24, 20);

    [Header("Menu")]
    [DisplayAs("Game Title")]
    [SerializeField] private string gameTitle = "My Game";

    [DisplayAs("Start Button Text")]
    [SerializeField] private string startText = "Start";

    [DisplayAs("Tutorial Button Text")]
    [SerializeField] private string tutorialButtonText = "How To Play";

    [Header("Starting The Game")]
    // Empty: the menu sits over this scene and just closes. Filled in: Start loads that scene instead.
    [DisplayAs("Scene To Load (optional)")]
    [SerializeField] private string sceneToLoad = "";

    [DisplayAs("Pause Game Behind Menu")]
    [SerializeField] private bool pauseWhileOpen = true;

    [Header("How To Play Page")]
    [DisplayAs("Page Title")]
    [SerializeField] private string tutorialTitle = "How To Play";

    [DisplayAs("Page Text", Lines = 8)]
    [SerializeField]
    private string tutorialText =
        "WASD  -  Walk\n" +
        "Shift  -  Sprint\n" +
        "Mouse  -  Look around\n" +
        "Left click  -  Talk to people\n" +
        "Hold left click  -  Pick up and carry things\n" +
        "Right click while carrying  -  Rotate what you're holding\n" +
        "Scroll while carrying  -  Move it closer or further away\n" +
        "Mouse, number keys or W/S + E  -  Pick what to say\n" +
        "Tab  -  Leave a conversation";

    [DisplayAs("Back Button Text")]
    [SerializeField] private string backText = "Back";

    [Header("Playtest")]
    [DisplayAs("Show Playtest Button")]
    [SerializeField] private bool showPlaytestButton = true;

    [DisplayAs("Playtest Button Text", ShowIf = nameof(showPlaytestButton))]
    [SerializeField] private string playtestButtonText = "Playtest";

    [DisplayAs("Open With F1 During Play")]
    [SerializeField] private bool allowInGamePlaytest = true;

    [DisplayAs("Playtest Page Title")]
    [SerializeField] private string playtestTitle = "Playtest";

    [DisplayAs("Reset Page Button Text")]
    [SerializeField] private string resetText = "Reset To Defaults";

    [DisplayAs("Reset Everything Button Text")]
    [SerializeField] private string resetAllText = "Reset Everything";

    [Foldout("Look")]
    [DisplayAs("Font (optional)")]
    [SerializeField] private Font font;

    [DisplayAs("Background")]
    [SerializeField] private Color backgroundColor = new Color(0.02f, 0.02f, 0.02f, 0.85f);

    [DisplayAs("Title Color")]
    [SerializeField] private Color titleColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Text Color")]
    [SerializeField] private Color textColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Button Text Color")]
    [SerializeField] private Color buttonColor = new Color(0.6f, 0.58f, 0.54f);

    [DisplayAs("Highlighted Button Text")]
    [SerializeField] private Color selectedTextColor = Color.black;

    [DisplayAs("Highlighted Button Background")]
    [SerializeField] private Color selectedBackColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Slider Track Color")]
    [SerializeField] private Color trackColor = new Color(0.25f, 0.24f, 0.22f);

    [DisplayAs("Box Color")]
    [SerializeField] private Color boxColor = new Color(0f, 0f, 0f, 0.92f);

    [DisplayAs("Box Border")]
    [SerializeField] private Color borderColor = new Color(0.85f, 0.83f, 0.78f, 0.5f);

    [DisplayAs("Text Shadow Color")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [Foldout("Sizes (as on a 1080p screen)")]
    [DisplayAs("Title Text Size", Min = 1f)]
    [SerializeField] private int titleSize = 72;

    [DisplayAs("Button Text Size", Min = 1f)]
    [SerializeField] private int buttonSize = 32;

    [DisplayAs("Page Title Text Size", Min = 1f)]
    [SerializeField] private int pageTitleSize = 40;

    [DisplayAs("Page Text Size", Min = 1f)]
    [SerializeField] private int pageTextSize = 28;

    [DisplayAs("Playtest Text Size", Min = 1f)]
    [SerializeField] private int settingTextSize = 24;

    [DisplayAs("Buttons Box Width", Min = 0f)]
    [SerializeField] private float buttonsWidth = 420f;

    [DisplayAs("How To Play Box Width", Min = 0f)]
    [SerializeField] private float tutorialWidth = 900f;

    [DisplayAs("Playtest Box Width", Min = 0f)]
    [SerializeField] private float playtestWidth = 960f;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Player (optional)")]
    [SerializeField] private FirstPersonController player;

    [DisplayAs("Object Grabber (optional)")]
    [SerializeField] private ObjectGrabber grabber;

    [DisplayAs("Player Dialogue (optional)")]
    [SerializeField] private PlayerDialogue dialogue;

    [DisplayAs("Game Complete (optional)")]
    [SerializeField] private GameComplete gameComplete;

    private readonly MenuStyle style = new MenuStyle();
    private readonly List<SettingsSection> sections = new List<SettingsSection>();
    private readonly List<Image> boxes = new List<Image>();
    private readonly List<Image> borders = new List<Image>();
    private readonly List<Shadow> textShadows = new List<Shadow>();

    private MenuList mainList;
    private MenuList tutorialList;
    private MenuList playtestList;

    private Canvas canvas;
    private GameObject mainPage;
    private GameObject tutorialPage;
    private GameObject playtestPage;
    private Image background;
    private Text titleText;
    private Text tutorialTitleText;
    private Text tutorialBodyText;
    private Text playtestTitleText;

    private Page page;
    private SettingsSection currentSection;
    private int resolutionIndex;
    private bool openedInGame;
    private float gameSpeed = 1f;

    public bool IsOpen { get; private set; }

    private MenuList CurrentList
    {
        get
        {
            switch (page)
            {
                case Page.Tutorial: return tutorialList;
                case Page.Playtest: return playtestList;
                case Page.Section: return currentSection.List;
                default: return mainList;
            }
        }
    }

    private void Awake()
    {
        if (player == null) player = FindAnyObjectByType<FirstPersonController>();
        if (grabber == null) grabber = FindAnyObjectByType<ObjectGrabber>();
        if (dialogue == null) dialogue = FindAnyObjectByType<PlayerDialogue>();
        if (gameComplete == null) gameComplete = FindAnyObjectByType<GameComplete>();
    }

    // Built in Start rather than Awake so every NPC has registered and every value holds its starting setting
    private void Start()
    {
        BuildSettings();
        BuildUI();
        ShowMenu();
    }

    private void OnValidate()
    {
        if (canvas != null) ApplyLook();
    }

    private void OnDestroy()
    {
        if (IsOpen) Time.timeScale = 1f;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null) return;

        if (!IsOpen)
        {
            if (keyboard.f1Key.wasPressedThisFrame && CanOpenPlaytestInGame()) OpenPlaytestInGame();
            return;
        }

        HoldGameplay();
        if (mouse == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame && page != Page.Main)
        {
            GoBack();
            return;
        }

        if (keyboard.f1Key.wasPressedThisFrame && openedInGame)
        {
            Close();
            return;
        }

        CurrentList.HandleInput(keyboard, mouse);
        if (IsOpen) CurrentList.Refresh();
    }

    public void ShowMenu()
    {
        Open(false);
        ShowPage(Page.Main);
    }

    public void StartGame()
    {
        if (string.IsNullOrWhiteSpace(sceneToLoad))
        {
            Close();
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneToLoad.Trim());
    }

    public void ShowTutorial() => ShowPage(Page.Tutorial);

    public void ShowMainPage() => ShowPage(Page.Main);

    public void ShowPlaytest()
    {
        if (IsOpen) ShowPage(Page.Playtest);
        else OpenPlaytestInGame();
    }

    // Puts every Playtest setting back to how it was when the game started
    public void ResetSettings()
    {
        foreach (SettingsSection section in sections) ResetSection(section);
    }

    private static void ResetSection(SettingsSection section)
    {
        foreach (MenuSetting setting in section.Settings) setting.ResetToDefault();
        section.List.MarkDirty();
    }

    private void Open(bool inGame)
    {
        IsOpen = true;
        openedInGame = inGame;
        canvas.gameObject.SetActive(true);

        if (pauseWhileOpen || inGame) Time.timeScale = 0f;
        HoldGameplay();
    }

    private void Close()
    {
        IsOpen = false;
        openedInGame = false;
        canvas.gameObject.SetActive(false);
        Time.timeScale = gameSpeed;

        FirstPersonController.SetCursorLocked(true);
        StartCoroutine(ReleaseInputNextFrame());
    }

    private void OpenPlaytestInGame()
    {
        Open(true);
        ShowPage(Page.Playtest);
    }

    private bool CanOpenPlaytestInGame()
    {
        if (!allowInGamePlaytest) return false;
        if (gameComplete != null && gameComplete.HasEnded) return false;
        return dialogue == null || !dialogue.IsTalking;
    }

    // Sections go back to the Playtest page. From a Playtest page opened with F1, Back returns to the game.
    private void GoBack()
    {
        if (page == Page.Section) ShowPage(Page.Playtest);
        else if (page == Page.Playtest && openedInGame) Close();
        else ShowPage(Page.Main);
    }

    private void OpenSection(SettingsSection section)
    {
        currentSection = section;
        ShowPage(Page.Section);
    }

    private void ShowPage(Page newPage)
    {
        page = newPage;
        mainPage.SetActive(page == Page.Main);
        tutorialPage.SetActive(page == Page.Tutorial);
        playtestPage.SetActive(page == Page.Playtest);
        foreach (SettingsSection section in sections) section.Root.SetActive(page == Page.Section && section == currentSection);

        Mouse mouse = Mouse.current;
        CurrentList.Reset(mouse != null ? mouse.position.ReadValue() : Vector2.zero);
        CurrentList.Refresh();
    }

    private void HoldGameplay()
    {
        if (player != null) player.InputLocked = true;
        FirstPersonController.SetCursorLocked(false);
    }

    // Waits a frame so the click that closed the menu isn't also read as a grab or a talk
    private IEnumerator ReleaseInputNextFrame()
    {
        yield return null;
        if (!IsOpen && player != null) player.InputLocked = false;
    }

    private void BuildSettings()
    {
        FirstPersonController p = player;
        ObjectGrabber g = grabber;
        PlayerDialogue d = dialogue;

        SettingsSection section = AddSection("Controls");
        if (p != null)
        {
            section.Settings.Add(MenuSetting.Slider("Mouse Sensitivity", 0.01f, 0.5f, 0.01f, "0.00", () => p.MouseSensitivity, v => p.MouseSensitivity = v));
            section.Settings.Add(MenuSetting.Toggle("Invert Mouse Up/Down", () => p.InvertY, v => p.InvertY = v));
        }
        if (g != null)
        {
            section.Settings.Add(MenuSetting.Slider("Held Object Turn Speed", 0.05f, 1f, 0.05f, "0.00", () => g.RotateSpeed, v => g.RotateSpeed = v));
            section.Settings.Add(MenuSetting.Slider("Scroll Push/Pull Step", 0.05f, 1f, 0.05f, "0.00", () => g.ScrollStep, v => g.ScrollStep = v));
        }

        section = AddSection("Display & Sound");
        section.Settings.Add(MenuSetting.Slider("Master Volume", 0f, 100f, 5f, "0", () => AudioListener.volume * 100f, v => AudioListener.volume = v / 100f));
        AddResolution(section);
        section.Settings.Add(MenuSetting.Toggle("Fullscreen", () => Screen.fullScreen, v => Screen.fullScreen = v));
        if (QualitySettings.names.Length > 1)
            section.Settings.Add(MenuSetting.Choice("Graphics Quality", QualitySettings.names, QualitySettings.GetQualityLevel, i => QualitySettings.SetQualityLevel(i, true)));
        section.Settings.Add(MenuSetting.Toggle("VSync", () => QualitySettings.vSyncCount > 0, v => QualitySettings.vSyncCount = v ? 1 : 0));
        section.Settings.Add(MenuSetting.Choice("Frame Rate Limit (VSync off)", FrameRateNames, CurrentFrameRateIndex, i => Application.targetFrameRate = FrameRates[i]));
        if (p != null)
        {
            section.Settings.Add(MenuSetting.Slider("Field Of View", 50f, 110f, 1f, "0", () => p.FieldOfView, v => p.FieldOfView = v));
            section.Settings.Add(MenuSetting.Slider("Crosshair Dot Size", 0f, 20f, 1f, "0", () => p.DotSize, v => p.DotSize = v));
            section.Settings.Add(MenuSetting.Slider("Crosshair Icon Size", 8f, 96f, 2f, "0", () => p.IconSize, v => p.IconSize = v));
        }

        if (p != null)
        {
            section = AddSection("Player");
            section.Settings.Add(MenuSetting.Slider("Walk Speed", 0.5f, 15f, 0.5f, "0.0", () => p.WalkSpeed, v => p.WalkSpeed = v));
            section.Settings.Add(MenuSetting.Slider("Sprint Speed", 0.5f, 25f, 0.5f, "0.0", () => p.SprintSpeed, v => p.SprintSpeed = v));
            section.Settings.Add(MenuSetting.Slider("Gravity", -40f, -1f, 0.5f, "0.0", () => p.Gravity, v => p.Gravity = v));
            section.Settings.Add(MenuSetting.Slider("Look Up/Down Limit", 30f, 90f, 1f, "0", () => p.LookLimit, v => p.LookLimit = v));
        }

        if (g != null)
        {
            section = AddSection("Grabbing");
            section.Settings.Add(MenuSetting.Slider("Grab Reach", 1f, 8f, 0.25f, "0.00", () => g.Reach, v => g.Reach = v));
            section.Settings.Add(MenuSetting.Slider("Grab Aim Forgiveness", 0f, 1f, 0.05f, "0.00", () => g.AimForgiveness, v => g.AimForgiveness = v));
            section.Settings.Add(MenuSetting.Slider("Heaviest Liftable Mass", 1f, 100f, 1f, "0", () => g.MaxMass, v => g.MaxMass = v));
            section.Settings.Add(MenuSetting.Slider("Closest Hold Distance", 0.25f, 5f, 0.25f, "0.00", () => g.ClosestHold, v => g.ClosestHold = v));
            section.Settings.Add(MenuSetting.Slider("Farthest Hold Distance", 0.5f, 8f, 0.25f, "0.00", () => g.FarthestHold, v => g.FarthestHold = v));
            section.Settings.Add(MenuSetting.Slider("Carry Strength", 1f, 40f, 1f, "0", () => g.FollowStrength, v => g.FollowStrength = v));
            section.Settings.Add(MenuSetting.Toggle("Glow On Grabbable Objects", () => g.ShowGlow, v => g.ShowGlow = v));
        }

        if (d != null)
        {
            section = AddSection("Talking");
            section.Settings.Add(MenuSetting.Slider("Talk Distance", 1f, 10f, 0.25f, "0.00", () => d.TalkRange, v => d.TalkRange = v));
            section.Settings.Add(MenuSetting.Slider("Talk Aim Forgiveness", 0f, 1f, 0.05f, "0.00", () => d.TalkAimForgiveness, v => d.TalkAimForgiveness = v));
            section.Settings.Add(MenuSetting.Toggle("Show Name At Crosshair", () => d.ShowTalkPrompt, v => d.ShowTalkPrompt = v));
            section.Settings.Add(MenuSetting.Slider("Text Speed (letters/sec)", 5f, 150f, 5f, "0", () => d.LettersPerSecond, v => d.LettersPerSecond = v));
            section.Settings.Add(MenuSetting.Slider("Pause After Sentences", 0f, 1f, 0.05f, "0.00", () => d.SentencePause, v => d.SentencePause = v));
        }

        // NPC rows show the first NPC's value and change every NPC at once
        section = AddSection("NPCs & World");
        if (NPCPatrol.Active.Count > 0)
        {
            section.Settings.Add(MenuSetting.Slider("NPC Walk Speed", 0.25f, 6f, 0.25f, "0.00", () => ReadNPC(n => n.WalkSpeed), v => ForEachNPC(n => n.WalkSpeed = v)));
            section.Settings.Add(MenuSetting.Toggle("NPC Random Wait", () => ReadNPC(n => n.UseRandomWait), v => ForEachNPC(n => n.UseRandomWait = v)));
            section.Settings.Add(MenuSetting.Slider("NPC Wait (not random)", 0f, 30f, 0.5f, "0.0", () => ReadNPC(n => n.WaitTime), v => ForEachNPC(n => n.WaitTime = v)));
            section.Settings.Add(MenuSetting.Slider("NPC Shortest Random Wait", 0f, 30f, 0.5f, "0.0", () => ReadNPC(n => n.ShortestWait), v => ForEachNPC(n => n.ShortestWait = v)));
            section.Settings.Add(MenuSetting.Slider("NPC Longest Random Wait", 0f, 30f, 0.5f, "0.0", () => ReadNPC(n => n.LongestWait), v => ForEachNPC(n => n.LongestWait = v)));
            section.Settings.Add(MenuSetting.Toggle("NPCs Stop For Player", () => ReadNPC(n => n.StopForPlayer), v => ForEachNPC(n => n.StopForPlayer = v)));
            section.Settings.Add(MenuSetting.Slider("NPC Stop Distance", 0f, 6f, 0.25f, "0.00", () => ReadNPC(n => n.StopRange), v => ForEachNPC(n => n.StopRange = v)));
        }

        NPCFacePlayer[] facers = FindObjectsByType<NPCFacePlayer>(FindObjectsSortMode.None);
        if (facers.Length > 0)
        {
            section.Settings.Add(MenuSetting.Slider("NPC Notice Distance", 0f, 20f, 0.5f, "0.0", () => facers[0].NoticeRange, v => SetAll(facers, f => f.NoticeRange = v)));
            section.Settings.Add(MenuSetting.Slider("NPC Turn Speed", 0.5f, 15f, 0.5f, "0.0", () => facers[0].TurnSpeed, v => SetAll(facers, f => f.TurnSpeed = v)));
        }

        // The game is paused while the menu is up, so this takes effect when it closes
        section.Settings.Add(MenuSetting.Slider("Game Speed", 0.25f, 3f, 0.25f, "0.00x", () => gameSpeed, v => gameSpeed = v));

        sections.RemoveAll(s => s.Settings.Count == 0);
    }

    private SettingsSection AddSection(string title)
    {
        var section = new SettingsSection { Title = title };
        sections.Add(section);
        return section;
    }

    // Lists each screen size once (Unity reports every refresh rate separately). Skipped where only one size exists, e.g. in the editor.
    private void AddResolution(SettingsSection section)
    {
        var sizes = new List<Vector2Int>();
        foreach (Resolution resolution in Screen.resolutions)
        {
            var size = new Vector2Int(resolution.width, resolution.height);
            if (!sizes.Contains(size)) sizes.Add(size);
        }

        var current = new Vector2Int(Screen.width, Screen.height);
        if (!sizes.Contains(current)) sizes.Add(current);
        if (sizes.Count < 2) return;

        var names = new string[sizes.Count];
        for (int i = 0; i < sizes.Count; i++) names[i] = sizes[i].x + " x " + sizes[i].y;

        // Screen.width only updates a frame after a change, so the chosen index is tracked here instead
        resolutionIndex = sizes.IndexOf(current);
        section.Settings.Add(MenuSetting.Choice("Resolution", names, () => resolutionIndex, i =>
        {
            resolutionIndex = i;
            Screen.SetResolution(sizes[i].x, sizes[i].y, Screen.fullScreenMode);
        }));
    }

    private static int CurrentFrameRateIndex()
    {
        int index = Array.IndexOf(FrameRates, Application.targetFrameRate);
        return index >= 0 ? index : FrameRates.Length - 1;
    }

    // Falls back to the type's default if every NPC has been removed since the menu was built
    private static T ReadNPC<T>(Func<NPCPatrol, T> read)
    {
        IReadOnlyList<NPCPatrol> npcs = NPCPatrol.Active;
        return npcs.Count > 0 && npcs[0] != null ? read(npcs[0]) : default;
    }

    private static void ForEachNPC(Action<NPCPatrol> apply)
    {
        IReadOnlyList<NPCPatrol> npcs = NPCPatrol.Active;
        for (int i = 0; i < npcs.Count; i++)
        {
            if (npcs[i] != null) apply(npcs[i]);
        }
    }

    private static void SetAll<T>(T[] targets, Action<T> apply) where T : UnityEngine.Object
    {
        foreach (T target in targets)
        {
            if (target != null) apply(target);
        }
    }

    private void UpdateStyle()
    {
        style.Font = font;
        style.ButtonTextSize = buttonSize;
        style.SettingTextSize = settingTextSize;
        style.Text = buttonColor;
        style.SelectedText = selectedTextColor;
        style.SelectedBack = selectedBackColor;
        style.Track = trackColor;
        style.Fill = titleColor;
        style.Shadow = shadowColor;
    }

    private void BuildUI()
    {
        UpdateStyle();
        mainList = new MenuList(style);
        tutorialList = new MenuList(style);
        playtestList = new MenuList(style);

        canvas = RuntimeUI.CreateCanvas("Main Menu UI", SortingOrder);
        background = RuntimeUI.CreateImage("Background", canvas.transform, backgroundColor);
        RuntimeUI.Stretch(background.rectTransform);

        BuildMainPage();
        BuildTutorialPage();
        BuildPlaytestPage();
        foreach (SettingsSection section in sections) BuildSectionPage(section);

        ApplyLook();
        canvas.gameObject.SetActive(false);
    }

    private void BuildMainPage()
    {
        RectTransform pageRect = RuntimeUI.CreateCenteredColumn("Main Page", canvas.transform, 56f);
        mainPage = pageRect.gameObject;

        titleText = CreateText("Title", pageRect, titleSize, TextAnchor.MiddleCenter);
        titleText.rectTransform.sizeDelta = new Vector2(1600f, 0f);

        Transform box = CreateBox("Buttons", pageRect, buttonsWidth, ButtonsBoxPadding, 2f);
        mainList.AddButton(box, () => startText, StartGame);
        mainList.AddButton(box, () => tutorialButtonText, ShowTutorial);
        if (showPlaytestButton) mainList.AddButton(box, () => playtestButtonText, ShowPlaytest);
    }

    private void BuildTutorialPage()
    {
        RectTransform pageRect = RuntimeUI.CreateCenteredColumn("How To Play Page", canvas.transform, 0f);
        tutorialPage = pageRect.gameObject;

        Transform box = CreateBox("How To Play", pageRect, tutorialWidth, TutorialBoxPadding, 18f);
        tutorialTitleText = CreateText("Page Title", box, pageTitleSize, TextAnchor.UpperLeft);
        tutorialBodyText = CreateText("Page Text", box, pageTextSize, TextAnchor.UpperLeft);
        tutorialBodyText.lineSpacing = 1.2f;

        tutorialList.AddButton(box, () => backText, ShowMainPage);
    }

    private void BuildPlaytestPage()
    {
        RectTransform pageRect = RuntimeUI.CreateCenteredColumn("Playtest Page", canvas.transform, 0f);
        playtestPage = pageRect.gameObject;

        Transform box = CreateBox("Playtest", pageRect, playtestWidth, PlaytestBoxPadding, 2f);
        playtestTitleText = CreateText("Page Title", box, pageTitleSize, TextAnchor.UpperLeft);

        foreach (SettingsSection section in sections) playtestList.AddButton(box, () => section.Title, () => OpenSection(section));

        playtestList.AddButton(box, () => resetAllText, ResetSettings);
        playtestList.AddButton(box, () => backText, GoBack);
    }

    private void BuildSectionPage(SettingsSection section)
    {
        RectTransform pageRect = RuntimeUI.CreateCenteredColumn(section.Title + " Page", canvas.transform, 0f);
        section.Root = pageRect.gameObject;
        section.List = new MenuList(style);

        Transform box = CreateBox(section.Title, pageRect, playtestWidth, PlaytestBoxPadding, 2f);
        section.TitleText = CreateText("Page Title", box, pageTitleSize, TextAnchor.UpperLeft);

        foreach (MenuSetting setting in section.Settings) section.List.AddSetting(box, setting);

        section.List.AddButton(box, () => resetText, () => ResetSection(section));
        section.List.AddButton(box, () => backText, GoBack);
    }

    private Text CreateText(string name, Transform parent, int size, TextAnchor alignment)
    {
        Text text = RuntimeUI.CreateText(name, parent, font, size, textColor, alignment, shadowColor, out Shadow shadow);
        textShadows.Add(shadow);
        return text;
    }

    private Transform CreateBox(string name, Transform parent, float width, RectOffset padding, float spacing)
    {
        Image box = RuntimeUI.CreateBox(name, parent, width, boxColor, borderColor, padding, spacing, borders);
        boxes.Add(box);
        return box.transform;
    }

    // Pushes Inspector values onto the built UI. Runs once after building, then whenever a value is edited.
    private void ApplyLook()
    {
        UpdateStyle();

        background.color = backgroundColor;
        foreach (Image box in boxes) box.color = boxColor;
        foreach (Image border in borders) border.color = borderColor;
        foreach (Shadow shadow in textShadows) shadow.effectColor = shadowColor;

        SetText(titleText, gameTitle, titleColor);
        SetText(tutorialTitleText, tutorialTitle, titleColor);
        SetText(tutorialBodyText, tutorialText, textColor);
        SetText(playtestTitleText, playtestTitle, titleColor);
        foreach (SettingsSection section in sections) SetText(section.TitleText, section.Title, titleColor);

        mainList.MarkDirty();
        tutorialList.MarkDirty();
        playtestList.MarkDirty();
        foreach (SettingsSection section in sections) section.List.MarkDirty();
    }

    private static void SetText(Text target, string value, Color color)
    {
        target.text = value;
        target.color = color;
    }
}