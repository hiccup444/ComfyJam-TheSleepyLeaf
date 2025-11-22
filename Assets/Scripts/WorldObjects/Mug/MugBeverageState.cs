// MugBeverageState.cs
using UnityEngine;
using System;

/// <summary>
/// Tracks liquid composition. Water fills SNAP to water color (no easing).
/// Only tea steeping (max 3), powder, and milk change color.
/// </summary>
[DisallowMultipleComponent]
public sealed class MugBeverageState : MonoBehaviour
{
    /// <summary>
    /// Fired when tea type is set. Provides the tea type.
    /// </summary>
    public static event Action<TeaType> OnTeaTypeSet;

    /// <summary>
    /// Fired when a steep occurs. Provides the new steep count.
    /// </summary>
    public static event Action<int> OnSteepRegistered;

    /// <summary>
    /// Fired when brewing is complete (3 steeps). Provides the tea type.
    /// </summary>
    public static event Action<TeaType> OnBrewingComplete;

    /// <summary>
    /// Fired when powder is added. Provides the tea type.
    /// </summary>
    public static event Action<TeaType> OnPowderAdded;

    /// <summary>
    /// Fired when milk is added.
    /// </summary>
    public static event Action OnMilkAdded;
    [Header("References")]
    [SerializeField] private SpriteRenderer liquidRenderer;
    [SerializeField] private GameObject spoonObject;
    [SerializeField] private GameObject steamObject;
    
    [Header("Steam Settings")]
    [Tooltip("First SpriteRenderer for crossfading between frames")]
    [SerializeField] private SpriteRenderer steamRendererA;
    [Tooltip("Second SpriteRenderer for crossfading between frames (should be layered on top of A)")]
    [SerializeField] private SpriteRenderer steamRendererB;
    [Tooltip("Array of sprites to cycle through for steam animation")]
    [SerializeField] private Sprite[] steamFrames;
    [Tooltip("Time in seconds for crossfade transition between frames")]
    [SerializeField] private float steamCrossfadeDuration = 0.5f;
    [Tooltip("Time to hold each frame before transitioning to next")]
    [SerializeField] private float steamFrameHoldDuration = 0.3f;
    [Tooltip("Target alpha for steam (0-255 range, converted internally)")]
    [SerializeField, Range(0, 255)] private int steamTargetAlpha = 60;

    [Header("Volume")]
    [SerializeField] private float maxVolume = 1f;
    [SerializeField] private float startVolume = 0f;

    [Header("Colors")]
    [SerializeField] private Color optionalMilkTint = new Color32(0xE4, 0xBB, 0x84, 0xFF);

    [Header("Rendering")]
    [Tooltip("If ON, liquid alpha scales with current volume. OFF = always full alpha.")]
    [SerializeField] private bool scaleAlphaByVolume = false; // DEFAULT OFF

    // Public state
    public bool HasWater => _hasWater;
    public WaterTemp? WaterTemperature => _hasWater ? _waterTemp : (WaterTemp?)null;
    public Color BaseWaterColor => _baseWaterColor;

    public bool HasTea => _teaType != TeaType.None;
    public TeaType TeaType => _teaType;
    public Color TargetTeaColor => _targetTeaColor;
    public bool MilkRequired => _milkRequired;
    public bool IsBrewed => _steepCount >= 3;

    public bool HasMilk => _hasMilk;
    public float MilkAmount => _milkAmount;
    public bool HasPowder => _hasPowder;
    public bool PowderRequired => _powderRequired;
    public SpriteRenderer LiquidRenderer => liquidRenderer;
    public Color PostPowderColor => _postPowderColor;

    public float CurrentVolume => _currentVolume;
    public bool IsEmpty() => _currentVolume <= 0.0001f;

    /// <summary>
    /// Returns the current visual color of the beverage (for use by fill animations, etc.)
    /// </summary>
    public Color GetCurrentVisualColor() => _currentColor;

    // Internal state
    bool _hasWater;
    WaterTemp _waterTemp;
    Color _baseWaterColor = Color.clear;

    TeaType _teaType = TeaType.None;
    Color _targetTeaColor = Color.clear;
    Color _prePowderColor = Color.clear;   // shown before milk (for powder drinks)
    Color _postPowderColor = Color.clear;  // shown after milk (for powder drinks)
    bool _milkRequired;
    bool _powderRequired;

    bool _hasMilk;
    float _milkAmount;
    bool _hasPowder;   // <-- set true by SetPowderType when we pour powder
    public int _steepCount;

    // Stirring state
    bool _needsStirring;   // true when powder + milk added but not stirred yet
    bool _isStirred;       // true once stirring is complete
    public bool IsStirred => _isStirred;

