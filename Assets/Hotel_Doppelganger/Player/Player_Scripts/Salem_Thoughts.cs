using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Salem talking to themselves: short lines that appear low on screen, one after another, as hints.
// Also shows the note panel while the player holds a readable Clue.
// Call Say from any UnityEvent. If there's none in the scene, one is made with default settings the first time it's needed
[AddComponentMenu("Game/UI/Detective Thoughts")]
public class DetectiveThoughts : MonoBehaviour
{
    // Under the dialogue box, so conversations cover it
    private const int SortingOrder = 90;
    private const float FadeTime = 0.25f;

    [Header("Thoughts")]
    [DisplayAs("Speaker Name")]
    [SerializeField] private string speakerName = "Salem";

    [DisplayAs("Shortest Time On Screen (seconds)", Min = 0.5f)]
    [SerializeField] private float minimumTime = 2.5f;

    [DisplayAs("Extra Time Per Letter (seconds)", Min = 0f)]
    [SerializeField] private float timePerLetter = 0.045f;

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

    [DisplayAs("Highlighted Words", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color highlightColor = new Color(1f, 0.45f, 0.35f);

    [DisplayAs("Text Shadow Color", HideIf = nameof(matchDialogueBox))]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [Foldout("Sizes (as on a 1080p screen)")]
    [DisplayAs("Thought Text Size", Min = 1f)]
    [SerializeField] private int thoughtSize = 28;

    [DisplayAs("Thought Box Width", Min = 100f)]
    [SerializeField] private float thoughtWidth = 900f;

    [DisplayAs("Distance From Bottom")]
    [SerializeField] private float thoughtHeight = 150f;

    [DisplayAs("Note Text Size", Min = 1f)]
    [SerializeField] private int noteSize = 28;

    [DisplayAs("Note Width", Min = 100f)]
    [SerializeField] private float noteWidth = 560f;

    private static DetectiveThoughts instance;

    private readonly Queue<string> waiting = new Queue<string>();
    private readonly List<Image> borders = new List<Image>();
    private readonly List<Shadow> shadows = new List<Shadow>();
    private readonly TypedLine typedLine = new TypedLine();

    private PlayerDialogue dialogue;
    private Canvas canvas;
    private CanvasGroup thoughtGroup;
    private Image thoughtBox;
    private Text thoughtName;
    private Text thoughtText;
    private Image noteBox;
    private Text noteTitle;
    private Text noteText;
    private string highlightTag;

    private float shownFor;
    private float showTime;
    private bool showing;
    private object noteOwner;

    // Lets scripts call DetectiveThoughts.Get().Say(...) without placing one in the scene first
    public static DetectiveThoughts Get()
    {
        if (instance == null) instance = FindAnyObjectByType<DetectiveThoughts>();
        if (instance == null) instance = new GameObject("Detective Thoughts").AddComponent<DetectiveThoughts>();
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
        dialogue = FindAnyObjectByType<PlayerDialogue>();
        BuildUI();
    }

    private void OnValidate()
    {
        if (canvas != null) ApplyLook();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    // Queues a line. Words between *asterisks* are highlighted, like in dialogue
    public void Say(string line) => Think(line, true);

    // Clues pass false, since they write their own journal entry with the thought included.
    // Named differently from Say so event dropdowns only ever offer the one-box version
    public void Think(string line, bool addToJournal)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        waiting.Enqueue(line.Trim());
        if (addToJournal) Journal.Get().AddThought(line.Trim());
    }

    public void ClearThoughts()
    {
        waiting.Clear();
        showing = false;
        thoughtGroup.alpha = 0f;
    }

    // The note panel belongs to whoever opened it, so dropping one note can't close another's panel
    public void ShowNote(object owner, string title, string text)
    {
        noteOwner = owner;
        noteTitle.text = title;
        noteTitle.gameObject.SetActive(!string.IsNullOrWhiteSpace(title));
        typedLine.Set(text);
        noteText.text = typedLine.Render(typedLine.Length, highlightTag);
        noteBox.gameObject.SetActive(true);
    }

    public void HideNote(object owner)
    {
        if (noteOwner != owner) return;
        noteOwner = null;
        noteBox.gameObject.SetActive(false);
    }

    private void Update()
    {
        // Thoughts wait while a conversation, menu or cutscene has the screen, and the note hides with them
        bool busy = (dialogue != null && dialogue.IsTalking) || IntroCutscene.IsPlaying || StartupNotice.IsShowing || Time.timeScale == 0f;
        if (canvas.enabled == busy) canvas.enabled = !busy;
        if (busy) return;

        float dt = Time.deltaTime;

        if (!showing)
        {
            if (waiting.Count == 0) return;
            StartThought(waiting.Dequeue());
        }

        shownFor += dt;
        float fadeOut = showTime - shownFor;
        thoughtGroup.alpha = Mathf.Clamp01(Mathf.Min(shownFor, fadeOut) / FadeTime);

        if (shownFor >= showTime) showing = false;
    }

    private void StartThought(string line)
    {
        typedLine.Set(line);
        thoughtText.text = typedLine.Render(typedLine.Length, highlightTag);
        showTime = Mathf.Max(minimumTime, typedLine.Length * timePerLetter) + FadeTime * 2f;
        shownFor = 0f;
        showing = true;
    }

    private void BuildUI()
    {
        canvas = RuntimeUI.CreateCanvas("Detective Thoughts UI", SortingOrder);

        thoughtBox = RuntimeUI.CreateBox("Thought", canvas.transform, thoughtWidth, boxColor, borderColor, new RectOffset(24, 24, 16, 18), 4f, borders);
        RectTransform thoughtRect = thoughtBox.rectTransform;
        thoughtRect.anchorMin = thoughtRect.anchorMax = new Vector2(0.5f, 0f);
        thoughtRect.pivot = new Vector2(0.5f, 0f);
        thoughtBox.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        thoughtGroup = thoughtBox.gameObject.AddComponent<CanvasGroup>();
        thoughtGroup.alpha = 0f;
        thoughtGroup.blocksRaycasts = false;

        thoughtName = CreateText("Name", thoughtBox.transform, 20, TextAnchor.UpperLeft);
        thoughtText = CreateText("Line", thoughtBox.transform, thoughtSize, TextAnchor.UpperLeft);
        thoughtText.supportRichText = true;

        // The note sits on the right, out of the way of the carried object in the middle of the view
        noteBox = RuntimeUI.CreateBox("Note", canvas.transform, noteWidth, boxColor, borderColor, new RectOffset(28, 28, 24, 28), 12f, borders);
        RectTransform noteRect = noteBox.rectTransform;
        noteRect.anchorMin = noteRect.anchorMax = new Vector2(1f, 0.5f);
        noteRect.pivot = new Vector2(1f, 0.5f);
        noteRect.anchoredPosition = new Vector2(-60f, 0f);
        noteBox.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        noteTitle = CreateText("Title", noteBox.transform, 22, TextAnchor.UpperLeft);
        noteText = CreateText("Text", noteBox.transform, noteSize, TextAnchor.UpperLeft);
        noteText.supportRichText = true;
        noteText.lineSpacing = 1.1f;
        noteBox.gameObject.SetActive(false);

        ApplyLook();
    }

    private Text CreateText(string name, Transform parent, int size, TextAnchor alignment)
    {
        Text text = RuntimeUI.CreateText(name, parent, font, size, textColor, alignment, shadowColor, out Shadow shadow);
        shadows.Add(shadow);
        return text;
    }

    private void ApplyLook()
    {
        bool match = matchDialogueBox && dialogue != null;
        Font activeFont = match ? dialogue.BoxFont : font;
        if (activeFont == null) activeFont = RuntimeUI.DefaultFont;

        Color box = match ? dialogue.BoxColor : boxColor;
        Color border = match ? dialogue.BoxBorderColor : borderColor;
        Color name = match ? dialogue.NameColor : nameColor;
        Color line = match ? dialogue.LineColor : textColor;
        Color shadow = match ? dialogue.ShadowColor : shadowColor;
        highlightTag = TypedLine.ColorTag(match ? dialogue.HighlightColor : highlightColor);

        thoughtBox.color = box;
        noteBox.color = box;
        foreach (Image edge in borders) edge.color = border;
        foreach (Shadow effect in shadows) effect.effectColor = shadow;
        foreach (Text text in new[] { thoughtName, thoughtText, noteTitle, noteText }) text.font = activeFont;

        thoughtName.text = speakerName.ToUpper() + " (THINKING)";
        thoughtName.color = name;
        thoughtText.color = line;
        thoughtText.fontSize = thoughtSize;
        noteTitle.color = name;
        noteText.color = line;
        noteText.fontSize = noteSize;

        thoughtBox.rectTransform.sizeDelta = new Vector2(thoughtWidth, 0f);
        thoughtBox.rectTransform.anchoredPosition = new Vector2(0f, thoughtHeight);
        noteBox.rectTransform.sizeDelta = new Vector2(noteWidth, 0f);
    }
}