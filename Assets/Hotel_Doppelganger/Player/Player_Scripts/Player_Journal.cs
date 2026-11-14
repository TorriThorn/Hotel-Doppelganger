using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Salem's journal: Items, People and Bestiary tabs. Opens with J, browsed with the mouse.
// Also where items are combined, and opens to present an item in a conversation. Made if there isn't one
[AddComponentMenu("Game/UI/Journal")]
public class Journal : MonoBehaviour
{
    private enum Tab
    {
        Items,
        People,
        Bestiary
    }

    [Serializable]
    public class Person
    {
        [DisplayAs("Name")]
        public string name;

        [DisplayAs("Role")]
        public string role;

        [DisplayAs("Description", Lines = 3)]
        public string description;

        [DisplayAs("Picture (optional)")]
        public Sprite picture;

        public Person() { }

        public Person(string name, string role, string description)
        {
            this.name = name;
            this.role = role;
            this.description = description;
        }
    }

    [Serializable]
    public class Creature
    {
        [DisplayAs("Name")]
        public string name;

        [DisplayAs("Type")]
        public string kind;

        [DisplayAs("Description", Lines = 4)]
        public string description;

        [DisplayAs("Weakness", Lines = 2)]
        public string weakness;

        [DisplayAs("Picture (optional)")]
        public Sprite picture;

        // Off, it shows as ??? until UnlockCreature is called with its name
        [DisplayAs("Known From The Start")]
        public bool startsKnown = true;

        public Creature() { }

        public Creature(string name, string kind, string description, string weakness, bool startsKnown)
        {
            this.name = name;
            this.kind = kind;
            this.description = description;
            this.weakness = weakness;
            this.startsKnown = startsKnown;
        }
    }

    private const int SortingOrder = 150;

    private const float ToastTime = 3f;
    private const float ToastFade = 0.3f;
    private const string UnknownName = "???";
    private const string ThoughtHeading = "WHAT SALEM THOUGHT";
    private const string WeaknessHeading = "WEAKNESS";
    private const string NotesHeading = "NOTES";
    private const string Bullet = "\u2022  ";
    private const string NoNotesText = "Nothing yet.";
    private const string PresentButtonText = "Present";
    private const string TakeOutButtonText = "Take Out";
    private const string InspectButtonText = "Inspect";
    private const string CombineButtonText = "Combine";
    private const string CancelText = "Cancel";
    private const float PictureHeight = 260f;
    private const string HintGap = "        ";
    private const float PresentGlowLow = 0.12f;
    private const float PresentGlowHigh = 0.3f;
    private const float PresentGlowSpeed = 2.5f;
    private const float PresentHoverGlow = 0.45f;

    [Header("Journal")]
    [DisplayAs("Open Key")]
    [SerializeField] private Key openKey = Key.J;

    [DisplayAs("Pause Game While Open")]
    [SerializeField] private bool pauseGame = true;

    [DisplayAs("Show Controls While Holding")]
    [SerializeField] private bool showHoldControls = true;

    [Foldout("Tab Titles And Messages")]
    [DisplayAs("Items Tab Title")]
    [SerializeField] private string itemsTitle = "Items";

    [DisplayAs("People Tab Title")]
    [SerializeField] private string peopleTitle = "People";

    [DisplayAs("Bestiary Tab Title")]
    [SerializeField] private string bestiaryTitle = "Bestiary";

    [DisplayAs("No Items Text")]
    [SerializeField] private string noItemsText = "Nothing yet. Pick up anything that looks important.";

    [DisplayAs("Unknown Creature Text")]
    [SerializeField] private string unknownCreatureText = "Salem hasn't come across this one yet.";

    // These follow the item's name, e.g. "Geno's Tool added to inventory"
    [DisplayAs("Item Combined Message")]
    [SerializeField] private string combinedText = "made by combining";

    [DisplayAs("Item Added Message")]
    [SerializeField] private string itemAddedText = "added to inventory";

    [DisplayAs("Item Put Away Message")]
    [SerializeField] private string itemPutAwayText = "put away";

    [DisplayAs("Notes Updated Message")]
    [SerializeField] private string notesUpdatedText = "Notes updated:";

    [DisplayAs("Bestiary Updated Message")]
    [SerializeField] private string bestiaryUpdatedText = "Bestiary updated";

    [Header("People")]
    [SerializeField] private List<Person> people = DefaultPeople();

    [Header("Bestiary")]
    [SerializeField] private List<Creature> creatures = DefaultCreatures();

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

    [DisplayAs("Space Above And Below", Min = 0f, Max = 400f)]
    [SerializeField] private float screenMargin = 60f;

    [DisplayAs("List Width", Min = 100f)]
    [SerializeField] private float listWidth = 440f;

    [DisplayAs("List Text Size", Min = 1f)]
    [SerializeField] private int listSize = 24;

    [DisplayAs("Page Text Size", Min = 1f)]
    [SerializeField] private int pageSize = 28;

    private static Journal instance;

