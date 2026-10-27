using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Builds screen-space UI from code so the dialogue box and menus don't need prefabs.
// Nothing here raycasts: menus hit-test their rows themselves, so no EventSystem is required.
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

    // A filled panel with a thin border that lays its children out top to bottom.
    // The border images are added to the list so their color can be changed later.
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
