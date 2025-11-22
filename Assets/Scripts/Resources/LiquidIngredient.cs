// LiquidIngredient.cs
using UnityEngine;

/// <summary>
/// Designer-tunable preset for a liquid (hot/cold water, teas, etc.).
/// Visuals (tint/sprites), fill behavior, audio, and optional stream shimmer.
/// Make one asset per ingredient and assign it to buttons/UI.
/// </summary>
[CreateAssetMenu(menuName = "Beverages/Liquid Ingredient", fileName = "LiquidIngredient")]
public sealed class LiquidIngredient : ScriptableObject
{
    [Header("Look")]
    [Tooltip("Multiplies the in-cup and stream sprites. Use white for neutral, then tint per ingredient.")]
    public Color tint = Color.white;

    [Tooltip("Optional: specific in-cup surface sprite. Leave null to reuse the mug's default.")]
    public Sprite inCupSprite;

    [Tooltip("Optional: specific stream column sprite. Leave null to reuse the default.")]
    public Sprite streamSprite;

    [Header("Behavior")]
    [Tooltip("Final fill level of the mug (0–1 of capacity).")]
    [Range(0f, 1f)] public float targetFill = 1f;

    [Tooltip("Seconds to animate from current fill to target fill.")]
    public float fillSeconds = 1.25f;

    [Tooltip("If true, adds on top of current fill; if false, resets to 0 before filling.")]
    public bool additive = false;

    [Header("Audio (optional)")]
    [Tooltip("Played when pouring starts.")]
    public AudioClip sfxStart;

    [Tooltip("Played when pouring stops.")]
    public AudioClip sfxStop;

    [Header("Shimmer (stream column)")]
    [Tooltip("Enable the dual-layer shimmer for the stream (uses an overlay renderer).")]
    public bool shimmerEnabled = true;

    [Tooltip("Alpha for the overlay stream layer (0.3–0.5 looks good).")]
    [Range(0f, 1f)] public float overlayAlpha = 0.4f;

    [Tooltip("Speed of shimmer motion; higher = faster sway/stretch.")]
    public float shimmerSpeed = 4f;

    [Tooltip("Amplitude of local XY sway for the overlay (world units).")]
    public Vector2 swayXY = new Vector2(0.01f, 0.02f);

    [Tooltip("Amount of vertical stretch (as a fraction of scale).")]
    [Range(0f, 0.2f)] public float yStretch = 0.04f;
}
