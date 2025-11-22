// Assets/Editor/CameraStationsVerifier.cs
#if UNITY_EDITOR
using Comfy.Camera;
using UnityEditor;
using UnityEngine;

static class CameraStationsVerifier
{
    [MenuItem("Tools/Comfy/Verify Station Hotkeys")]
    static void VerifyHotkeys()
    {
        var stations = Object.FindObjectsByType<CameraStations>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (stations.Length == 0)
        {
            Debug.Log("[CameraStationsVerifier] No CameraStations components found in open scenes.");
            return;
        }

        foreach (var station in stations)
        {
            var entries = station.stations ?? System.Array.Empty<CameraStations.StationEntry>();
            string context = station.gameObject.scene.IsValid()
                ? $"{station.gameObject.scene.name}/{station.gameObject.name}"
                : station.gameObject.name;

            Debug.Log($"[CameraStationsVerifier] {context}: {entries.Length} station(s).");
            bool warnForNone = station.enableHotkeys;

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                int raw = (int)entry.hotkey;
                string name = InputKeyReader.DescribeKey(entry.hotkey);
                string label = string.IsNullOrEmpty(entry.label) ? "(no label)" : entry.label;
                Debug.Log($"    Index {i}: label='{label}' hotkeyInt={raw} hotkey={name}", station);

                if (warnForNone && entry.hotkey == KeyCode.None)
                {
                    Debug.LogWarning($"[CameraStationsVerifier] Station '{label}' has hotkeys enabled but no key assigned (index {i}, object {context}).", station);
                }
            }
        }
    }

    [MenuItem("Tools/Comfy/Normalize Station Hotkeys")]
    static void NormalizeHotkeys()
    {
        var stations = Object.FindObjectsByType<CameraStations>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var station in stations)
        {
            var so = new SerializedObject(station);
            var array = so.FindProperty("stations");
            if (array == null)
                continue;

            for (int i = 0; i < array.arraySize; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                var hotkeyProp = element.FindPropertyRelative("hotkey");
                if (hotkeyProp != null)
                {
                    hotkeyProp.intValue = hotkeyProp.intValue;
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(station);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[CameraStationsVerifier] Station hotkeys normalized (re-serialized).");
    }
}
#endif
