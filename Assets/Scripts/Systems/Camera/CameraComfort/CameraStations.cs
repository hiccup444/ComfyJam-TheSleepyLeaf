// Assets/Scripts/CameraComfort/CameraStations.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System;

namespace Comfy.Camera
{
    /// <summary>
    /// Generic list of camera jump anchors. Each entry can have its own hotkey.
    /// </summary>
    [AddComponentMenu("Comfy/Camera Comfort Stations")]
    public sealed class CameraStations : MonoBehaviour
    {
        /// <summary>
        /// Fired when the camera station changes. Provides the new station index.
        /// </summary>
        public static event Action<int> OnStationChanged;
        [System.Serializable]
        public struct StationEntry
        {
            [Tooltip("Where to frame the camera (X/Y used; Z is preserved).")]
            public Transform anchor;

            [Tooltip("Optional quick-jump key (e.g., Alpha1, Alpha2...). Leave None to disable.")]
            public KeyCode hotkey;

            [Tooltip("Optional label for clarity (e.g., Dispenser, Ice...).")]
            public string label;

            [Tooltip("Jump duration (seconds) for this station. 0 = snap.")]
            [Min(0f)] public float duration;
        }

        [Tooltip("List of stations you can jump to. Add/remove freely.")]
        public StationEntry[] stations = new StationEntry[0];

        [Tooltip("If enabled, pressing a station's hotkey triggers a jump.")]
        public bool enableHotkeys = true;

        [Tooltip("Default duration if an entry's duration is 0.")]
        [Min(0f)] public float defaultDuration = 0.2f;

        [Header("Debug")]
        [SerializeField] bool debugHotkeys = false;

        CameraRig2D _rig;
        int _currentIndex = -1;

        public int CurrentIndex => _currentIndex;

        // Mouse-only arrows (optional)
        GameObject _arrowsRoot;
        Button _leftButton;
        Button _rightButton;
        Button _upButton;
        Button _downButton;

        void Awake()
        {
            _rig = GetComponent<CameraRig2D>();
        }

        void Start()
        {
            // Try to bind the MouseOnlyArrows UI if present in the scene (already in MainCanvas prefab)
            TryHookMouseOnlyArrows();
            UpdateArrowsVisibility();
        }

        void Update()
        {
            // If UI prefab loaded after us, try to bind once.
            if (_arrowsRoot == null)
            {
                TryHookMouseOnlyArrows();
                UpdateArrowsVisibility();
            }

            if (!enableHotkeys || _rig == null) return;

            // Don't allow hotkey station jumps during cutscene
            var cutsceneManager = FindFirstObjectByType<CutsceneManager>();
            if (cutsceneManager != null && cutsceneManager.IsCutscenePlaying)
                return;

            for (int i = 0; i < stations.Length; i++)
            {
                var entry = stations[i];
                if (entry.hotkey == KeyCode.None)
                    continue;

                if (InputKeyReader.GetKeyDown(entry.hotkey))
                {
                    if (debugHotkeys)
                    {
                        string label = string.IsNullOrEmpty(entry.label) ? $"Station {i}" : entry.label;
#if UNITY_EDITOR
                        Debug.Log($"[CameraStations] Hotkey hit index={i} label='{label}' storedInt={(int)entry.hotkey} key={entry.hotkey} described='{InputKeyReader.DescribeKey(entry.hotkey)}'", this);
#endif
                    }

                    float duration = entry.duration > 0f ? entry.duration : defaultDuration;
                    JumpToIndex(i, duration);
                    break;
                }
            }
        }

        /// <summary>Jump to a station by index (returns false if invalid/missing).</summary>
        public bool JumpToIndex(int index, float durationSeconds = 0.2f)
        {
            if (_rig == null) return false;
            if (index < 0 || index >= stations.Length) return false;

            var anchor = stations[index].anchor;
            if (anchor == null) return false;

            _rig.RequestStationJump(anchor.position, Mathf.Max(0f, durationSeconds));

            // Only fire event if station actually changed
            if (_currentIndex != index)
            {
                _currentIndex = index;
                OnStationChanged?.Invoke(index);
            }
            else
            {
                _currentIndex = index;
            }

            UpdateArrowsVisibility();
            return true;
        }

        /// <summary>Jump to the next valid station (wraps). Returns false if none available.</summary>
        public bool JumpToNext()
        {
            if (stations == null || stations.Length == 0) return false;

            int start = _currentIndex;
            if (start < 0 || start >= stations.Length)
                start = -1; // start before first to include index 0 on first pass

            int next = FindNextValidIndex(start);
            if (next < 0) return false;

            float duration = stations[next].duration > 0f ? stations[next].duration : defaultDuration;
            return JumpToIndex(next, duration);
        }

        /// <summary>Jump to the previous valid station (wraps). Returns false if none available.</summary>
        public bool JumpToPrevious()
        {
            if (stations == null || stations.Length == 0) return false;

            int start = _currentIndex;
            if (start < 0 || start >= stations.Length)
                start = 0; // start after 0 so we can move back to last

            int prev = FindPreviousValidIndex(start);
            if (prev < 0) return false;

            float duration = stations[prev].duration > 0f ? stations[prev].duration : defaultDuration;
            return JumpToIndex(prev, duration);
        }

