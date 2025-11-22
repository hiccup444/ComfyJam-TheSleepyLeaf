using UnityEngine;

namespace JamesKJamKit.Services.Audio
{
    /// <summary>
    /// Describes a sound effect "event": one or more clips + how to play them.
    /// </summary>
    [CreateAssetMenu(fileName = "SFX_", menuName = "Audio/SFX Event")]
    public sealed class SFXEvent : ScriptableObject
    {
        [Header("Clips")]
        public AudioClip[] clips;

        [Header("Mix")]
        [Range(0f, 1f)] public float volume = 1f;
        public Vector2 pitchRange = new(1f, 1f);                 // e.g., (0.97, 1.03)
        [Tooltip("0 = fully 2D (UI), 1 = fully 3D (world-anchored)")]
        [Range(0f, 1f)] public float spatialBlend = 0f;

        [Header("3D Settings (if spatial)")]
        public AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;
        public float minDistance = 1.5f;
        public float maxDistance = 12f;
        [Tooltip("Indoor/UI often sounds better with Doppler = 0")]
        [Range(0f, 5f)] public float dopplerLevel = 0f;

        [Header("Looping (lightweight ambience)")]
        public bool loop = false;

        /// <summary>Returns a random clip or null if none assigned.</summary>
        public AudioClip GetRandomClip()
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[Random.Range(0, clips.Length)];
        }
    }
}
