using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// Colors, font and sizes shared by every row in a MenuList, filled in by the owning menu
public sealed class MenuStyle
{
    public Font Font;
    public int ButtonTextSize = 32;
    public int SettingTextSize = 24;
    public Color Text = Color.grey;
    public Color SelectedText = Color.black;
    public Color SelectedBack = Color.white;
    public Color Track = Color.grey;
    public Color Fill = Color.white;
    public Color Shadow = Color.black;
}

// A value a row can change: a stepped slider, an on/off switch, or a choice that cycles through names
public sealed class MenuSetting
{
    public readonly string Label;
    public readonly float Min;
    public readonly float Max;
    public readonly bool IsToggle;

    private readonly float step;
    private readonly string format;
    private readonly string[] choices;
    private readonly Func<float> get;
    private readonly Action<float> set;
    private readonly float defaultValue;

    private MenuSetting(string label, float min, float max, float step, string format, bool isToggle, string[] choices,
                        Func<float> get, Action<float> set)
    {
        Label = label;
        Min = min;
        Max = max;
        IsToggle = isToggle;
        this.step = step;
        this.format = format;
        this.choices = choices;
        this.get = get;
        this.set = set;
        defaultValue = get();
    }

    public static MenuSetting Slider(string label, float min, float max, float step, string format, Func<float> get, Action<float> set) =>
        new MenuSetting(label, min, max, step, format, false, null, get, set);

    public static MenuSetting Toggle(string label, Func<bool> get, Action<bool> set) =>
        new MenuSetting(label, 0f, 1f, 1f, null, true, null, () => get() ? 1f : 0f, v => set(v > 0.5f));

    public static MenuSetting Choice(string label, string[] choices, Func<int> get, Action<int> set) =>
        new MenuSetting(label, 0f, Mathf.Max(0, choices.Length - 1), 1f, null, false, choices, () => get(), v => set(Mathf.RoundToInt(v)));

    public bool IsChoice => choices != null;

    public bool HasTrack => !IsToggle && !IsChoice;

    public float Value => get();
    public float Fraction => Mathf.InverseLerp(Min, Max, get());

    public string ValueText
    {
        get
        {
            if (IsToggle) return get() > 0.5f ? "On" : "Off";
            if (IsChoice) return choices.Length > 0 ? choices[Mathf.Clamp(Mathf.RoundToInt(get()), 0, choices.Length - 1)] : "-";
            return get().ToString(format);
        }
    }

    public void SetValue(float value)
    {
        if (step > 0f) value = Min + Mathf.Round((value - Min) / step) * step;
        set(Mathf.Clamp(value, Min, Max));
    }

    // Choices wrap around at either end, sliders stop at their limits
    public void Nudge(int direction)
    {
        if (IsToggle) Flip();
        else if (IsChoice) set(((int)Mathf.Round(get()) + direction + choices.Length) % Mathf.Max(1, choices.Length));
        else SetValue(get() + direction * step);
    }

    public void Flip() => set(get() > 0.5f ? 0f : 1f);

    public void ResetToDefault() => set(defaultValue);
}

// One row: a button, or a setting when Setting is set
public sealed class MenuItem
{
    public Image Back;
    public Text Label;
    public Shadow LabelShadow;
    public Func<string> GetText;
    public Action OnPress;

    public MenuSetting Setting;
    public RectTransform Track;
    public Image TrackImage;
    public RectTransform Fill;
    public Image FillImage;
    public Text Value;
    public Shadow ValueShadow;

    public RectTransform Rect => Back.rectTransform;
    public bool IsSlider => Setting != null && Setting.HasTrack;
}

// A mouse-only column of rows: hover to highlight, click to press. Sliders drag or scroll; toggles and choices
// step forward on left click and back on right click. With selectOnClick, hovering doesn't select
public sealed class MenuList
{
    private const float MouseMoveThresholdSqr = 1f;
    private const float TrackGrabMargin = 10f;
    private const float TrackWidth = 280f;
    private const float TrackHeight = 10f;
    private const float ValueWidth = 110f;
    private const float WideValueExtra = 140f;

