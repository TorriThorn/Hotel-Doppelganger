using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Shows a field in the Inspector under a friendly name instead of its code name.
// Optional extras: a minimum (and maximum, which makes a slider), a multi-line text box,
// and showing or hiding the field depending on a tick box next to it.
// The saved data is untouched, so adding or changing this never loses settings.
[AttributeUsage(AttributeTargets.Field)]
public class DisplayAsAttribute : PropertyAttribute
{
    public readonly string Label;

    // Only show this field while the named bool field is ticked.
    public string ShowIf;

    // Hide this field while the named bool field is ticked.
    public string HideIf;

    // Lowest allowed value. Setting Max as well turns the field into a slider.
    public float Min = float.NegativeInfinity;
    public float Max = float.PositiveInfinity;

    // For text: draw a wrapping text box at least this many lines tall.
    public int Lines;

    public DisplayAsAttribute(string label) => Label = label;

    public bool HasMin => !float.IsNegativeInfinity(Min);
    public bool HasSlider => HasMin && !float.IsPositiveInfinity(Max);
}

// Starts a collapsible section in the Inspector. The section holds this field and the ones after it,
// up to the next field with a [Header] or another [Foldout]. Sections start closed.
[AttributeUsage(AttributeTargets.Field)]
public class FoldoutAttribute : Attribute
{
    public readonly string Title;

    public FoldoutAttribute(string title) => Title = title;
}

// Puts a field inside a dialogue entry under a closed "More Options" section, for settings most entries don't need.
[AttributeUsage(AttributeTargets.Field)]
public class MoreOptionsAttribute : Attribute { }

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(DisplayAsAttribute))]
public class DisplayAsDrawer : PropertyDrawer
{
    private const float TextAreaGap = 2f;
    private const float InspectorMargin = 40f;

    private static GUIStyle wrappingTextArea;

    private static GUIStyle WrappingTextArea =>
        wrappingTextArea ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

    private DisplayAsAttribute Display => (DisplayAsAttribute)attribute;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // A small negative height removes the gap a hidden field would leave behind
        if (!IsVisible(property)) return -EditorGUIUtility.standardVerticalSpacing;

        if (IsMultiline(property))
        {
            float width = EditorGUIUtility.currentViewWidth - InspectorMargin;
            float minHeight = WrappingTextArea.lineHeight * Display.Lines + WrappingTextArea.padding.vertical;
            float textHeight = WrappingTextArea.CalcHeight(new GUIContent(property.stringValue), width);
            return EditorGUIUtility.singleLineHeight + TextAreaGap + Mathf.Max(minHeight, textHeight);
        }

        return EditorGUI.GetPropertyHeight(property, label, true);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (!IsVisible(property)) return;

        var content = new GUIContent(Display.Label, label.tooltip);
        EditorGUI.BeginProperty(position, content, property);

        if (IsMultiline(property)) DrawMultiline(position, property, content);
        else if (Display.HasSlider && property.propertyType == SerializedPropertyType.Float) DrawFloatSlider(position, property, content);
        else if (Display.HasSlider && property.propertyType == SerializedPropertyType.Integer) DrawIntSlider(position, property, content);
        else DrawDefault(position, property, content);

