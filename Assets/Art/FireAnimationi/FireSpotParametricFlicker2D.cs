using UnityEngine;


[RequireComponent(typeof(UnityEngine.Rendering.Universal.Light2D))]
public class FireSpotParametricFlicker2D : MonoBehaviour
{
    public UnityEngine.Rendering.Universal.Light2D light2D;
    public Color baseColor = new(1f, 0.58f, 0.2f, 1f);
    public float baseIntensity = 0.9f, intensityAmp = 0.2f;
    public float radiusBase = 2f, radiusAmp = 0.25f;   // cone length
    public float angleBase = 35f, angleAmp = 6f;       // cone width
    [Range(0f, 1f)] public float innerRadiusRatio = 0.6f;
    [Range(0f, 1f)] public float innerAngleRatio = 0.5f;
    public float speed = 3f, colorShift = 0.03f;

    float seed;

    void Awake() {
        light2D = light2D ? light2D : GetComponent<UnityEngine.Rendering.Universal.Light2D>();
        light2D.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Point; // ensure runtime control of cone params
        seed = Random.value * 100f;
    }

    void Update() {
        float t = Time.time * speed;
        float n1 = Mathf.PerlinNoise(seed, t) - 0.5f;
        float n2 = Mathf.PerlinNoise(seed + 17f, t * 0.7f) - 0.5f;

        light2D.intensity = Mathf.Max(0, baseIntensity + n1 * 2f * intensityAmp);

        // Point light cone controls:
        float outerRadius = Mathf.Max(0.01f, radiusBase + n2 * 2f * radiusAmp);
        light2D.pointLightOuterRadius = outerRadius;
        light2D.pointLightInnerRadius = Mathf.Clamp(outerRadius * Mathf.Clamp01(innerRadiusRatio), 0f, outerRadius);

        float outerAngle = Mathf.Clamp(angleBase + n2 * 2f * angleAmp, 0f, 360f); // cone width-ish
        light2D.pointLightOuterAngle = outerAngle;
        light2D.pointLightInnerAngle = Mathf.Clamp(outerAngle * Mathf.Clamp01(innerAngleRatio), 0f, outerAngle);

        // subtle warm shift
        var c = baseColor; float d = n1 * colorShift;
        c.r = Mathf.Clamp01(c.r + d); c.g = Mathf.Clamp01(c.g + d * 0.8f);
        light2D.color = c;
    }
}
