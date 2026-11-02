using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Salem's journal: every clue picked up, with its note text and what Salem made of it, plus a running page of
// Salem's other thoughts. Opens with J. Clues add themselves; if there's no Journal in the scene, one is made.
[AddComponentMenu("Game/UI/Journal")]
public class Journal : MonoBehaviour
{
    private sealed class Entry
    {
        public string Title;
        public string Body;
        public string Thought;
    }

    private const int SortingOrder = 150;     // above the dialogue box, below the main menu
    private const float ToastTime = 3f;
    private const float ToastFade = 0.3f;
    private const string Bullet = "•  ";

    [Header("Journal")]
    [DisplayAs("Open Key")]
    [SerializeField] private Key openKey = Key.J;

    [DisplayAs("Title")]
    [SerializeField] private string title = "Journal";

    [DisplayAs("Thoughts Page Title")]
    [SerializeField] private string thoughtsTitle = "Salem's Thoughts";

    [DisplayAs("Empty Journal Text")]
    [SerializeField] private string emptyText = "Nothing yet. Pick up anything that looks important.";

    [DisplayAs("New Entry Message")]
    [SerializeField] private string newEntryText = "Journal updated";

    [DisplayAs("Pause Game While Open")]
    [SerializeField] private bool pauseGame = true;

    [Foldout("Look")]
    [DisplayAs("Match NPC Dialogue Box")]
    [SerializeField] private bool matchDialogueBox = true;

