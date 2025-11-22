using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Comfy.Camera
{
    /// <summary>
    /// Tunable settings that drive the camera comfort rig. Instances can be shared as assets or cloned per scene.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraComfortSettings", menuName = "Comfy/Camera Comfort Settings", order = 0)]
        public sealed class CameraComfortSettings : ScriptableObject
        {
            [SerializeField] SettingsData data = SettingsData.CreateDefault();

        public event Action<SettingsData> OnSettingsChanged;

#if UNITY_EDITOR
        SettingsData _lastValidatedData = SettingsData.CreateDefault();
#endif

        /// <summary>Returns a value-type snapshot so callers can mutate without touching the asset.</summary>
        public SettingsData CreateSnapshot() => data;

        /// <summary>Overwrites the stored data (editor use).</summary>
        public void Apply(SettingsData source)
        {
            data = source;
#if UNITY_EDITOR
            if (Application.isPlaying) TryNotifyChange();
#endif
        }

        /// <summary>Builds a readable cheat-sheet of all settings with brief explanations (useful for overlays).</summary>
        public string BuildTooltipSummary()
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("Camera Comfort – Settings Cheat-Sheet");
            sb.AppendLine($"Pan Method: {data.panMethod} — " + Tooltips.PanMethod);
            sb.AppendLine($"Drag Button: {data.dragButton} — " + Tooltips.DragButton);
            sb.AppendLine($"Reserve RMB: {data.rmbReservedForInteractions} — " + Tooltips.RMBReserved);
            sb.AppendLine($"Edge Pan: {data.edgePanEnabled} — " + Tooltips.EdgePan);
            sb.AppendLine($"Edge Deadzone: {data.edgeDeadzonePx}px — " + Tooltips.EdgeDeadzonePx);
            sb.AppendLine($"Pan Speed: {data.panSpeed} — " + Tooltips.PanSpeed);
            sb.AppendLine($"Pan Accel: {data.panAccelSeconds}s — " + Tooltips.PanAccelSeconds);
            sb.AppendLine($"Scroll = Vertical Pan: {data.scrollIsVerticalPan} — " + Tooltips.ScrollIsVerticalPan);
            sb.AppendLine($"Zoom Enabled: {data.zoomEnabled} — " + Tooltips.ZoomEnabled);
            sb.AppendLine($"Zoom Range: {data.zoomMin:0.##}..{data.zoomMax:0.##} — " + Tooltips.ZoomRange);
            sb.AppendLine($"Auto-Focus While Dragging: {data.autoFocusWhileDragging} — " + Tooltips.AutoFocus);
            sb.AppendLine($"Auto-Focus Strength: {data.autoFocusStrength:0.##} — " + Tooltips.AutoFocusStrength);
            sb.AppendLine($"Auto-Focus Max Sec: {data.autoFocusMaxSeconds:0.##} — " + Tooltips.AutoFocusMaxSeconds);
            sb.AppendLine($"Inertia Enabled: {data.inertiaEnabled} — " + Tooltips.InertiaEnabled);
            sb.AppendLine($"Inertia: {data.inertia:0.##} — " + Tooltips.Inertia);
            sb.AppendLine($"World Bounds: {data.cameraWorldBounds} — " + Tooltips.CameraWorldBounds);
            sb.AppendLine($"Motion-Safe Preset: {data.motionSafePreset} — " + Tooltips.MotionSafePreset);
            return sb.ToString();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying)
            {
                _lastValidatedData = data;
                return;
            }
            TryNotifyChange();
        }

        bool TryNotifyChange()
        {
            if (EqualityComparer<SettingsData>.Default.Equals(_lastValidatedData, data))
                return false;

            _lastValidatedData = data;
            OnSettingsChanged?.Invoke(data);
            return true;
        }
