using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Shadow = UnityEngine.UI.Shadow;
using Text = UnityEngine.UI.Text;

// Ends the game: fades the screen out, then shows a Game Complete screen with Play Again and Quit.
// Trigger it from any event by calling EndGame (e.g. a dialogue choice's On Chosen or a zone's On Placed).
// Builds its own UI at runtime. Put it on any GameObject in the scene.
[AddComponentMenu("Game/UI/Game Complete")]
public class GameComplete : MonoBehaviour
{
    private class MenuButton
    {
        public RectTransform Rect;
        public Image Back;
        public Text Label;
        public Shadow Shadow;
        public Action OnPress;
    }

    private const float MouseMoveThresholdSqr = 1f;
    private const float BorderThickness = 2f;

    [Header("Screen")]
    [DisplayAs("Title")]
    [SerializeField] private string title = "Game Complete";

    [DisplayAs("Message", Lines = 3)]
    [SerializeField] private string message = "Thanks for playing!";

    [DisplayAs("Play Again Button Text")]
    [SerializeField] private string playAgainText = "Play Again";

    [DisplayAs("Show Quit Button")]
    [SerializeField] private bool showQuitButton = true;

    [DisplayAs("Quit Button Text", ShowIf = nameof(showQuitButton))]
    [SerializeField] private string quitText = "Quit";

    [Header("Fade")]
    [DisplayAs("Wait Before Fading (seconds)", Min = 0f)]
    [SerializeField] private float delayBeforeFade = 0.5f;

    [DisplayAs("Fade Out Time (seconds)", Min = 0f)]
    [SerializeField] private float fadeDuration = 2f;

    [DisplayAs("Text Fade In Time (seconds)", Min = 0f)]
    [SerializeField] private float textFadeDuration = 1f;

    [Foldout("Events")]
    public UnityEvent onGameComplete;

    [Foldout("Look")]
    [DisplayAs("Font (optional)")]
    [SerializeField] private Font font;

    [DisplayAs("Fade Color")]
    [SerializeField] private Color fadeColor = Color.black;

    [DisplayAs("Title Color")]
    [SerializeField] private Color titleColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Message Color")]
    [SerializeField] private Color textColor = new Color(0.6f, 0.58f, 0.54f);

    [DisplayAs("Button Text Color")]
    [SerializeField] private Color buttonColor = new Color(0.6f, 0.58f, 0.54f);

    [DisplayAs("Highlighted Button Text")]
    [SerializeField] private Color selectedTextColor = Color.black;

    [DisplayAs("Highlighted Button Background")]
    [SerializeField] private Color selectedBackColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Box Color")]
    [SerializeField] private Color boxColor = new Color(0f, 0f, 0f, 0.92f);

    [DisplayAs("Box Border")]
    [SerializeField] private Color borderColor = new Color(0.85f, 0.83f, 0.78f, 0.5f);

