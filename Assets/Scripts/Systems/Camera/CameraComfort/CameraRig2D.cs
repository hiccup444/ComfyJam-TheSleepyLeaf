using System.Text;
using UnityEngine;
using UnityCamera = UnityEngine.Camera;
#if USE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Comfy.Camera
{
    /// <summary>
    /// Primary motion driver. Reads settings, captures input, and applies bounded camera movement.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Comfy/Camera Comfort Rig 2D")]
    public sealed class CameraRig2D : MonoBehaviour
    {
        [SerializeField] ExecutionPhase executionPhase = ExecutionPhase.LateUpdate;
        [SerializeField] UnityCamera fallbackCamera;

        CameraComfortRuntime _runtime;
        CameraComfortSettings.SettingsData _settings;
        CameraComfortAudit _audit;
        CameraProvider _provider;
        UnityCamera _camera;
        Transform _cameraTransform;
        bool _cameraIsOrtho;
        float _baseOrthographicSize;
        float _baseFieldOfView;
        float _zoomScale = 1f;

        Vector2 _currentVelocity;
        Vector2 _frameVelocity;
        bool _edgeActive;
        bool _autoFocusActive;

        bool _runtimeEnabled;
        bool _initialized;
        bool _dragActive;
        Vector3 _dragCameraOrigin;
        Vector3 _dragGrabWorld;

        bool _focusDragging;
        ICameraFocusTarget _focusPrimary;
        ICameraFocusTarget _focusPartner;
        float _focusTimer;

        bool _jumpActive;
        Vector3 _jumpStart;
        Vector3 _jumpTarget;
        float _jumpDuration = 0.2f;
        float _jumpElapsed;

        ExecutionPhase _currentPhase;
        bool _overlayVisible;
        readonly StringBuilder _overlayBuilder = new StringBuilder(256);
        readonly GUIContent _overlayContent = new GUIContent();
        GUIStyle _overlayStyle;
        Rect _overlayRect = new Rect(16f, 16f, 360f, 160f);

        public UnityCamera TargetCamera => _camera;
        public PanMethod CurrentPanMethod => _settings.panMethod;
        public bool IsEdgePanActive => _edgeActive;
        public bool IsAutoFocusActive => _autoFocusActive;
        public bool IsPassive => !_runtimeEnabled || (_audit != null && _audit.ShouldSuppressRig);

        internal void AttachRuntime(CameraComfortRuntime runtime)
        {
            _runtime = runtime;
        }

        internal void BindAudit(CameraComfortAudit audit, CameraProvider provider)
        {
            _audit = audit;
            _provider = provider;
            EnsureCamera();
            if (_audit != null && _camera != null)
                _audit.Register(this, _camera, _provider);
        }

        internal void ApplySettings(CameraComfortSettings.SettingsData settings)
        {
            _settings = settings;
            _initialized = true;
            if (!_settings.zoomEnabled && _zoomScale != 1f)
                ResetZoom();
        }

        internal void SetRuntimeEnabled(bool enabled)
        {
            _runtimeEnabled = enabled;
            if (!enabled)
            {
                _currentVelocity = Vector2.zero;
                _frameVelocity = Vector2.zero;
                _dragActive = false;
                _jumpActive = false;
            }
            EnsureCamera();
        }

        internal void SetExecutionPhase(ExecutionPhase phase)
        {
            _currentPhase = phase;
            executionPhase = phase;
        }

        internal void ForceEnable()
        {
            _currentVelocity = Vector2.zero;
            _frameVelocity = Vector2.zero;
        }

        internal void SetFocusTargets(ICameraFocusTarget primary, ICameraFocusTarget partner, bool dragging)
        {
            _focusPrimary = primary;
            _focusPartner = partner;
            _focusDragging = dragging;
            if (!dragging)
                _focusTimer = 0f;
        }

        internal void RequestStationJump(Vector3 targetPosition, float durationSeconds)
        {
            if (!EnsureCamera())
                return;

            _jumpStart = _cameraTransform.position;
            _jumpTarget = new Vector3(targetPosition.x, targetPosition.y, _jumpStart.z);
            _jumpDuration = Mathf.Max(0.01f, durationSeconds);
            _jumpElapsed = 0f;
            _jumpActive = true;
        }

        void Awake()
        {
            _currentPhase = executionPhase;
            EnsureCamera();
        }

        void OnEnable()
        {
            EnsureCamera();
        }

        void Update()
        {
            if (CameraInput.GetKeyDown(KeyCode.F9))
                _overlayVisible = !_overlayVisible;

            if (_currentPhase == ExecutionPhase.Update)
                Tick(Time.deltaTime);
        }

        void LateUpdate()
        {
            if (_currentPhase == ExecutionPhase.LateUpdate)
                Tick(Time.deltaTime);
        }

        void Tick(float deltaTime)
        {
            if (!_initialized)
                return;

            if (!EnsureCamera())
                return;

            if (!_runtimeEnabled)
            {
                NotifyPassive();
                return;
            }

            bool conflict = _audit != null && _audit.ShouldSuppressRig;
            if (conflict)
            {
                NotifyPassive();
                return;
            }

            float dt = Mathf.Max(0.0001f, deltaTime);
            Vector3 position = _cameraTransform.position;

            _frameVelocity = Vector2.zero;

            bool safePreset = _settings.motionSafePreset || _settings.panMethod == PanMethod.MotionSafe;
            bool allowWASD = _settings.panMethod == PanMethod.WASDOnly || _settings.panMethod == PanMethod.EdgeAndDrag;
            bool allowDrag = !safePreset && (_settings.panMethod == PanMethod.EdgeAndDrag || _settings.panMethod == PanMethod.DragOnly);
            bool allowEdge = !safePreset && _settings.panMethod == PanMethod.EdgeAndDrag && _settings.edgePanEnabled;

            if (safePreset && _settings.panMethod != PanMethod.WASDOnly)
                allowWASD = false;

            Vector2 keyboardDir = Vector2.zero;
            if (allowWASD)
            {
                keyboardDir.x = (CameraInput.GetKey(KeyCode.D) ? 1f : 0f) - (CameraInput.GetKey(KeyCode.A) ? 1f : 0f);
                keyboardDir.y = (CameraInput.GetKey(KeyCode.W) ? 1f : 0f) - (CameraInput.GetKey(KeyCode.S) ? 1f : 0f);
                if (keyboardDir.sqrMagnitude > 1f)
                    keyboardDir.Normalize();

                if (keyboardDir.sqrMagnitude > 0f)
                    _frameVelocity += keyboardDir * _settings.panSpeed;
            }

            Vector2 edgeDir = Vector2.zero;
            if (allowEdge)
            {
                Vector2 pointer = CameraInput.MousePosition;
                int deadZone = Mathf.Max(1, _settings.edgeDeadzonePx);
                float screenWidth = Screen.width;
                float screenHeight = Screen.height;
                if (screenWidth > 0f && screenHeight > 0f &&
                    pointer.x >= 0f && pointer.y >= 0f &&
                    pointer.x <= screenWidth && pointer.y <= screenHeight)
                {
                    if (pointer.x <= deadZone)
                        edgeDir.x = -Mathf.Clamp01((deadZone - pointer.x) / deadZone);
                    else if (pointer.x >= screenWidth - deadZone)
                        edgeDir.x = Mathf.Clamp01((pointer.x - (screenWidth - deadZone)) / deadZone);

                    if (pointer.y <= deadZone)
                        edgeDir.y = -Mathf.Clamp01((deadZone - pointer.y) / deadZone);
                    else if (pointer.y >= screenHeight - deadZone)
                        edgeDir.y = Mathf.Clamp01((pointer.y - (screenHeight - deadZone)) / deadZone);
                }
            }

            if (keyboardDir.sqrMagnitude > 0f)
                edgeDir = Vector2.zero;

            if (edgeDir.sqrMagnitude > 0f)
                _frameVelocity += edgeDir * _settings.panSpeed;

            _edgeActive = edgeDir.sqrMagnitude > 0.0001f;

            HandleScroll(safePreset, dt);
            HandleDrag(allowDrag, dt, ref position);
            ApplyVelocity(dt, ref position);
            HandleAutoFocus(safePreset, dt, ref position);
            HandleJump(dt, ref position);
            position = ClampToBounds(position);
            _cameraTransform.position = position;

            if (_audit != null)
            {
                float ortho = _cameraIsOrtho ? _camera.orthographicSize : 0f;
                float fov = !_cameraIsOrtho ? _camera.fieldOfView : 0f;
                _audit.NotifyRigMovement(position, _cameraTransform.rotation, ortho, fov);
            }
        }

        void NotifyPassive()
        {
            if (_camera == null)
                return;

            if (_audit != null)
            {
                float ortho = _cameraIsOrtho ? _camera.orthographicSize : 0f;
                float fov = !_cameraIsOrtho ? _camera.fieldOfView : 0f;
                _audit.NotifyRigPassive(_cameraTransform.position, _cameraTransform.rotation, ortho, fov);
            }
        }

        void HandleScroll(bool safePreset, float deltaTime)
        {
            if (safePreset)
                return;

            Vector2 scroll = CameraInput.ScrollDelta;
            float scrollY = scroll.y;
            if (Mathf.Abs(scrollY) < 0.0001f)
                return;

            if (_settings.zoomEnabled)
            {
                AdjustZoom(scrollY);
            }
            else if (_settings.scrollIsVerticalPan)
            {
                _frameVelocity.y += scrollY * _settings.panSpeed;
            }
        }

        void HandleDrag(bool allowDrag, float deltaTime, ref Vector3 position)
        {
            if (!allowDrag)
            {
                if (_dragActive)
                {
                    _dragActive = false;
                }
                return;
            }

            bool altHeld = CameraInput.GetKey(KeyCode.LeftAlt) || CameraInput.GetKey(KeyCode.RightAlt);
            bool wantsDrag = false;
            bool triggerDown = false;
            bool triggerUp = false;

            switch (_settings.dragButton)
            {
                case MouseDragButton.Middle:
                    wantsDrag = CameraInput.GetMouseButton(2);
                    triggerDown = CameraInput.GetMouseButtonDown(2);
                    triggerUp = CameraInput.GetMouseButtonUp(2);
                    break;
                case MouseDragButton.AltPlusLeft:
                    wantsDrag = altHeld && CameraInput.GetMouseButton(0);
                    triggerDown = altHeld && CameraInput.GetMouseButtonDown(0);
                    triggerUp = CameraInput.GetMouseButtonUp(0) || !altHeld;
                    break;
                case MouseDragButton.None:
                    wantsDrag = false;
                    triggerDown = false;
                    triggerUp = _dragActive;
                    break;
            }

            if (triggerDown && wantsDrag && !_dragActive)
            {
                _dragActive = true;
                _dragCameraOrigin = position;
                _dragGrabWorld = ScreenToWorld(CameraInput.MousePosition);
            }

            if (_dragActive && wantsDrag)
            {
                Vector3 currentWorld = ScreenToWorld(CameraInput.MousePosition);
                Vector3 offset = _dragGrabWorld - currentWorld;
                offset.z = 0f;

                float lerp = _settings.panAccelSeconds <= 0f
                    ? 1f
                    : Mathf.Clamp01(deltaTime / _settings.panAccelSeconds);

                Vector3 target = _dragCameraOrigin + offset;
                position = Vector3.Lerp(position, target, lerp);

                if (deltaTime > 0f)
                    _currentVelocity = Vector2.Lerp(_currentVelocity, (Vector2)(offset / deltaTime), lerp);
            }

            if ((_dragActive && (!wantsDrag || triggerUp)) || _settings.dragButton == MouseDragButton.None)
            {
                _dragActive = false;
            }
        }

        void ApplyVelocity(float deltaTime, ref Vector3 position)
        {
            if (_frameVelocity.sqrMagnitude > 0f)
            {
                float accel = _settings.panAccelSeconds <= 0f
                    ? 1f
                    : Mathf.Clamp01(deltaTime / _settings.panAccelSeconds);
                _currentVelocity = Vector2.Lerp(_currentVelocity, _frameVelocity, accel);
            }
            else
            {
                if (_settings.inertiaEnabled && _currentVelocity.sqrMagnitude > 0f)
                {
                    float damping = Mathf.Clamp01(_settings.inertia);
                    float decay = Mathf.Pow(damping, Mathf.Max(deltaTime * 60f, 1f));
                    _currentVelocity *= decay;
                    if (_currentVelocity.sqrMagnitude < 0.0001f)
                        _currentVelocity = Vector2.zero;
                }
                else
                {
                    _currentVelocity = Vector2.zero;
                }
            }

            Vector3 delta = new Vector3(_currentVelocity.x, _currentVelocity.y, 0f) * deltaTime;
            position += delta;
        }

        void HandleAutoFocus(bool safePreset, float deltaTime, ref Vector3 position)
        {
            _autoFocusActive = false;

            if (safePreset || !_settings.autoFocusWhileDragging || !_focusDragging)
            {
                _focusTimer = 0f;
                return;
            }

            Bounds focusBounds;
            if (!TryBuildFocusBounds(out focusBounds))
            {
                _focusTimer = 0f;
                return;
            }

            _focusTimer += deltaTime;
            // While dragging, keep auto-focus active without a hard time cap to maintain center.
            // If designers want a cap, set autoFocusMaxSeconds <= 0 to disable, or we can revisit with a separate flag.

            var focusPoint = focusBounds.center;
            Vector3 target = new Vector3(focusPoint.x, focusPoint.y, position.z);
            // Exponential smoothing with per-second gain: 1-exp(-k*dt).
            // k = autoFocusStrength (s^-1). Larger values follow more tightly; smaller values give more slack.
            float lerp = 1f - Mathf.Exp(-Mathf.Max(0f, _settings.autoFocusStrength) * deltaTime);
            lerp = Mathf.Clamp01(lerp);
            position = Vector3.Lerp(position, target, lerp);
            _autoFocusActive = true;
        }

        void HandleJump(float deltaTime, ref Vector3 position)
        {
            if (!_jumpActive)
                return;

            _jumpElapsed += deltaTime;
            float t = Mathf.Clamp01(_jumpElapsed / _jumpDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            position = Vector3.Lerp(_jumpStart, _jumpTarget, eased);

            if (t >= 1f)
                _jumpActive = false;
        }

        void AdjustZoom(float scrollDelta)
        {
            if (_camera == null)
                return;

            float sensitivity = 0.05f;
            _zoomScale = Mathf.Clamp(_zoomScale - scrollDelta * sensitivity, _settings.zoomMin, _settings.zoomMax);
            if (_cameraIsOrtho)
                _camera.orthographicSize = _baseOrthographicSize * _zoomScale;
            else
                _camera.fieldOfView = Mathf.Clamp(_baseFieldOfView / Mathf.Max(0.01f, _zoomScale), 10f, 170f);
        }

        void ResetZoom()
        {
            _zoomScale = 1f;
            if (_camera == null)
                return;
            if (_cameraIsOrtho)
                _camera.orthographicSize = _baseOrthographicSize;
            else
                _camera.fieldOfView = _baseFieldOfView;
        }

        bool TryBuildFocusBounds(out Bounds bounds)
        {
            bool hasBounds = false;
            bounds = default;

            if (_focusPrimary != null)
            {
                bounds = _focusPrimary.GetFocusBounds();
                hasBounds = true;
            }

            if (_focusPartner != null)
            {
                if (hasBounds)
                    bounds.Encapsulate(_focusPartner.GetFocusBounds());
                else
                {
                    bounds = _focusPartner.GetFocusBounds();
                    hasBounds = true;
                }
            }

            return hasBounds;
        }

        Vector3 ClampToBounds(Vector3 position)
        {
            Rect bounds = _settings.cameraWorldBounds;
            if (bounds.width <= 0f || bounds.height <= 0f)
                return position;

            float halfWidth = 0f;
            float halfHeight = 0f;

            if (_cameraIsOrtho)
            {
                halfHeight = _camera.orthographicSize;
                halfWidth = halfHeight * _camera.aspect;
            }

            float minX = bounds.xMin + halfWidth;
            float maxX = bounds.xMax - halfWidth;
            float minY = bounds.yMin + halfHeight;
            float maxY = bounds.yMax - halfHeight;

            if (minX > maxX)
            {
                float centre = (minX + maxX) * 0.5f;
                minX = maxX = centre;
            }

            if (minY > maxY)
            {
                float centre = (minY + maxY) * 0.5f;
                minY = maxY = centre;
            }

            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.y = Mathf.Clamp(position.y, minY, maxY);
            return position;
        }

        bool EnsureCamera()
        {
            if (_camera != null)
                return true;

            _camera = fallbackCamera;
            if (_camera == null)
                _camera = GetComponent<UnityCamera>();
            if (_camera == null)
                _camera = GetComponentInChildren<UnityCamera>();
            if (_camera == null)
                _camera = UnityCamera.main;
            if (_camera == null && UnityCamera.allCamerasCount > 0)
            {
                var cameras = UnityCamera.allCameras;
                for (int i = 0; i < cameras.Length; i++)
                {
                    if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                    {
                        _camera = cameras[i];
                        break;
                    }
                }
            }

            if (_camera == null)
                return false;

            _cameraTransform = _camera.transform;
            _cameraIsOrtho = _camera.orthographic;
            _baseOrthographicSize = _cameraIsOrtho ? _camera.orthographicSize : 0f;
            _baseFieldOfView = !_cameraIsOrtho ? _camera.fieldOfView : 60f;
            _zoomScale = 1f;

            if (_provider != null)
                _provider.Register(_camera);

            if (_audit != null)
                _audit.UpdateCameraReference(_camera);

            return true;
        }

        Vector3 ScreenToWorld(Vector2 screen)
        {
            if (_camera == null)
                return Vector3.zero;

            float z = _cameraIsOrtho
                ? Mathf.Abs(_cameraTransform.position.z)
                : Mathf.Abs(_camera.nearClipPlane);

            var point = new Vector3(screen.x, screen.y, z);
            var world = _camera.ScreenToWorldPoint(point);
            world.z = _cameraTransform.position.z;
            return world;
        }

        void BuildOverlay()
        {
            _overlayBuilder.Length = 0;
            _overlayBuilder.AppendLine("Camera Comfort Rig");
            _overlayBuilder.Append("Passive: ").Append(IsPassive ? "Yes" : "No").AppendLine();
            if (_audit != null)
            {
                bool conflict = _audit.IsConflictDetected;
                _overlayBuilder.Append("Conflict: ").Append(conflict ? "Detected" : "Clear").AppendLine();
                if (conflict && !string.IsNullOrEmpty(_audit.Summary))
                    _overlayBuilder.Append(_audit.Summary).AppendLine();
            }
            _overlayBuilder.Append("Pan Method: ").Append(_settings.panMethod).AppendLine();
            _overlayBuilder.Append("Speed: ").Append(_settings.panSpeed.ToString("0.##"))
                .Append(" accel: ").Append(_settings.panAccelSeconds.ToString("0.##")).AppendLine();
            _overlayBuilder.Append("Edge Active: ").Append(_edgeActive ? "Yes" : "No")
                .Append(" (Enabled: ").Append(_settings.edgePanEnabled ? "Yes" : "No").Append(")").AppendLine();
            _overlayBuilder.Append("Drag Active: ").Append(_dragActive ? "Yes" : "No").AppendLine();
            _overlayBuilder.Append("Auto Focus: ").Append(_autoFocusActive ? "Yes" : "No").AppendLine();
            _overlayBuilder.Append("Motion Safe: ").Append(_settings.motionSafePreset ? "Yes" : "No").AppendLine();
            _overlayContent.text = _overlayBuilder.ToString();
        }

        void OnGUI()
        {
            if (!_overlayVisible)
                return;

            if (_overlayStyle == null)
            {
                _overlayStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 12,
                    wordWrap = true
                };
            }

            BuildOverlay();
            GUI.Box(_overlayRect, _overlayContent, _overlayStyle);
        }

        static class CameraInput
        {
#if USE_INPUT_SYSTEM
            static Keyboard KeyboardDevice => Keyboard.current;
            static Mouse MouseDevice => Mouse.current;

            public static bool GetKey(KeyCode key)
            {
                var keyboard = KeyboardDevice;
                if (keyboard == null)
                    return false;

                switch (key)
                {
                    case KeyCode.W: return keyboard.wKey.isPressed;
                    case KeyCode.A: return keyboard.aKey.isPressed;
                    case KeyCode.S: return keyboard.sKey.isPressed;
                    case KeyCode.D: return keyboard.dKey.isPressed;
                    case KeyCode.LeftAlt: return keyboard.leftAltKey.isPressed;
                    case KeyCode.RightAlt: return keyboard.rightAltKey.isPressed;
                    case KeyCode.F9: return keyboard.f9Key.isPressed;
                    default: return false;
                }
            }

            public static bool GetKeyDown(KeyCode key)
            {
                var keyboard = KeyboardDevice;
                if (keyboard == null)
                    return false;

                switch (key)
                {
                    case KeyCode.W: return keyboard.wKey.wasPressedThisFrame;
                    case KeyCode.A: return keyboard.aKey.wasPressedThisFrame;
                    case KeyCode.S: return keyboard.sKey.wasPressedThisFrame;
                    case KeyCode.D: return keyboard.dKey.wasPressedThisFrame;
                    case KeyCode.LeftAlt: return keyboard.leftAltKey.wasPressedThisFrame;
                    case KeyCode.RightAlt: return keyboard.rightAltKey.wasPressedThisFrame;
                    case KeyCode.F9: return keyboard.f9Key.wasPressedThisFrame;
                    default: return false;
                }
            }

            public static bool GetMouseButton(int index)
            {
                var mouse = MouseDevice;
                if (mouse == null)
                    return false;
                return index switch
                {
                    0 => mouse.leftButton.isPressed,
                    1 => mouse.rightButton.isPressed,
                    2 => mouse.middleButton.isPressed,
                    _ => false
                };
            }

            public static bool GetMouseButtonDown(int index)
            {
                var mouse = MouseDevice;
                if (mouse == null)
                    return false;
                return index switch
                {
                    0 => mouse.leftButton.wasPressedThisFrame,
                    1 => mouse.rightButton.wasPressedThisFrame,
                    2 => mouse.middleButton.wasPressedThisFrame,
                    _ => false
                };
            }

            public static bool GetMouseButtonUp(int index)
            {
                var mouse = MouseDevice;
                if (mouse == null)
                    return false;
                return index switch
                {
                    0 => mouse.leftButton.wasReleasedThisFrame,
                    1 => mouse.rightButton.wasReleasedThisFrame,
                    2 => mouse.middleButton.wasReleasedThisFrame,
                    _ => false
                };
            }

            public static Vector2 MousePosition
            {
                get
                {
                    var mouse = MouseDevice;
                    return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
                }
            }

            public static Vector2 ScrollDelta
            {
                get
                {
                    var mouse = MouseDevice;
                    return mouse != null ? mouse.scroll.ReadValue() : Vector2.zero;
                }
            }
#else
            public static bool GetKey(KeyCode key) => Input.GetKey(key);
            public static bool GetKeyDown(KeyCode key) => Input.GetKeyDown(key);
            public static bool GetMouseButton(int index) => Input.GetMouseButton(index);
            public static bool GetMouseButtonDown(int index) => Input.GetMouseButtonDown(index);
            public static bool GetMouseButtonUp(int index) => Input.GetMouseButtonUp(index);
            public static Vector2 MousePosition => Input.mousePosition;
            public static Vector2 ScrollDelta => Input.mouseScrollDelta;
#endif
        }
    }
}
