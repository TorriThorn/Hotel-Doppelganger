using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Ends the game: fades out, then shows a Game Complete screen with Play Again and Quit.
// Trigger it by calling EndGame from any UnityEvent, e.g. a dialogue choice's On Chosen.
[AddComponentMenu("Game/UI/Game Complete")]
public class GameComplete : MonoBehaviour
{
    private const int SortingOrder = 300;   // above the dialogue box and main menu

    private static RectOffset ButtonsBoxPadding => new RectOffset(16, 16, 14, 14);

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

    private readonly MenuStyle style = new MenuStyle();
    private readonly List<Image> borders = new List<Image>();

    private MenuList buttons;
    private Canvas canvas;
    private Image fadeImage;
    private CanvasGroup content;
    private Image buttonsBox;
    private Text titleText;
    private Shadow titleShadow;
    private Text messageText;
    private Shadow messageShadow;

    private bool buttonsReady;

    public bool HasEnded { get; private set; }

    private void Awake()
    {
        if (player == null) player = FindAnyObjectByType<FirstPersonController>();
        if (dialogue == null) dialogue = FindAnyObjectByType<PlayerDialogue>();
        BuildUI();
    }

    private void OnValidate()
    {
        if (canvas != null) ApplyLook();
    }

    private void OnDestroy()
    {
        if (HasEnded) Time.timeScale = 1f;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        if (!HasEnded) return;

        HoldGameplay();

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (!buttonsReady || keyboard == null || mouse == null) return;

        buttons.HandleInput(keyboard, mouse);
        buttons.Refresh();
    }

    // Starts the ending. Calling it again while it's running does nothing.
    public void EndGame()
    {
        if (HasEnded) return;

        HasEnded = true;
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
        UnityEngine.Application.Quit();
#endif
    }

    private IEnumerator EndingRoutine()
    {
        // Let whatever triggered the ending (e.g. a dialogue choice) finish its own frame first
        yield return null;
        if (dialogue != null) dialogue.EndConversation();

        canvas.gameObject.SetActive(true);
        SetFade(0f);
        content.alpha = 0f;

        if (delayBeforeFade > 0f) yield return new WaitForSecondsRealtime(delayBeforeFade);
        yield return Animate(fadeDuration, SetFade);

        Time.timeScale = 0f;
        onGameComplete?.Invoke();

        Mouse mouse = Mouse.current;
        buttons.Reset(mouse != null ? mouse.position.ReadValue() : Vector2.zero);
        buttons.Refresh();

        yield return Animate(textFadeDuration, t => content.alpha = t);
        buttonsReady = true;
    }

    // Eases 0 to 1 on real time, so it keeps running while the game is paused
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
            apply(Mathf.SmoothStep(0f, 1f, time / duration));
            yield return null;
        }
    }

    private void SetFade(float amount)
    {
        Color color = fadeColor;
        color.a *= amount;
        fadeImage.color = color;
    }

    private void HoldGameplay()
    {
        if (player != null) player.InputLocked = true;
        FirstPersonController.SetCursorLocked(false);
    }

    private void BuildUI()
    {
        UpdateStyle();
        buttons = new MenuList(style);

        canvas = RuntimeUI.CreateCanvas("Game Complete UI", SortingOrder);

        fadeImage = RuntimeUI.CreateImage("Fade", canvas.transform, Color.clear);
        RuntimeUI.Stretch(fadeImage.rectTransform);

        RectTransform page = RuntimeUI.CreateCenteredColumn("Content", canvas.transform, 32f);
        content = page.gameObject.AddComponent<CanvasGroup>();
        content.blocksRaycasts = false;

        titleText = RuntimeUI.CreateText("Title", page, font, titleSize, titleColor, TextAnchor.MiddleCenter, shadowColor, out titleShadow);
        titleText.rectTransform.sizeDelta = new Vector2(1600f, 0f);

        messageText = RuntimeUI.CreateText("Message", page, font, messageSize, textColor, TextAnchor.MiddleCenter, shadowColor, out messageShadow);
        messageText.rectTransform.sizeDelta = new Vector2(1200f, 0f);

        buttonsBox = RuntimeUI.CreateBox("Buttons", page, buttonsWidth, boxColor, borderColor, ButtonsBoxPadding, 2f, borders);
        buttons.AddButton(buttonsBox.transform, () => playAgainText, PlayAgain);
        if (showQuitButton) buttons.AddButton(buttonsBox.transform, () => quitText, QuitGame);

        ApplyLook();
        canvas.gameObject.SetActive(false);
    }

    private void UpdateStyle()
    {
        style.Font = font;
        style.ButtonTextSize = buttonSize;
        style.Text = buttonColor;
        style.SelectedText = selectedTextColor;
        style.SelectedBack = selectedBackColor;
        style.Shadow = shadowColor;
    }

    private void ApplyLook()
    {
        UpdateStyle();

        titleText.text = title;
        titleText.color = titleColor;
        titleShadow.effectColor = shadowColor;

        messageText.text = message;
        messageText.color = textColor;
        messageShadow.effectColor = shadowColor;
        messageText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));

        buttonsBox.color = boxColor;
        foreach (Image border in borders) border.color = borderColor;

        buttons.MarkDirty();
    }
}