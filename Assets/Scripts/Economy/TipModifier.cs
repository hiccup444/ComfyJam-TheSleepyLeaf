using UnityEngine;

/// <summary>
/// Maps Temperature (0..100) to a tip multiplier using an AnimationCurve.
/// Default: 0 -> 0.8x, 100 -> 1.2x.
/// </summary>
[CreateAssetMenu(fileName = "TipModifier", menuName = "Economy/Tip Modifier")]
public sealed class TipModifier : ScriptableObject
{
    [Tooltip("X = temperature (0..100), Y = multiplier (e.g., 0.8..1.2).")]
    public AnimationCurve temperatureToTip = new AnimationCurve(
        new Keyframe(0f, 0.8f),
        new Keyframe(100f, 1.2f)
    );

    public float GetTipMultiplier(float temperature)
    {
        float t = Mathf.Clamp(temperature, 0f, 100f);
        return temperatureToTip != null ? temperatureToTip.Evaluate(t) : 1f;
    }
}

