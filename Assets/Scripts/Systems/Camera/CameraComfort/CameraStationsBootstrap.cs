using UnityEngine;

namespace Comfy.Camera
{
    /// <summary>
    /// Sets the camera rig to stations-only mode (no manual pan/auto-focus) and
    /// computes world bounds from assigned station anchors. Optionally snaps to
    /// the first station (UL) on start.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Comfy/Camera Comfort/Stations-Only Bootstrap")]
    [RequireComponent(typeof(CameraComfortRuntime))]
    [RequireComponent(typeof(CameraStations))]
    public sealed class CameraStationsBootstrap : MonoBehaviour
    {
        CameraComfortRuntime _runtime;
        CameraStations _stations;
        UnityEngine.Camera _camera;

        bool _initialized;

        void Awake()
        {
            _runtime = GetComponent<CameraComfortRuntime>();
            _stations = GetComponent<CameraStations>();
        }

        void Start()
        {
            _camera = _runtime != null && _runtime.Rig != null ? _runtime.Rig.TargetCamera : null;
            Configure(true);
        }

        void Update()
        {
            // Live-apply while playing so designers can tweak settings asset in real time.
            Configure(false);
        }

        /// <summary>Applies station-mode: MotionSafe, disables auto-focus/zoom/scroll, sets default duration, and sets bounds from anchors + padding + view extents.</summary>
        void Configure(bool onStart)
        {
            if (_runtime == null || _stations == null)
                return;

            var settings = _runtime.Settings;

            if (settings.stationMode)
            {
                // Lock movement to station jumps only, no auto-focus, no extra motion sources.
                _runtime.SetPanMethod(PanMethod.MotionSafe);
                _runtime.SetAutoFocus(false, 0f, 0f);
                _runtime.SetZoomEnabled(false);
                _runtime.SetScrollAsVerticalPan(false);
                _runtime.SetEdgePan(false, 16);

                // Disable legacy camera controllers if present
                var legacyPan = GetComponent<CameraPanZoom2DPlus>();
                if (legacyPan != null && legacyPan.enabled) legacyPan.enabled = false;
                var legacyFollow = GetComponent<CameraFollowDuringDrag2D>();
                if (legacyFollow != null && legacyFollow.enabled) legacyFollow.enabled = false;
            }
            else
            {
                // Re-enable legacy controllers when station mode is off (if they exist)
                var legacyPan = GetComponent<CameraPanZoom2DPlus>();
                if (legacyPan != null && !legacyPan.enabled) legacyPan.enabled = true;
                var legacyFollow = GetComponent<CameraFollowDuringDrag2D>();
                if (legacyFollow != null && !legacyFollow.enabled) legacyFollow.enabled = true;
            }

            // Sync default station duration from settings
            if (_stations.defaultDuration != settings.stationDefaultDuration)
                _stations.defaultDuration = Mathf.Max(0f, settings.stationDefaultDuration);

            // Compute bounds by encapsulating all valid anchors, then add padding.
            bool hasAny = false;
            Vector2 min = default;
            Vector2 max = default;
            var entries = _stations.stations ?? System.Array.Empty<CameraStations.StationEntry>();
            for (int i = 0; i < entries.Length; i++)
            {
                var t = entries[i].anchor;
                if (t == null) continue;
                var p = (Vector2)t.position;
                if (!hasAny)
                {
                    min = max = p;
                    hasAny = true;
                }
                else
                {
                    if (p.x < min.x) min.x = p.x;
                    if (p.y < min.y) min.y = p.y;
                    if (p.x > max.x) max.x = p.x;
                    if (p.y > max.y) max.y = p.y;
                }
            }

            if (settings.stationMode && hasAny)
            {
                // Pull camera reference (may appear after Start)
                if (_camera == null)
                    _camera = _runtime.Rig != null ? _runtime.Rig.TargetCamera : UnityEngine.Camera.main;

                float pad = Mathf.Max(0f, settings.stationBoundsPadding);
                float halfHeight = 0f, halfWidth = 0f;
                if (_camera != null && _camera.orthographic)
                {
                    halfHeight = _camera.orthographicSize;
                    halfWidth = halfHeight * _camera.aspect;
                }

                // Ensure the camera center can reach anchors: expand by view half extents.
                float minX = min.x - (halfWidth + pad);
                float maxX = max.x + (halfWidth + pad);
                float minY = min.y - (halfHeight + pad);
                float maxY = max.y + (halfHeight + pad);

                float width = Mathf.Max(0.001f, (maxX - minX));
                float height = Mathf.Max(0.001f, (maxY - minY));
                var rect = new Rect(minX, minY, width, height);
                _runtime.SetBounds(rect);
            }

            if (onStart && settings.stationSnapToFirstOnStart)
            {
                _runtime.JumpToStationIndex(0, 0f);
            }
        }
    }
}
