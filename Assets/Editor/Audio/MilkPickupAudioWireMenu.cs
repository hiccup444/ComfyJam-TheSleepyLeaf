using UnityEditor;
using UnityEngine;

public static class MilkPickupAudioWireMenu
{
    [MenuItem("Tools/Audio/Wire Milk Pickup Audio (Prefabs with Milk)")]
    public static void WireMilkPickupAudio()
    {
        string[] searchIn = new[] { "Assets" };
        string[] guids = AssetDatabase.FindAssets("t:Prefab", searchIn);
        int modified = 0, total = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;
            try
            {
                // Look for MilkTiltVisual or LiquidStream to identify milk prefabs
                var milkVisual = root.GetComponentInChildren<MilkTiltVisual>(true);
                var stream     = root.GetComponentInChildren<LiquidStream>(true);
                if (milkVisual == null && stream == null) continue;
                total++;

                // Find the draggable child (milkFront has DragItem2D)
                var drag = root.GetComponentInChildren<DragItem2D>(true);
                if (drag == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[MilkPickupAudioWire] No DragItem2D found in '{path}'. Skipping.");
#endif
                    continue;
                }

                var host = drag.gameObject;
                bool changed = false;

                var src = host.GetComponent<AudioSource>();
                if (src == null)
                {
                    src = host.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.loop = false;
                    changed = true;
                }

                var audio = host.GetComponent<MilkPickupAudio>();
                if (audio == null)
                {
                    audio = host.AddComponent<MilkPickupAudio>();
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    modified++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

#if UNITY_EDITOR
        Debug.Log($"[MilkPickupAudioWire] Processed {total} milk prefabs. Modified: {modified}.");
#endif
        if (modified > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}

