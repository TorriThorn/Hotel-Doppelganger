using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Component = UnityEngine.Component;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Builds screen-space UI from code, so nothing needs prefabs. Nothing raycasts, so no EventSystem is needed
public static class RuntimeUI
{
    public const float BorderThickness = 2f;

    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    private static readonly Vector2 ShadowOffset = new Vector2(2f, -2f);
    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    private static Font defaultFont;

    public static Font DefaultFont
    {
        get
        {
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return defaultFont;
        }
    }

    // Scaled canvases are laid out for 1080p and resized to fit; unscaled ones work in raw pixels
    public static Canvas CreateCanvas(string name, int sortingOrder, bool scaleWithScreen = true)
    {
        var canvas = new GameObject(name).AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        if (scaleWithScreen)
        {
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 1f;
        }

        return canvas;
    }

    public static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    public static RectTransform CreateCentered(string name, Transform parent, Vector2 size)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = Center;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        return rect;
    }

    public static Image CreateImage(string name, Transform parent, Color color)
    {
        var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static Text CreateText(string name, Transform parent, Font font, int size, Color color,
                                  TextAnchor alignment, Color shadowColor, out Shadow shadow)
    {
        var text = CreateRect(name, parent).gameObject.AddComponent<Text>();
        text.font = font != null ? font : DefaultFont;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;

        shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = shadowColor;
        shadow.effectDistance = ShadowOffset;
        return text;
    }

    // Full-screen layer that stacks its children in the middle of the screen
    public static RectTransform CreateCenteredColumn(string name, Transform parent, float spacing)
    {
        RectTransform rect = CreateRect(name, parent);
        Stretch(rect);

        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return rect;
    }

    // A bordered panel that stacks its children top to bottom. Borders go in the list for recoloring
    public static Image CreateBox(string name, Transform parent, float width, Color color, Color borderColor,
                                  RectOffset padding, float spacing, List<Image> borders)
    {
        Image box = CreateImage(name, parent, color);
        box.rectTransform.sizeDelta = new Vector2(width, 0f);

        RectTransform rect = box.rectTransform;
        borders.Add(CreateEdge(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, BorderThickness), borderColor));
        borders.Add(CreateEdge(rect, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, BorderThickness), borderColor));
        borders.Add(CreateEdge(rect, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(BorderThickness, 0f), borderColor));
        borders.Add(CreateEdge(rect, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(BorderThickness, 0f), borderColor));

        var layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = padding;
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return box;
    }

    // A horizontal strip whose background doubles as the highlight when the row is selected
    public static Image CreateRow(string name, Transform parent, float minHeight, RectOffset padding, float spacing)
    {
        Image back = CreateImage(name, parent, Color.clear);

        var layout = back.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = padding;
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        back.gameObject.AddComponent<LayoutElement>().minHeight = minHeight;
        return back;
    }

    public static LayoutElement FixedWidth(Component target, float width)
    {
        var element = target.gameObject.AddComponent<LayoutElement>();
        element.minWidth = width;
        element.preferredWidth = width;
        return element;
    }

    public static void FillWidth(Component target) => target.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static bool Contains(RectTransform rect, Vector2 screenPoint) =>
        RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);

    private static Image CreateEdge(RectTransform box, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Color color)
    {
        Image edge = CreateImage("Border", box, color);
        edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        RectTransform rect = edge.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        return edge;
    }
}

// Wheel and scroll-bar scrolling for a column pinned to the top or bottom of a clipped area.
// The bar only shows when there's more than fits; drag the handle or click the track to jump
public sealed class WheelScroll
{
    private const float StepPerNotch = 70f;
    private const float Smoothing = 14f;
    private const float BarWidth = 6f;
    private const float BarGap = 12f;
    private const float MinHandleFraction = 0.08f;

    private readonly RectTransform viewport;
    private readonly RectTransform content;
    private readonly bool pinnedToBottom;
    private readonly float restingY;
    private readonly Image track;
    private readonly Image handle;

    private float target;
    private float current;
    private float overflow;
    private bool dragging;
    private float grabOffset;

    // Narrowed so text never runs under the bar
    public WheelScroll(RectTransform viewport, RectTransform content, bool pinnedToBottom, Color trackColor, Color handleColor)
    {
        this.viewport = viewport;
        this.content = content;
        this.pinnedToBottom = pinnedToBottom;
        restingY = content.anchoredPosition.y;

        float room = BarWidth + BarGap;
        content.sizeDelta = new Vector2(content.sizeDelta.x - room, content.sizeDelta.y);
        content.anchoredPosition = new Vector2(content.anchoredPosition.x - room * content.pivot.x, restingY);

        track = RuntimeUI.CreateImage("Scroll Bar", viewport, trackColor);
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(1f, 0f);
        trackRect.anchorMax = Vector2.one;
        trackRect.pivot = new Vector2(1f, 0.5f);
        trackRect.sizeDelta = new Vector2(BarWidth, 0f);
        trackRect.anchoredPosition = Vector2.zero;

        handle = RuntimeUI.CreateImage("Handle", trackRect, handleColor);
        RectTransform handleRect = handle.rectTransform;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
        track.gameObject.SetActive(false);
    }

