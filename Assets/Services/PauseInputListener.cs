using UnityEngine;

namespace JamesKJamKit.Services
{
    /// <summary>
    /// Simple input bridge that toggles the pause state when the Escape key is pressed.
    /// </summary>
    public sealed class PauseInputListener : MonoBehaviour
    {
        [SerializeField]
        private KeyCode toggleKey = KeyCode.Escape;

        private void Update()
        {
            if (!Input.GetKeyDown(toggleKey))
                return;

            // Only allow pausing in scenes that actually have a GameManager.
            if (GameManager.Instance == null)
                return;

            // If the intro cutscene is active, ignore pause input.
            // CutsceneManager handles ESC to skip without opening the pause UI.
            if (!GameManager.Instance.HasPlayedIntroCutscene())
                return;

            PauseController.Instance?.TogglePause();
        }
    }
}
