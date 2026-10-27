using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Shadow = UnityEngine.UI.Shadow;
using Text = UnityEngine.UI.Text;

// Title screen shown when the game opens: Start, How To Play, and a Playtest page for changing
// important values while testing. The Playtest page can also be opened during play with F1.
// Builds its own UI at runtime. Put it on any GameObject in the scene.
// Mouse, or W/S to choose, A/D to change a value, Enter/E/Space to press. Esc goes back.
[AddComponentMenu("Game/UI/Main Menu")]
public class MainMenu : MonoBehaviour
{
    private enum Page { Main, Tutorial, Playtest }

    // A value the Playtest page can change: a slider, or an on/off switch when IsToggle is set
    private class Setting
    {
        public string Label;
        public float Min;
        public float Max;
        public float Step;
        public string Format;
        public bool IsToggle;
        public Func<float> Get;
        public Action<float> Set;
        public float Default;
    }

    // One row on a page: a button, or a setting when Setting is filled in
    private class MenuItem
    {
        public RectTransform Rect;
        public Image Back;
        public Text Label;
        public Shadow LabelShadow;
        public Func<string> GetText;
        public Action OnPress;

        public Setting Setting;
        public RectTransform Track;
        public Image TrackImage;
        public RectTransform Fill;
        public Image FillImage;
        public Text Value;
        public Shadow ValueShadow;
    }

    private const float MouseMoveThresholdSqr = 1f;
    private const float BorderThickness = 2f;
    private const float TrackWidth = 280f;
    private const float TrackHeight = 10f;
    private const float TrackGrabMargin = 10f;
    private const float ValueWidth = 110f;

    [Header("Menu")]
    [DisplayAs("Game Title")]
    [SerializeField] private string gameTitle = "My Game";

    [DisplayAs("Start Button Text")]
    [SerializeField] private string startText = "Start";

    [DisplayAs("Tutorial Button Text")]
    [SerializeField] private string tutorialButtonText = "How To Play";

    [Header("Starting The Game")]
    // Empty: the menu sits over this scene and simply closes. Filled in: Start loads that scene instead
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

    [DisplayAs("Reset Button Text")]
    [SerializeField] private string resetText = "Reset To Defaults";

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

    private readonly List<MenuItem> mainItems = new List<MenuItem>();
    private readonly List<MenuItem> tutorialItems = new List<MenuItem>();
    private readonly List<MenuItem> playtestItems = new List<MenuItem>();
    private readonly List<Setting> settings = new List<Setting>();
    private readonly List<Image> boxes = new List<Image>();
    private readonly List<Image> borders = new List<Image>();
    private readonly List<Shadow> textShadows = new List<Shadow>();

    private GameObject canvasObject;
    private GameObject mainPage;
    private GameObject tutorialPage;
    private GameObject playtestPage;
    private Image background;
    private Text titleText;
    private Text pageTitleText;
    private Text pageText;
    private Text playtestTitleText;

    private bool isOpen;
    private bool openedInGame;
    private Page page;
    private int selected;
    private Vector2 lastMousePosition;
    private MenuItem dragging;
    private float gameSpeed = 1f;

    public bool IsOpen => isOpen;

    private List<MenuItem> CurrentItems
    {
        get
        {
            switch (page)
            {
                case Page.Tutorial: return tutorialItems;
                case Page.Playtest: return playtestItems;
                default: return mainItems;
            }
        }
    }

    // ---------- Lifecycle ----------

    private void Awake()
    {
        if (player == null) player = FindAnyObjectByType<FirstPersonController>();
        if (grabber == null) grabber = FindAnyObjectByType<ObjectGrabber>();
        if (dialogue == null) dialogue = FindAnyObjectByType<PlayerDialogue>();
        if (gameComplete == null) gameComplete = FindAnyObjectByType<GameComplete>();
    }

    // Built in Start so every NPC has registered itself and every value has its starting setting
    private void Start()
    {
        BuildSettings();
        BuildUI();
        ShowMenu();
    }