    [DisplayAs("Text Shadow Color")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [Foldout("Sizes (as on a 1080p screen)")]
    [DisplayAs("Title Text Size", Min = 1f)]
    [SerializeField] private int titleSize = 72;

    [DisplayAs("Message Text Size", Min = 1f)]
    [SerializeField] private int messageSize = 32;

    [DisplayAs("Button Text Size", Min = 1f)]
    [SerializeField] private int buttonSize = 32;

    [DisplayAs("Buttons Box Width", Min = 0f)]
    [SerializeField] private float buttonsWidth = 420f;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Player (optional)")]
    [SerializeField] private FirstPersonController player;

    [DisplayAs("Player Dialogue (optional)")]
    [SerializeField] private PlayerDialogue dialogue;

    private readonly List<MenuButton> buttons = new List<MenuButton>();

    private GameObject canvasObject;
    private Image fadeImage;
    private CanvasGroup content;
    private Text titleText;
    private Text messageText;
    private Image buttonsBox;
    private readonly List<Image> borders = new List<Image>();

    private bool hasEnded;
    private bool buttonsReady;
    private int selected;
    private Vector2 lastMousePosition;

    public bool HasEnded => hasEnded;

    // ---------- Lifecycle ----------

    private void Awake()
    {
        if (player == null) player = FindAnyObjectByType<FirstPersonController>();
        if (dialogue == null) dialogue = FindAnyObjectByType<PlayerDialogue>();
        BuildUI();
    }

    private void OnDestroy()
    {
        if (hasEnded) Time.timeScale = 1f;
        if (canvasObject != null) Destroy(canvasObject);
    }

    private void Update()
    {
        if (!hasEnded) return;

        HoldGameplay();

        if (!buttonsReady || Keyboard.current == null || Mouse.current == null) return;

        if (!HandleKeyboard()) HandleMouse();
        RefreshButtons();
    }

    // ---------- Public actions (usable from UnityEvents) ----------

    // Starts the ending. Calling it again while it's already running does nothing.
    public void EndGame()
    {
        if (hasEnded) return;

        hasEnded = true;
        StartCoroutine(EndingRoutine());
    }

    public void PlayAgain()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- Ending sequence ----------

    private IEnumerator EndingRoutine()
    {
        // Lets whatever triggered the ending (e.g. a dialogue choice) finish first
        yield return null;
        if (dialogue != null) dialogue.EndConversation();

        canvasObject.SetActive(true);
        SetFade(0f);
        content.alpha = 0f;

        if (delayBeforeFade > 0f) yield return new WaitForSecondsRealtime(delayBeforeFade);

        yield return Animate(fadeDuration, SetFade);

        Time.timeScale = 0f;
        onGameComplete?.Invoke();

        lastMousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        RefreshButtons();

        yield return Animate(textFadeDuration, t => content.alpha = t);
        buttonsReady = true;
    }

    // Runs a 0-to-1 animation on real time, so it isn't affected by pausing
    private static IEnumerator Animate(float duration, Action<float> apply)
    {
        if (duration <= 0f)
        {
            apply(1f);
            yield break;
        }

        for (float time = 0f; time < duration;)
        {
            time += Time.unscaledDeltaTime;
            apply(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / duration)));
            yield return null;
        }
    }

    private void SetFade(float amount)
    {
        Color color = fadeColor;
        color.a = fadeColor.a * amount;
        fadeImage.color = color;
    }

    // Keeps the player frozen and the cursor free once the ending has started
    private void HoldGameplay()
    {
        if (player != null) player.InputLocked = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ---------- Input ----------

    // Returns true if a button was pressed this frame
    private bool HandleKeyboard()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
            selected = (selected - 1 + buttons.Count) % buttons.Count;
        if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame)
            selected = (selected + 1) % buttons.Count;

        bool confirm = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                       || keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame;
        if (!confirm) return false;

        buttons[selected].OnPress();
        return true;
    }

    private void HandleMouse()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();

        // Hover only changes the selection when the mouse moves, so it doesn't fight the keyboard
        bool mouseMoved = (mousePosition - lastMousePosition).sqrMagnitude > MouseMoveThresholdSqr;
        if (mouseMoved) lastMousePosition = mousePosition;

        for (int i = 0; i < buttons.Count; i++)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(buttons[i].Rect, mousePosition, null)) continue;

            if (mouseMoved) selected = i;
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                buttons[i].OnPress();
                return;
            }
        }
    }

    private void RefreshButtons()
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            MenuButton button = buttons[i];
            bool isSelected = i == selected;

            button.Back.color = isSelected ? selectedBackColor : Color.clear;
            button.Label.color = isSelected ? selectedTextColor : buttonColor;
            button.Shadow.enabled = !isSelected;
        }
    }

    // ---------- UI construction ----------

    private void BuildUI()
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        canvasObject = new GameObject("Game Complete UI", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;   // Above the dialogue and main menu

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        fadeImage = MakeImage(canvasObject.transform, "Fade", Color.clear);
        Stretch(fadeImage.rectTransform);

        var page = new GameObject("Content", typeof(RectTransform), typeof(CanvasGroup), typeof(VerticalLayoutGroup));
        page.transform.SetParent(canvasObject.transform, false);
        Stretch((RectTransform)page.transform);
        content = page.GetComponent<CanvasGroup>();
        content.blocksRaycasts = false;

        VerticalLayoutGroup layout = page.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 32f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        titleText = MakeText(page.transform, "Title", titleSize, titleColor, TextAnchor.MiddleCenter);
        titleText.rectTransform.sizeDelta = new Vector2(1600f, 0f);
        titleText.text = title;

        messageText = MakeText(page.transform, "Message", messageSize, textColor, TextAnchor.MiddleCenter);
        messageText.rectTransform.sizeDelta = new Vector2(1200f, 0f);
        messageText.text = message;
        messageText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));

        buttonsBox = MakeBox(page.transform, buttonsWidth);
        buttons.Add(MakeButton(buttonsBox.transform, playAgainText, PlayAgain));
        if (showQuitButton) buttons.Add(MakeButton(buttonsBox.transform, quitText, QuitGame));

        canvasObject.SetActive(false);
    }

    private Image MakeBox(Transform parent, float width)
    {
        Image box = MakeImage(parent, "Buttons", boxColor);
        box.rectTransform.sizeDelta = new Vector2(width, 0f);

        borders.Add(MakeEdge(box.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, BorderThickness)));
        borders.Add(MakeEdge(box.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, BorderThickness)));
        borders.Add(MakeEdge(box.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(BorderThickness, 0f)));
        borders.Add(MakeEdge(box.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(BorderThickness, 0f)));

        VerticalLayoutGroup layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 14, 14);
        layout.spacing = 2f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return box;
    }

    private Image MakeEdge(Transform box, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
    {
        Image edge = MakeImage(box, "Border", borderColor);
        edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        RectTransform rect = edge.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        return edge;
    }

    private MenuButton MakeButton(Transform parent, string label, Action onPress)
    {
        var row = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        Image back = row.GetComponent<Image>();
        back.color = Color.clear;
        back.raycastTarget = false;

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        row.GetComponent<LayoutElement>().minHeight = buttonSize + 20f;

        Text text = MakeText(row.transform, "Label", buttonSize, buttonColor, TextAnchor.MiddleCenter);
        text.text = label;

        return new MenuButton
        {
            Rect = (RectTransform)row.transform,
            Back = back,
            Label = text,
            Shadow = text.GetComponent<Shadow>(),
            OnPress = onPress
        };
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