    private readonly MenuStyle style = new MenuStyle();
    private readonly List<Image> borders = new List<Image>();
    private readonly List<Shadow> shadows = new List<Shadow>();
    private readonly TypedLine typedLine = new TypedLine();
    private readonly HashSet<Creature> knownCreatures = new HashSet<Creature>();
    private readonly List<Creature> bestiaryOrder = new List<Creature>();
    private readonly List<Person> peopleOrder = new List<Person>();
    private readonly Dictionary<Person, List<string>> personNotes = new Dictionary<Person, List<string>>();
    private readonly List<Grabbable> items = new List<Grabbable>();
    private readonly HashSet<Grabbable> putAway = new HashSet<Grabbable>();
    private readonly HashSet<Grabbable> questItems = new HashSet<Grabbable>();
    private readonly HashSet<Grabbable> zonePieces = new HashSet<Grabbable>();

    private MenuList itemsList;
    private MenuList peopleList;
    private MenuList bestiaryList;
    private MenuList actionList;
    private MenuItem actionButton;
    private MenuItem inspectButton;
    private MenuItem combineButton;
    private MenuList combineList;
    private GameObject combineRoot;
    private Image combineBox;
    private Text combineTitle;
    private RectTransform combineColumn;
    private readonly List<Grabbable> partners = new List<Grabbable>();
    private ItemInspector inspector;
    private bool selectedCanInspect;
    private ObjectGrabber grabber;
    private Rigidbody lastHeld;
    private Grabbable newestItem;
    private Action<Grabbable> presentCallback;
    private bool presenting;
    private int openedFrame = -1;
    private FirstPersonController player;
    private PlayerDialogue dialogue;
    private MainMenu menu;
    private GameComplete gameComplete;

    private Canvas canvas;
    private GameObject panel;
    private Image dim;
    private Image box;
    private RectTransform itemsColumn;
    private RectTransform peopleColumn;
    private RectTransform bestiaryColumn;
    private Text itemsTabText;
    private Text peopleTabText;
    private Text bestiaryTabText;
    private Text pageTitle;
    private Text pageBody;
    private Text thoughtLabel;
    private Text pageThought;
    private Image pagePicture;
    private WheelScroll pageScroll;
    private Text footerText;
    private Text toastText;
    private Text holdHint;
    private Grabbable hintItem;
    private CanvasGroup toast;
    private string highlightTag;

    private bool isOpen;
    private Tab tab;
    private int shownIndex = -1;
    private float toastLeft;
    private float previousTimeScale = 1f;

    public bool IsOpen => isOpen;
    public bool HasItems => items.Count > 0;
    public IReadOnlyList<Grabbable> Items => items;

    public static Journal Current => instance;

    public static bool IsPresenting => instance != null && instance.presenting;

    private MenuList CurrentList
    {
        get
        {
            switch (tab)
            {
                case Tab.People: return peopleList;
                case Tab.Bestiary: return bestiaryList;
                default: return itemsList;
            }
        }
    }

