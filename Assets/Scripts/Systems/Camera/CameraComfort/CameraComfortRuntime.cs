using UnityEngine;

namespace Comfy.Camera
{
    /// <summary>
    /// Facade for runtime configuration. Hosts a settings snapshot and forwards mutations to the rig.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Comfy/Camera Comfort Runtime")]
    public sealed class CameraComfortRuntime : MonoBehaviour
    {
        [SerializeField] CameraComfortSettings settingsAsset;
        [SerializeField] bool activateOnEnable;
        [SerializeField] bool liveLinkInEditor = true;

        CameraRig2D _rig;
        CameraStations _stations;
        CameraComfortAudit _audit;
        CameraProvider _provider;
        CameraComfortSettings.SettingsData _settings;
        bool _initialized;
#if UNITY_EDITOR
        bool _liveLinkSubscribed;
#endif

        void Awake()
        {
            _rig = GetComponent<CameraRig2D>();
            _stations = GetComponent<CameraStations>();
            _audit = GetComponent<CameraComfortAudit>();
            _provider = GetComponent<CameraProvider>();

            _settings = settingsAsset != null
                ? settingsAsset.CreateSnapshot()
                : CameraComfortSettings.SettingsData.CreateDefault();

            if (_rig != null)
            {
                _rig.AttachRuntime(this);
                _rig.ApplySettings(_settings);
                if (_audit != null)
                    _rig.BindAudit(_audit, _provider);
            }

            _initialized = true;

#if UNITY_EDITOR
            if (Application.isPlaying && liveLinkInEditor)
                SubscribeToSettingsAsset();
#endif
        }

        void OnEnable()
        {
            if (!_initialized)
                return;

            EnableRig(activateOnEnable);

#if UNITY_EDITOR
            if (Application.isPlaying && liveLinkInEditor)
                SubscribeToSettingsAsset();
#endif
        }

#if UNITY_EDITOR
        void OnDisable()
        {
            UnsubscribeFromSettingsAsset();
        }

        void OnDestroy()
        {
            UnsubscribeFromSettingsAsset();
        }
#endif

        /// <summary>Enables or disables the comfort rig without destroying it (opt-in path).</summary>
        public void EnableRig(bool enabled)
        {
            if (_rig != null)
                _rig.SetRuntimeEnabled(enabled);
        }

        public void Apply(CameraComfortSettings settings)
        {
            if (settings == null) return;
            _settings = settings.CreateSnapshot();
            PushSettings();
        }

        public void SetPanMethod(PanMethod method)
        {
            _settings.panMethod = method;
            PushSettings();
        }

        public void SetDragButton(MouseDragButton button)
        {
            _settings.dragButton = button;
            PushSettings();
        }

        public void SetEdgePan(bool enabled, int deadzonePixels)
        {
            _settings.edgePanEnabled = enabled;
            _settings.edgeDeadzonePx = Mathf.Max(0, deadzonePixels);
            PushSettings();
        }

        public void SetPanSpeed(float speed)
        {
            _settings.panSpeed = Mathf.Max(0f, speed);
            PushSettings();
        }

        public void SetPanAccelSeconds(float seconds)
        {
            _settings.panAccelSeconds = Mathf.Max(0f, seconds);
            PushSettings();
        }

        public void SetScrollAsVerticalPan(bool enabled)
        {
            _settings.scrollIsVerticalPan = enabled;
            PushSettings();
        }

        public void SetZoomEnabled(bool enabled)
        {
            _settings.zoomEnabled = enabled;
            PushSettings();
        }

        public void SetZoomRange(float minMultiplier, float maxMultiplier)
        {
            _settings.zoomMin = Mathf.Max(0.1f, minMultiplier);
            _settings.zoomMax = Mathf.Max(_settings.zoomMin, maxMultiplier);
            PushSettings();
        }

        public void SetAutoFocus(bool enabled, float strength, float maxSeconds)
        {
            _settings.autoFocusWhileDragging = enabled;
            _settings.autoFocusStrength = Mathf.Max(0f, strength);
            _settings.autoFocusMaxSeconds = Mathf.Max(0f, maxSeconds);
            PushSettings();
        }

        public void SetInertia(bool enabled, float inertia)
        {
            _settings.inertiaEnabled = enabled;
            _settings.inertia = Mathf.Clamp01(inertia);
            PushSettings();
        }

        public void SetMotionSafe(bool enabled)
        {
            _settings.motionSafePreset = enabled;
            PushSettings();
        }

        public void SetBounds(Rect bounds)
        {
            _settings.cameraWorldBounds = bounds;
            PushSettings();
        }

        public void SetExecutionPhase(ExecutionPhase phase)
        {
            if (_rig != null)
                _rig.SetExecutionPhase(phase);
        }

        public void SetReservedRightMouse(bool reserved)
        {
            _settings.rmbReservedForInteractions = reserved;
            PushSettings();
        }

        // -------- Station Jump (generic) --------

        /// <summary>Jump to a station by array index.</summary>
        public bool JumpToStationIndex(int index, float durationSeconds = 0.2f)
            => _stations != null && _stations.JumpToIndex(index, durationSeconds);

        /// <summary>Jump to a station by its label (case-insensitive).</summary>
        public bool JumpToStationLabel(string label, float durationSeconds = 0.2f)
            => _stations != null && _stations.JumpToLabel(label, durationSeconds);

        /// <summary>Jump directly to a given anchor Transform.</summary>
        public bool JumpToStationAnchor(Transform anchor, float durationSeconds = 0.2f)
            => _stations != null && _stations.JumpToAnchor(anchor, durationSeconds);

        // ---------------------------------------

        public void SetFocusTargets(ICameraFocusTarget primary, ICameraFocusTarget partner, bool dragging)
        {
            if (_rig != null)
                _rig.SetFocusTargets(primary, partner, dragging);
        }

        public CameraRig2D Rig => _rig;
        public CameraComfortAudit Audit => _audit;
        public CameraComfortSettings.SettingsData Settings => _settings;

        public void ForceEnable()
        {
            _audit?.ForceEnable();
            _rig?.ForceEnable();
        }

        void PushSettings()
        {
            if (_rig != null)
                _rig.ApplySettings(_settings);
        }

#if UNITY_EDITOR
        void SubscribeToSettingsAsset()
        {
            if (_liveLinkSubscribed || settingsAsset == null) return;
            settingsAsset.OnSettingsChanged += HandleSettingsChanged;
            _liveLinkSubscribed = true;
        }

        void UnsubscribeFromSettingsAsset()
        {
            if (!_liveLinkSubscribed || settingsAsset == null) return;
            settingsAsset.OnSettingsChanged -= HandleSettingsChanged;
            _liveLinkSubscribed = false;
        }

        void HandleSettingsChanged(CameraComfortSettings.SettingsData _)
        {
            if (!liveLinkInEditor || settingsAsset == null) return;
            Apply(settingsAsset);
        }
#endif
    }
}
