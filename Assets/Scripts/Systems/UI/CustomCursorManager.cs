using UnityEngine;

/// <summary>
/// Manages custom cursor sprites. Provides an explicit API to switch between default and grab states.
/// </summary>
public sealed class CustomCursorManager : MonoBehaviour
{
    [Header("Cursor Sprites")]
    [Tooltip("The default cursor sprite")]
    [SerializeField] Sprite defaultCursor;

    [Tooltip("The cursor sprite shown when grabbing items (click/drag)")]
    [SerializeField] Sprite grabCursor;

    [Header("Cursor Settings")]
    [Tooltip("The offset from the top-left of the cursor image (hotspot). Use (0,0) for top-left, or enable Auto Calculate.")]
    [SerializeField] Vector2 cursorHotspot = Vector2.zero;

    [Tooltip("Automatically calculate hotspot based on cursor sprite's pivot")]
    [SerializeField] bool autoCalculateHotspot = true;

    [Tooltip("Should cursor be confined to game window?")]
    [SerializeField] CursorMode cursorMode = CursorMode.Auto;

    [Tooltip("Maximum cursor size for WebGL compatibility (128x128 recommended)")]
    [SerializeField] int maxCursorSize = 128;

    public static CustomCursorManager Instance { get; private set; }

    CursorState _currentState = CursorState.Default;
    Texture2D _defaultTexture;
    Texture2D _grabTexture;

    enum CursorState
    {
        Default,
        Grab
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CustomCursorManager] Multiple instances detected. This instance will still run but others may have been left behind.", this);
        }
        Instance = this;

        // Convert sprites to Texture2D for cursor usage
        if (defaultCursor != null)
        {
            _defaultTexture = SpriteToTexture2D(defaultCursor);
#if UNITY_EDITOR
            if (_defaultTexture != null)
            {
                int visiblePixels = CountVisiblePixels(_defaultTexture);
                Debug.Log($"[CustomCursorManager] Default cursor loaded: {_defaultTexture.width}x{_defaultTexture.height}, visible pixels: {visiblePixels}/{_defaultTexture.width * _defaultTexture.height}", this);

                // Calculate and log suggested hotspot
                Vector2 suggestedHotspot = CalculateHotspot(defaultCursor, _defaultTexture);
                Debug.Log($"[CustomCursorManager] Sprite pivot: {defaultCursor.pivot}, Suggested hotspot: {suggestedHotspot}", this);
            }
            else
            {
                Debug.LogError("[CustomCursorManager] Failed to convert default cursor sprite to texture!", this);
            }
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning("[CustomCursorManager] No default cursor sprite assigned!", this);
#endif
        }

        if (grabCursor != null)
        {
            _grabTexture = SpriteToTexture2D(grabCursor);
#if UNITY_EDITOR
            Debug.Log($"[CustomCursorManager] Grab cursor loaded: {_grabTexture.width}x{_grabTexture.height}", this);
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning("[CustomCursorManager] No grab cursor sprite assigned!", this);
#endif
        }

        // Hide hardware cursor and use custom
        if (_defaultTexture != null)
        {
            Vector2 hotspot = autoCalculateHotspot && defaultCursor != null
                ? CalculateHotspot(defaultCursor, _defaultTexture)
                : cursorHotspot;
#if UNITY_EDITOR
            Debug.Log($"[CustomCursorManager] Testing cursor with hotspot at {hotspot} using mode {cursorMode}", this);
#endif
            Cursor.SetCursor(_defaultTexture, hotspot, cursorMode);
            Cursor.visible = true;

#if UNITY_EDITOR
            Debug.Log("[CustomCursorManager] Custom cursor system initialized", this);
            Debug.Log($"[CustomCursorManager] Cursor.visible = {Cursor.visible}, lockState = {Cursor.lockState}", this);
#endif
        }
    }

    /// <summary>
    /// Switches the cursor to the grab image (used when clicking/dragging).
    /// </summary>
    public void StartGrabCursor()
    {
        SetCursorState(CursorState.Grab);
    }

    /// <summary>
    /// Reverts the cursor to the default image.
    /// </summary>
    public void StopGrabCursor()
    {
        SetCursorState(CursorState.Default);
    }

    void SetCursorState(CursorState newState)
    {
        if (_currentState == newState)
            return;

        _currentState = newState;

        Texture2D cursorTexture = newState == CursorState.Grab ? _grabTexture : _defaultTexture;
        Sprite cursorSprite = newState == CursorState.Grab ? grabCursor : defaultCursor;

        if (cursorTexture != null)
        {
            // Calculate hotspot (either manual or auto from sprite pivot)
            Vector2 adjustedHotspot = autoCalculateHotspot && cursorSprite != null
                ? CalculateHotspot(cursorSprite, cursorTexture)
                : cursorHotspot;

            Cursor.SetCursor(cursorTexture, adjustedHotspot, cursorMode);
            Cursor.visible = true;

#if UNITY_EDITOR
            Debug.Log($"[CustomCursorManager] Set cursor to {newState} (size: {cursorTexture.width}x{cursorTexture.height}, hotspot: {adjustedHotspot}, mode: {cursorMode})", this);

            // Verify the texture has visible pixels
            Color[] pixels = cursorTexture.GetPixels();
            int visCount = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > 0.1f) visCount++;
            }
            Debug.Log($"[CustomCursorManager] Cursor texture has {visCount}/{pixels.Length} visible pixels", this);