    private static RectOffset ButtonPadding => new RectOffset(12, 12, 8, 8);
    private static RectOffset SettingPadding => new RectOffset(12, 12, 6, 6);

    private readonly List<MenuItem> items = new List<MenuItem>();
    private readonly MenuStyle style;
    private readonly bool selectOnClick;

    private int selected;
    private MenuItem dragging;
    private Vector2 lastMousePosition;
    private bool dirty = true;

    public MenuList(MenuStyle style, bool selectOnClick = false)
    {
        this.style = style;
        this.selectOnClick = selectOnClick;
    }

    public int Count => items.Count;
    public int Selected => selected;

    public void SetSelected(int index)
    {
        if (items.Count > 0) Select(Mathf.Clamp(index, 0, items.Count - 1));
    }

    public MenuItem AddButton(Transform parent, Func<string> getText, Action onPress)
    {
        Image back = RuntimeUI.CreateRow("Button", parent, style.ButtonTextSize + 20f, ButtonPadding, 0f);
        Text label = RuntimeUI.CreateText("Label", back.transform, style.Font, style.ButtonTextSize, style.Text,
                                          TextAnchor.MiddleCenter, style.Shadow, out Shadow shadow);
        RuntimeUI.FillWidth(label);

        return Add(new MenuItem { Back = back, Label = label, LabelShadow = shadow, GetText = getText, OnPress = onPress });
    }

    // Label on the left, slider track in the middle (sliders only), value on the right
    public MenuItem AddSetting(Transform parent, MenuSetting setting)
    {
        int size = style.SettingTextSize;
        Image back = RuntimeUI.CreateRow(setting.Label, parent, size + 16f, SettingPadding, 16f);
        var item = new MenuItem { Back = back, Setting = setting };

        item.Label = RuntimeUI.CreateText("Label", back.transform, style.Font, size, style.Text, TextAnchor.MiddleLeft, style.Shadow, out item.LabelShadow);
        item.Label.text = setting.Label;
        RuntimeUI.FillWidth(item.Label);

        // Rows without a track give its space to the value, so "1920 x 1080" fits
        float valueWidth = setting.HasTrack ? ValueWidth : ValueWidth + WideValueExtra;

        if (!setting.HasTrack)
        {
            RuntimeUI.FixedWidth(RuntimeUI.CreateRect("Spacer", back.transform), TrackWidth - WideValueExtra);
        }
        else
        {
            item.TrackImage = RuntimeUI.CreateImage("Track", back.transform, style.Track);
            item.Track = item.TrackImage.rectTransform;
            LayoutElement track = RuntimeUI.FixedWidth(item.TrackImage, TrackWidth);
            track.minHeight = TrackHeight;
            track.preferredHeight = TrackHeight;

            item.FillImage = RuntimeUI.CreateImage("Fill", item.Track, style.Fill);
            item.Fill = item.FillImage.rectTransform;
            item.Fill.anchorMin = Vector2.zero;
            item.Fill.anchorMax = new Vector2(0f, 1f);
            item.Fill.offsetMin = Vector2.zero;
            item.Fill.offsetMax = Vector2.zero;
        }

        item.Value = RuntimeUI.CreateText("Value", back.transform, style.Font, size, style.Text, TextAnchor.MiddleRight, style.Shadow, out item.ValueShadow);
        RuntimeUI.FixedWidth(item.Value, valueWidth);

        return Add(item);
    }

    public void RemoveLast()
    {
        if (items.Count == 0) return;

        MenuItem last = items[items.Count - 1];
        items.RemoveAt(items.Count - 1);
        UnityEngine.Object.Destroy(last.Back.gameObject);
        selected = Mathf.Clamp(selected, 0, Mathf.Max(0, items.Count - 1));
        dirty = true;
    }

    public void Clear()
    {
        while (items.Count > 0) RemoveLast();
        selected = 0;
    }

    // Call when the list appears, so a resting mouse doesn't count as a hover
    public void Reset(Vector2 mousePosition)
    {
        selected = 0;
        dragging = null;
        lastMousePosition = mousePosition;
        dirty = true;
    }

