using System.Text;
using UnityEngine;
using UnityCamera = UnityEngine.Camera;

namespace Comfy.Camera
{
    /// <summary>
    /// Watches the active camera for external writes. When another system takes control, the rig is put into Passive mode.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Comfy/Camera Comfort Audit")]
    public sealed class CameraComfortAudit : MonoBehaviour
    {
        const float PositionTolerance = 0.0005f;
        const float RotationTolerance = 0.01f;
        const float SizeTolerance = 0.001f;

        UnityCamera _camera;
        CameraRig2D _rig;
        CameraProvider _provider;
        Vector3 _lastRecordedPosition;
        Quaternion _lastRecordedRotation = Quaternion.identity;
        float _lastRecordedOrtho;
        float _lastRecordedFov;
        int _lastRigFrame = -1;
        bool _conflict;
        bool _forceOverride;
        int _forceFrame;
        readonly StringBuilder _summaryBuilder = new StringBuilder(192);
        string _summary = string.Empty;

        internal void Register(CameraRig2D rig, UnityCamera camera, CameraProvider provider)
        {
            _rig = rig;
            _camera = camera;
            _provider = provider;
            if (_camera != null)
            {
                CacheState(_camera);
            }
        }

        internal void UpdateCameraReference(UnityCamera camera)
        {
            _camera = camera;
            if (_camera != null)
            {
                CacheState(_camera);
            }
        }

        internal void NotifyRigMovement(Vector3 pos, Quaternion rot, float orthoSize, float fov)
        {
            _lastRecordedPosition = pos;
            _lastRecordedRotation = rot;
            _lastRecordedOrtho = orthoSize;
            _lastRecordedFov = fov;
            _lastRigFrame = Time.frameCount;
            _conflict = false;
        }

        internal void NotifyRigPassive(Vector3 pos, Quaternion rot, float orthoSize, float fov)
        {
            _lastRecordedPosition = pos;
            _lastRecordedRotation = rot;
            _lastRecordedOrtho = orthoSize;
            _lastRecordedFov = fov;
        }

        void LateUpdate()
        {
            if (_camera == null)
            {
                TryResolveCamera();
                return;
            }

            if (_forceOverride && Time.frameCount - _forceFrame > 5)
                _forceOverride = false;

            Vector3 pos = _camera.transform.position;
            Quaternion rot = _camera.transform.rotation;
            float ortho = _camera.orthographic ? _camera.orthographicSize : 0f;
            float fov = !_camera.orthographic ? _camera.fieldOfView : 0f;

            bool changed =
                !Approximately(pos, _lastRecordedPosition, PositionTolerance) ||
                !Approximately(rot, _lastRecordedRotation, RotationTolerance) ||
                (_camera.orthographic && Mathf.Abs(ortho - _lastRecordedOrtho) > SizeTolerance) ||
                (!_camera.orthographic && Mathf.Abs(fov - _lastRecordedFov) > SizeTolerance);

            if (changed && Time.frameCount != _lastRigFrame)
            {
                if (!_forceOverride)
                {
                    _conflict = true;
                    _summary = BuildSummary();
                }
            }
            else if (!changed)
            {
                _conflict = false;
            }
        }

        public bool IsConflictDetected => _conflict;

        public string Summary => _summary;

        internal bool ShouldSuppressRig => _conflict && !_forceOverride;

        public void ForceEnable()
        {
            _conflict = false;
            _forceOverride = true;
            _forceFrame = Time.frameCount;
            _summary = string.Empty;
        }

        void TryResolveCamera()
        {
            if (_rig != null)
            {
                var resolved = _rig.TargetCamera;
                if (resolved != null)
                {
                    UpdateCameraReference(resolved);
                    return;
                }
            }

            if (_provider != null)
            {
                var providerCamera = CameraProvider.Active;
                if (providerCamera != null)
                {
                    UpdateCameraReference(providerCamera);
                    return;
                }
            }

            if (UnityCamera.main != null)
            {
                UpdateCameraReference(UnityCamera.main);
            }
        }

        static bool Approximately(Vector3 a, Vector3 b, float tolerance)
        {
            return (a - b).sqrMagnitude <= tolerance * tolerance;
        }

        static bool Approximately(Quaternion a, Quaternion b, float tolerance)
        {
            if (Quaternion.Dot(a, b) > 0.999999f)
                return true;
            float angle = Quaternion.Angle(a, b);
            return angle <= tolerance;
        }

        string BuildSummary()
        {
            _summaryBuilder.Length = 0;
            _summaryBuilder.Append("External camera movement detected. Active components: ");

            if (_camera != null)
            {
                var behaviours = _camera.GetComponents<MonoBehaviour>();
                bool first = true;
                for (int i = 0; i < behaviours.Length; i++)
                {
                    var behaviour = behaviours[i];
                    if (behaviour == null)
                        continue;
                    if (ReferenceEquals(behaviour, _rig) || ReferenceEquals(behaviour, this))
                        continue;
                    if (!first)
                        _summaryBuilder.Append(", ");
                    _summaryBuilder.Append(behaviour.GetType().Name);
                    first = false;
                }

                if (first)
                    _summaryBuilder.Append("Camera");
            }
            else
            {
                _summaryBuilder.Append("No camera reference");
            }

            return _summaryBuilder.ToString();
        }

        void CacheState(UnityCamera camera)
        {
            _lastRecordedPosition = camera.transform.position;
            _lastRecordedRotation = camera.transform.rotation;
            _lastRecordedOrtho = camera.orthographic ? camera.orthographicSize : 0f;
            _lastRecordedFov = camera.orthographic ? 0f : camera.fieldOfView;
            _lastRigFrame = -1;
            _conflict = false;
            _summary = string.Empty;
        }
    }
}
