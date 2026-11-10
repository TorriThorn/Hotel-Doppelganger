using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Salem's journal: an Items tab for the inventory and a Bestiary tab for the monsters. Opens and closes with J,
// and is browsed with the mouse. A clue or quest item can be looked over in the hand and goes into the inventory
// when let go, and a clue's page shows its note and what Salem thought. Placement Zone pieces drop as normal
// so they can be set in place. Items with a recipe can be combined, like Silent Hill, and only the items that fit
// are offered. In a conversation it also opens to present an item, like Ace Attorney.
// If there's no Journal in the scene, one is made
[AddComponentMenu("Game/UI/Journal")]
public class Journal : MonoBehaviour
{
    private enum Tab
    {
        Items,
        Bestiary
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

        // Left off, the entry shows as ??? until something calls UnlockCreature with its name
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

    // Above the dialogue box, below the main menu
    private const int SortingOrder = 150;

    private const float ToastTime = 3f;
    private const float ToastFade = 0.3f;
    private const string UnknownName = "???";
    private const string ThoughtHeading = "WHAT SALEM THOUGHT";
    private const string WeaknessHeading = "WEAKNESS";
    private const string PresentButtonText = "Present";
    private const string TakeOutButtonText = "Take Out";
    private const string InspectButtonText = "Inspect";
    private const string CombineButtonText = "Combine";
    private const string CancelText = "Cancel";
    private const float PictureHeight = 260f;
    private const string HintGap = "        ";

    [Header("Journal")]
    [DisplayAs("Open Key")]
    [SerializeField] private Key openKey = Key.J;

    [DisplayAs("Pause Game While Open")]
    [SerializeField] private bool pauseGame = true;

    [Header("Items")]
    [DisplayAs("Items Tab Title")]
    [SerializeField] private string itemsTitle = "Items";

    [DisplayAs("Show Controls While Holding")]
    [SerializeField] private bool showHoldControls = true;

    [DisplayAs("No Items Text")]
    [SerializeField] private string noItemsText = "Nothing yet. Pick up anything that looks important.";

    // These follow the item's name, e.g. "Geno's Tool added to inventory"
    [DisplayAs("Item Combined Message")]
    [SerializeField] private string combinedText = "made by combining";

    [DisplayAs("Item Added Message")]
    [SerializeField] private string itemAddedText = "added to inventory";

    [DisplayAs("Item Put Away Message")]
    [SerializeField] private string itemPutAwayText = "put away";

    [Header("Bestiary")]
    [DisplayAs("Bestiary Tab Title")]
    [SerializeField] private string bestiaryTitle = "Bestiary";

    [DisplayAs("Unknown Creature Text")]
    [SerializeField] private string unknownCreatureText = "Salem hasn't come across this one yet.";

    [DisplayAs("Bestiary Updated Message")]
    [SerializeField] private string bestiaryUpdatedText = "Bestiary updated";

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

    // The box runs from near the top of the screen to near the bottom, leaving this much space at each end
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
    private readonly List<Grabbable> items = new List<Grabbable>();
    private readonly HashSet<Grabbable> putAway = new HashSet<Grabbable>();
    private readonly HashSet<Grabbable> questItems = new HashSet<Grabbable>();
    private readonly HashSet<Grabbable> zonePieces = new HashSet<Grabbable>();

    private MenuList itemsList;
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
    private RectTransform bestiaryColumn;
    private Text itemsTabText;
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

    // The journal if one exists, without making one
    public static Journal Current => instance;

    public static bool IsPresenting => instance != null && instance.presenting;

    private MenuList CurrentList => tab == Tab.Bestiary ? bestiaryList : itemsList;

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

    // Any object an NPC asks to be shown counts as a quest item. Anything a Placement Zone takes has to be carried there
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

