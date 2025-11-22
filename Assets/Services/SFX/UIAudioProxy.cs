using UnityEngine;

namespace JamesKJamKit.Services.Audio
{
    /// <summary>
    /// Put ONE of these on your Canvas. Each Button calls Play(sfxEvent) and passes its own SFXEvent.
    /// </summary>
    public sealed class UIAudioProxy : MonoBehaviour
    {
        [SerializeField, Range(0f, 2f)] private float defaultVolume = 1f;

        private void Awake()
        {
            _ = defaultVolume; // Marks the field as "used" to silence warning
        }
        public void Play(SFXEvent sfxEvent)
        {
            var am = JamesKJamKit.Services.AudioManager.Instance; // your AudioManager lives in Services
            if (!am) return;

            if (sfxEvent) am.PlaySFX(sfxEvent);
            else am.PlayUiClick();
        }
    }
}