    public void MarkDirty() => dirty = true;

    public void HandleInput(Mouse mouse)
    {
        if (items.Count > 0) HandleMouse(mouse);
    }

    public void Refresh()
    {
        if (!dirty) return;
        dirty = false;

        for (int i = 0; i < items.Count; i++) Draw(items[i], i == selected);
    }

    private MenuItem Add(MenuItem item)
    {
        items.Add(item);
        dirty = true;
        return item;
    }

    private void HandleMouse(Mouse mouse)
    {
        Vector2 position = mouse.position.ReadValue();

        // A drag keeps following the mouse until the button is released, even off the row
        if (dragging != null)
        {
            if (mouse.leftButton.isPressed) SetFromTrack(dragging, position);
            else dragging = null;
            return;
        }

        // Only an actual mouse move changes the selection
        bool moved = (position - lastMousePosition).sqrMagnitude > MouseMoveThresholdSqr;
        if (moved) lastMousePosition = position;

        int hovered = IndexAt(position);
        if (hovered < 0) return;

        if (moved && !selectOnClick) Select(hovered);

        MenuItem item = items[hovered];
        float scroll = mouse.scroll.ReadValue().y;
        if (item.IsSlider && scroll != 0f)
        {
            item.Setting.Nudge(scroll > 0f ? 1 : -1);
            dirty = true;
        }

        if (mouse.rightButton.wasPressedThisFrame && item.Setting != null && !item.IsSlider)
        {
            Select(hovered);
            item.Setting.Nudge(-1);
            dirty = true;
            return;
        }

        if (!mouse.leftButton.wasPressedThisFrame) return;

        Select(hovered);
        if (!item.IsSlider) Press(item);
        else if (IsOverTrack(item, position))
        {
            dragging = item;
            SetFromTrack(item, position);
        }
    }

    private void Press(MenuItem item)
    {
        dirty = true;
        if (item.Setting == null) item.OnPress?.Invoke();
        else if (!item.Setting.HasTrack) item.Setting.Nudge(1);
    }

    private void Select(int index)
    {
        if (index == selected) return;
        selected = index;
        dirty = true;
    }

    private int IndexAt(Vector2 screenPoint)
    {
        for (int i = 0; i < items.Count; i++)
        {
            // Hidden rows keep their old rectangle, so skip them
            if (items[i].Back.gameObject.activeInHierarchy && RuntimeUI.Contains(items[i].Rect, screenPoint)) return i;
        }

        return -1;
    }

    private static bool IsOverTrack(MenuItem item, Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(item.Track, screenPoint, null, out Vector2 local);
        Rect rect = item.Track.rect;
        return local.x >= rect.xMin - TrackGrabMargin && local.x <= rect.xMax + TrackGrabMargin;
    }

    private void SetFromTrack(MenuItem item, Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(item.Track, screenPoint, null, out Vector2 local);
        Rect rect = item.Track.rect;
        float fraction = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);

        float before = item.Setting.Value;
        item.Setting.SetValue(Mathf.Lerp(item.Setting.Min, item.Setting.Max, fraction));
        if (!Mathf.Approximately(before, item.Setting.Value)) dirty = true;
    }

    private void Draw(MenuItem item, bool isSelected)
    {
        Color textColor = isSelected ? style.SelectedText : style.Text;

        item.Back.color = isSelected ? style.SelectedBack : Color.clear;
        item.Label.color = textColor;
        item.LabelShadow.enabled = !isSelected;
        item.LabelShadow.effectColor = style.Shadow;

        if (item.Setting == null)
        {
            if (item.GetText != null) item.Label.text = item.GetText();
            return;
        }

        item.Value.text = item.Setting.ValueText;
        item.Value.color = textColor;
        item.ValueShadow.enabled = !isSelected;
        item.ValueShadow.effectColor = style.Shadow;

        if (item.Track == null) return;

        item.TrackImage.color = style.Track;
        item.FillImage.color = isSelected ? style.SelectedText : style.Fill;
        item.Fill.anchorMax = new Vector2(item.Setting.Fraction, 1f);
    }
}