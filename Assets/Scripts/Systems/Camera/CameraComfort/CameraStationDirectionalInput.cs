using UnityEngine;

namespace Comfy.Camera
{
    /// <summary>
    /// Maps WASD to station jumps among three stations ordered UL, UR, LR.
    /// A=Left, D=Right, W=Up, S=Down. LL is intentionally unreachable.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Comfy/Camera Comfort/Station Directional Input (WASD)")]
    [RequireComponent(typeof(CameraStations))]
    [RequireComponent(typeof(CameraComfortRuntime))]
    public sealed class CameraStationDirectionalInput : MonoBehaviour
    {
        CameraStations _stations;
        CameraComfortRuntime _runtime;
        float _nextAllowedTime;

        void Awake()
        {
            _stations = GetComponent<CameraStations>();
            _runtime = GetComponent<CameraComfortRuntime>();
        }

        void Update()
        {
            if (_stations == null || _runtime == null)
                return;

            var s = _runtime.Settings;
            if (!s.stationWasdEnabled)
                return;

            int dirX = 0;
            int dirY = 0;

            if (InputKeyReader.GetKeyDown(KeyCode.A)) dirX = -1;
            else if (InputKeyReader.GetKeyDown(KeyCode.D)) dirX = 1;
            else if (InputKeyReader.GetKeyDown(KeyCode.W)) dirY = 1;
            else if (InputKeyReader.GetKeyDown(KeyCode.S)) dirY = -1;

            if (dirX == 0 && dirY == 0)
                return;

            if (Time.time < _nextAllowedTime)
                return;

            int current = FindNearestStationIndex();
            if (current < 0)
                return;

            int targetIndex = MapDirectionToTarget(current, dirX, dirY);
            if (targetIndex < 0)
            {
                // Handle UL down as a chain UL->UR->LR
                if (current == 0 && dirY < 0)
                {
                    float d1 = GetDurationForIndex(1);
                    float d2 = GetDurationForIndex(2);
                    _runtime.JumpToStationIndex(1, d1);
                    _nextAllowedTime = Time.time + Mathf.Max(s.stationInputCooldownSeconds, d1 + d2 + 0.05f);
                    StartCoroutine(DelayedSecondHop(d1, 2, d2));
                }
                return;
            }

            float d = GetDurationForIndex(targetIndex);
            _runtime.JumpToStationIndex(targetIndex, d);
            _nextAllowedTime = Time.time + Mathf.Max(s.stationInputCooldownSeconds, d + 0.02f);
        }

        System.Collections.IEnumerator DelayedSecondHop(float delay, int targetIndex, float duration)
        {
            yield return new WaitForSeconds(Mathf.Max(0.01f, delay));
            _runtime.JumpToStationIndex(targetIndex, duration);
        }

        int MapDirectionToTarget(int currentIndex, int dx, int dy)
        {
            // Order: 0=UL, 1=UR, 2=LR
            if (currentIndex == 0)
            {
                if (dx > 0) return 1; // UL -> UR
                if (dy < 0) return -1; // chain handled by caller
            }
            else if (currentIndex == 1)
            {
                if (dx < 0) return 0; // UR -> UL
                if (dy < 0) return 2; // UR -> LR
            }
            else if (currentIndex == 2)
            {
                if (dy > 0) return 1; // LR -> UR
            }
            return -1;
        }

        int FindNearestStationIndex()
        {
            var cam = _runtime.Rig != null ? _runtime.Rig.TargetCamera : null;
            if (cam == null) cam = UnityEngine.Camera.main;
            if (cam == null) return -1;

            var entries = _stations.stations ?? System.Array.Empty<CameraStations.StationEntry>();
            int bestIdx = -1;
            float bestDistSq = float.PositiveInfinity;
            for (int i = 0; i < entries.Length; i++)
            {
                var t = entries[i].anchor;
                if (t == null) continue;
                float d = (t.position - cam.transform.position).sqrMagnitude;
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        float GetDurationForIndex(int index)
        {
            var entries = _stations.stations ?? System.Array.Empty<CameraStations.StationEntry>();
            if (index < 0 || index >= entries.Length)
                return _stations.defaultDuration;
            var e = entries[index];
            return e.duration > 0f ? e.duration : _stations.defaultDuration;
        }
    }
}

