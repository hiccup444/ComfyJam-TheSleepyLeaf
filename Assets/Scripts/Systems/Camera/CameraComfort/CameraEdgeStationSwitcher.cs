using System.Collections;
using UnityEngine;

namespace Comfy.Camera
{
    /// <summary>
    /// Triggers station jumps when the mouse nears screen edges. Uses three stations ordered as:
    /// 0: UL, 1: UR, 2: LR. Lower-left (LL) is intentionally unreachable. Bottom edge from UL chains UL→UR→LR.
    /// Requires CameraStations to have anchors in the above order.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Comfy/Camera Comfort/Edge Station Switcher")]
    [RequireComponent(typeof(CameraStations))]
    [RequireComponent(typeof(CameraComfortRuntime))]
    public sealed class CameraEdgeStationSwitcher : MonoBehaviour
    {
        [Tooltip("Enable debug logs.")]
        [SerializeField] bool debugLogs = false;

        CameraStations _stations;
        CameraComfortRuntime _runtime;
        UnityEngine.Camera _camera;
        float _nextAllowedTime;
        Coroutine _chainRoutine;

        void Awake()
        {
            _stations = GetComponent<CameraStations>();
            _runtime = GetComponent<CameraComfortRuntime>();
        }

        void Start()
        {
            _camera = _runtime != null && _runtime.Rig != null ? _runtime.Rig.TargetCamera : null;
            if (_camera == null) _camera = UnityEngine.Camera.main;
        }

        void Update()
        {
            if (_stations == null || _runtime == null || _camera == null)
                return;

            // Don't allow edge switching during cutscene
            var cutsceneManager = FindFirstObjectByType<CutsceneManager>();
            if (cutsceneManager != null && cutsceneManager.IsCutscenePlaying)
                return;

            // Screen/Mouse checks
            int w = Screen.width;
            int h = Screen.height;
            if (w <= 0 || h <= 0) return;

            Vector3 mp = Input.mousePosition;
            if (mp.x < 0f || mp.y < 0f || mp.x > w || mp.y > h)
                return;

            var s = _runtime.Settings;
            if (!s.stationEdgeSwitchEnabled)
                return;

            int dz = Mathf.Max(1, s.stationEdgeDeadzonePx);
            bool onLeft = mp.x <= dz;
            bool onRight = mp.x >= (w - dz);
            bool onBottom = mp.y <= dz;
            bool onTop = mp.y >= (h - dz);

            // No edge proximity -> no action
            if (!onLeft && !onRight && !onBottom && !onTop)
                return;

            float margin = Mathf.Clamp01(s.stationEdgeCornerMargin);
            if (margin > 0f)
            {
                bool verticalCenter = mp.y >= h * margin && mp.y <= h * (1f - margin);
                bool horizontalCenter = mp.x >= w * margin && mp.x <= w * (1f - margin);

                if (!verticalCenter)
                {
                    onLeft = false;
                    onRight = false;
                }
                if (!horizontalCenter)
                {
                    onTop = false;
                    onBottom = false;
                }
            }

            if (Time.time < _nextAllowedTime)
                return;

            // Require camera to be near a station to accept an edge switch
            int current = FindNearestStationIndex();
            if (current < 0)
                return;
            if (!IsNearStation(current, _runtime.Settings.stationProximity))
                return;

            // Choose direction by strongest proximity to edge; break ties with horizontal preference
            float leftScore = onLeft ? 1f - Mathf.Clamp01((float)mp.x / dz) : 0f;
            float rightScore = onRight ? 1f - Mathf.Clamp01((float)(w - mp.x) / dz) : 0f;
            float bottomScore = onBottom ? 1f - Mathf.Clamp01((float)mp.y / dz) : 0f;
            float topScore = onTop ? 1f - Mathf.Clamp01((float)(h - mp.y) / dz) : 0f;

            if (current == 0)
                bottomScore = 0f;

            Direction dir = Direction.None;
            float best = 0f;
            // Prefer horizontal when equal (UX expectation for side edges)
            if (leftScore > best) { best = leftScore; dir = Direction.Left; }
            if (rightScore > best) { best = rightScore; dir = Direction.Right; }
            if (bottomScore > best) { best = bottomScore; dir = Direction.Down; }
            if (topScore > best) { best = topScore; dir = Direction.Up; }

            if (dir == Direction.None)
                return;

            TrySwitch(current, dir);
        }