    // Rendering cache
    Color _currentColor = Color.clear;
    float _currentVolume;

    void Awake()
    {
        ResolveRenderer();
        _currentVolume = Mathf.Clamp(startVolume, 0f, maxVolume);
        
        // Disable steam initially
        if (steamObject != null)
            steamObject.SetActive(false);
        
        if (_currentVolume <= 0f)
        {
            _currentColor = Color.clear;
            if (liquidRenderer != null) liquidRenderer.enabled = false;
        }
        else
        {
            if (liquidRenderer != null) liquidRenderer.enabled = true;
            ApplyColor(_currentColor);
        }
    }

    void Reset() => ResolveRenderer();
#if UNITY_EDITOR
    void OnValidate() => ResolveRenderer();
#endif

    // -----------------------------
    // Water (snap to water, no easing)
    // -----------------------------
    public void RegisterWater(WaterTemp temp, Color waterColor)
    {
        RegisterWaterNonReset(temp, waterColor, addVolume: maxVolume);
    }

    public void RegisterWaterNonReset(WaterTemp temp, Color waterColor, float addVolume)
    {
        addVolume = Mathf.Max(0f, addVolume);
        if (addVolume <= 0f) return;

        _hasWater = true;
        _waterTemp = temp;
        _baseWaterColor = waterColor;

        _currentVolume = Mathf.Min(maxVolume, _currentVolume + addVolume);

        // Enable steam if hot water, disable if cold
        UpdateSteamVisual();

        bool hasAnyIngredient = HasTea || _powderRequired || _hasPowder || _hasMilk;

        if (!hasAnyIngredient)
        {
            _currentColor = waterColor;
            ApplyColor(_currentColor);
        }
        else
        {
            // Keep current look; ingredients control color.
            // If milk was added before water, ensure alpha is set to 1 now that water is added
            if (_hasMilk && _currentColor.a < 0.01f)
            {
                var correctedColor = _currentColor;
                correctedColor.a = 1f;
                _currentColor = correctedColor;
            }
            ApplyColor(_currentColor);
        }
    }

    // -----------------------------
    // Tea / Powder / Milk
    // -----------------------------
    public void SetTeaType(TeaType type, Color targetColor, bool milkRequired)
    {
        _teaType = type;
        _targetTeaColor = targetColor;
        _milkRequired = milkRequired;
        _powderRequired = false;
        // _hasPowder stays false for teabags

        OnTeaTypeSet?.Invoke(type);

        if (!_hasWater) return;
        UpdateLiquidColor();
    }

    /// <summary>
    /// Called by packet pourer for powder drinks (e.g., Hot Chocolate).
    /// Now marks _hasPowder = true immediately so RecipeEngine can pass the Powder checklist.
    /// </summary>
    public void SetPowderType(TeaType type, Color prePowderColor, Color postPowderColor, bool milkRequired, bool powderRequired)
    {
        _teaType = type;
        _prePowderColor = prePowderColor;
        _postPowderColor = postPowderColor;
        _targetTeaColor = prePowderColor;
        _milkRequired = milkRequired;
        _powderRequired = powderRequired;

        // Mark powder as added
        if (powderRequired)
        {
            _hasPowder = true;
            OnPowderAdded?.Invoke(type);
        }

        if (!_hasWater) return;

        // If milk was already added before powder, enable spoon now
        if (_hasMilk && powderRequired && milkRequired)
        {
            _needsStirring = true;
            if (spoonObject != null)
            {
                spoonObject.SetActive(true);
                // Re-enable the SpoonStirrer component in case it was disabled from previous use
                var spoonStirrer = spoonObject.GetComponent<SpoonStirrer>();
                if (spoonStirrer != null)
                    spoonStirrer.enabled = true;
                #if UNITY_EDITOR
                Debug.Log("[MugBeverageState] Powder added after milk - enabling spoon for stirring");
                #endif
            }
            // Don't change color yet - wait for stirring
            return;
        }

        UpdateLiquidColor();
    }

    public void AddMilk(float amount = 1f)
    {
        if (amount > 0f) _milkAmount += amount;
        if (_hasMilk) return;

        _hasMilk = true;
        OnMilkAdded?.Invoke();

        // For powder drinks, enable spoon and wait for stirring (check if powder is REQUIRED, not just if we have it yet)
        if (_powderRequired)
        {
            // Check if we actually have the powder yet
            if (_hasPowder)
            {
                // Have both powder and milk - enable spoon
                _needsStirring = true;
                if (spoonObject != null)
                {
                    spoonObject.SetActive(true);
                    // Re-enable the SpoonStirrer component in case it was disabled from previous use
                    var spoonStirrer = spoonObject.GetComponent<SpoonStirrer>();
                    if (spoonStirrer != null)
                        spoonStirrer.enabled = true;
                }
                // Don't change color yet - wait for stirring
                return;
            }
            else
            {
                // Milk added but no powder yet - just wait, don't change color
                #if UNITY_EDITOR
                Debug.Log("[MugBeverageState] Milk added, waiting for powder before stirring");
                #endif
                return;
            }
        }

        if (_milkRequired)
        {
            if (HasTea && _hasWater) UpdateLiquidColor();
        }
        else
        {
            ApplyOptionalMilkTint();
        }
    }

