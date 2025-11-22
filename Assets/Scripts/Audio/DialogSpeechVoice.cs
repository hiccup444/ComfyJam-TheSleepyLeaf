using UnityEngine;
using UnityEngine.Audio;

[RequireComponent(typeof(AudioSource))]
public class DialogSpeechVoice : MonoBehaviour
{
    [Tooltip("Default settings applied when no override is provided.")]
    public DialogSpeechSettings settings;
    [Range(0f, 1f)] public float extraPitchJitter = 0.05f;

    private AudioSource _src;
    private DialogSpeechSettings _activeSettings;

    void Awake()
    {
        _src = GetComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = false;
        ApplySettings(settings);
    }

    public void ApplySettings(DialogSpeechSettings overrideSettings)
    {
        _activeSettings = overrideSettings ?? settings;
        _src.outputAudioMixerGroup = _activeSettings?.mixerGroup;
    }

    public void PlayForChar(char c)
    {
        if (_activeSettings == null || !DialogSpeechSettings.IsLetter(c))
        {
            return;
        }

        AudioClip clip = _activeSettings.ClipFor(c);
        if (clip == null)
        {
            return;
        }

        float jitter = _activeSettings.pitchJitter + extraPitchJitter;
        float pitch = _activeSettings.basePitch + _activeSettings.speakerPitchOffset + Random.Range(-jitter, jitter);
        pitch = Mathf.Clamp(pitch, 0.1f, 3f);

        if (Mathf.Abs(_activeSettings.semitoneJitter) > 0.001f)
        {
            float semitoneShift = Random.Range(-_activeSettings.semitoneJitter, _activeSettings.semitoneJitter);
            pitch *= Mathf.Pow(2f, semitoneShift / 12f);
        }

        _src.pitch = pitch;
        _src.PlayOneShot(clip, _activeSettings.volume);
    }

    void OnValidate()
    {
        if (_src == null)
            _src = GetComponent<AudioSource>();
        ApplySettings(settings);
    }
}
