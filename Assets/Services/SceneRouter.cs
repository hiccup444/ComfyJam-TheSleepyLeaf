using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JamesKJamKit.Services
{
    /// <summary>
    /// Lightweight scene management facade that exposes semantic scene loading helpers and an event
    /// for observers to react when the active scene changes.
    /// </summary>
    [DefaultExecutionOrder(-9998)]
    public sealed class SceneRouter : MonoBehaviour
    {
        [SerializeField]
        private string mainMenuSceneName = "MainMenu";

        public string MainMenuSceneName => mainMenuSceneName;

        [SerializeField]
        private string gameSceneName = "Game";

        [SerializeField]
        private float minimumLoadTime = 0.1f;

        public static SceneRouter Instance { get; private set; }

        public event Action<string> OnSceneChanged;

        private bool _isLoading;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void Start()
        {
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                Instance = null;
            }
        }

        public void LoadMainMenu()
        {
            if (!_isLoading)
            {
                StartCoroutine(LoadSceneRoutine(mainMenuSceneName));
            }
        }

        public void LoadGame()
        {
            if (!_isLoading)
            {
                StartCoroutine(LoadSceneRoutine(gameSceneName));
            }
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) yield break;

            _isLoading = true;

            var async = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (async == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"SceneRouter failed to load scene '{sceneName}'. " +
                               "Ensure the scene is added to the build settings and the name is correct.");
#endif
                _isLoading = false;
                yield break;
            }
            var startTime = Time.unscaledTime;
            while (!async.isDone)
            {
                yield return null;
            }

            // Ensure a minimum frame for transitions/animation.
            var elapsed = Time.unscaledTime - startTime;
            if (elapsed < minimumLoadTime)
            {
                yield return new WaitForSecondsRealtime(minimumLoadTime - elapsed);
            }

            _isLoading = false;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            OnSceneChanged?.Invoke(scene.name);
        }
    }
}
