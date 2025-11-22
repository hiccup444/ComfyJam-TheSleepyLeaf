using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(menuName = "Audio/DialogSpeech Settings", fileName = "DialogSpeechSettings")]
public class DialogSpeechSettings : ScriptableObject
{
    public bool usePerLetter = true;
    public AudioClip[] letterClips = new AudioClip[26]; // A..Z
    public AudioClip vowelClip;
    public AudioClip consonantClip;
    public float basePitch = 1f;  // 0.5–2
    public float pitchJitter = 0.12f; // ±
    public float semitoneJitter = 0f; // ± n semitones
    public float volume = 0.85f; // 0–1
    public float baseCharSeconds = 0.02f;
    public float commaPauseMul = 5f;
    public float periodPauseMul = 8f;
    public float ellipsisPauseMul = 10f;
    public float speakerPitchOffset = 0f;
    public float speakerSpeedMul = 1f;
    public AudioMixerGroup mixerGroup;
    public float typewriterCharsPerSecond = 0f; // override when > 0

    public AudioClip ClipFor(char c)
    {
        if (!IsLetter(c))
            return null;

        char upper = char.ToUpperInvariant(c);

        if (usePerLetter)
        {
            int index = upper - 'A';
            if (index >= 0 && index < letterClips.Length)
            {
                AudioClip letterClip = letterClips[index];
                if (letterClip != null)
                {
                    return letterClip;
                }
            }
        }

        return IsVowel(upper) ? vowelClip : consonantClip;
    }

    public static bool IsLetter(char c)
    {
        return c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';
    }

    public static bool IsVowel(char c)
    {
        char upper = char.ToUpperInvariant(c);
        switch (upper)
        {
            case 'A':
            case 'E':
            case 'I':
            case 'O':
            case 'U':
            case 'Y':
                return true;
        }

        return false;
    }
}