#endif
        }
        else
        {
            // Fallback to default hardware cursor if texture is missing
            Cursor.SetCursor(null, Vector2.zero, cursorMode);
        }
    }

    Vector2 CalculateHotspot(Sprite sprite, Texture2D texture)
    {
        if (sprite == null || texture == null)
            return Vector2.zero;

        // Convert sprite pivot (in sprite space) to texture space
        // Sprite pivot is normalized (0-1), so we scale by texture size
        float pivotX = sprite.pivot.x / sprite.rect.width;
        float pivotY = sprite.pivot.y / sprite.rect.height;

        // Invert Y because Unity cursor hotspot is top-left origin, sprite pivot is bottom-left
        Vector2 hotspot = new Vector2(
            pivotX * texture.width,
            texture.height - (pivotY * texture.height)
        );

        return hotspot;
    }

    Texture2D SpriteToTexture2D(Sprite sprite)
    {
        if (sprite == null) return null;

        // IMPORTANT: mipChain MUST be false for cursor textures
        // Unity cursor textures also need Linear color space (not sRGB)
        // Always use RenderTexture approach to ensure proper format

        Rect spriteRect = sprite.rect;

        // Create texture with explicit parameters: width, height, format, mipChain, linear
        Texture2D baseTex = new Texture2D(
            (int)spriteRect.width,
            (int)spriteRect.height,
            TextureFormat.RGBA32,
            false,  // mipChain = false (NO mipmaps)
            true    // linear = true (Linear color space)
        );
#if UNITY_EDITOR
        baseTex.alphaIsTransparency = true;  // Editor-only flag that improves cursor import previews
#endif

        RenderTexture rt = RenderTexture.GetTemporary(
            sprite.texture.width,
            sprite.texture.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear
        );

        Graphics.Blit(sprite.texture, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        baseTex.ReadPixels(spriteRect, 0, 0);
        baseTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

#if UNITY_EDITOR
        Debug.Log($"[CustomCursorManager] Created cursor texture: format={baseTex.format}, mipmaps={baseTex.mipmapCount}, size={baseTex.width}x{baseTex.height}", this);
#endif

        // Resize if needed - Unity has platform-specific cursor size limits
        // Windows/Editor: typically 128x128 max
        if (baseTex.width > maxCursorSize || baseTex.height > maxCursorSize)
        {
            Texture2D resized = ResizeTexture(baseTex, maxCursorSize, maxCursorSize);
            Destroy(baseTex); // Clean up the original oversized texture
#if UNITY_EDITOR
            Debug.Log($"[CustomCursorManager] Resized cursor from {baseTex.width}x{baseTex.height} to {resized.width}x{resized.height}", this);
#endif
            return resized;
        }

        return baseTex;
    }

    Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        // Use RenderTexture for high-quality bilinear filtering
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        rt.filterMode = FilterMode.Bilinear;

        RenderTexture.active = rt;
        Graphics.Blit(source, rt);

        Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false, true);
#if UNITY_EDITOR
        result.alphaIsTransparency = true;
#endif
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }

    void OnDisable()
    {
        // Restore default cursor when disabled
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        // Restore default cursor when destroyed
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    System.Collections.IEnumerator SwitchToRealCursorAfterDelay()
    {
        yield return new WaitForSeconds(2f);
#if UNITY_EDITOR
        Debug.Log("[CustomCursorManager] Switching from test cursor to real cursor...", this);
#endif
        SetCursorState(CursorState.Default);
    }

#if UNITY_EDITOR
    int CountVisiblePixels(Texture2D texture)
    {
        if (texture == null) return 0;
        Color[] pixels = texture.GetPixels();
        int count = 0;
        foreach (Color pixel in pixels)
        {
            if (pixel.a > 0.1f) count++;
        }
        return count;
    }
#endif
}