    public bool IsInventoryItem(Grabbable item)
    {
        if (item == null) return false;
        if (item.inventoryItem || questItems.Contains(item)) return true;
        return item.TryGetComponent(out Clue clue) && clue.AddsToInventory;
    }

    // Also usable from UnityEvents, e.g. to hand the player an item. Adding one that's already there does nothing
    public void AddItem(Grabbable item)
    {
        if (item == null || items.Contains(item)) return;

        // Rows show whatever item is at their place in the sorted list, so sorting moves the names, not the rows
        Grabbable selected = SelectedItem;
        items.Add(item);
        items.Sort(CompareItems);
        int place = items.Count - 1;
        itemsList.AddButton(itemsColumn, () => items[place].DisplayName, null);
        newestItem = item;

        KeepSelection(itemsList, items.IndexOf(selected), tab == Tab.Items);
        Announce(item.DisplayName + " " + itemAddedText);
    }

    // Opens on the Items tab with a Present button. The callback gets the chosen item, or null if the player backs out
    public void OpenToPresent(Action<Grabbable> picked)
    {
        if (isOpen) return;

        presenting = true;
        presentCallback = picked;
        OpenPanel();
        ShowTab(Tab.Items);
    }

    // For UnityEvents, e.g. a dialogue choice's On Chosen once Salem learns what a suspect really is
    public void UnlockCreature(string creatureName)
    {
        Creature creature = FindCreature(creatureName);
        if (creature == null || !knownCreatures.Add(creature)) return;

        Creature selected = tab == Tab.Bestiary && bestiaryOrder.Count > 0 ? bestiaryOrder[Mathf.Clamp(bestiaryList.Selected, 0, bestiaryOrder.Count - 1)] : null;
        SortBestiary();
        KeepSelection(bestiaryList, bestiaryOrder.IndexOf(selected), tab == Tab.Bestiary);
        Announce(bestiaryUpdatedText);
    }

    // After a list is re-sorted, the same entry stays selected and an open page is redrawn
    private void KeepSelection(MenuList sorted, int index, bool onThisTab)
    {
        if (index >= 0) sorted.SetSelected(index);
        sorted.MarkDirty();
        if (isOpen && onThisTab) shownIndex = -1;
    }

    private static int CompareItems(Grabbable a, Grabbable b) =>
        string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);

    // Known creatures from A to Z, then the ??? ones in the order they're listed in the Inspector, so nothing is given away
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

    public bool IsCreatureKnown(string creatureName)
    {
        Creature creature = FindCreature(creatureName);
        return creature != null && knownCreatures.Contains(creature);
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

        // The click that opened it (e.g. on a dialogue choice) shouldn't also count as a click outside
        if (Time.frameCount == openedFrame) return;

        // The inspect view sits over the journal and takes the input until it's closed
        Mouse mouse = Mouse.current;
        if (inspector.IsOpen)
        {
            if (keyboard[openKey].wasPressedThisFrame) inspector.Close();
            else inspector.Update(mouse);
            return;
        }

        // So does the combine menu: pick a partner, or click outside or press J to cancel
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

    // The buttons under an item's page: Inspect for anything with a model, Combine when something in the
    // inventory fits with it, then Present while presenting or Take Out for something put away
    private void UpdateActionButtons(Mouse mouse)
    {
        bool showInspect = tab == Tab.Items && selectedCanInspect;
        bool showCombine = tab == Tab.Items && !presenting && partners.Count > 0;
        bool showAction = ActionLabel(SelectedItem) != null;

        bool changed = SetShown(inspectButton, showInspect);
        changed |= SetShown(combineButton, showCombine);
        changed |= SetShown(actionButton, showAction);
        if (changed) actionList.MarkDirty();
        if (!showInspect && !showCombine && !showAction) return;

        if (mouse != null) actionList.HandleInput(mouse);
        actionList.Refresh();
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

    // Every item in the inventory that has a recipe with this one, in list order
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

    // Both parts leave the inventory and the result takes their place, already put away
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
            // Made items always belong in the inventory, so letting go of one later puts it away again
            questItems.Add(result);
            result.gameObject.SetActive(false);
            AddItem(result);
            putAway.Add(result);
            itemsList.SetSelected(items.IndexOf(result));
            Announce(result.DisplayName + " " + combinedText);

            // Making a clue counts as finding it, so Salem comments and its On Found events (like unlocking
            // an NPC's Present line) run now rather than waiting for it to be taken out and picked up
            if (result.TryGetComponent(out Clue clue)) clue.MarkFound();
        }

        shownIndex = -1;
        if (!string.IsNullOrWhiteSpace(recipe.salemThinks)) DetectiveThoughts.Get().Say(recipe.salemThinks);
        recipe.onCombined?.Invoke();
    }