        enum Direction { None, Left, Right, Up, Down }

        void TrySwitch(int currentIndex, Direction dir)
        {
            var s = _runtime.Settings;
            // Expected order: 0=UL, 1=UR, 2=LR
            int targetIndex = -1;
            bool chain = false;
            int chainMid = -1;

            switch (currentIndex)
            {
                case 0: // UL
                    if (dir == Direction.Right) targetIndex = 1; // UL -> UR
                    else if (dir == Direction.Down) { chain = true; chainMid = 1; targetIndex = 2; } // UL -> UR -> LR
                    break;
                case 1: // UR
                    if (dir == Direction.Left) targetIndex = 0; // UR -> UL
                    else if (dir == Direction.Down) targetIndex = 2; // UR -> LR
                    break;
                case 2: // LR
                    if (dir == Direction.Up) targetIndex = 1; // LR -> UR
                    break;
            }

            if (targetIndex < 0)
                return;

            if (chain && chainMid >= 0)
            {
                // Chain UL -> UR -> LR using station durations
                float d1 = GetDurationForIndex(chainMid);
                float d2 = GetDurationForIndex(targetIndex);

#if UNITY_EDITOR
                if (debugLogs) Debug.Log($"[EdgeStationSwitcher] Chain {currentIndex} -> {chainMid} -> {targetIndex} (d1={d1:0.###}, d2={d2:0.###})", this);
#endif

                // Stop any previous chain
                if (_chainRoutine != null)
                {
                    StopCoroutine(_chainRoutine);
                    _chainRoutine = null;
                }

                // Kick first hop now, second after d1
                _runtime.JumpToStationIndex(chainMid, d1);
                _nextAllowedTime = Time.time + Mathf.Max(s.stationEdgeCooldownSeconds, d1 + d2 + 0.05f);
                _chainRoutine = StartCoroutine(DelayedSecondHop(d1, targetIndex, d2));
            }
            else
            {
                float d = GetDurationForIndex(targetIndex);
#if UNITY_EDITOR
                if (debugLogs) Debug.Log($"[EdgeStationSwitcher] Jump {currentIndex} -> {targetIndex} (d={d:0.###})", this);
#endif
                _runtime.JumpToStationIndex(targetIndex, d);
                _nextAllowedTime = Time.time + Mathf.Max(s.stationEdgeCooldownSeconds, d + 0.02f);
            }
        }

        IEnumerator DelayedSecondHop(float delay, int targetIndex, float targetDuration)
        {
            yield return new WaitForSeconds(Mathf.Max(0.01f, delay));
            _runtime.JumpToStationIndex(targetIndex, targetDuration);
            _chainRoutine = null;
        }

        float GetDurationForIndex(int index)
        {
            var entries = _stations.stations ?? System.Array.Empty<CameraStations.StationEntry>();
            if (index < 0 || index >= entries.Length)
                return _stations.defaultDuration;
            var e = entries[index];
            return e.duration > 0f ? e.duration : _stations.defaultDuration;
        }

        int FindNearestStationIndex()
        {
            var cam = _camera != null ? _camera.transform : null;
            if (cam == null) return -1;
            var entries = _stations.stations ?? System.Array.Empty<CameraStations.StationEntry>();
            int bestIdx = -1;
            float bestDistSq = float.PositiveInfinity;
            for (int i = 0; i < entries.Length; i++)
            {
                var t = entries[i].anchor;
                if (t == null) continue;
                float d = (t.position - cam.position).sqrMagnitude;
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        bool IsNearStation(int index, float proximity)
        {
            var entries = _stations.stations ?? System.Array.Empty<CameraStations.StationEntry>();
            if (index < 0 || index >= entries.Length) return false;
            var t = entries[index].anchor;
            if (t == null || _camera == null) return false;
            float d = Vector2.Distance((Vector2)t.position, (Vector2)_camera.transform.position);
            return d <= Mathf.Max(0.01f, proximity);
        }
    }
}