        EditorGUI.EndProperty();
    }

    private bool IsMultiline(SerializedProperty property) =>
        Display.Lines > 0 && property.propertyType == SerializedPropertyType.String;

    private void DrawMultiline(Rect position, SerializedProperty property, GUIContent content)
    {
        var labelRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(labelRect, content);

        var areaRect = new Rect(position.x, labelRect.yMax + TextAreaGap, position.width, position.yMax - labelRect.yMax - TextAreaGap);
        areaRect = EditorGUI.IndentedRect(areaRect);

        EditorGUI.BeginChangeCheck();
        string value = EditorGUI.TextArea(areaRect, property.stringValue, WrappingTextArea);
        if (EditorGUI.EndChangeCheck()) property.stringValue = value;
    }

    private void DrawFloatSlider(Rect position, SerializedProperty property, GUIContent content)
    {
        EditorGUI.BeginChangeCheck();
        float value = EditorGUI.Slider(position, content, property.floatValue, Display.Min, Display.Max);
        if (EditorGUI.EndChangeCheck()) property.floatValue = value;
    }

    private void DrawIntSlider(Rect position, SerializedProperty property, GUIContent content)
    {
        EditorGUI.BeginChangeCheck();
        int value = EditorGUI.IntSlider(position, content, property.intValue, (int)Display.Min, (int)Display.Max);
        if (EditorGUI.EndChangeCheck()) property.intValue = value;
    }

    private void DrawDefault(Rect position, SerializedProperty property, GUIContent content)
    {
        EditorGUI.PropertyField(position, property, content, true);
        if (!Display.HasMin) return;

        if (property.propertyType == SerializedPropertyType.Float && property.floatValue < Display.Min)
            property.floatValue = Display.Min;
        else if (property.propertyType == SerializedPropertyType.Integer && property.intValue < Display.Min)
            property.intValue = Mathf.CeilToInt(Display.Min);
    }

    private bool IsVisible(SerializedProperty property)
    {
        if (!string.IsNullOrEmpty(Display.ShowIf) && !ReadSiblingBool(property, Display.ShowIf, true)) return false;
        if (!string.IsNullOrEmpty(Display.HideIf) && ReadSiblingBool(property, Display.HideIf, false)) return false;
        return true;
    }

    // Finds a bool field next to this one, including inside list elements
    private static bool ReadSiblingBool(SerializedProperty property, string fieldName, bool fallback)
    {
        string path = property.propertyPath;
        int lastDot = path.LastIndexOf('.');
        string siblingPath = lastDot >= 0 ? path.Substring(0, lastDot + 1) + fieldName : fieldName;

        SerializedProperty sibling = property.serializedObject.FindProperty(siblingPath);
        return sibling != null && sibling.propertyType == SerializedPropertyType.Boolean ? sibling.boolValue : fallback;
    }
}

// Draws a component's fields in order, putting [Foldout] sections in collapsible groups.
// Each section remembers whether it was left open.
public abstract class FoldoutInspector : Editor
{
    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const string ScriptFieldPath = "m_Script";

    private static GUIStyle sectionStyle;

    private static GUIStyle SectionStyle =>
        sectionStyle ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

    private readonly Dictionary<string, string> sectionByField = new Dictionary<string, string>();

    private void OnEnable() => MapSections();

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty property = serializedObject.GetIterator();
        string currentSection = null;
        bool sectionOpen = true;

        for (bool enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
        {
            if (property.propertyPath == ScriptFieldPath)
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(property);
                continue;
            }

            sectionByField.TryGetValue(property.name, out string section);

            if (section != currentSection)
            {
                if (currentSection != null && sectionOpen) EditorGUI.indentLevel--;

                currentSection = section;
                sectionOpen = section == null || DrawSectionHeader(section);

                if (section != null && sectionOpen) EditorGUI.indentLevel++;
            }

            if (sectionOpen) EditorGUILayout.PropertyField(property, true);
        }

        if (currentSection != null && sectionOpen) EditorGUI.indentLevel--;

        serializedObject.ApplyModifiedProperties();
    }

    private bool DrawSectionHeader(string title)
    {
        string key = $"{target.GetType().FullName}.{title}.Open";
        bool wasOpen = EditorPrefs.GetBool(key, false);

        EditorGUILayout.Space(4f);
        bool isOpen = EditorGUILayout.Foldout(wasOpen, title, true, SectionStyle);
        if (isOpen != wasOpen) EditorPrefs.SetBool(key, isOpen);

        return isOpen;
    }

    // Works out which section each field belongs to: a [Foldout] starts one, the next [Header] or [Foldout] ends it
    private void MapSections()
    {
        sectionByField.Clear();

        var hierarchy = new List<Type>();
        for (Type type = target.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            hierarchy.Insert(0, type);

        foreach (Type type in hierarchy)
        {
            string section = null;

            foreach (FieldInfo field in type.GetFields(FieldFlags))
            {
                var foldout = field.GetCustomAttribute<FoldoutAttribute>();
                if (foldout != null) section = foldout.Title;
                else if (field.GetCustomAttribute<HeaderAttribute>() != null) section = null;

                if (section != null) sectionByField[field.Name] = section;
            }
        }
    }
}

[CanEditMultipleObjects, CustomEditor(typeof(FirstPersonController))] public class FirstPersonControllerEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(ObjectGrabber))] public class ObjectGrabberEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(Grabbable))] public class GrabbableEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(PlacementZone))] public class PlacementZoneEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(PlayerDialogue))] public class PlayerDialogueEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(NPCDialogue))] public class NPCDialogueEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(NPCPatrol))] public class NPCPatrolEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(NPCFacePlayer))] public class NPCFacePlayerEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(NPCSpriteSwap))] public class NPCSpriteSwapEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(MainMenu))] public class MainMenuEditor : FoldoutInspector { }
[CanEditMultipleObjects, CustomEditor(typeof(GameComplete))] public class GameCompleteEditor : FoldoutInspector { }