    public bool IsDragging => dragging;

    public void SetColors(Color trackColor, Color handleColor)
    {
        track.color = trackColor;
        handle.color = handleColor;
    }

    // True while the mouse is on the bar, so the click isn't also taken by what's behind
    public bool IsOver(Mouse mouse) =>
        dragging || (mouse != null && track.gameObject.activeSelf && RuntimeUI.Contains(track.rectTransform, mouse.position.ReadValue()));

    // Back to the pinned end at once
    public void Reset()
    {
        target = 0f;
        current = 0f;
        dragging = false;
        Apply();
    }

    // Back to the pinned end, gliding there
    public void ReturnToStart() => target = 0f;

    // Takes unscaled time, so it still scrolls while the game is paused
    public void Update(Mouse mouse, float dt)
    {
        overflow = Mathf.Max(0f, content.rect.height - viewport.rect.height);

        if (mouse != null)
        {
            Vector2 point = mouse.position.ReadValue();
            if (overflow > 0f) UpdateDrag(mouse, point);
            else dragging = false;

            float wheel = mouse.scroll.ReadValue().y;

            if (wheel != 0f && RuntimeUI.Contains(viewport, point)) target += Mathf.Sign(wheel) * StepPerNotch * (pinnedToBottom ? 1f : -1f);
        }

        target = Mathf.Clamp(target, 0f, overflow);
        if (dragging) current = target;
        else if (!Mathf.Approximately(current, target))
        {
            current = Mathf.Lerp(current, target, 1f - Mathf.Exp(-Smoothing * dt));
            if (Mathf.Abs(current - target) < 0.5f) current = target;
        }

        current = Mathf.Clamp(current, 0f, overflow);
        Apply();
        UpdateBar();
    }

    private void UpdateDrag(Mouse mouse, Vector2 point)
    {
        if (!mouse.leftButton.isPressed)
        {
            dragging = false;
            return;
        }

        float pointFromTop = FractionFromTop(point);
        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (!RuntimeUI.Contains(track.rectTransform, point)) return;

            dragging = true;
            float handleTop = HandleTop();
            bool onHandle = pointFromTop >= handleTop && pointFromTop <= handleTop + VisibleFraction();
            grabOffset = onHandle ? pointFromTop - handleTop : VisibleFraction() * 0.5f;
        }

        if (!dragging) return;

        float top = Mathf.Clamp(pointFromTop - grabOffset, 0f, 1f - VisibleFraction());
        float shownFromTop = top * content.rect.height;
        target = pinnedToBottom ? overflow - shownFromTop : shownFromTop;
    }

    private float FractionFromTop(Vector2 point)
    {
        RectTransform trackRect = track.rectTransform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRect, point, null, out Vector2 local);
        Rect rect = trackRect.rect;
        return rect.height > 0f ? Mathf.Clamp01((rect.yMax - local.y) / rect.height) : 0f;
    }

    private float VisibleFraction()
    {
        float height = content.rect.height;
        return height > 0f ? Mathf.Clamp01(viewport.rect.height / height) : 1f;
    }

    // Where the top of the visible part is, as a fraction of the whole column from its top
    private float HandleTop()
    {
        float height = content.rect.height;
        if (height <= 0f) return 0f;

        float shownFromTop = pinnedToBottom ? overflow - current : current;
        return shownFromTop / height;
    }

    private void UpdateBar()
    {
        bool show = overflow > 0f;
        if (track.gameObject.activeSelf != show) track.gameObject.SetActive(show);
        if (!show) return;

        // Stays easy to grab in a long log
        float size = Mathf.Max(VisibleFraction(), MinHandleFraction);
        float top = 1f - HandleTop() * (1f - size) / Mathf.Max(0.0001f, 1f - VisibleFraction());
        RectTransform handleRect = handle.rectTransform;
        handleRect.anchorMin = new Vector2(0f, top - size);
        handleRect.anchorMax = new Vector2(1f, top);
    }

    private void Apply()
    {
        Vector2 position = content.anchoredPosition;
        position.y = pinnedToBottom ? restingY - current : restingY + current;
        content.anchoredPosition = position;
    }
}