    [DisplayAs("Font (optional)", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Font font;

    [DisplayAs("Box Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color boxColor = new Color(0f, 0f, 0f, 0.92f);

    [DisplayAs("Box Border", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color borderColor = new Color(0.85f, 0.83f, 0.78f, 0.5f);

    [DisplayAs("Label Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color labelColor = new Color(0.45f, 0.44f, 0.41f);

    [DisplayAs("Text Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color textColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Highlighted Words", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color highlightColor = new Color(1f, 0.45f, 0.35f);

    [DisplayAs("Text Shadow Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [DisplayAs("Screen Dim")]
    [SerializeField] private Color dimColor = new Color(0f, 0f, 0f, 0.6f);

    [Foldout("Sizes (as on a 1080p screen)")]
    [DisplayAs("Box Width", Min = 400f)]
    [SerializeField] private float boxWidth = 1400f;

    [DisplayAs("Box Height", Min = 300f)]
    [SerializeField] private float boxHeight = 760f;

    [DisplayAs("List Width", Min = 100f)]
    [SerializeField] private float listWidth = 440f;

    [DisplayAs("List Text Size", Min = 1f)]
    [SerializeField] private int listSize = 24;

    [DisplayAs("Page Text Size", Min = 1f)]
    [SerializeField] private int pageSize = 28;

    private static Journal instance;

    private readonly List<Entry> entries = new List<Entry>();
    private readonly List<string> thoughts = new List<string>();
    private readonly HashSet<string> loggedThoughts = new HashSet<string>();
    private readonly MenuStyle style = new MenuStyle();
    private readonly List<Image> borders = new List<Image>();
    private readonly List<Shadow> shadows = new List<Shadow>();
    private readonly TypedLine typedLine = new TypedLine();

    private MenuList list;
    private Entry thoughtsEntry;
    private FirstPersonController player;
    private PlayerDialogue dialogue;
    private MainMenu menu;
    private GameComplete gameComplete;

    private Canvas canvas;
    private GameObject panel;
    private Image dim;
    private Image box;
    private RectTransform listColumn;
    private Text headerText;
    private Text pageTitle;
    private Text pageBody;
    private Text thoughtLabel;
    private Text pageThought;
    private Text footerText;
    private Text toastText;
    private CanvasGroup toast;
    private string highlightTag;

    private bool isOpen;
    private int shownIndex = -1;
    private float toastLeft;
    private float previousTimeScale = 1f;

    public bool IsOpen => isOpen;

    public static Journal Get()
    {
        if (instance == null) instance = FindAnyObjectByType<Journal>();
        if (instance == null) instance = new GameObject("Journal").AddComponent<Journal>();
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
        player = FindAnyObjectByType<FirstPersonController>();
        dialogue = FindAnyObjectByType<PlayerDialogue>();
        menu = FindAnyObjectByType<MainMenu>();
        gameComplete = FindAnyObjectByType<GameComplete>();
        BuildUI();
    }

    private void OnValidate()
    {
        if (canvas != null) ApplyLook();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (isOpen) Time.timeScale = previousTimeScale;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    // A clue's entry: its title, the note text if it has any, and what Salem thought
    public void Add(string entryTitle, string body, string thought)
    {
        var entry = new Entry { Title = entryTitle, Body = body, Thought = thought };
        entries.Add(entry);
        list.AddButton(listColumn, () => entry.Title, null);
        Announce();
    }

    // Thoughts that aren't tied to a clue all go on one page, so the list stays short
    public void AddThought(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !loggedThoughts.Add(line)) return;

        thoughts.Add(line);
        if (thoughtsEntry == null)
        {
            thoughtsEntry = new Entry { Title = thoughtsTitle };
            entries.Add(thoughtsEntry);
            list.AddButton(listColumn, () => thoughtsTitle, null);
        }

        thoughtsEntry.Body = Bullet + string.Join("\n\n" + Bullet, thoughts);
        if (isOpen && list.Selected == entries.IndexOf(thoughtsEntry)) shownIndex = -1;
        Announce();
    }

    private void Update()
    {
        UpdateToast(Time.unscaledDeltaTime);

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (!isOpen)
        {
            if (keyboard[openKey].wasPressedThisFrame && CanOpen()) Open();
            return;
        }

        HoldGameplay();

        if (keyboard[openKey].wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null) list.HandleInput(keyboard, mouse);
        list.Refresh();

        if (list.Selected != shownIndex) ShowEntry(list.Selected);
    }

    // Only from normal play: not over a conversation, a menu, a cutscene or the ending
    private bool CanOpen()
    {
        if (Time.timeScale == 0f || IntroCutscene.IsPlaying || StartupNotice.IsShowing) return false;
        if (dialogue != null && dialogue.IsTalking) return false;
        if (menu != null && menu.IsOpen) return false;
        return gameComplete == null || !gameComplete.HasEnded;
    }

    private void Open()
    {
        isOpen = true;
        panel.SetActive(true);
        toastLeft = 0f;

        previousTimeScale = Time.timeScale;
        if (pauseGame) Time.timeScale = 0f;
        HoldGameplay();

        Mouse mouse = Mouse.current;
        list.Reset(mouse != null ? mouse.position.ReadValue() : Vector2.zero);
        list.SetSelected(entries.Count - 1);
        list.Refresh();
        ShowEntry(list.Selected);
    }

    private void Close()
    {
        isOpen = false;
        panel.SetActive(false);
        Time.timeScale = previousTimeScale;
        FirstPersonController.SetCursorLocked(true);
        StartCoroutine(ReleaseInputNextFrame());
    }

    // Waits a frame so the key that closed the journal isn't also read by the game
    private IEnumerator ReleaseInputNextFrame()
    {
        yield return null;
        if (!isOpen && player != null) player.InputLocked = false;
    }

    private void HoldGameplay()
    {
        if (player != null) player.InputLocked = true;
        FirstPersonController.SetCursorLocked(false);
    }

    private void ShowEntry(int index)
    {
        shownIndex = index;

        if (entries.Count == 0)
        {
            SetPage(string.Empty, emptyText, string.Empty);
            return;
        }

        Entry entry = entries[Mathf.Clamp(index, 0, entries.Count - 1)];
        SetPage(entry.Title, entry.Body, entry.Thought);
    }

    private void SetPage(string heading, string body, string thought)
    {
        pageTitle.text = heading;
        pageTitle.gameObject.SetActive(!string.IsNullOrWhiteSpace(heading));

        typedLine.Set(body);
        pageBody.text = typedLine.Render(typedLine.Length, highlightTag);
        pageBody.gameObject.SetActive(!string.IsNullOrWhiteSpace(body));

        bool hasThought = !string.IsNullOrWhiteSpace(thought);
        thoughtLabel.gameObject.SetActive(hasThought);
        pageThought.gameObject.SetActive(hasThought);
        if (hasThought)
        {
            typedLine.Set(thought);
            pageThought.text = typedLine.Render(typedLine.Length, highlightTag);
        }
    }

    private void Announce()
    {
        if (isOpen) return;
        toastText.text = newEntryText + "   [" + openKey + "]";
        toastLeft = ToastTime;
    }

    private void UpdateToast(float dt)
    {
        if (toastLeft <= 0f)
        {
            if (toast.alpha != 0f) toast.alpha = 0f;
            return;
        }

        // Waits out conversations and menus, so it's seen once the player is back in the lounge
        if (!CanOpen())
        {
            if (toast.alpha != 0f) toast.alpha = 0f;
            return;
        }

        toastLeft -= dt;
        float shown = ToastTime - toastLeft;
        toast.alpha = Mathf.Clamp01(Mathf.Min(shown, toastLeft) / ToastFade);
    }

    private void BuildUI()
    {
        UpdateStyle();
        list = new MenuList(style);

        canvas = RuntimeUI.CreateCanvas("Journal UI", SortingOrder);

        RectTransform panelRect = RuntimeUI.CreateRect("Panel", canvas.transform);
        RuntimeUI.Stretch(panelRect);
        panel = panelRect.gameObject;

        dim = RuntimeUI.CreateImage("Dim", panelRect, dimColor);
        RuntimeUI.Stretch(dim.rectTransform);

        RectTransform column = RuntimeUI.CreateCenteredColumn("Content", panelRect, 0f);
        box = RuntimeUI.CreateBox("Journal", column, boxWidth, boxColor, borderColor, new RectOffset(40, 40, 32, 28), 20f, borders);
        box.gameObject.AddComponent<LayoutElement>().minHeight = boxHeight;

        headerText = CreateText("Header", box.transform, 22, TextAnchor.UpperLeft);

        // List of entries on the left, the chosen page on the right
        Image row = RuntimeUI.CreateRow("Pages", box.transform, boxHeight - 140f, new RectOffset(0, 0, 0, 0), 48f);
        row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
        row.GetComponent<LayoutElement>().flexibleHeight = 1f;

        listColumn = CreateColumn("List", row.transform, 2f);
        RuntimeUI.FixedWidth(listColumn, listWidth);

        RectTransform page = CreateColumn("Page", row.transform, 14f);
        RuntimeUI.FillWidth(page);

        pageTitle = CreateText("Title", page, 34, TextAnchor.UpperLeft);
        pageBody = CreateText("Body", page, pageSize, TextAnchor.UpperLeft);
        pageBody.supportRichText = true;
        pageBody.lineSpacing = 1.15f;
        thoughtLabel = CreateText("Thought Label", page, 20, TextAnchor.UpperLeft);
        pageThought = CreateText("Thought", page, pageSize, TextAnchor.UpperLeft);
        pageThought.supportRichText = true;
        pageThought.fontStyle = FontStyle.Italic;

        footerText = CreateText("Footer", box.transform, 20, TextAnchor.LowerRight);

        // A short "Journal updated" note in the corner when something new is written down
        toastText = CreateText("New Entry", canvas.transform, 24, TextAnchor.UpperRight);
        RectTransform toastRect = toastText.rectTransform;
        toastRect.anchorMin = toastRect.anchorMax = toastRect.pivot = Vector2.one;
        toastRect.anchoredPosition = new Vector2(-40f, -32f);
        toastRect.sizeDelta = new Vector2(700f, 40f);
        toast = toastText.gameObject.AddComponent<CanvasGroup>();
        toast.alpha = 0f;

        ApplyLook();
        panel.SetActive(false);
    }

    private static RectTransform CreateColumn(string name, Transform parent, float spacing)
    {
        RectTransform rect = RuntimeUI.CreateRect(name, parent);
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return rect;
    }

    private Text CreateText(string name, Transform parent, int size, TextAnchor alignment)
    {
        Text text = RuntimeUI.CreateText(name, parent, font, size, textColor, alignment, shadowColor, out Shadow shadow);
        shadows.Add(shadow);
        return text;
    }

    private bool Matching => matchDialogueBox && dialogue != null;

    private void UpdateStyle()
    {
        bool match = Matching;
        style.Font = match ? dialogue.BoxFont : font;
        style.ButtonTextSize = listSize;
        style.Text = match ? dialogue.NameColor : labelColor;
        style.SelectedText = Color.black;
        style.SelectedBack = match ? dialogue.LineColor : textColor;
        style.Shadow = match ? dialogue.ShadowColor : shadowColor;
    }

    private void ApplyLook()
    {
        UpdateStyle();
        bool match = Matching;

        Font activeFont = style.Font != null ? style.Font : RuntimeUI.DefaultFont;
        Color label = match ? dialogue.NameColor : labelColor;
        Color line = match ? dialogue.LineColor : textColor;
        Color highlight = match ? dialogue.HighlightColor : highlightColor;
        highlightTag = TypedLine.ColorTag(highlight);

        dim.color = dimColor;
        box.color = match ? dialogue.BoxColor : boxColor;
        Color border = match ? dialogue.BoxBorderColor : borderColor;
        foreach (Image edge in borders) edge.color = border;
        foreach (Shadow shadow in shadows) shadow.effectColor = style.Shadow;
        foreach (Text text in new[] { headerText, pageTitle, pageBody, thoughtLabel, pageThought, footerText, toastText }) text.font = activeFont;

        headerText.text = title.ToUpper();
        headerText.color = label;
        pageTitle.color = highlight;
        pageBody.color = line;
        pageBody.fontSize = pageSize;
        thoughtLabel.text = "WHAT SALEM THOUGHT";
        thoughtLabel.color = label;
        pageThought.color = line;
        pageThought.fontSize = pageSize;
        footerText.text = "W/S or mouse: browse      " + openKey + " / Esc: close";
        footerText.color = label;
        toastText.color = line;

        box.rectTransform.sizeDelta = new Vector2(boxWidth, 0f);
        list.MarkDirty();
        shownIndex = -1;
    }
}