    // Rows show whatever item is at their place, so dropping the last row is enough to shorten the list
    private void RemoveItem(Grabbable item)
    {
        if (item == null || !items.Remove(item)) return;

        putAway.Remove(item);
        item.gameObject.SetActive(false);
        itemsList.RemoveLast();
        itemsList.MarkDirty();
        if (newestItem == item) newestItem = null;
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

    // Placement Zone pieces are carried rather than put away, so they're listed as soon as they're picked up
    private void CollectHeldItem()
    {
        Rigidbody body = grabber != null && grabber.IsHolding ? grabber.HeldObject : null;
        if (body == lastHeld) return;

        lastHeld = body;
        if (body == null || !body.TryGetComponent(out Grabbable item) || !IsInventoryItem(item)) return;
        if (zonePieces.Contains(item)) AddItem(item);
    }

    // A line along the bottom of the screen while something is held. It's worked out once per pick-up,
    // since letting go is the only thing that changes what the controls do
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

    // Letting go of an inventory item puts it away, so it can be looked over in the hand first
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

        // A new item already says "added to inventory"
        bool isNew = !items.Contains(item);
        AddItem(item);
        grabber.PutAway();
        putAway.Add(item);
        lastHeld = null;
        if (!isNew) Announce(item.DisplayName + " " + itemPutAwayText);
    }

    // Closes without touching the cursor or controls, since the conversation underneath still has them
    private void FinishPresenting(Grabbable picked)
    {
        inspector.Close();
        CloseCombine();
        presenting = false;
        isOpen = false;
        panel.SetActive(false);
        Time.timeScale = previousTimeScale;

        Action<Grabbable> callback = presentCallback;
        presentCallback = null;
        callback?.Invoke(picked);
    }

    // Clicking a tab's name opens it
    private bool TabPressed(Mouse mouse, out Tab pressed)
    {
        pressed = tab;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

        Vector2 position = mouse.position.ReadValue();
        if (RuntimeUI.Contains(itemsTabText.rectTransform, position)) pressed = Tab.Items;
        else if (RuntimeUI.Contains(bestiaryTabText.rectTransform, position)) pressed = Tab.Bestiary;

        return pressed != tab;
    }

