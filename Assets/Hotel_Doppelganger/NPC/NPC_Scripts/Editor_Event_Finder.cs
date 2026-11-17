#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Right-click an object in the Hierarchy and choose "Find Events Pointing Here" to list every UnityEvent in the
// scene that does something to it, e.g. to track down what keeps moving or showing an item
public static class EventFinder
{
    private const int MaxShown = 30;

    [UnityEditor.MenuItem("GameObject/Find Events Pointing Here", false, 49)]
    private static void FindEventsPointingHere()
    {
        GameObject target = Selection.activeGameObject;
        if (target == null) return;

        var found = new List<string>();
        foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (behaviour == null) continue;

            var serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference || !property.propertyPath.EndsWith("m_Target")) continue;
                if (!PointsAt(property.objectReferenceValue, target)) continue;

                string methodPath = property.propertyPath.Substring(0, property.propertyPath.Length - "m_Target".Length) + "m_MethodName";
                SerializedProperty method = serialized.FindProperty(methodPath);
                found.Add($"{behaviour.gameObject.name} ({behaviour.GetType().Name}) → {method?.stringValue}\n    {EventName(property.propertyPath)}");
            }
        }

        var message = new StringBuilder();
        if (found.Count == 0) message.Append("Nothing in this scene's events points at " + target.name + ".");
        for (int i = 0; i < found.Count && i < MaxShown; i++) message.AppendLine(found[i]);
        if (found.Count > MaxShown) message.Append($"...and {found.Count - MaxShown} more");

        EditorUtility.DisplayDialog("Events pointing at " + target.name, message.ToString(), "OK");
    }

    [UnityEditor.MenuItem("GameObject/Find Events Pointing Here", true)]
    private static bool CanFind() => Selection.activeGameObject != null;

    private static bool PointsAt(UnityEngine.Object reference, GameObject target)
    {
        if (reference == null) return false;
        if (reference == target) return true;
        return reference is Component component && component.gameObject == target;
    }

    // Turns "nodes.Array.data[3].choices.Array.data[0].onChosen.m_PersistentCalls..." into "nodes[3].choices[0].onChosen"
    private static string EventName(string path)
    {
        int calls = path.IndexOf(".m_PersistentCalls");
        string trimmed = calls >= 0 ? path.Substring(0, calls) : path;
        return trimmed.Replace(".Array.data[", "[");
    }
}
#endif
