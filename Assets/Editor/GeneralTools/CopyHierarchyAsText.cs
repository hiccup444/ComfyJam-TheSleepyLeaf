using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

public static class CopyHierarchyAsText
{
    [MenuItem("Tools/Copy Hierarchy (Indented Text) %#h")] // Ctrl/Cmd+Shift+H
    public static void CopySelectedHierarchy()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("Copy Hierarchy", "Select a GameObject in the Scene or a Prefab asset in Project.", "OK");
            return;
        }

        // If it's a prefab asset in Project window, open a temp contents root.
        var path = AssetDatabase.GetAssetPath(go);
        GameObject root = null;
        bool isPrefabAsset = !string.IsNullOrEmpty(path) && path.EndsWith(".prefab");
        if (isPrefabAsset)
        {
            root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Copy Hierarchy", "Could not load prefab contents.", "OK");
                return;
            }
        }
        else
        {
            // Scene object → get the top-most root of the selection
            root = go.transform.root.gameObject;
        }

        var sb = new StringBuilder();
        Dump(root.transform, 0, sb);

        // If selection wasn’t the actual root, also include only that branch:
        if (!isPrefabAsset && go.transform != root.transform)
        {
            sb.AppendLine();
            sb.AppendLine("---- SELECTED BRANCH ONLY ----");
            Dump(go.transform, 0, sb);
        }

        // Cleanup prefab contents if we opened it
        if (isPrefabAsset)
            PrefabUtility.UnloadPrefabContents(root);

        EditorGUIUtility.systemCopyBuffer = sb.ToString();
        Debug.Log("Hierarchy copied to clipboard.\n" + sb);
    }

    static void Dump(Transform t, int depth, StringBuilder sb)
    {
        // Line: indentation + name [tag, layer]
        var tag = t.gameObject.tag;
        var layerName = LayerMask.LayerToName(t.gameObject.layer);
        sb.Append(' ', depth * 2)
          .Append(t.name)
          .Append("  [tag: ").Append(tag).Append(", layer: ").Append(layerName).Append(']');

        // Optional: include a compact component list on the same line:
        var comps = t.GetComponents<Component>();
        if (comps.Length > 0)
        {
            sb.Append("  {");
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                var type = comps[i].GetType().Name;
                if (type == "Transform") continue;
                if (i > 0) sb.Append(", ");
                sb.Append(type);
            }
            sb.Append('}');
        }
        sb.AppendLine();

        // Recurse
        for (int i = 0; i < t.childCount; i++)
            Dump(t.GetChild(i), depth + 1, sb);
    }
}