    private void OnDestroy()
    {
        if (isOpen) Time.timeScale = 1f;
        if (canvasObject != null) Destroy(canvasObject);
    }

    private void Update()
    {
        if (!isOpen)
        {
            if (CanOpenPlaytestInGame() && Keyboard.current.f1Key.wasPressedThisFrame) OpenPlaytestInGame();
            return;
        }

        HoldGameplay();

        if (Keyboard.current == null || Mouse.current == null) return;

        if (!HandleKeyboard()) HandleMouse();
        RefreshVisuals();
    }

    // ---------- Public actions (usable from UnityEvents) ----------

    public void ShowMenu()
    {
        Open(false);
        ShowPage(Page.Main);
    }

    public void StartGame()
    {
        if (!string.IsNullOrWhiteSpace(sceneToLoad))
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(sceneToLoad.Trim());
            return;
        }

        Close();
    }

    public void ShowTutorial() => ShowPage(Page.Tutorial);

    public void ShowMainPage() => ShowPage(Page.Main);

    public void ShowPlaytest()
    {
        if (isOpen) ShowPage(Page.Playtest);
        else OpenPlaytestInGame();
    }

    public void ResetSettings()
    {
        foreach (Setting setting in settings) setting.Set(setting.Default);
    }

    // ---------- Opening and closing ----------

    private void Open(bool inGame)
    {
        isOpen = true;
        openedInGame = inGame;
        dragging = null;
        canvasObject.SetActive(true);

        if (pauseWhileOpen || inGame) Time.timeScale = 0f;

        lastMousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        HoldGameplay();
    }

    private void Close()
    {
        isOpen = false;
        openedInGame = false;
        dragging = null;
        canvasObject.SetActive(false);
        Time.timeScale = gameSpeed;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        StartCoroutine(ReleaseInputNextFrame());
    }

    private void OpenPlaytestInGame()
    {
        Open(true);
        ShowPage(Page.Playtest);
        RefreshVisuals();
    }

    private bool CanOpenPlaytestInGame()
    {
        if (!allowInGamePlaytest || Keyboard.current == null) return false;
        if (gameComplete != null && gameComplete.HasEnded) return false;
        return dialogue == null || !dialogue.IsTalking;
    }

    // From the Playtest page opened with F1, Back returns to the game; otherwise to the main page
    private void GoBack()
    {
        if (page == Page.Playtest && openedInGame) Close();
        else ShowPage(Page.Main);
    }

    private void ShowPage(Page newPage)
    {
        page = newPage;
        selected = 0;
        dragging = null;

        mainPage.SetActive(page == Page.Main);
        tutorialPage.SetActive(page == Page.Tutorial);
        playtestPage.SetActive(page == Page.Playtest);
    }

    // Keeps the player frozen and the cursor free while the menu is up
    private void HoldGameplay()
    {
        if (player != null) player.InputLocked = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // Waits a frame so the click that closed the menu isn't also read as a grab or a talk
    private IEnumerator ReleaseInputNextFrame()
    {
        yield return null;
        if (!isOpen && player != null) player.InputLocked = false;
    }

    // ---------- Playtest settings ----------

    private void BuildSettings()
    {
        settings.Clear();

        if (player != null)
        {
            FirstPersonController p = player;
            AddSlider("Walk Speed", 0.5f, 15f, 0.5f, "0.0", () => p.WalkSpeed, v => p.WalkSpeed = v);
            AddSlider("Sprint Speed", 0.5f, 25f, 0.5f, "0.0", () => p.SprintSpeed, v => p.SprintSpeed = v);
            AddSlider("Mouse Sensitivity", 0.01f, 0.5f, 0.01f, "0.00", () => p.MouseSensitivity, v => p.MouseSensitivity = v);
            AddToggle("Invert Mouse Up/Down", () => p.InvertY, v => p.InvertY = v);
        }

        if (grabber != null)
        {
            ObjectGrabber g = grabber;
            AddSlider("Grab Reach", 1f, 8f, 0.25f, "0.00", () => g.Reach, v => g.Reach = v);
            AddSlider("Grab Aim Forgiveness", 0f, 1f, 0.05f, "0.00", () => g.AimForgiveness, v => g.AimForgiveness = v);
            AddSlider("Heaviest Liftable Mass", 1f, 100f, 1f, "0", () => g.MaxMass, v => g.MaxMass = v);
        }

        if (dialogue != null)
        {
            PlayerDialogue d = dialogue;
            AddSlider("Talk Distance", 1f, 10f, 0.25f, "0.00", () => d.TalkRange, v => d.TalkRange = v);
            AddSlider("Text Speed (letters/sec)", 5f, 150f, 5f, "0", () => d.LettersPerSecond, v => d.LettersPerSecond = v);
        }

        // NPC values show the first NPC's setting and change every NPC at once
        if (NPCPatrol.All.Count > 0)
        {
            AddSlider("NPC Walk Speed", 0.25f, 6f, 0.25f, "0.00", () => ReadNPC(n => n.WalkSpeed), v => ForEachNPC(n => n.WalkSpeed = v));
            AddToggle("NPC Random Wait", () => ReadNPC(n => n.UseRandomWait), v => ForEachNPC(n => n.UseRandomWait = v));
            AddSlider("NPC Wait (not random)", 0f, 30f, 0.5f, "0.0", () => ReadNPC(n => n.WaitTime), v => ForEachNPC(n => n.WaitTime = v));
            AddSlider("NPC Shortest Random Wait", 0f, 30f, 0.5f, "0.0", () => ReadNPC(n => n.ShortestWait), v => ForEachNPC(n => n.ShortestWait = v));
            AddSlider("NPC Longest Random Wait", 0f, 30f, 0.5f, "0.0", () => ReadNPC(n => n.LongestWait), v => ForEachNPC(n => n.LongestWait = v));
        }

        // Applied when the menu closes, since the game is paused while it's open
        AddSlider("Game Speed", 0.25f, 3f, 0.25f, "0.00x", () => gameSpeed, v => gameSpeed = v);
    }

    private void AddSlider(string label, float min, float max, float step, string format, Func<float> get, Action<float> set)
    {
        settings.Add(new Setting
        {
            Label = label,
            Min = min,
            Max = max,
            Step = step,
            Format = format,
            Get = get,
            Set = set,
            Default = get()
        });
    }

    private void AddToggle(string label, Func<bool> get, Action<bool> set)
    {
        settings.Add(new Setting
        {
            Label = label,
            Min = 0f,
            Max = 1f,
            Step = 1f,
            IsToggle = true,
            Get = () => get() ? 1f : 0f,
            Set = v => set(v > 0.5f),
            Default = get() ? 1f : 0f
        });
    }

    // Reads the first NPC's value, or a default if every NPC has since been removed
    private static T ReadNPC<T>(Func<NPCPatrol, T> read) =>
        NPCPatrol.All.Count > 0 && NPCPatrol.All[0] != null ? read(NPCPatrol.All[0]) : default;

    private static void ForEachNPC(Action<NPCPatrol> apply)
    {
        foreach (NPCPatrol npc in NPCPatrol.All)
        {
            if (npc != null) apply(npc);
        }
    }

    private static void ApplyValue(Setting setting, float value)
    {
        if (setting.Step > 0f) value = setting.Min + Mathf.Round((value - setting.Min) / setting.Step) * setting.Step;
        setting.Set(Mathf.Clamp(value, setting.Min, setting.Max));
    }

    private static void Nudge(Setting setting, int direction)
    {
        if (setting.IsToggle) Toggle(setting);
        else ApplyValue(setting, setting.Get() + direction * setting.Step);
    }

    private static void Toggle(Setting setting) => setting.Set(setting.Get() > 0.5f ? 0f : 1f);

    private static string ValueText(Setting setting)
    {
        if (setting.IsToggle) return setting.Get() > 0.5f ? "On" : "Off";
        return setting.Get().ToString(setting.Format);
    }

    // ---------- Input ----------

    // Returns true if something was pressed this frame
    private bool HandleKeyboard()
    {
        Keyboard keyboard = Keyboard.current;
        List<MenuItem> items = CurrentItems;

        if (keyboard.escapeKey.wasPressedThisFrame && page != Page.Main)
        {
            GoBack();
            return true;
        }

        if (keyboard.f1Key.wasPressedThisFrame && openedInGame)
        {
            Close();
            return true;
        }

        if (items.Count == 0) return false;

        if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
            selected = (selected - 1 + items.Count) % items.Count;
        if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame)
            selected = (selected + 1) % items.Count;

        MenuItem item = items[selected];

        int direction = (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame ? 1 : 0)
                      - (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame ? 1 : 0);
        if (direction != 0 && item.Setting != null) Nudge(item.Setting, direction);

        bool confirm = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                       || keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame;
        if (!confirm) return false;

        Press(item);
        return true;
    }

    private void HandleMouse()
    {
        List<MenuItem> items = CurrentItems;
        Vector2 mousePosition = Mouse.current.position.ReadValue();

        // Dragging a slider keeps going until the button is let go, even outside the row
        if (dragging != null)
        {
            if (Mouse.current.leftButton.isPressed) SetFromTrack(dragging, mousePosition);
            else dragging = null;
            return;
        }

        // Hover only changes the selection when the mouse moves, so it doesn't fight the keyboard
        bool mouseMoved = (mousePosition - lastMousePosition).sqrMagnitude > MouseMoveThresholdSqr;
        if (mouseMoved) lastMousePosition = mousePosition;

        bool clicked = Mouse.current.leftButton.wasPressedThisFrame;
        float scroll = Mouse.current.scroll.ReadValue().y;

        for (int i = 0; i < items.Count; i++)
        {
            MenuItem item = items[i];
            if (!RectTransformUtility.RectangleContainsScreenPoint(item.Rect, mousePosition, null)) continue;

            if (mouseMoved) selected = i;

            bool isSlider = item.Setting != null && !item.Setting.IsToggle;
            if (isSlider && scroll != 0f) Nudge(item.Setting, scroll > 0f ? 1 : -1);

            if (!clicked) return;
            selected = i;

            if (!isSlider) Press(item);
            else if (IsOverTrack(item, mousePosition))
            {
                dragging = item;
                SetFromTrack(item, mousePosition);
            }

            return;
        }
    }

    private static void Press(MenuItem item)
    {
        if (item.Setting == null) item.OnPress?.Invoke();
        else if (item.Setting.IsToggle) Toggle(item.Setting);
    }

    private static bool IsOverTrack(MenuItem item, Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(item.Track, screenPoint, null, out Vector2 local);
        Rect rect = item.Track.rect;
        return local.x >= rect.xMin - TrackGrabMargin && local.x <= rect.xMax + TrackGrabMargin;
    }

    private static void SetFromTrack(MenuItem item, Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(item.Track, screenPoint, null, out Vector2 local);
        Rect rect = item.Track.rect;
        float fraction = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
        ApplyValue(item.Setting, Mathf.Lerp(item.Setting.Min, item.Setting.Max, fraction));
    }

    // ---------- Visuals ----------

    // Applied every frame so Inspector tweaks show up live
    private void RefreshVisuals()
    {
        background.color = backgroundColor;
        foreach (Image box in boxes) box.color = boxColor;
        foreach (Image border in borders) border.color = borderColor;
        foreach (Shadow shadow in textShadows) shadow.effectColor = shadowColor;

        titleText.text = gameTitle;
        titleText.color = titleColor;
        pageTitleText.text = tutorialTitle;
        pageTitleText.color = titleColor;
        pageText.text = tutorialText;
        pageText.color = textColor;
        playtestTitleText.text = playtestTitle;
        playtestTitleText.color = titleColor;

        List<MenuItem> items = CurrentItems;
        for (int i = 0; i < items.Count; i++) RefreshItem(items[i], i == selected);
    }

    private void RefreshItem(MenuItem item, bool isSelected)
    {
        Color text = isSelected ? selectedTextColor : buttonColor;

        item.Back.color = isSelected ? selectedBackColor : Color.clear;
        item.Label.color = text;
        item.LabelShadow.enabled = !isSelected;

        if (item.Setting == null)
        {
            item.Label.text = item.GetText();
            return;
        }

        item.Label.text = item.Setting.Label;
        item.Value.text = ValueText(item.Setting);
        item.Value.color = text;
        item.ValueShadow.enabled = !isSelected;

        if (item.Track == null) return;

        float fraction = Mathf.InverseLerp(item.Setting.Min, item.Setting.Max, item.Setting.Get());
        item.TrackImage.color = trackColor;
        item.FillImage.color = isSelected ? selectedTextColor : titleColor;
        item.Fill.anchorMax = new Vector2(fraction, 1f);
    }

    // ---------- UI construction ----------

    private void BuildUI()
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        canvasObject = new GameObject("Main Menu UI", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;   // Above the dialogue UI

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        background = MakeImage(canvasObject.transform, "Background", backgroundColor);
        Stretch(background.rectTransform);

        BuildMainPage();
        BuildTutorialPage();
        BuildPlaytestPage();

        canvasObject.SetActive(false);
    }

    private void BuildMainPage()
    {
        mainPage = MakePage("Main Page", 56f);

        titleText = MakeText(mainPage.transform, "Title", titleSize, titleColor, TextAnchor.MiddleCenter);
        titleText.rectTransform.sizeDelta = new Vector2(1600f, 0f);

        Image box = MakeBox(mainPage.transform, "Buttons", buttonsWidth, new RectOffset(16, 16, 14, 14), 2f);
        mainItems.Add(MakeButton(box.transform, () => startText, StartGame));
        mainItems.Add(MakeButton(box.transform, () => tutorialButtonText, ShowTutorial));
        if (showPlaytestButton) mainItems.Add(MakeButton(box.transform, () => playtestButtonText, ShowPlaytest));
    }

    private void BuildTutorialPage()
    {
        tutorialPage = MakePage("How To Play Page", 0f);

        Image box = MakeBox(tutorialPage.transform, "How To Play", tutorialWidth, new RectOffset(36, 36, 28, 24), 18f);
        pageTitleText = MakeText(box.transform, "Page Title", pageTitleSize, titleColor, TextAnchor.UpperLeft);
        pageText = MakeText(box.transform, "Page Text", pageTextSize, textColor, TextAnchor.UpperLeft);
        pageText.lineSpacing = 1.2f;

        tutorialItems.Add(MakeButton(box.transform, () => backText, ShowMainPage));
    }

    private void BuildPlaytestPage()
    {
        playtestPage = MakePage("Playtest Page", 0f);

        Image box = MakeBox(playtestPage.transform, "Playtest", playtestWidth, new RectOffset(28, 28, 24, 20), 2f);
        playtestTitleText = MakeText(box.transform, "Page Title", pageTitleSize, titleColor, TextAnchor.UpperLeft);

        foreach (Setting setting in settings) playtestItems.Add(MakeSettingRow(box.transform, setting));

        playtestItems.Add(MakeButton(box.transform, () => resetText, ResetSettings));
        playtestItems.Add(MakeButton(box.transform, () => backText, GoBack));
    }

    // A full-screen layer that stacks its children in the middle of the screen
    private GameObject MakePage(string objectName, float spacing)
    {
        var pageObject = new GameObject(objectName, typeof(RectTransform), typeof(VerticalLayoutGroup));
        pageObject.transform.SetParent(canvasObject.transform, false);
        Stretch((RectTransform)pageObject.transform);

        VerticalLayoutGroup layout = pageObject.GetComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        return pageObject;
    }

    private Image MakeBox(Transform parent, string objectName, float width, RectOffset padding, float spacing)
    {
        Image box = MakeImage(parent, objectName, boxColor);
        box.rectTransform.sizeDelta = new Vector2(width, 0f);
        boxes.Add(box);

        borders.Add(MakeEdge(box.transform, "Border Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, BorderThickness)));
        borders.Add(MakeEdge(box.transform, "Border Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, BorderThickness)));
        borders.Add(MakeEdge(box.transform, "Border Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(BorderThickness, 0f)));
        borders.Add(MakeEdge(box.transform, "Border Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(BorderThickness, 0f)));

        VerticalLayoutGroup layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = padding;
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return box;
    }

    private Image MakeEdge(Transform box, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
    {
        Image edge = MakeImage(box, objectName, borderColor);
        edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        RectTransform rect = edge.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        return edge;
    }

    private GameObject MakeRow(Transform parent, string objectName, float minHeight, out Image back)
    {
        var row = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        back = row.GetComponent<Image>();
        back.color = Color.clear;
        back.raycastTarget = false;

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 6, 6);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        row.GetComponent<LayoutElement>().minHeight = minHeight;
        return row;
    }

    private MenuItem MakeButton(Transform parent, Func<string> getText, Action onPress)
    {
        GameObject row = MakeRow(parent, "Button", buttonSize + 20f, out Image back);

        Text label = MakeText(row.transform, "Label", buttonSize, buttonColor, TextAnchor.MiddleCenter);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        label.text = getText();

        return new MenuItem
        {
            Rect = (RectTransform)row.transform,
            Back = back,
            Label = label,
            LabelShadow = label.GetComponent<Shadow>(),
            GetText = getText,
            OnPress = onPress
        };
    }

    // Label on the left, slider in the middle (sliders only), value on the right
    private MenuItem MakeSettingRow(Transform parent, Setting setting)
    {
        GameObject row = MakeRow(parent, setting.Label, settingTextSize + 16f, out Image back);
        var item = new MenuItem { Rect = (RectTransform)row.transform, Back = back, Setting = setting };

        item.Label = MakeText(row.transform, "Label", settingTextSize, buttonColor, TextAnchor.MiddleLeft);
        item.Label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        item.LabelShadow = item.Label.GetComponent<Shadow>();

        if (setting.IsToggle)
        {
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(row.transform, false);
            spacer.GetComponent<LayoutElement>().minWidth = TrackWidth;
        }
        else
        {
            item.TrackImage = MakeImage(row.transform, "Track", trackColor);
            item.Track = item.TrackImage.rectTransform;
            LayoutElement trackLayout = item.TrackImage.gameObject.AddComponent<LayoutElement>();
            trackLayout.minWidth = TrackWidth;
            trackLayout.preferredWidth = TrackWidth;
            trackLayout.minHeight = TrackHeight;
            trackLayout.preferredHeight = TrackHeight;

            item.FillImage = MakeImage(item.Track, "Fill", titleColor);
            item.Fill = item.FillImage.rectTransform;
            item.Fill.anchorMin = Vector2.zero;
            item.Fill.anchorMax = new Vector2(0f, 1f);
            item.Fill.offsetMin = Vector2.zero;
            item.Fill.offsetMax = Vector2.zero;
        }

        item.Value = MakeText(row.transform, "Value", settingTextSize, buttonColor, TextAnchor.MiddleRight);
        LayoutElement valueLayout = item.Value.gameObject.AddComponent<LayoutElement>();
        valueLayout.minWidth = ValueWidth;
        valueLayout.preferredWidth = ValueWidth;
        item.ValueShadow = item.Value.GetComponent<Shadow>();

        return item;
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
        textShadows.Add(shadow);

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