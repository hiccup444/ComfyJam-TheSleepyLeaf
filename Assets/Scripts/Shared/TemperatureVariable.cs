using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Simple shared temperature value (0..100) that other systems can read.
/// Designers can create one asset and wire it into FireController.
/// </summary>
[CreateAssetMenu(fileName = "TemperatureVariable", menuName = "Shared/Temperature Variable")]
public sealed class TemperatureVariable : ScriptableObject
{
    [Range(0f, 100f)]
    [SerializeField] private float value = 0f;

    [Tooltip("Invoked when the temperature value changes.")]
    public UnityEvent<float> onChanged;

    public void Set(float v)
    {
        float nv = Mathf.Clamp(v, 0f, 100f);
        if (!Mathf.Approximately(value, nv))
        {
            value = nv;
            onChanged?.Invoke(value);
        }
        else
        {
            value = nv;
        }
    }

    public float Get() => value;
}