// Draws a dialogue choice or event dialogue entry with its [MoreOptions] fields tucked into a closed section.
// Each entry is labelled by its text (choices) or its Event Id (event dialogue).
[CustomPropertyDrawer(typeof(NPCDialogue.Choice))]
[CustomPropertyDrawer(typeof(NPCDialogue.EventDialogue))]
public class DialogueEntryDrawer : PropertyDrawer
{
    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string MoreOptionsTitle = "More Options";
    private const string EventCallsPath = "onChosen.m_PersistentCalls.m_Calls";

    private static float Line => EditorGUIUtility.singleLineHeight;
    private static float Gap => EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = Line;
        if (!property.isExpanded) return height;

        SplitFields(property, out List<SerializedProperty> main, out List<SerializedProperty> more);
        foreach (SerializedProperty field in main) height += Gap + EditorGUI.GetPropertyHeight(field, true);

        if (more.Count == 0) return height;

        height += Gap + Line;
        if (IsMoreOpen(property))
        {
            foreach (SerializedProperty field in more) height += Gap + EditorGUI.GetPropertyHeight(field, true);
        }

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var row = new Rect(position.x, position.y, position.width, Line);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, EntryName(property), true);
        if (!property.isExpanded) return;

        SplitFields(property, out List<SerializedProperty> main, out List<SerializedProperty> more);
        float y = row.yMax;

        EditorGUI.indentLevel++;
        y = DrawFields(position, y, main);

        if (more.Count > 0)
        {
            y += Gap;
            var moreRow = new Rect(position.x, y, position.width, Line);
            string title = HasEvents(property) ? MoreOptionsTitle + " (has events)" : MoreOptionsTitle;
            bool open = EditorGUI.Foldout(moreRow, IsMoreOpen(property), title, true);
            SetMoreOpen(property, open);
            y = moreRow.yMax;

            if (open)
            {
                EditorGUI.indentLevel++;
                DrawFields(position, y, more);
                EditorGUI.indentLevel--;
            }
        }

        EditorGUI.indentLevel--;
    }

    private static float DrawFields(Rect position, float y, List<SerializedProperty> fields)
    {
        foreach (SerializedProperty field in fields)
        {
            y += Gap;
            float height = EditorGUI.GetPropertyHeight(field, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, Mathf.Max(0f, height)), field, true);
            y += height;
        }

        return y;
    }

    private void SplitFields(SerializedProperty property, out List<SerializedProperty> main, out List<SerializedProperty> more)
    {
        main = new List<SerializedProperty>();
        more = new List<SerializedProperty>();

        Type type = EntryType();
        SerializedProperty child = property.Copy();
        SerializedProperty end = property.GetEndProperty();

        for (bool enterChildren = true; child.NextVisible(enterChildren); enterChildren = false)
        {
            if (SerializedProperty.EqualContents(child, end)) break;

            FieldInfo field = type?.GetField(child.name, FieldFlags);
            bool isMore = field != null && field.IsDefined(typeof(MoreOptionsAttribute), false);
            (isMore ? more : main).Add(child.Copy());
        }
    }

    // The drawer is used for list elements, so the field's type is the list's element type
    private Type EntryType()
    {
        Type type = fieldInfo.FieldType;
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
        return type;
    }

    private string EntryName(SerializedProperty property)
    {
        bool isEvent = EntryType() == typeof(NPCDialogue.EventDialogue);
        string value = property.FindPropertyRelative(isEvent ? "unlockId" : "text")?.stringValue;

        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return isEvent ? "(no Event Id yet)" : "(empty choice)";
    }

    private static bool HasEvents(SerializedProperty property)
    {
        SerializedProperty calls = property.FindPropertyRelative(EventCallsPath);
        return calls != null && calls.arraySize > 0;
    }

    private static string MoreKey(SerializedProperty property) =>
        $"{property.serializedObject.targetObject.GetHashCode()}.{property.propertyPath}.MoreOptions";

    private static bool IsMoreOpen(SerializedProperty property) => SessionState.GetBool(MoreKey(property), false);

    private static void SetMoreOpen(SerializedProperty property, bool open) => SessionState.SetBool(MoreKey(property), open);
}
#endif