        /// <summary>Jump to the first station whose label matches (case-insensitive).</summary>
        public bool JumpToLabel(string label, float durationSeconds = 0.2f)
        {
            if (string.IsNullOrEmpty(label)) return false;
            for (int i = 0; i < stations.Length; i++)
            {
                if (string.Equals(stations[i].label, label, System.StringComparison.OrdinalIgnoreCase))
                    return JumpToIndex(i, durationSeconds);
            }
            return false;
        }

        /// <summary>Jump directly to a Transform (must match an entry anchor).</summary>
        public bool JumpToAnchor(Transform anchor, float durationSeconds = 0.2f)
        {
            if (_rig == null || anchor == null) return false;
            _rig.RequestStationJump(anchor.position, Mathf.Max(0f, durationSeconds));
            return true;
        }

        // ----- UI integration (MouseOnlyArrows) -----

        void TryHookMouseOnlyArrows()
        {
            // Find by instance name (present in MainCanvas prefab as a PrefabInstance)
            GameObject arrows = GameObject.Find("MouseOnlyArrows");
            if (arrows == null)
            {
                // Fallback: search all loaded RectTransforms by name and prefer scene objects
                var rects = Resources.FindObjectsOfTypeAll<RectTransform>();
                for (int i = 0; i < rects.Length; i++)
                {
                    var go = rects[i] != null ? rects[i].gameObject : null;
                    if (go != null && go.name == "MouseOnlyArrows" && go.scene.IsValid())
                    {
                        arrows = go;
                        break;
                    }
                }
            }
            if (arrows == null)
                return;

            _arrowsRoot = arrows;

            // Buttons are children named ArrowLeft / ArrowRight, optional ArrowUp / ArrowDown inside the prefab
            var leftT = _arrowsRoot.transform.Find("ArrowLeft");
            var rightT = _arrowsRoot.transform.Find("ArrowRight");
            var upT = _arrowsRoot.transform.Find("ArrowUp");
            var downT = _arrowsRoot.transform.Find("ArrowDown");

            if (leftT != null)
            {
                _leftButton = leftT.GetComponent<Button>();
                if (_leftButton == null)
                    _leftButton = leftT.gameObject.AddComponent<Button>();
                // Visual-only: disable interaction and remove listeners
                _leftButton.onClick.RemoveAllListeners();
                _leftButton.interactable = false;
                leftT.gameObject.SetActive(false);
            }

            if (rightT != null)
            {
                _rightButton = rightT.GetComponent<Button>();
                if (_rightButton == null)
                    _rightButton = rightT.gameObject.AddComponent<Button>();
                // Visual-only: disable interaction and remove listeners
                _rightButton.onClick.RemoveAllListeners();
                _rightButton.interactable = false;
                rightT.gameObject.SetActive(false);
            }

            if (upT != null)
            {
                _upButton = upT.GetComponent<Button>();
                if (_upButton == null)
                    _upButton = upT.gameObject.AddComponent<Button>();
                _upButton.onClick.RemoveAllListeners();
                _upButton.interactable = false;
                upT.gameObject.SetActive(false);
            }

            if (downT != null)
            {
                _downButton = downT.GetComponent<Button>();
                if (_downButton == null)
                    _downButton = downT.gameObject.AddComponent<Button>();
                _downButton.onClick.RemoveAllListeners();
                _downButton.interactable = false;
                downT.gameObject.SetActive(false);
            }
        }

        void OnLeftArrowClicked()
        {
            JumpToPrevious();
        }

        void OnRightArrowClicked()
        {
            JumpToNext();
        }

        void UpdateArrowsVisibility()
        {
            if (_arrowsRoot == null)
                return;

            // Default: hide all
            if (_leftButton != null) _leftButton.gameObject.SetActive(false);
            if (_rightButton != null) _rightButton.gameObject.SetActive(false);
            if (_upButton != null) _upButton.gameObject.SetActive(false);
            if (_downButton != null) _downButton.gameObject.SetActive(false);

            // Show specific arrows based on current station index (0=UL, 1=UR, 2=LR)
            if (_currentIndex == 0)
            {
                if (_rightButton != null) _rightButton.gameObject.SetActive(true);
            }
            else if (_currentIndex == 1)
            {
                if (_leftButton != null) _leftButton.gameObject.SetActive(true);
                if (_downButton != null) _downButton.gameObject.SetActive(true);
            }
            else if (_currentIndex == 2)
            {
                if (_upButton != null) _upButton.gameObject.SetActive(true);
            }
        }

        int CountValidStations()
        {
            if (stations == null || stations.Length == 0) return 0;
            int count = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i].anchor != null)
                    count++;
            }
            return count;
        }

        int FindNextValidIndex(int from)
        {
            if (stations == null || stations.Length == 0) return -1;
            int n = stations.Length;
            for (int step = 1; step <= n; step++)
            {
                int i = (from + step) % n;
                if (stations[i].anchor != null)
                    return i;
            }
            return -1;
        }

        int FindPreviousValidIndex(int from)
        {
            if (stations == null || stations.Length == 0) return -1;
            int n = stations.Length;
            for (int step = 1; step <= n; step++)
            {
                int i = (from - step) % n;
                if (i < 0) i += n;
                if (stations[i].anchor != null)
                    return i;
            }
            return -1;
        }
    }
}