    /// <summary>
    /// Optional path if some items add powder in a second step.
    /// For packet-pour powder drinks, SetPowderType already sets _hasPowder = true.
    /// </summary>
    public void AddPowder()
    {
        if (_hasPowder) return;
        _hasPowder = true;

        if (_powderRequired && HasTea && _hasWater)
        {
            // if milk is required, we still show pre color until milk arrives (handled in UpdateLiquidColor)
            UpdateLiquidColor();
        }
    }

    /// <summary>
    /// Called by SpoonStirrer to progressively update color during stirring.
    /// </summary>
    public void UpdateStirProgress(float progress)
    {
        if (!_needsStirring || liquidRenderer == null) return;
        
        // Blend from pre-powder to post-powder color based on progress
        Color blendedColor = Color.Lerp(_prePowderColor, _postPowderColor, progress);
        ApplyColor(blendedColor);
    }

    /// <summary>
    /// Called by SpoonStirrer when stirring is complete.
    /// </summary>
    public void CompleteStir()
    {
        if (!_needsStirring) return;
        
        _isStirred = true;
        _needsStirring = false;

        // Hide the spoon
        if (spoonObject != null)
            spoonObject.SetActive(false);

        // Now apply the final color
        if (_powderRequired && _hasPowder && _hasMilk && _hasWater)
        {
            UpdateLiquidColor();
        }
    }

    /// <summary>Only place color "eases": 3 dips darken toward tea color.</summary>
    public void RegisterSteep()
    {
        if (!_hasWater || !HasTea) return;
        if (_powderRequired) return; // no dips for powder drinks
        if (_steepCount >= 3) return;

        _steepCount++;
        OnSteepRegistered?.Invoke(_steepCount);

        // Check if brewing is complete
        if (_steepCount >= 3)
        {
            OnBrewingComplete?.Invoke(_teaType);
        }

        UpdateLiquidColor();
    }

    // -----------------------------
    // Sink helpers
    // -----------------------------
    public void DrainContent(float amount)
    {
        if (amount <= 0f || IsEmpty()) return;

        _currentVolume = Mathf.Max(0f, _currentVolume - amount);
        UpdateLiquidVisuals();

        if (IsEmpty() && liquidRenderer != null)
        {
            var c = liquidRenderer.color; c.a = 0f;
            liquidRenderer.color = c;
            liquidRenderer.enabled = false;
        }
    }

    public void ClearToEmpty()
    {
        _currentVolume = 0f;

        _hasWater = false;
        _waterTemp = default;
        _baseWaterColor = Color.clear;

        _teaType = TeaType.None;
        _targetTeaColor = Color.clear;
        _prePowderColor = Color.clear;
        _postPowderColor = Color.clear;
        _milkRequired = false;
        _powderRequired = false;

        _hasMilk = false;
        _milkAmount = 0f;
        _hasPowder = false;
        _steepCount = 0;

        _needsStirring = false;
        _isStirred = false;
        if (spoonObject != null)
            spoonObject.SetActive(false);

        // Disable steam when empty
        StopCoroutine(nameof(AnimateSteamCrossfade));
        if (steamObject != null)
            steamObject.SetActive(false);

        _currentColor = Color.clear;

        if (liquidRenderer != null)
        {
            liquidRenderer.enabled = false;
            var c = liquidRenderer.color; c.a = 0f;
            liquidRenderer.color = c;
        }

        var cupState = GetComponent<CupState>();
        if (cupState != null)
            cupState.ClearToppings();
    }

    // -----------------------------
    // Internals
    // -----------------------------
    void UpdateLiquidColor()
    {
        if (!_hasWater) return;

        // Powder drinks path (e.g., Hot Chocolate)
        if (_powderRequired)
        {
            // No dips for powder. Show prePowder until milk is added;
            // if milk is required, switch to postPowder when milk arrives.
            if (_hasMilk && _postPowderColor.a > 0f)
            {
                ApplyColor(_postPowderColor);
            }
            else
            {
                ApplyColor(_prePowderColor);
            }
            return;
        }

        // Teabag path
        if (!HasTea || _steepCount <= 0)
        {
            ApplyColor(_baseWaterColor);
            return;
        }

        float blend = Mathf.Clamp01(_steepCount / 3f);
        var color = Color.Lerp(_baseWaterColor, _targetTeaColor, blend);
        ApplyColor(color);
    }

