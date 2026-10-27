using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Shows a field under a friendly name. Optional: a minimum (plus a maximum for a slider), a multi-line
// text box, and showing or hiding the field based on a bool next to it. Only the display changes,
// so adding or editing this never touches saved values.
[AttributeUsage(AttributeTargets.Field)]
public class DisplayAsAttribute : PropertyAttribute
{
    public readonly string Label;

    // Show only while the named bool field is ticked
    public string ShowIf;

    // Hide while the named bool field is ticked
    public string HideIf;

    public float Min = float.NegativeInfinity;
    public float Max = float.PositiveInfinity;

    // For strings: a wrapping text box at least this many lines tall
    public int Lines;

    public DisplayAsAttribute(string label) => Label = label;

    public bool HasMin => !float.IsNegativeInfinity(Min);
    public bool HasSlider => HasMin && !float.IsPositiveInfinity(Max);
}

// Starts a collapsible Inspector section that runs until the next [Header] or [Foldout]. Sections start closed.
[AttributeUsage(AttributeTargets.Field)]
public class FoldoutAttribute : Attribute
{
    public readonly string Title;

    public FoldoutAttribute(string title) => Title = title;
}

// Tucks a dialogue entry field into its closed "More Options" section
[AttributeUsage(AttributeTargets.Field)]
public class MoreOptionsAttribute : Attribute { }

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(DisplayAsAttribute))]
public class DisplayAsDrawer : PropertyDrawer
{
    private const float TextAreaGap = 2f;
    private const float InspectorMargin = 40f;

    private static GUIStyle wrappingTextArea;

    private static GUIStyle WrappingTextArea => wrappingTextArea ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

    private DisplayAsAttribute Display => (DisplayAsAttribute)attribute;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Negative spacing closes the gap a hidden field would otherwise leave
        if (!IsVisible(property)) return -EditorGUIUtility.standardVerticalSpacing;
        if (!IsMultiline(property)) return EditorGUI.GetPropertyHeight(property, label, true);

        GUIStyle style = WrappingTextArea;
        float width = EditorGUIUtility.currentViewWidth - InspectorMargin;
        float minHeight = style.lineHeight * Display.Lines + style.padding.vertical;
        float textHeight = style.CalcHeight(new GUIContent(property.stringValue), width);
        return EditorGUIUtility.singleLineHeight + TextAreaGap + Mathf.Max(minHeight, textHeight);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (!IsVisible(property)) return;

        var content = new GUIContent(Display.Label, label.tooltip);
        EditorGUI.BeginProperty(position, content, property);

        if (IsMultiline(property)) DrawMultiline(position, property, content);
        else if (Display.HasSlider && property.propertyType == SerializedPropertyType.Float) DrawFloatSlider(position, property, content);
        else if (Display.HasSlider && property.propertyType == SerializedPropertyType.Integer) DrawIntSlider(position, property, content);
        else DrawWithMinimum(position, property, content);

        EditorGUI.EndProperty();
    }

    // Values are only written back when changed, otherwise multi-object editing would copy one object's value to all
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

    private bool IsMultiline(SerializedProperty property) =>
        Display.Lines > 0 && property.propertyType == SerializedPropertyType.String;

    private static void DrawMultiline(Rect position, SerializedProperty property, GUIContent content)
    {
        var labelRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(labelRect, content);

        float top = labelRect.yMax + TextAreaGap;
        Rect areaRect = EditorGUI.IndentedRect(new Rect(position.x, top, position.width, position.yMax - top));

        EditorGUI.BeginChangeCheck();
        string value = EditorGUI.TextArea(areaRect, property.stringValue, WrappingTextArea);
        if (EditorGUI.EndChangeCheck()) property.stringValue = value;
    }

    private void DrawWithMinimum(Rect position, SerializedProperty property, GUIContent content)
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
        return string.IsNullOrEmpty(Display.HideIf) || !ReadSiblingBool(property, Display.HideIf, false);
    }

    // Looks up a bool next to this field, which also works inside list elements
    private static bool ReadSiblingBool(SerializedProperty property, string fieldName, bool fallback)
    {
        string path = property.propertyPath;
        int lastDot = path.LastIndexOf('.');
        string siblingPath = lastDot >= 0 ? path.Substring(0, lastDot + 1) + fieldName : fieldName;

        SerializedProperty sibling = property.serializedObject.FindProperty(siblingPath);
        return sibling != null && sibling.propertyType == SerializedPropertyType.Boolean ? sibling.boolValue : fallback;
    }
}

// Draws fields in order, grouping [Foldout] sections under collapsible headers that remember whether they were open
public abstract class FoldoutInspector : Editor
{
    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const string ScriptFieldPath = "m_Script";

    // Reflection only needs doing once per component type, not every time the Inspector opens
    private static readonly Dictionary<Type, Dictionary<string, string>> sectionsByType = new Dictionary<Type, Dictionary<string, string>>();

    private static GUIStyle sectionStyle;