#endif

        [Serializable]
        public struct SettingsData
        {
            [Tooltip(Tooltips.PanMethod)]
            public PanMethod panMethod;

            [Tooltip(Tooltips.DragButton)]
            public MouseDragButton dragButton;

            [Tooltip(Tooltips.RMBReserved)]
            public bool rmbReservedForInteractions;

            [Tooltip(Tooltips.EdgePan)]
            public bool edgePanEnabled;

            [Tooltip(Tooltips.EdgeDeadzonePx)]
            public int edgeDeadzonePx;

            [Tooltip(Tooltips.PanSpeed)]
            public float panSpeed;

            [Tooltip(Tooltips.PanAccelSeconds)]
            public float panAccelSeconds;

            [Tooltip(Tooltips.ScrollIsVerticalPan)]
            public bool scrollIsVerticalPan;

            [Tooltip(Tooltips.ZoomEnabled)]
            public bool zoomEnabled;

            [Tooltip(Tooltips.ZoomRange)]
            public float zoomMin;

            [Tooltip(Tooltips.ZoomRange)]
            public float zoomMax;

            [Tooltip(Tooltips.AutoFocus)]
            public bool autoFocusWhileDragging;

            [Tooltip(Tooltips.AutoFocusStrength)]
            public float autoFocusStrength;

            [Tooltip(Tooltips.AutoFocusMaxSeconds)]
            public float autoFocusMaxSeconds;

            [Tooltip(Tooltips.InertiaEnabled)]
            public bool inertiaEnabled;

            [Tooltip(Tooltips.Inertia)]
            public float inertia;

            [Tooltip(Tooltips.CameraWorldBounds)]
            public Rect cameraWorldBounds;

            [Tooltip(Tooltips.MotionSafePreset)]
            public bool motionSafePreset;

            // ---- Stations-only mode + helpers ----

            [Tooltip(Tooltips.StationMode)]
            public bool stationMode;

            [Tooltip(Tooltips.StationSnapToFirstOnStart)]
            public bool stationSnapToFirstOnStart;

            [Tooltip(Tooltips.StationBoundsPadding)]
            public float stationBoundsPadding;

            [Tooltip(Tooltips.StationDefaultDuration)]
            public float stationDefaultDuration;

            [Tooltip(Tooltips.StationWasdEnabled)]
            public bool stationWasdEnabled;

            [Tooltip(Tooltips.StationInputCooldownSeconds)]
            public float stationInputCooldownSeconds;

            [Tooltip(Tooltips.StationEdgeSwitchEnabled)]
            public bool stationEdgeSwitchEnabled;

            [Tooltip(Tooltips.StationEdgeDeadzonePx)]
            public int stationEdgeDeadzonePx;

            [Tooltip(Tooltips.StationEdgeCooldownSeconds)]
            public float stationEdgeCooldownSeconds;

            [Tooltip(Tooltips.StationProximity)]
            public float stationProximity;

            [Tooltip(Tooltips.StationEdgeCornerMargin)]
            public float stationEdgeCornerMargin;

            public static SettingsData CreateDefault()
            {
                SettingsData defaults;
                defaults.panMethod = PanMethod.EdgeAndDrag;
                defaults.dragButton = MouseDragButton.Middle;
                defaults.rmbReservedForInteractions = true;
                defaults.edgePanEnabled = true;
                defaults.edgeDeadzonePx = 16;
                defaults.panSpeed = 4f;
                defaults.panAccelSeconds = 0.16f;
                defaults.scrollIsVerticalPan = true;
                defaults.zoomEnabled = false;
                defaults.zoomMin = 1.0f;
                defaults.zoomMax = 1.15f;
                defaults.autoFocusWhileDragging = true;
                defaults.autoFocusStrength = 0.25f;
                defaults.autoFocusMaxSeconds = 0.6f;
                defaults.inertiaEnabled = true;
                defaults.inertia = 0.85f;
                defaults.cameraWorldBounds = new Rect(-20f, -10f, 40f, 20f);
                defaults.motionSafePreset = false;
                // Stations-only defaults
                defaults.stationMode = true;
                defaults.stationSnapToFirstOnStart = true;
                defaults.stationBoundsPadding = 2.0f;
                defaults.stationDefaultDuration = 0.7f;
                defaults.stationWasdEnabled = true;
                defaults.stationInputCooldownSeconds = 0.25f;
                defaults.stationEdgeSwitchEnabled = true;
                defaults.stationEdgeDeadzonePx = 16;
                defaults.stationEdgeCooldownSeconds = 0.35f;
                defaults.stationProximity = 0.25f;
                defaults.stationEdgeCornerMargin = 0.15f;
                return defaults;
            }
        }

        /// <summary>Centralized tooltip strings (single source for Inspector + summary).</summary>
        public static class Tooltips
        {
            public const string PanMethod = "Choose how players move the camera: Edge+Drag, WASD only, Drag only, or Motion-Safe (no automatic motion).";
            public const string DragButton = "Which gesture drags the world: Middle Mouse (default) or Alt+Left Mouse. RMB is reserved for interactions.";
            public const string RMBReserved = "Keep Right-Mouse free for gameplay interactions (camera never binds RMB).";
            public const string EdgePan = "Allow gentle movement when the cursor nears the screen edges.";
            public const string EdgeDeadzonePx = "How close (in pixels) the cursor must get to the screen edge before edge-pan starts.";
            public const string PanSpeed = "Base panning speed (units/second).";
            public const string PanAccelSeconds = "Ramp-in time for panning (seconds). Lower = snappier response.";
            public const string ScrollIsVerticalPan = "Use the scroll wheel for vertical panning (when zoom is disabled).";
            public const string ZoomEnabled = "Enable tiny zoom adjustments. Off by default to avoid motion sickness.";
            public const string ZoomRange = "Minimum/maximum zoom scale when zoom is enabled (keep a tight range).";
            public const string AutoFocus = "While dragging, gently nudge the camera to keep the item and its target in view.";
            public const string AutoFocusStrength = "How strongly the camera nudges toward the focus point each second.";
            public const string AutoFocusMaxSeconds = "How long auto-focus is allowed to run per drag action.";
            public const string InertiaEnabled = "Keep motion gliding briefly after input ends.";
            public const string Inertia = "Glide amount (0–1). Higher = longer coast.";
            public const string CameraWorldBounds = "World-space rectangle the camera must stay inside (accounts for view size).";
            public const string MotionSafePreset = "Turn off edge-pan, drag-pan, zoom, and auto-focus for motion-sensitive players.";

            // Stations-only mode + helpers
            public const string StationMode = "Lock camera to predefined stations (UL, UR, LR). Movement only via jumps.";
            public const string StationSnapToFirstOnStart = "Snap to the first station (UL) on start.";
            public const string StationBoundsPadding = "Extra world-units padding around min/max station anchors for computed bounds.";
            public const string StationDefaultDuration = "Default station jump duration (seconds) used when a station's duration is 0.";
            public const string StationWasdEnabled = "Map WASD to station jumps (A=Left, D=Right, W=Up, S=Down).";
            public const string StationInputCooldownSeconds = "Cooldown between station jumps triggered by inputs (seconds).";
            public const string StationEdgeSwitchEnabled = "Trigger station jumps when mouse nears screen edges.";
            public const string StationEdgeDeadzonePx = "Screen-edge proximity in pixels for edge-triggered station jumps.";
            public const string StationEdgeCooldownSeconds = "Cooldown between edge-triggered station jumps (seconds).";
            public const string StationProximity = "Distance threshold (world units) to consider the camera at a station (prevents mid-jump triggers).";
            public const string StationEdgeCornerMargin = "Ignore corner zones; only trigger when the mouse is near the center of the active edge.";
        }
    }

    public enum PanMethod { EdgeAndDrag, WASDOnly, DragOnly, MotionSafe }
    public enum MouseDragButton { None, Middle, AltPlusLeft }
    public enum ExecutionPhase { Update, LateUpdate }
}
