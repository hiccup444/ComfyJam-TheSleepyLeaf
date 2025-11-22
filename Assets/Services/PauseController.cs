using System;
using UnityEngine;

namespace JamesKJamKit.Services
{
    /// <summary>
    /// Tracks whether the game is currently paused and emits an event when that state changes.
    /// Consumers can subscribe to toggle gameplay logic or dim audio as required.
    /// </summary>
    [DefaultExecutionOrder(-9997)]
    public sealed class PauseController : MonoBehaviour
    {
        public static PauseController Instance { get; private set; }

        public event Action<bool> OnPauseChanged;

        public bool IsPaused { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            OnPauseChanged?.Invoke(paused);
        }

        public void TogglePause()
        {
            SetPaused(!IsPaused);
        }
    }
}
