using UnityEngine;
using UnityCamera = UnityEngine.Camera;

namespace Comfy.Camera
{
    /// <summary>
    /// Lightweight reader so legacy systems can stay on <see cref="UnityEngine.Camera.main"/> while migrating to the comfort rig.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Comfy/Camera Comfort Provider")]
    public sealed class CameraProvider : MonoBehaviour
    {
        static CameraProvider _instance;
        static UnityCamera _activeCamera;

        [SerializeField] UnityCamera explicitCamera;

        internal void Register(UnityCamera camera)
        {
            explicitCamera = camera;
            _activeCamera = camera;
            _instance = this;
        }

        internal void Unregister(UnityCamera camera)
        {
            if (_activeCamera == camera)
            {
                _activeCamera = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        static UnityCamera ResolveFallback()
        {
            if (_activeCamera != null)
                return _activeCamera;

            if (UnityCamera.main != null)
                return UnityCamera.main;

            if (UnityCamera.allCamerasCount > 0)
            {
                var cams = UnityCamera.allCameras;
                for (int i = 0; i < cams.Length; i++)
                {
                    var cam = cams[i];
                    if (cam != null && cam.isActiveAndEnabled)
                        return cam;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the camera managed by the comfort rig, or falls back to <see cref="UnityEngine.Camera.main"/> if the rig is passive.
        /// </summary>
        public static UnityCamera Active => ResolveFallback();

        void Awake()
        {
            if (explicitCamera == null)
                explicitCamera = GetComponent<UnityCamera>();

            if (explicitCamera == null)
                explicitCamera = GetComponentInChildren<UnityCamera>();

            if (explicitCamera != null)
                Register(explicitCamera);
        }

        void OnEnable()
        {
            if (explicitCamera != null)
                Register(explicitCamera);
        }

        void OnDisable()
        {
            if (explicitCamera != null)
                Unregister(explicitCamera);
        }

        void OnDestroy()
        {
            if (explicitCamera != null)
                Unregister(explicitCamera);
        }
    }
}