    private Grabbable SelectedItem => tab == Tab.Items && items.Count > 0 ? items[Mathf.Clamp(itemsList.Selected, 0, items.Count - 1)] : null;

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
        grabber = FindAnyObjectByType<ObjectGrabber>();
        if (grabber != null) grabber.PutAwayOnRelease = PutAwayIfInventoryItem;
        BuildUI();
    }

    private void OnValidate()
    {
        if (canvas != null) ApplyLook();
    }

    // Anything an NPC asks to see is a quest item; anything a Placement Zone takes has to be carried
    private void Start()
    {
        foreach (NPCDialogue npc in FindObjectsByType<NPCDialogue>())
        {
            foreach (NPCDialogue.EventDialogue entry in npc.eventDialogue)
            {
                if (entry != null && entry.requiredItem != null) questItems.Add(entry.requiredItem);
            }
        }

        foreach (PlacementZone zone in FindObjectsByType<PlacementZone>())
        {
            if (zone.AcceptedObject != null) zonePieces.Add(zone.AcceptedObject);
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            if (grabber != null) grabber.PutAwayOnRelease = null;
        }
        if (isOpen) Time.timeScale = previousTimeScale;
        presentCallback = null;
        inspector?.Close();
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        UpdateToast(Time.unscaledDeltaTime);
        CollectHeldItem();
        UpdateHoldHint();

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (!isOpen)
        {
            if (keyboard[openKey].wasPressedThisFrame && CanOpen()) Open();
            return;
        }

        HoldGameplay();

        // So the click that opened it doesn't count as a click outside
        if (Time.frameCount == openedFrame) return;

        // The inspect view and the combine menu take the input while they're open
        Mouse mouse = Mouse.current;
        if (inspector.IsOpen)
        {
            if (keyboard[openKey].wasPressedThisFrame) inspector.Close();
            else inspector.Update(mouse);
            return;
        }

        if (combineRoot.activeSelf)
        {
            UpdateCombine(keyboard, mouse);
            return;
        }

        if (keyboard[openKey].wasPressedThisFrame || ClickedOutside(mouse))
        {
            if (presenting) FinishPresenting(null);
            else Close();
            return;
        }

        if (TabPressed(mouse, out Tab pressed))
        {
            ShowTab(pressed);
            return;
        }

        MenuList current = CurrentList;
        if (mouse != null) current.HandleInput(mouse);
        current.Refresh();
        pageScroll.Update(mouse, Time.unscaledDeltaTime);

        if (current.Selected != shownIndex) ShowEntry(current.Selected);
        UpdateActionButtons(mouse);
    }

    public bool IsInventoryItem(Grabbable item)
    {
        if (item == null) return false;
        if (item.inventoryItem || questItems.Contains(item)) return true;
        return item.TryGetComponent(out Clue clue) && clue.AddsToInventory;
    }

    // Also for UnityEvents, e.g. to hand the player an item
    public void AddItem(Grabbable item)
    {
        if (item == null || items.Contains(item)) return;

        // Rows show whatever item is at their place, so sorting moves the names, not the rows
        Grabbable selected = SelectedItem;
        items.Add(item);
        items.Sort(CompareItems);
        int place = items.Count - 1;
        itemsList.AddButton(itemsColumn, () => items[place].DisplayName, null);
        newestItem = item;

        KeepSelection(itemsList, items.IndexOf(selected), tab == Tab.Items);
        Announce(item.DisplayName + " " + itemAddedText);
    }

    // For good, e.g. something used up or stolen
    public void RemoveItem(Grabbable item)
    {
        if (item == null || !items.Remove(item)) return;

        putAway.Remove(item);
        item.gameObject.SetActive(false);
        itemsList.RemoveLast();
        itemsList.MarkDirty();
        if (newestItem == item) newestItem = null;
    }

    // The callback gets the chosen item, or null if the player backs out
    public void OpenToPresent(Action<Grabbable> picked)
    {
        if (isOpen) return;

        presenting = true;
        presentCallback = picked;
        OpenPanel();
        ShowTab(Tab.Items);
    }

    // Written as "Name: note", e.g. "James: Working on security drones." Unknown people are added
    public void AddPersonNote(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return;

        int split = entry.IndexOf(':');
        if (split <= 0) return;

        string personName = entry.Substring(0, split).Trim();
        string note = entry.Substring(split + 1).Trim();
        if (note.Length == 0) return;

        Person person = FindPerson(personName) ?? AddPerson(new Person(personName, string.Empty, string.Empty));
        if (!personNotes.TryGetValue(person, out List<string> notes))
        {
            notes = new List<string>();
            personNotes[person] = notes;
        }
        if (notes.Contains(note)) return;

        notes.Add(note);
        if (isOpen && tab == Tab.People) shownIndex = -1;
        Announce(notesUpdatedText + " " + person.name);
    }

    public void UnlockCreature(string creatureName)
    {
        Creature creature = FindCreature(creatureName);
        if (creature == null || !knownCreatures.Add(creature)) return;

        bool onBestiary = tab == Tab.Bestiary && bestiaryOrder.Count > 0;
        Creature selected = onBestiary ? bestiaryOrder[Mathf.Clamp(bestiaryList.Selected, 0, bestiaryOrder.Count - 1)] : null;
        SortBestiary();
        KeepSelection(bestiaryList, bestiaryOrder.IndexOf(selected), tab == Tab.Bestiary);
        Announce(bestiaryUpdatedText);
    }

    public bool IsCreatureKnown(string creatureName)
    {
        Creature creature = FindCreature(creatureName);
        return creature != null && knownCreatures.Contains(creature);
    }

    // Keeps the same entry selected after a re-sort
    private void KeepSelection(MenuList sorted, int index, bool onThisTab)
    {
        if (index >= 0) sorted.SetSelected(index);
        sorted.MarkDirty();
        if (isOpen && onThisTab) shownIndex = -1;
    }

    private static int CompareItems(Grabbable a, Grabbable b) =>
        string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);

    // Known creatures A to Z, then the ??? ones in Inspector order
    private void SortBestiary()
    {
        bestiaryOrder.Sort((a, b) =>
        {
            bool aKnown = knownCreatures.Contains(a);
            bool bKnown = knownCreatures.Contains(b);
            if (aKnown != bKnown) return aKnown ? -1 : 1;
            return aKnown
                ? string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase)
                : creatures.IndexOf(a).CompareTo(creatures.IndexOf(b));
        });
    }

    // Leaves the cursor and controls alone, since the conversation still has them
    private void FinishPresenting(Grabbable picked)
    {
        inspector.Close();
        CloseCombine();
        presenting = false;
        actionButton.Label.fontStyle = FontStyle.Normal;
        actionList.MarkDirty();
        isOpen = false;
        panel.SetActive(false);
        Time.timeScale = previousTimeScale;

        Action<Grabbable> callback = presentCallback;
        presentCallback = null;
        callback?.Invoke(picked);
    }

    private bool TabPressed(Mouse mouse, out Tab pressed)
    {
        pressed = tab;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

        Vector2 position = mouse.position.ReadValue();
        if (RuntimeUI.Contains(itemsTabText.rectTransform, position)) pressed = Tab.Items;
        else if (RuntimeUI.Contains(peopleTabText.rectTransform, position)) pressed = Tab.People;
        else if (RuntimeUI.Contains(bestiaryTabText.rectTransform, position)) pressed = Tab.Bestiary;

        return pressed != tab;
    }

    private bool ClickedOutside(Mouse mouse) =>
        mouse != null && mouse.leftButton.wasPressedThisFrame && !RuntimeUI.Contains(box.rectTransform, mouse.position.ReadValue());

    // Not over a conversation, a menu, a cutscene or the ending
    private bool CanOpen()
    {
        if (Time.timeScale == 0f || IntroCutscene.IsPlaying || StartupNotice.IsShowing) return false;
        if (dialogue != null && dialogue.IsTalking) return false;
        if (menu != null && menu.IsOpen) return false;
        return gameComplete == null || !gameComplete.HasEnded;
    }

    private void Open()
    {
        OpenPanel();
        ShowTab(Tab.Items);
        itemsList.SetSelected(Mathf.Max(0, items.IndexOf(newestItem)));
        itemsList.Refresh();
        ShowEntry(itemsList.Selected);
    }

    private void OpenPanel()
    {
        isOpen = true;
        openedFrame = Time.frameCount;
        panel.SetActive(true);
        toastLeft = 0f;
        UpdateFooter();

        previousTimeScale = Time.timeScale;
        if (pauseGame) Time.timeScale = 0f;
        HoldGameplay();
    }

    private void ShowTab(Tab newTab)
    {
        tab = newTab;
        itemsColumn.gameObject.SetActive(tab == Tab.Items);
        peopleColumn.gameObject.SetActive(tab == Tab.People);
        bestiaryColumn.gameObject.SetActive(tab == Tab.Bestiary);
        StyleTabs();

        Mouse mouse = Mouse.current;
        CurrentList.Reset(mouse != null ? mouse.position.ReadValue() : Vector2.zero);
        CurrentList.Refresh();
        ShowEntry(CurrentList.Selected);
    }

    private void Close()
    {
        inspector.Close();
        CloseCombine();
        isOpen = false;
        panel.SetActive(false);
        Time.timeScale = previousTimeScale;
        FirstPersonController.SetCursorLocked(true);
        StartCoroutine(ReleaseInputNextFrame());
    }

    // So the closing key isn't also read by the game
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

    private void Announce(string message)
    {
        if (isOpen) return;
        toastText.text = message + "   [" + openKey + "]";
        toastLeft = ToastTime;
    }

    private void UpdateToast(float dt)
    {
        if (toastLeft <= 0f)
        {
            if (toast.alpha != 0f) toast.alpha = 0f;
            return;
        }

        // Waits out conversations and menus
        if (!CanOpen())
        {
            if (toast.alpha != 0f) toast.alpha = 0f;
            return;
        }

        toastLeft -= dt;
        float shown = ToastTime - toastLeft;
        toast.alpha = Mathf.Clamp01(Mathf.Min(shown, toastLeft) / ToastFade);
    }

    private Person AddPerson(Person person)
    {
        Person selected = tab == Tab.People && peopleOrder.Count > 0 ? peopleOrder[Mathf.Clamp(peopleList.Selected, 0, peopleOrder.Count - 1)] : null;
        peopleOrder.Add(person);
        peopleOrder.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));

        int place = peopleOrder.Count - 1;
        peopleList.AddButton(peopleColumn, () => peopleOrder[place].name, null);
        KeepSelection(peopleList, peopleOrder.IndexOf(selected), tab == Tab.People);
        return person;
    }

    private Person FindPerson(string personName)
    {
        string key = personName.Trim();
        foreach (Person person in peopleOrder)
        {
            if (string.Equals(person.name?.Trim(), key, StringComparison.OrdinalIgnoreCase)) return person;
        }
        return null;
    }

    private Creature FindCreature(string creatureName)
    {
        if (string.IsNullOrWhiteSpace(creatureName)) return null;

        string key = creatureName.Trim();
        foreach (Creature creature in creatures)
        {
            if (creature != null && string.Equals(creature.name?.Trim(), key, StringComparison.OrdinalIgnoreCase)) return creature;
        }
        return null;
    }

    // Inspect, Combine, and Take Out or Present under an item's page
    private void UpdateActionButtons(Mouse mouse)
    {
        bool showInspect = tab == Tab.Items && !presenting && selectedCanInspect;
        bool showCombine = tab == Tab.Items && !presenting && partners.Count > 0;
        bool showAction = ActionLabel(SelectedItem) != null;

        bool changed = SetShown(inspectButton, showInspect);
        changed |= SetShown(combineButton, showCombine);
        changed |= SetShown(actionButton, showAction);
        if (changed) actionList.MarkDirty();
        if (!showInspect && !showCombine && !showAction) return;

        if (mouse != null) actionList.HandleInput(mouse);
        actionList.Refresh();
        if (presenting && showAction) StylePresentButton(mouse);
    }

    // Bold, in the dialogue's present blue, on a softly pulsing band that brightens on hover
    private void StylePresentButton(Mouse mouse)
    {
        Color blue = dialogue != null ? dialogue.PresentColor : highlightColor;
        bool hovered = mouse != null && RuntimeUI.Contains(actionButton.Rect, mouse.position.ReadValue());
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PresentGlowSpeed);

        actionButton.Label.fontStyle = FontStyle.Bold;
        actionButton.Label.color = hovered ? Color.white : blue;
        actionButton.LabelShadow.enabled = true;
        actionButton.Back.color = new Color(blue.r, blue.g, blue.b, hovered ? PresentHoverGlow : Mathf.Lerp(PresentGlowLow, PresentGlowHigh, pulse));
    }

    private static bool SetShown(MenuItem button, bool show)
    {
        GameObject row = button.Back.gameObject;
        if (row.activeSelf == show) return false;

        row.SetActive(show);
        return true;
    }

    private string ActionLabel(Grabbable item)
    {
        if (item == null) return null;
        if (presenting) return PresentButtonText;
        return putAway.Contains(item) ? TakeOutButtonText : null;
    }

    private void FindPartners(Grabbable item)
    {
        partners.Clear();
        if (item == null) return;

        foreach (Grabbable other in items)
        {
            if (other != item && FindRecipe(item, other) != null) partners.Add(other);
        }
    }

    private static Grabbable.Combination FindRecipe(Grabbable a, Grabbable b)
    {
        foreach (Grabbable.Combination recipe in a.combinations)
        {
            if (recipe != null && recipe.other == b) return recipe;
        }
        foreach (Grabbable.Combination recipe in b.combinations)
        {
            if (recipe != null && recipe.other == a) return recipe;
        }
        return null;
    }

    private void OpenCombine()
    {
        Grabbable item = SelectedItem;
        if (item == null || partners.Count == 0) return;

        combineList.Clear();
        foreach (Grabbable partner in partners)
        {
            Grabbable other = partner;
            combineList.AddButton(combineColumn, () => other.DisplayName, () => Combine(item, other));
        }
        combineList.AddButton(combineColumn, () => CancelText, CloseCombine);

        combineTitle.text = "COMBINE " + item.DisplayName.ToUpper() + " WITH";
        combineRoot.SetActive(true);

        Mouse mouse = Mouse.current;
        combineList.Reset(mouse != null ? mouse.position.ReadValue() : Vector2.zero);
        combineList.Refresh();
    }

    private void UpdateCombine(Keyboard keyboard, Mouse mouse)
    {
        bool clickedOff = mouse != null && mouse.leftButton.wasPressedThisFrame && !RuntimeUI.Contains(combineBox.rectTransform, mouse.position.ReadValue());
        if (keyboard[openKey].wasPressedThisFrame || clickedOff)
        {
            CloseCombine();
            return;
        }

        if (mouse != null) combineList.HandleInput(mouse);
        if (combineRoot.activeSelf) combineList.Refresh();
    }

    private void CloseCombine()
    {
        combineRoot.SetActive(false);
        combineList.Clear();
    }

    // Both parts leave the inventory and the result takes their place
    private void Combine(Grabbable a, Grabbable b)
    {
        Grabbable.Combination recipe = FindRecipe(a, b);
        CloseCombine();
        if (recipe == null) return;

        RemoveItem(a);
        RemoveItem(b);

        Grabbable result = recipe.result;
        if (result != null)
        {
            // So letting go of it later puts it away again
            questItems.Add(result);
            result.gameObject.SetActive(false);
            AddItem(result);
            putAway.Add(result);
            itemsList.SetSelected(items.IndexOf(result));
            Announce(result.DisplayName + " " + combinedText);

            // Making a clue counts as finding it, so its On Found events run now
            if (result.TryGetComponent(out Clue clue)) clue.MarkFound();
        }

        shownIndex = -1;
        if (!string.IsNullOrWhiteSpace(recipe.salemThinks)) DetectiveThoughts.Get().Say(recipe.salemThinks);
        recipe.onCombined?.Invoke();
    }

    private void PressActionButton()
    {
        Grabbable item = SelectedItem;
        if (item == null) return;

        if (presenting)
        {
            FinishPresenting(item);
            return;
        }

        if (!putAway.Remove(item)) return;

        Close();
        if (grabber != null && item.TryGetComponent(out Rigidbody body)) grabber.TakeOut(body);
        else item.gameObject.SetActive(true);
    }

    // Placement Zone pieces are listed as soon as they're picked up
    private void CollectHeldItem()
    {
        Rigidbody body = grabber != null && grabber.IsHolding ? grabber.HeldObject : null;
        if (body == lastHeld) return;

        lastHeld = body;
        if (body == null || !body.TryGetComponent(out Grabbable item) || !IsInventoryItem(item)) return;
        if (zonePieces.Contains(item)) AddItem(item);
    }

    // Worked out once per pick-up
    private void UpdateHoldHint()
    {
        Grabbable item = null;
        bool holding = grabber != null && grabber.IsHolding && grabber.HeldObject != null;
        if (holding) grabber.HeldObject.TryGetComponent(out item);
        else if (holdHint.text.Length > 0) holdHint.text = string.Empty;

        bool talking = dialogue != null && dialogue.IsTalking;
        bool show = showHoldControls && holding && !isOpen && !talking;
        if (holdHint.gameObject.activeSelf != show) holdHint.gameObject.SetActive(show);
        if (!show || (item == hintItem && holdHint.text.Length > 0)) return;

        hintItem = item;
        holdHint.text = HoldControls(item);
    }

    private string HoldControls(Grabbable item)
    {
        bool inventoryItem = IsInventoryItem(item);
        bool putsAway = inventoryItem && !zonePieces.Contains(item);
        string release = grabber.ToggleHold ? "Click" : "Let Go";

        string text = string.Empty;
        if (item == null || item.canRotate) text += HintKey("Right Mouse") + "Turn" + HintGap;
        text += HintKey("Scroll") + "Closer / Further" + HintGap;
        text += HintKey(release) + (putsAway ? "Put Away" : "Drop");
        return text;
    }

    private string HintKey(string key) => highlightTag + "[" + key + "]</color>  ";

    private bool PutAwayIfInventoryItem(Grabbable item)
    {
        if (!IsInventoryItem(item) || zonePieces.Contains(item)) return false;

        PutAwayHeld();
        return true;
    }

    private void PutAwayHeld()
    {
        Rigidbody body = grabber != null && grabber.IsHolding ? grabber.HeldObject : null;
        if (body == null || !body.TryGetComponent(out Grabbable item) || !IsInventoryItem(item)) return;

        bool isNew = !items.Contains(item);
        AddItem(item);
        grabber.PutAway();
        putAway.Add(item);
        lastHeld = null;
        if (!isNew) Announce(item.DisplayName + " " + itemPutAwayText);
    }

    private void ShowEntry(int index)
    {
        shownIndex = index;
        selectedCanInspect = false;
        partners.Clear();
        pageScroll.Reset();
        actionList.MarkDirty();
        if (tab == Tab.Bestiary) ShowCreature(index);
        else if (tab == Tab.People) ShowPerson(index);
        else ShowItem(index);
    }

    // Description, then a clue's note, then what Salem thought
    private void ShowItem(int index)
    {
        if (items.Count == 0)
        {
            SetPage(string.Empty, noItemsText, ThoughtHeading, string.Empty, null);
            return;
        }

        Grabbable item = items[Mathf.Clamp(index, 0, items.Count - 1)];
        string body = item.itemDescription;
        string thought = string.Empty;

        if (item.TryGetComponent(out Clue clue))
        {
            if (!string.IsNullOrWhiteSpace(clue.NoteText))
            {
                // The note's title is skipped when it's the item's name
                bool titleIsName = string.Equals(clue.NoteTitle?.Trim(), item.DisplayName, StringComparison.OrdinalIgnoreCase);
                string note = string.IsNullOrWhiteSpace(clue.NoteTitle) || titleIsName ? clue.NoteText : "*" + clue.NoteTitle.Trim() + "*\n" + clue.NoteText;
                body = string.IsNullOrWhiteSpace(body) ? note : body + "\n\n" + note;
            }

            thought = clue.Thought;
            foreach (string conclusion in clue.Conclusions)
                thought = string.IsNullOrWhiteSpace(thought) ? conclusion : thought + "\n\n" + conclusion;
        }

        selectedCanInspect = ItemInspector.CanInspect(item);
        FindPartners(item);
        SetPage(item.DisplayName, body, ThoughtHeading, thought, item.itemPicture);
    }

    private void ShowPerson(int index)
    {
        if (peopleOrder.Count == 0)
        {
            SetPage(string.Empty, NoNotesText, NotesHeading, string.Empty, null);
            return;
        }

        Person person = peopleOrder[Mathf.Clamp(index, 0, peopleOrder.Count - 1)];
        string body = string.IsNullOrWhiteSpace(person.role) ? person.description : "*" + person.role.Trim() + "*\n\n" + person.description;

        string notes = NoNotesText;
        if (personNotes.TryGetValue(person, out List<string> list) && list.Count > 0)
            notes = Bullet + string.Join("\n\n" + Bullet, list);

        SetPage(person.name, body, NotesHeading, notes, person.picture);
    }

    private void ShowCreature(int index)
    {
        if (bestiaryOrder.Count == 0)
        {
            SetPage(string.Empty, unknownCreatureText, WeaknessHeading, string.Empty, null);
            return;
        }

        Creature creature = bestiaryOrder[Mathf.Clamp(index, 0, bestiaryOrder.Count - 1)];
        if (!knownCreatures.Contains(creature))
        {
            SetPage(UnknownName, unknownCreatureText, WeaknessHeading, string.Empty, null);
            return;
        }

        string body = string.IsNullOrWhiteSpace(creature.kind) ? creature.description : "*" + creature.kind + "*\n\n" + creature.description;
        SetPage(creature.name, body, WeaknessHeading, creature.weakness, creature.picture);
    }

    private void SetPage(string heading, string body, string thoughtHeading, string thought, Sprite picture)
    {
        pagePicture.sprite = picture;
        pagePicture.gameObject.SetActive(picture != null);
        thoughtLabel.text = thoughtHeading;

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

    private static List<Person> DefaultPeople() => new List<Person>
    {
        new Person("Helena", "James's girlfriend  \u00b7  Socialite",
            "Here with James on his business trip. Dramatic and tearful. Wants James to buy her a penthouse in the city."),
        new Person("James", "Tech billionaire  \u00b7  Helena's boyfriend",
            "In town for an important business meeting. Cold, logical and dismissive. Geno was one of his loudest critics."),
        new Person("Carmela", "Certified witch  \u00b7  Potion maker",
            "Called to the hotel to deliver a sobering tonic to a drunk guest. Paranoid and avoids confrontation."),
        new Person("Zain", "Hotel guest",
            "Drinking heavily. Lost his job at James's company and seems to be running from someone."),
        new Person("Murray", "Businessman  \u00b7  Caviar distributor",
            "Ships expensive caviar in signature tins. Keeps a close eye on Zain."),
        new Person("Yaroslava", "Potion and amulet seller",
            "Uncertified magic practitioner. Bitter toward certified witches, especially Carmela.")
    };

    // Placeholders until the real write-ups are ready
    private static List<Creature> DefaultCreatures() => new List<Creature>
    {
        new Creature("Werewolf", "Shapeshifter",
            "Sample text. A person cursed to take the shape of a great wolf. Leaves *deep claw marks* and is strongest under a full moon.",
            "Sample text. *Silver*, and staying indoors on the full moon.", true),
        new Creature("Vampire", "Undead",
            "Sample text. Looks human, keeps to the shadows and never seems to eat. Can bend weaker minds to its will.",
            "Sample text. *Sunlight*, garlic and a stake through the heart.", true),
        new Creature("Thrall", "Mind controlled",
            "Sample text. Someone under another creature's control. Glassy eyes, flat voice and *gaps in their memory*.",
            "Sample text. Breaking the hold of whatever controls them.", true),
        new Creature("Fishman", "Aquatic",
            "Sample text. Passes for human on land, but sheds *scales* and is never far from water.",
            "Sample text. *Drying out*. Keep them away from water for long enough and they weaken.", false),
        new Creature("Doppelganger", "Mimic",
            "Sample text. Can take the face and voice of anyone it has studied. The real person is rarely seen again.",
            "Sample text. Questions only the real person could answer.", false)
    };

    private void BuildUI()
    {
        UpdateStyle();
        // Entries only change on a click, not on hover
        itemsList = new MenuList(style, true);
        peopleList = new MenuList(style, true);
        bestiaryList = new MenuList(style, true);
        actionList = new MenuList(style);

        canvas = RuntimeUI.CreateCanvas("Journal UI", SortingOrder);

        RectTransform panelRect = RuntimeUI.CreateRect("Panel", canvas.transform);
        RuntimeUI.Stretch(panelRect);
        panel = panelRect.gameObject;

        dim = RuntimeUI.CreateImage("Dim", panelRect, dimColor);
        RuntimeUI.Stretch(dim.rectTransform);

        box = RuntimeUI.CreateBox("Journal", panelRect, boxWidth, boxColor, borderColor, new RectOffset(40, 40, 32, 28), 20f, borders);
        RectTransform boxRect = box.rectTransform;
        boxRect.anchorMin = new Vector2(0.5f, 0f);
        boxRect.anchorMax = new Vector2(0.5f, 1f);
        boxRect.pivot = new Vector2(0.5f, 0.5f);
        boxRect.anchoredPosition = Vector2.zero;
        boxRect.sizeDelta = new Vector2(boxWidth, -2f * screenMargin);

        Image tabs = RuntimeUI.CreateRow("Tabs", box.transform, 0f, new RectOffset(0, 0, 0, 0), 40f);
        itemsTabText = CreateText("Items Tab", tabs.transform, 24, TextAnchor.MiddleLeft);
        peopleTabText = CreateText("People Tab", tabs.transform, 24, TextAnchor.MiddleLeft);
        bestiaryTabText = CreateText("Bestiary Tab", tabs.transform, 24, TextAnchor.MiddleLeft);

        Image row = RuntimeUI.CreateRow("Pages", box.transform, 0f, new RectOffset(0, 0, 0, 0), 48f);
        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.UpperLeft;
        rowLayout.childForceExpandHeight = true;
        row.GetComponent<LayoutElement>().flexibleHeight = 1f;

        itemsColumn = CreateColumn("Items List", row.transform, 2f);
        RuntimeUI.FixedWidth(itemsColumn, listWidth);

        peopleColumn = CreateColumn("People List", row.transform, 2f);
        RuntimeUI.FixedWidth(peopleColumn, listWidth);
        foreach (Person person in people)
        {
            if (person != null) AddPerson(person);
        }
        peopleColumn.gameObject.SetActive(false);

        bestiaryColumn = CreateColumn("Bestiary List", row.transform, 2f);
        RuntimeUI.FixedWidth(bestiaryColumn, listWidth);
        foreach (Creature creature in creatures)
        {
            if (creature == null) continue;
            if (creature.startsKnown) knownCreatures.Add(creature);
            bestiaryOrder.Add(creature);
        }
        SortBestiary();

        for (int i = 0; i < bestiaryOrder.Count; i++)
        {
            int place = i;
            bestiaryList.AddButton(bestiaryColumn, () => knownCreatures.Contains(bestiaryOrder[place]) ? bestiaryOrder[place].name : UnknownName, null);
        }
        bestiaryColumn.gameObject.SetActive(false);

        RectTransform pageView = RuntimeUI.CreateRect("Page View", row.transform);
        var viewSize = pageView.gameObject.AddComponent<LayoutElement>();
        viewSize.flexibleWidth = 1f;
        viewSize.flexibleHeight = 1f;
        pageView.gameObject.AddComponent<RectMask2D>();

        RectTransform page = CreateColumn("Page", pageView, 14f);
        page.anchorMin = new Vector2(0f, 1f);
        page.anchorMax = Vector2.one;
        page.pivot = new Vector2(0.5f, 1f);
        page.sizeDelta = Vector2.zero;
        page.anchoredPosition = Vector2.zero;
        page.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        pageScroll = new WheelScroll(pageView, page, false, Color.clear, Color.clear);

        pagePicture = RuntimeUI.CreateImage("Picture", page, Color.white);
        pagePicture.preserveAspect = true;
        var pictureSize = pagePicture.gameObject.AddComponent<LayoutElement>();
        pictureSize.minHeight = PictureHeight;
        pictureSize.preferredHeight = PictureHeight;
        pagePicture.gameObject.SetActive(false);

        pageTitle = CreateText("Title", page, 34, TextAnchor.UpperLeft);
        pageBody = CreateText("Body", page, pageSize, TextAnchor.UpperLeft);
        pageBody.supportRichText = true;
        pageBody.lineSpacing = 1.15f;
        thoughtLabel = CreateText("Thought Label", page, 20, TextAnchor.UpperLeft);
        pageThought = CreateText("Thought", page, pageSize, TextAnchor.UpperLeft);
        pageThought.supportRichText = true;
        pageThought.fontStyle = FontStyle.Italic;

        inspectButton = actionList.AddButton(page, () => InspectButtonText, () => inspector.Open(SelectedItem));
        inspectButton.Back.gameObject.SetActive(false);
        combineButton = actionList.AddButton(page, () => CombineButtonText, OpenCombine);
        combineButton.Back.gameObject.SetActive(false);
        actionButton = actionList.AddButton(page, () => ActionLabel(SelectedItem) ?? string.Empty, PressActionButton);
        actionButton.Back.gameObject.SetActive(false);

        footerText = CreateText("Footer", box.transform, 20, TextAnchor.LowerRight);
        BuildCombineMenu(panelRect);

        holdHint = CreateText("Holding Controls", canvas.transform, 24, TextAnchor.MiddleCenter);
        holdHint.supportRichText = true;
        RectTransform hintRect = holdHint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 48f);
        hintRect.sizeDelta = new Vector2(1600f, 40f);
        holdHint.gameObject.SetActive(false);

        toastText = CreateText("New Entry", canvas.transform, 24, TextAnchor.UpperRight);
        RectTransform toastRect = toastText.rectTransform;
        toastRect.anchorMin = toastRect.anchorMax = toastRect.pivot = Vector2.one;
        toastRect.anchoredPosition = new Vector2(-40f, -32f);
        toastRect.sizeDelta = new Vector2(700f, 40f);
        toast = toastText.gameObject.AddComponent<CanvasGroup>();
        toast.alpha = 0f;

        // Built last so it draws over everything else
        inspector = new ItemInspector(canvas.transform, font);

        ApplyLook();
        panel.SetActive(false);
    }

    private void BuildCombineMenu(RectTransform parent)
    {
        combineList = new MenuList(style);

        RectTransform rootRect = RuntimeUI.CreateRect("Combine Menu", parent);
        RuntimeUI.Stretch(rootRect);
        combineRoot = rootRect.gameObject;

        Image shade = RuntimeUI.CreateImage("Shade", rootRect, new Color(0f, 0f, 0f, 0.5f));
        RuntimeUI.Stretch(shade.rectTransform);

        RectTransform column = RuntimeUI.CreateCenteredColumn("Content", rootRect, 0f);
        combineBox = RuntimeUI.CreateBox("Combine", column, 640f, boxColor, borderColor, new RectOffset(32, 32, 26, 22), 14f, borders);
        combineTitle = CreateText("Title", combineBox.transform, 22, TextAnchor.UpperLeft);
        combineColumn = CreateColumn("Partners", combineBox.transform, 2f);

        combineRoot.SetActive(false);
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
        combineBox.color = box.color;
        Color border = match ? dialogue.BoxBorderColor : borderColor;
        foreach (Image edge in borders) edge.color = border;
        pageScroll.SetColors(new Color(border.r, border.g, border.b, border.a * 0.35f), line);

        // A touch lighter than the box so dark items still stand out
        Color boxShade = match ? dialogue.BoxColor : boxColor;
        Color inspectBackdrop = Color.Lerp(new Color(boxShade.r, boxShade.g, boxShade.b), Color.white, 0.07f);
        inspector.ApplyLook(activeFont, line, label, style.Shadow, inspectBackdrop, highlightTag);
        foreach (Shadow shadow in shadows) shadow.effectColor = style.Shadow;
        Text[] texts = { itemsTabText, peopleTabText, bestiaryTabText, pageTitle, pageBody, thoughtLabel, pageThought, footerText, toastText, holdHint };
        foreach (Text text in texts) text.font = activeFont;

        combineTitle.color = label;
        combineTitle.font = activeFont;
        itemsTabText.text = itemsTitle.ToUpper();
        peopleTabText.text = peopleTitle.ToUpper();
        bestiaryTabText.text = bestiaryTitle.ToUpper();
        StyleTabs();
        pageTitle.color = highlight;
        pageBody.color = line;
        pageBody.fontSize = pageSize;
        thoughtLabel.color = label;
        pageThought.color = line;
        pageThought.fontSize = pageSize;
        UpdateFooter();
        footerText.color = label;
        toastText.color = line;
        holdHint.color = line;
        holdHint.text = string.Empty;

        box.rectTransform.sizeDelta = new Vector2(boxWidth, -2f * screenMargin);
        itemsList.MarkDirty();
        peopleList.MarkDirty();
        bestiaryList.MarkDirty();
        actionList.MarkDirty();
        combineList.MarkDirty();
        shownIndex = -1;
    }

    private void UpdateFooter()
    {
        footerText.text = presenting
            ? "Pick an item and click Present      Click outside or press " + openKey + " to cancel"
            : "Click a tab to switch pages      Scroll to read more      Click outside or press " + openKey + " to close";
    }

    private void StyleTabs()
    {
        bool match = Matching;
        Color label = match ? dialogue.NameColor : labelColor;
        Color highlight = match ? dialogue.HighlightColor : highlightColor;
        itemsTabText.color = tab == Tab.Items ? highlight : label;
        peopleTabText.color = tab == Tab.People ? highlight : label;
        bestiaryTabText.color = tab == Tab.Bestiary ? highlight : label;
    }
}