    void ApplyOptionalMilkTint()
    {
        if (liquidRenderer == null) return;

        var current = liquidRenderer.color;
        var milkColor = new Color(optionalMilkTint.r, optionalMilkTint.g, optionalMilkTint.b, current.a);
        _currentColor = milkColor;
        liquidRenderer.enabled = true;
        liquidRenderer.color = milkColor;
    }

    void ApplyColor(Color color)
    {
        _currentColor = color;

        if (liquidRenderer != null)
        {
            liquidRenderer.enabled = true;

            float alpha = color.a;
            if (scaleAlphaByVolume && maxVolume > 0f)
                alpha *= Mathf.Clamp01(_currentVolume / maxVolume);

            var c = color; c.a = alpha;
            liquidRenderer.color = c;
        }
    }

    void UpdateLiquidVisuals() => ApplyColor(_currentColor);

    /// <summary>
    /// Enable steam for hot water, disable for cold water or no water.
    /// </summary>
    void UpdateSteamVisual()
    {
        if (steamObject == null) return;

        bool shouldShowSteam = _hasWater && _waterTemp == WaterTemp.Hot;
        
        if (shouldShowSteam)
        {
            // Enable the GameObject first
            if (!steamObject.activeSelf)
            {
                steamObject.SetActive(true);
                
                // Start crossfade animation coroutine
                StopCoroutine(nameof(AnimateSteamCrossfade));
                StartCoroutine(AnimateSteamCrossfade());
            }
        }
        else
        {
            // Immediately disable steam for cold water
            StopCoroutine(nameof(AnimateSteamCrossfade));
            steamObject.SetActive(false);
        }
    }

    /// <summary>
    /// Animates steam by crossfading between frames in the steamFrames array.
    /// Uses two sprite renderers to create a smooth blend effect.
    /// </summary>
    System.Collections.IEnumerator AnimateSteamCrossfade()
    {
        if (steamObject == null || steamRendererA == null || steamRendererB == null) 
            yield break;
        
        if (steamFrames == null || steamFrames.Length == 0)
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[MugBeverageState] No steam frames assigned!", this);
            #endif
            yield break;
        }

        float targetAlpha = steamTargetAlpha / 255f;
        int currentFrameIndex = 0;
        
        // Initialize: A shows first frame at full alpha, B is invisible
        steamRendererA.sprite = steamFrames[0];
        steamRendererB.sprite = steamFrames.Length > 1 ? steamFrames[1] : steamFrames[0];
        
        Color colorA = steamRendererA.color;
        colorA.a = targetAlpha;
        steamRendererA.color = colorA;
        
        Color colorB = steamRendererB.color;
        colorB.a = 0f;
        steamRendererB.color = colorB;

        // Loop forever through all frames
        while (true)
        {
            // Hold current frame
            yield return new WaitForSeconds(steamFrameHoldDuration);

            // Determine next frame
            int nextFrameIndex = (currentFrameIndex + 1) % steamFrames.Length;
            
            // Set the fading-in renderer to the next frame
            steamRendererB.sprite = steamFrames[nextFrameIndex];

            // Crossfade: A fades out, B fades in
            float elapsed = 0f;
            while (elapsed < steamCrossfadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / steamCrossfadeDuration);

                colorA = steamRendererA.color;
                colorA.a = Mathf.Lerp(targetAlpha, 0f, t);
                steamRendererA.color = colorA;

                colorB = steamRendererB.color;
                colorB.a = Mathf.Lerp(0f, targetAlpha, t);
                steamRendererB.color = colorB;

                yield return null;
            }

            // Ensure final values
            colorA.a = 0f;
            steamRendererA.color = colorA;
            colorB.a = targetAlpha;
            steamRendererB.color = colorB;

            // Swap roles: what was B is now A
            currentFrameIndex = nextFrameIndex;
            
            // Swap the renderers' roles for next iteration
            var temp = steamRendererA;
            steamRendererA = steamRendererB;
            steamRendererB = temp;
        }
    }

    void ResolveRenderer()
    {
        if (liquidRenderer != null) return;
        var tr = transform.Find("Visuals/FillPivot/LiquidInCup");
        if (tr != null) liquidRenderer = tr.GetComponent<SpriteRenderer>();
    }
}

public enum TeaType
{
    None = 0,
    Lavender,
    GingerGreen,
    Peppermint,
    Chamomile,
    LondonFog,
    HotChocolate
}