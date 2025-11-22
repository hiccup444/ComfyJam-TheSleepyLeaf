// Assets/Editor/CameraStationsDrawer.cs
#if UNITY_EDITOR
using Comfy.Camera;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(CameraStations.StationEntry))]
public sealed class CameraStationsStationEntryDrawer : PropertyDrawer
{
    static string _listeningPath;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // 4 lines (Anchor, Label, Duration, Hotkey row) + small padding.
        return EditorGUIUtility.singleLineHeight * 4f + 6f;
    }

    public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(rect, label, property);

        float lineHeight = EditorGUIUtility.singleLineHeight;
        float y = rect.y + 2f;
        const float pad = 2f;

        var propAnchor = property.FindPropertyRelative("anchor");
        var propHotkey = property.FindPropertyRelative("hotkey");
        var propLabel = property.FindPropertyRelative("label");
        var propDuration = property.FindPropertyRelative("duration");

        EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, lineHeight), propAnchor, new GUIContent("Anchor"));
        y += lineHeight + pad;

        EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, lineHeight), propLabel, new GUIContent("Label"));
        y += lineHeight + pad;

        EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, lineHeight), propDuration, new GUIContent("Duration (s)"));
        y += lineHeight + pad;

        var hotkeyRow = new Rect(rect.x, y, rect.width, lineHeight);
        var assignRect = hotkeyRow;
        assignRect.width = hotkeyRow.width - 80f;

        var clearRect = hotkeyRow;
        clearRect.x = assignRect.xMax + 4f;
        clearRect.width = 76f;

        bool listening = _listeningPath == property.propertyPath;
        KeyCode currentKey = (KeyCode)propHotkey.intValue;
        bool hasKey = currentKey != KeyCode.None;
        string buttonText = listening
            ? "Press any key… (Esc/Backspace = None)"
            : (hasKey ? $"Hotkey: {InputKeyReader.DescribeKey(currentKey)}" : "Assign Hotkey");

        if (GUI.Button(assignRect, buttonText))
        {
            _listeningPath = listening ? null : property.propertyPath;
            GUI.FocusControl(null);
        }

        EditorGUI.BeginDisabledGroup(!hasKey && !listening);
        if (GUI.Button(clearRect, "Clear"))
        {
            propHotkey.intValue = (int)KeyCode.None;
            _listeningPath = null;
            property.serializedObject.ApplyModifiedProperties();
            GUI.changed = true;
        }
        EditorGUI.EndDisabledGroup();

        var evt = Event.current;
        if (listening && evt != null && evt.type == EventType.KeyDown)
        {
            var pressed = evt.keyCode;
            bool clear = pressed == KeyCode.Escape || pressed == KeyCode.Backspace || pressed == KeyCode.Delete;

            if (clear)
            {
                propHotkey.intValue = (int)KeyCode.None;
            }
            else if (pressed != KeyCode.None)
            {
                propHotkey.intValue = (int)pressed;
            }

            if (clear || pressed != KeyCode.None)
            {
                _listeningPath = null;
                property.serializedObject.ApplyModifiedProperties();
                GUI.changed = true;
                evt.Use();
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
        }

        EditorGUI.EndProperty();
    }
}
#endif