    // Clicking anywhere off the journal closes it
    private bool ClickedOutside(Mouse mouse) =>
        mouse != null && mouse.leftButton.wasPressedThisFrame && !RuntimeUI.Contains(box.rectTransform, mouse.position.ReadValue());

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
        OpenPanel();
        // Opens on the newest item
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
        selectedCanInspect = false;
        partners.Clear();
        pageScroll.Reset();
        actionList.MarkDirty();
        if (tab == Tab.Bestiary) ShowCreature(index);
        else ShowItem(index);
    }

    // The item's description, then a clue's note, then what Salem thought of it and anything he pieced together later
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
                // The note's title only goes above it when the item is called something else, so it isn't shown twice
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

    // Unknown creatures keep their name, picture and weakness hidden
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
        // Entries only change on a click, so moving to a page's button doesn't switch the page on the way
        itemsList = new MenuList(style, true);
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
        bestiaryTabText = CreateText("Bestiary Tab", tabs.transform, 24, TextAnchor.MiddleLeft);

        // List on the left, the chosen page on the right, filling everything between the tabs and the footer
        Image row = RuntimeUI.CreateRow("Pages", box.transform, 0f, new RectOffset(0, 0, 0, 0), 48f);
        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.UpperLeft;
        rowLayout.childForceExpandHeight = true;
        row.GetComponent<LayoutElement>().flexibleHeight = 1f;

        itemsColumn = CreateColumn("Items List", row.transform, 2f);
        RuntimeUI.FixedWidth(itemsColumn, listWidth);

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

        // The page sits in a clipped window that reaches down to the footer, and only very long pages need to scroll
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

        // Inspect, Combine, then Present or Take Out, under an item's page
        inspectButton = actionList.AddButton(page, () => InspectButtonText, () => inspector.Open(SelectedItem));
        inspectButton.Back.gameObject.SetActive(false);
        combineButton = actionList.AddButton(page, () => CombineButtonText, OpenCombine);
        combineButton.Back.gameObject.SetActive(false);
        actionButton = actionList.AddButton(page, () => ActionLabel(SelectedItem) ?? string.Empty, PressActionButton);
        actionButton.Back.gameObject.SetActive(false);

        footerText = CreateText("Footer", box.transform, 20, TextAnchor.LowerRight);
        BuildCombineMenu(panelRect);

        // The controls reminder along the bottom of the screen while something is held
        holdHint = CreateText("Holding Controls", canvas.transform, 24, TextAnchor.MiddleCenter);
        holdHint.supportRichText = true;
        RectTransform hintRect = holdHint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 48f);
        hintRect.sizeDelta = new Vector2(1600f, 40f);
        holdHint.gameObject.SetActive(false);

        // A short "added to inventory" note in the corner when something new comes in
        toastText = CreateText("New Entry", canvas.transform, 24, TextAnchor.UpperRight);
        RectTransform toastRect = toastText.rectTransform;
        toastRect.anchorMin = toastRect.anchorMax = toastRect.pivot = Vector2.one;
        toastRect.anchoredPosition = new Vector2(-40f, -32f);
        toastRect.sizeDelta = new Vector2(700f, 40f);
        toast = toastText.gameObject.AddComponent<CanvasGroup>();
        toast.alpha = 0f;

        // Built last so it draws over everything else in the journal
        inspector = new ItemInspector(canvas.transform, font);

        ApplyLook();
        panel.SetActive(false);
    }

    // A small box over the journal listing only the items the chosen one combines with
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

        // The inspect view's backdrop is the box color, a touch lighter so dark items still stand out
        Color boxShade = match ? dialogue.BoxColor : boxColor;
        Color inspectBackdrop = Color.Lerp(new Color(boxShade.r, boxShade.g, boxShade.b), Color.white, 0.07f);
        inspector.ApplyLook(activeFont, line, label, style.Shadow, inspectBackdrop, highlightTag);
        foreach (Shadow shadow in shadows) shadow.effectColor = style.Shadow;
        foreach (Text text in new[] { itemsTabText, bestiaryTabText, pageTitle, pageBody, thoughtLabel, pageThought, footerText, toastText, holdHint }) text.font = activeFont;

        combineTitle.color = label;
        combineTitle.font = activeFont;
        itemsTabText.text = itemsTitle.ToUpper();
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

    // The open tab is lit like a selected row; the other matches the labels
    private void StyleTabs()
    {
        bool match = Matching;
        Color label = match ? dialogue.NameColor : labelColor;
        Color highlight = match ? dialogue.HighlightColor : highlightColor;
        itemsTabText.color = tab == Tab.Items ? highlight : label;
        bestiaryTabText.color = tab == Tab.Bestiary ? highlight : label;
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

    // Placeholder entries until the real write-ups are ready
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
}