    private static GUIStyle SectionStyle => sectionStyle ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

    private Dictionary<string, string> sectionByField;
    private string prefsPrefix;

    private void OnEnable()
    {
        Type type = target.GetType();
        sectionByField = SectionsFor(type);
        prefsPrefix = type.FullName + ".";
    }

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
        string key = prefsPrefix + title + ".Open";
        bool wasOpen = EditorPrefs.GetBool(key, false);

        EditorGUILayout.Space(4f);
        bool isOpen = EditorGUILayout.Foldout(wasOpen, title, true, SectionStyle);
        if (isOpen != wasOpen) EditorPrefs.SetBool(key, isOpen);

        return isOpen;
    }

    // A [Foldout] starts a section and the next [Header] or [Foldout] ends it. Base class fields come first.
    private static Dictionary<string, string> SectionsFor(Type componentType)
    {
        if (sectionsByType.TryGetValue(componentType, out Dictionary<string, string> sections)) return sections;

        sections = new Dictionary<string, string>();
        var hierarchy = new Stack<Type>();
        for (Type type = componentType; type != null && type != typeof(MonoBehaviour); type = type.BaseType) hierarchy.Push(type);

        foreach (Type type in hierarchy)
        {
            string section = null;

            foreach (FieldInfo field in type.GetFields(FieldFlags))
            {
                var foldout = field.GetCustomAttribute<FoldoutAttribute>();
                if (foldout != null) section = foldout.Title;
                else if (field.IsDefined(typeof(HeaderAttribute), false)) section = null;

                if (section != null) sections[field.Name] = section;
            }
        }

        sectionsByType[componentType] = sections;
        return sections;
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

// Draws a dialogue choice or event entry labelled by its text (or Event Id), with [MoreOptions] fields in a closed section
[CustomPropertyDrawer(typeof(NPCDialogue.Choice))]
[CustomPropertyDrawer(typeof(NPCDialogue.EventDialogue))]
public class DialogueEntryDrawer : PropertyDrawer
{
    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string MoreOptionsTitle = "More Options";
    private const string MoreOptionsWithEventsTitle = "More Options (has events)";
    private const string EventCallsPath = "onChosen.m_PersistentCalls.m_Calls";

    private static readonly Dictionary<Type, HashSet<string>> moreFieldsByType = new Dictionary<Type, HashSet<string>>();

    private readonly List<SerializedProperty> mainFields = new List<SerializedProperty>();
    private readonly List<SerializedProperty> moreFields = new List<SerializedProperty>();

    private static float Line => EditorGUIUtility.singleLineHeight;
    private static float Gap => EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return Line;

        SplitFields(property);
        float height = Line + FieldsHeight(mainFields);
        if (moreFields.Count == 0) return height;

        height += Gap + Line;
        return IsMoreOpen(property) ? height + FieldsHeight(moreFields) : height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var row = new Rect(position.x, position.y, position.width, Line);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, EntryName(property), true);
        if (!property.isExpanded) return;

        SplitFields(property);
        EditorGUI.indentLevel++;
        float y = DrawFields(position, row.yMax, mainFields);

        if (moreFields.Count > 0)
        {
            var moreRow = new Rect(position.x, y + Gap, position.width, Line);
            string title = HasEvents(property) ? MoreOptionsWithEventsTitle : MoreOptionsTitle;
            bool open = EditorGUI.Foldout(moreRow, IsMoreOpen(property), title, true);
            SessionState.SetBool(MoreKey(property), open);

            if (open)
            {
                EditorGUI.indentLevel++;
                DrawFields(position, moreRow.yMax, moreFields);
                EditorGUI.indentLevel--;
            }
        }

        EditorGUI.indentLevel--;
    }

    private static float FieldsHeight(List<SerializedProperty> fields)
    {
        float height = 0f;
        foreach (SerializedProperty field in fields) height += Gap + EditorGUI.GetPropertyHeight(field, true);
        return height;
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

    private void SplitFields(SerializedProperty property)
    {
        mainFields.Clear();
        moreFields.Clear();

        HashSet<string> moreNames = MoreFieldNames(EntryType());
        SerializedProperty child = property.Copy();
        SerializedProperty end = property.GetEndProperty();

        for (bool enterChildren = true; child.NextVisible(enterChildren); enterChildren = false)
        {
            if (SerializedProperty.EqualContents(child, end)) break;
            (moreNames.Contains(child.name) ? moreFields : mainFields).Add(child.Copy());
        }
    }

    private static HashSet<string> MoreFieldNames(Type type)
    {
        if (moreFieldsByType.TryGetValue(type, out HashSet<string> names)) return names;

        names = new HashSet<string>();
        foreach (FieldInfo field in type.GetFields(FieldFlags))
        {
            if (field.IsDefined(typeof(MoreOptionsAttribute), false)) names.Add(field.Name);
        }

        moreFieldsByType[type] = names;
        return names;
    }

    // The drawer is used on list elements, so the type we want is the list's element type
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
}
#endif