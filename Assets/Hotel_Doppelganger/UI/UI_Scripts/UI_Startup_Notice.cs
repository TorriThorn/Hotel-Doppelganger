using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// A full-screen notice shown once when the game first opens, before the main menu.
// A click dismisses it once it has been up for a moment
[AddComponentMenu("Game/UI/Startup Notice")]
public class StartupNotice : MonoBehaviour
{
    // Above everything, including Game Complete
    private const int SortingOrder = 400;

    private const float FadeTime = 0.5f;
    private const float HintFadeTime = 0.4f;

    // Survives scene reloads, so Play Again goes straight to the menu
    private static bool shownThisSession;

    [Header("Notice")]
    [DisplayAs("Title")]
    [SerializeField] private string title = "Work In Progress";

    [DisplayAs("Message", Lines = 4)]
    [SerializeField]
    private string message =
        "This is not the final version of Hotel Doppelganger.\n\n" +
        "All characters, art, writing and gameplay are a work in progress\n" +
        "and subject to change.";

    [DisplayAs("Build Label (optional)")]
    [SerializeField] private string buildLabel = "Playtest Build";

    [DisplayAs("Continue Hint")]
    [SerializeField] private string continueText = "Click to continue";

    [Header("Timing")]
    [DisplayAs("Can Skip After (seconds)", Min = 0f)]
    [SerializeField] private float minimumTime = 1.5f;

    [DisplayAs("Show Again On Play Again")]
    [SerializeField] private bool showOnEveryLoad;

    [Foldout("Look")]
    [DisplayAs("Font (optional)")]
    [SerializeField] private Font font;

    [DisplayAs("Background")]
    [SerializeField] private Color backgroundColor = Color.black;

    [DisplayAs("Title Color")]
    [SerializeField] private Color titleColor = new Color(0.85f, 0.83f, 0.78f);

    [DisplayAs("Text Color")]
    [SerializeField] private Color textColor = new Color(0.6f, 0.58f, 0.54f);

    [DisplayAs("Text Shadow Color")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [Foldout("Sizes (as on a 1080p screen)")]
    [DisplayAs("Title Size", Min = 1f)]
    [SerializeField] private int titleSize = 64;

    [DisplayAs("Message Size", Min = 1f)]
    [SerializeField] private int messageSize = 32;

    [DisplayAs("Small Text Size", Min = 1f)]
    [SerializeField] private int smallSize = 24;

    private readonly List<Shadow> shadows = new List<Shadow>();

    private Canvas canvas;
    private CanvasGroup group;
    private CanvasGroup textGroup;
    private Image background;
    private Text titleText;
    private Text messageText;
    private Text labelText;
    private Text hintText;
    private CanvasGroup hintGroup;

    public static bool IsShowing { get; private set; }

    // Static values can outlive a play session in the editor, so start each one fresh
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        shownThisSession = false;
        IsShowing = false;
    }

    private void Awake()
    {
        IsShowing = false;
        if (shownThisSession && !showOnEveryLoad) return;

        shownThisSession = true;
        IsShowing = true;
        BuildUI();
        StartCoroutine(ShowRoutine());
    }

    private void OnValidate()
    {
        if (canvas != null) ApplyLook();
    }

    private void OnDestroy()
    {
        if (canvas != null)
        {
            IsShowing = false;
            Destroy(canvas.gameObject);
        }
    }

    private IEnumerator ShowRoutine()
    {
        // The background is solid from the first frame, so the menu never shows through; only the words fade in
        yield return Animate(FadeTime, t => textGroup.alpha = t);

        float waitUntil = Time.unscaledTime + Mathf.Max(0f, minimumTime - FadeTime);
        while (Time.unscaledTime < waitUntil) yield return null;

        yield return Animate(HintFadeTime, t => hintGroup.alpha = t);
        while (!Clicked()) yield return null;

        // Words out first, then the black lifts to reveal the menu
        yield return Animate(FadeTime, t => textGroup.alpha = 1f - t);
        yield return Animate(FadeTime, t => group.alpha = 1f - t);

        // The menu underneath ignores input while this is up, so release it a frame after the dismissing press
        Destroy(canvas.gameObject);
        canvas = null;
        yield return null;
        IsShowing = false;
    }

    private static bool Clicked()
    {
        Mouse mouse = Mouse.current;
        return mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame);
    }

    // Eases 0 to 1 on real time, so it works even if something has paused the game
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
        canvas = RuntimeUI.CreateCanvas("Startup Notice UI", SortingOrder);

        RectTransform root = RuntimeUI.CreateRect("Root", canvas.transform);
        RuntimeUI.Stretch(root);
        group = root.gameObject.AddComponent<CanvasGroup>();

        background = RuntimeUI.CreateImage("Background", root, backgroundColor);
        RuntimeUI.Stretch(background.rectTransform);

        RectTransform words = RuntimeUI.CreateRect("Words", root);
        RuntimeUI.Stretch(words);
        textGroup = words.gameObject.AddComponent<CanvasGroup>();
        textGroup.alpha = 0f;

        RectTransform column = RuntimeUI.CreateCenteredColumn("Content", words, 28f);
        titleText = CreateText("Title", column, titleSize, 1400f);
        messageText = CreateText("Message", column, messageSize, 1400f);
        messageText.lineSpacing = 1.15f;
        labelText = CreateText("Build Label", column, smallSize, 1400f);

        // The hint sits near the bottom and only fades in once the notice can be skipped
        hintText = RuntimeUI.CreateText("Hint", words, font, smallSize, textColor, TextAnchor.MiddleCenter, shadowColor, out Shadow hintShadow);
        shadows.Add(hintShadow);
        RectTransform hintRect = hintText.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 80f);
        hintRect.sizeDelta = new Vector2(1200f, 40f);
        hintGroup = hintText.gameObject.AddComponent<CanvasGroup>();
        hintGroup.alpha = 0f;

        ApplyLook();
    }

    private Text CreateText(string name, Transform parent, int size, float width)
    {
        Text text = RuntimeUI.CreateText(name, parent, font, size, textColor, TextAnchor.MiddleCenter, shadowColor, out Shadow shadow);
        shadows.Add(shadow);
        text.rectTransform.sizeDelta = new Vector2(width, 0f);
        return text;
    }

    private void ApplyLook()
    {
        background.color = backgroundColor;

        titleText.text = title;
        titleText.color = titleColor;
        titleText.fontSize = titleSize;

        messageText.text = message;
        messageText.color = textColor;
        messageText.fontSize = messageSize;

        labelText.text = buildLabel;
        labelText.color = new Color(textColor.r, textColor.g, textColor.b, textColor.a * 0.6f);
        labelText.fontSize = smallSize;
        labelText.gameObject.SetActive(!string.IsNullOrWhiteSpace(buildLabel));

        hintText.text = continueText;
        hintText.color = textColor;
        hintText.fontSize = smallSize;

        Font activeFont = font != null ? font : RuntimeUI.DefaultFont;
        foreach (Text text in new[] { titleText, messageText, labelText, hintText }) text.font = activeFont;
        foreach (Shadow shadow in shadows) shadow.effectColor = shadowColor;
    }
}