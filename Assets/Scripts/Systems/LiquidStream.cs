using UnityEngine;
using System.Collections.Generic;

public class LiquidStream : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The original liquid sprite to duplicate")]
    [SerializeField] private GameObject liquidPrefab;
    
    [Tooltip("Transform to raycast from (milk spout position)")]
    [SerializeField] private Transform raycastOrigin;
    
    [Tooltip("Parent transform for spawned liquid sprites (should be milkFront)")]
    [SerializeField] private Transform liquidParent;
    
    [Header("Collision Sprite")]
    [Tooltip("Sprites to cycle through at collision point (splash/puddle animation)")]
    [SerializeField] private Sprite[] collisionSprites;
    [Tooltip("Time between sprite changes at collision point")]
    [SerializeField] private float collisionSpriteInterval = 0.2f;
    [Tooltip("Parent for collision sprite (null = use liquidParent)")]
    [SerializeField] private Transform collisionSpriteParent;
    [Tooltip("Sorting layer name for collision sprite")]
    [SerializeField] private string collisionSpriteSortingLayerName = "Default";
    [Tooltip("Sorting order within the layer for collision sprite")]
    [SerializeField] private int collisionSpriteSortingOrder = 1;
    
    [Header("Stream Settings")]
    [Tooltip("Distance between each liquid sprite (adjust to connect seamlessly)")]
    [SerializeField] private float liquidSpriteSpacing = 3.47f; // world units
    [Tooltip("Maximum distance above surface to allow pouring (world units)")]
    [SerializeField] private float maxPourHeight = 3.47f;       // world units
    
    [Header("Masking")]
    [Tooltip("Use sprite mask to gradually reveal liquid stream")]
    [SerializeField] private bool useMask = true;

    [Tooltip("Stream sprites' mask interaction")]
    [SerializeField] private SpriteMaskInteraction streamMaskInteraction = SpriteMaskInteraction.VisibleInsideMask;

    [Tooltip("Enable custom front/back sorting range for the mask")]
    [SerializeField] private bool maskUseCustomRange = true;

    [Tooltip("Sorting layer name for the spawned SpriteMask")]
    [SerializeField] private string maskSortingLayerName = "Default";

    [Tooltip("Back (lower) sorting order for mask range")]
    [SerializeField] private int maskBackSortingOrder = -200;

    [Tooltip("Front (higher) sorting order for mask range")]
    [SerializeField] private int maskFrontSortingOrder = 200;

    [Tooltip("Speed at which the mask expands downward (units per second)")]
    [SerializeField] private float maskExpansionSpeed = 5f;

    [Tooltip("Width of the mask (should be wide enough to cover all liquid sprites)")]
    [SerializeField] private float maskWidth = 5f;

    [Tooltip("Y offset for mask start position relative to raycast origin")]
    [SerializeField] private float maskStartOffset = 0.3f;

    [Tooltip("Mask sprite to use (white rectangle / 9-slice friendly)")]
    [SerializeField] private Sprite maskSprite;

    [Header("Tiling / Pool")]
    [Tooltip("Overlap sprites to tile seamlessly without gaps (0-1, 0.5 = 50% overlap)")]
    [Range(0f, 0.95f)]
    [SerializeField] private float spriteOverlap = 0.5f;

    [Tooltip("Maximum number of liquid sprites in the pool")]
    [SerializeField] private int maxLiquidSprites = 10;

    [Tooltip("Maximum distance to pour (if no surface detected)")]
    [SerializeField] private float maxPourDistance = 10f;

    [Header("Surface Detection")]
    [Tooltip("Layers to detect as pour surfaces (legacy fallback)")]
    [SerializeField] private LayerMask surfaceLayer;
    [Tooltip("Layers that represent CupLiquid surfaces")]
    [SerializeField] private LayerMask cupLiquidLayerMask;
    [Tooltip("Tags to detect as pour surfaces")]
    [SerializeField] private string[] surfaceTags = { "CupLiquid", "PourSurface", "CupSurface" };

    [Header("Milk Settings")]
    [Tooltip("Milk units granted per impact with a CupLiquid surface")]
    [SerializeField] private float milkPerHit = 1f;
    [Tooltip("Cooldown before the same collider can award milk again")]
    [SerializeField] private float milkHitCooldown = 0.05f;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugRay = true;
    [SerializeField] private Color debugRayColor = Color.cyan;
    
    public event System.Action OnPourStart;
    public event System.Action OnPourStop;

    private List<GameObject> activeLiquidSprites = new List<GameObject>();
    private bool isPouring = false;
    private Vector3 originalLiquidScale;
    
    // Collision sprite fields
    private GameObject collisionSpriteObject;
    private SpriteRenderer collisionSpriteRenderer;
    private float collisionSpriteTimer = 0f;
    private int currentCollisionSpriteIndex = 0;
    
    // Mask fields
    private GameObject maskObject;
    private SpriteMask spriteMask;
    private float currentMaskHeight = 0f;
    
    // Pour duration check
    private MilkTiltVisual milkTiltVisual;

    const string CupLiquidTag = "CupLiquid";
    const string CupSurfaceTag = "CupSurface";

    enum SurfaceType { None = 0, CupLiquid, CupSurface, Other }

    private Collider2D lastMilkCollider;
    private float lastMilkTime = float.NegativeInfinity;

    void Update()
    {
        // Lazy initialization of MilkTiltVisual
        if (milkTiltVisual == null)
            milkTiltVisual = GetComponent<MilkTiltVisual>();
        
        if (isPouring)
        {
            UpdateLiquidStream();
            UpdateCollisionSprite();
            UpdateMask();
        }
        
        if (liquidPrefab != null && originalLiquidScale == Vector3.zero)
            originalLiquidScale = liquidPrefab.transform.localScale;
    }

    public bool HasValidPourSurface()
    {
        if (raycastOrigin == null)
            return false;

        if (!TryGetPourSurface(raycastOrigin.position, maxPourDistance, out var hit, out var surfaceType))
            return false;

        if (hit.collider == null || surfaceType == SurfaceType.None)
            return false;

        // Must be close enough to the surface (not too high)
        return hit.distance <= maxPourHeight;
    }

    public void StartPouring()
    {
        if (!isPouring)
        {
            isPouring = true;
            currentMaskHeight = 0f; // Reset mask height
#if UNITY_EDITOR
            Debug.Log("LiquidStream: Started pouring");
#endif
            ResetMilkHitState();
            try { OnPourStart?.Invoke(); } catch { }
        }
    }

    public void StopPouring()
    {
        if (isPouring)
        {
            isPouring = false;
            ClearLiquidSprites();
            if (collisionSpriteObject != null) collisionSpriteObject.SetActive(false);
            if (maskObject != null) maskObject.SetActive(false);
#if UNITY_EDITOR
            Debug.Log("LiquidStream: Stopped pouring");
#endif
            try { OnPourStop?.Invoke(); } catch { }
        }
        ResetMilkHitState();
    }

    void UpdateMask()
    {
        if (!useMask || raycastOrigin == null)
            return;

        if (!TryGetPourSurface(raycastOrigin.position, maxPourDistance, out var hit, out _))
        {
            if (maskObject != null) maskObject.SetActive(false);
            return;
        }

        if (!hit.collider || hit.distance > maxPourHeight)
        {
            if (maskObject != null) maskObject.SetActive(false);
            return;
        }
        
        float pourDistance = hit.distance;
        
        // Create mask object if it doesn't exist
        if (maskObject == null)
        {
            Transform parent = liquidParent != null ? liquidParent : transform;
            maskObject = new GameObject("LiquidMask");
            maskObject.transform.SetParent(parent, true);

            spriteMask = maskObject.AddComponent<SpriteMask>();
            spriteMask.isCustomRangeActive = maskUseCustomRange;

            // Apply inspector layer & range
            int layerId = SortingLayer.NameToID(string.IsNullOrEmpty(maskSortingLayerName) ? "Default" : maskSortingLayerName);
            spriteMask.frontSortingLayerID = layerId;
            spriteMask.backSortingLayerID  = layerId;
            spriteMask.frontSortingOrder   = maskFrontSortingOrder;
            spriteMask.backSortingOrder    = maskBackSortingOrder;

            if (maskSprite != null)
            {
                spriteMask.sprite = maskSprite;
            }
            else
            {
                // Create a simple white rect sprite if none provided
                const int S = 64;
                Texture2D tex = new Texture2D(S, S);
                var pixels = new Color[S * S];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
                tex.SetPixels(pixels); tex.Apply();
                spriteMask.sprite = Sprite.Create(tex, new Rect(0,0,S,S), new Vector2(0.5f,1f), S);
            }
        }
        else
        {
            // Keep mask range in sync with inspector changes at runtime
            spriteMask.isCustomRangeActive = maskUseCustomRange;
            int layerId = SortingLayer.NameToID(string.IsNullOrEmpty(maskSortingLayerName) ? "Default" : maskSortingLayerName);
            spriteMask.frontSortingLayerID = layerId;
            spriteMask.backSortingLayerID  = layerId;
            spriteMask.frontSortingOrder   = maskFrontSortingOrder;
            spriteMask.backSortingOrder    = maskBackSortingOrder;
        }
        
        // Expand mask
        currentMaskHeight += maskExpansionSpeed * Time.deltaTime;
        currentMaskHeight = Mathf.Min(currentMaskHeight, pourDistance + maskStartOffset);
        
        // Position & scale mask
        maskObject.SetActive(true);
        Vector3 maskPosition = raycastOrigin.position;
        maskPosition.y += maskStartOffset;
        maskObject.transform.position = maskPosition;
        maskObject.transform.rotation = Quaternion.identity;
        maskObject.transform.localScale = new Vector3(maskWidth, currentMaskHeight, 1f);
    }

    void UpdateCollisionSprite()
    {
        if (collisionSprites == null || collisionSprites.Length == 0) return;
        if (raycastOrigin == null) return;
        
        if (!TryGetPourSurface(raycastOrigin.position, maxPourDistance, out var hit, out _))
        {
            if (collisionSpriteObject != null) collisionSpriteObject.SetActive(false);
            return;
        }
        
        if (!hit.collider || hit.distance > maxPourHeight)
        {
            if (collisionSpriteObject != null) collisionSpriteObject.SetActive(false);
            return;
        }
        
        float pourDistance = hit.distance;
        bool maskReachedBottom = !useMask || currentMaskHeight >= (pourDistance + maskStartOffset - 0.1f);
        if (!maskReachedBottom)
        {
            if (collisionSpriteObject != null) collisionSpriteObject.SetActive(false);
            return;
        }
        
        if (collisionSpriteObject == null)
        {
            Transform parent = collisionSpriteParent != null ? collisionSpriteParent :
                              (liquidParent != null ? liquidParent : transform);
            
            collisionSpriteObject = new GameObject("CollisionSprite");
            collisionSpriteObject.transform.SetParent(parent, true);
            collisionSpriteRenderer = collisionSpriteObject.AddComponent<SpriteRenderer>();
            collisionSpriteRenderer.sprite = collisionSprites[0];
            collisionSpriteRenderer.flipX = true;
            collisionSpriteRenderer.sortingLayerName = collisionSpriteSortingLayerName;
            collisionSpriteRenderer.sortingOrder = collisionSpriteSortingOrder;
            currentCollisionSpriteIndex = 0;
        }
        
        collisionSpriteObject.SetActive(true);
        collisionSpriteObject.transform.position = hit.point;
        collisionSpriteObject.transform.rotation = Quaternion.identity;
        
        collisionSpriteTimer += Time.deltaTime;
        if (collisionSpriteTimer >= collisionSpriteInterval)
        {
            collisionSpriteTimer = 0f;
            currentCollisionSpriteIndex = (currentCollisionSpriteIndex + 1) % collisionSprites.Length;
            collisionSpriteRenderer.sprite = collisionSprites[currentCollisionSpriteIndex];
        }
    }

    void UpdateLiquidStream()
    {
        if (liquidPrefab == null || raycastOrigin == null)
            return;

        if (!TryGetPourSurface(raycastOrigin.position, maxPourDistance, out var hit, out var surfaceType))
        {
            HideActiveLiquidSprites();
            ResetMilkHitState();
            return;
        }

        if (!hit.collider || hit.distance > maxPourHeight)
        {
            HideActiveLiquidSprites();
            ResetMilkHitState();
            return;
        }

        if (surfaceType == SurfaceType.CupLiquid) TryDispatchMilk(hit.collider);
        else ResetMilkHitState();

        float pourDistance = hit.distance;

        if (showDebugRay)
            Debug.DrawRay(raycastOrigin.position, Vector2.down * pourDistance, debugRayColor);
        
        float effectiveSpacing = liquidSpriteSpacing * (1f - spriteOverlap);
        int totalSprites = maxLiquidSprites;
        
        while (activeLiquidSprites.Count < totalSprites)
            SpawnLiquidSprite();
        
        float scrollOffset = (maxPourDistance - pourDistance);
        
        for (int i = 0; i < activeLiquidSprites.Count; i++)
        {
            var go = activeLiquidSprites[i];
            if (go == null) continue;

            Vector3 worldPos = raycastOrigin.position;
            worldPos.y -= (i * effectiveSpacing);
            worldPos.y += scrollOffset;
            
            bool isVisible = worldPos.y <= raycastOrigin.position.y &&
                             worldPos.y >= (raycastOrigin.position.y - pourDistance);
            
            if (go.activeSelf != isVisible) go.SetActive(isVisible);
            if (!isVisible) continue;

            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = originalLiquidScale;
        }
    }

    void SpawnLiquidSprite()
    {
        Transform parent = liquidParent != null ? liquidParent : transform;
        GameObject newLiquid = Instantiate(liquidPrefab, parent);
        newLiquid.SetActive(true);
        
        SpriteRenderer sr = newLiquid.GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.maskInteraction = streamMaskInteraction;
        
        activeLiquidSprites.Add(newLiquid);
    }

    void RemoveLastLiquidSprite()
    {
        if (activeLiquidSprites.Count == 0) return;
        int lastIndex = activeLiquidSprites.Count - 1;
        if (activeLiquidSprites[lastIndex] != null)
            Destroy(activeLiquidSprites[lastIndex]);
        activeLiquidSprites.RemoveAt(lastIndex);
    }

    void ClearLiquidSprites()
    {
        foreach (var liquid in activeLiquidSprites)
            if (liquid != null) Destroy(liquid);
        activeLiquidSprites.Clear();
    }

    void OnDisable()
    {
        ClearLiquidSprites();
        if (collisionSpriteObject != null) { Destroy(collisionSpriteObject); collisionSpriteObject = null; }
        if (maskObject != null)            { Destroy(maskObject);            maskObject = null; }
    }

    void TryDispatchMilk(Collider2D collider)
    {
        if (collider == null) return;

        if (milkTiltVisual != null && !milkTiltVisual.IsMilkRegistered)
        {
            if (Time.frameCount % 60 == 0)
            {
                float progress = milkTiltVisual.PouringTime / 2f; // 2s required
#if UNITY_EDITOR
                Debug.Log($"[LiquidStream] Pour progress: {progress * 100f:F0}%");
#endif
            }
            return;
        }

        float now = Time.time;
        float cooldown = Mathf.Max(0f, milkHitCooldown);
        if (collider == lastMilkCollider && now - lastMilkTime < cooldown) return;

        lastMilkCollider = collider;
        lastMilkTime = now;

        var beverage = collider.GetComponentInParent<MugBeverageState>() ?? collider.GetComponent<MugBeverageState>();
        if (beverage != null)
        {
            beverage.AddMilk(milkPerHit);
            if (lastMilkTime == now)
            {
#if UNITY_EDITOR
                Debug.Log($"[LiquidStream] Milk added after {milkTiltVisual?.PouringTime:F2}s");
#endif
                // Notify MilkTiltVisual that milk has been applied to this specific cup
                if (milkTiltVisual != null)
                    milkTiltVisual.NotifyMilkApplied(beverage);
            }
        }
    }

    void ResetMilkHitState()
    {
        lastMilkCollider = null;
        lastMilkTime = float.NegativeInfinity;
    }

    void HideActiveLiquidSprites()
    {
        foreach (var liquid in activeLiquidSprites)
            if (liquid != null && liquid.activeSelf) liquid.SetActive(false);
    }

    bool TryGetPourSurface(Vector2 origin, float distance, out RaycastHit2D hit, out SurfaceType surfaceType)
    {
        if (cupLiquidLayerMask.value != 0)
        {
            var liquidHit = Physics2D.Raycast(origin, Vector2.down, distance, cupLiquidLayerMask);
            if (liquidHit.collider != null)
            {
                hit = liquidHit; surfaceType = SurfaceType.CupLiquid; return true;
            }
        }

        if (surfaceLayer.value != 0)
        {
            var fallback = Physics2D.Raycast(origin, Vector2.down, distance, surfaceLayer);
            if (fallback.collider != null)
            {
                hit = fallback;
                surfaceType = GetSurfaceType(fallback.collider);
                if (surfaceType == SurfaceType.None && MatchesSurfaceTag(fallback.collider))
                    surfaceType = SurfaceType.Other;
                return true;
            }
        }

        var hits = Physics2D.RaycastAll(origin, Vector2.down, distance);
        if (hits != null && hits.Length > 0)
        {
            RaycastHit2D? best = null; SurfaceType bestType = SurfaceType.None;

            foreach (var candidate in hits)
            {
                if (candidate.collider == null) continue;

                var candidateType = GetSurfaceType(candidate.collider);
                if (candidateType == SurfaceType.CupLiquid)
                { hit = candidate; surfaceType = candidateType; return true; }

                if (best == null)
                {
                    if (candidateType != SurfaceType.None) { best = candidate; bestType = candidateType; continue; }
                    if (MatchesSurfaceTag(candidate.collider)) { best = candidate; bestType = SurfaceType.Other; }
                }
            }

            if (best.HasValue) { hit = best.Value; surfaceType = bestType; return true; }
        }

        hit = default; surfaceType = SurfaceType.None; return false;
    }

    static SurfaceType GetSurfaceType(Collider2D collider)
    {
        if (collider == null) return SurfaceType.None;
        if (collider.CompareTag(CupLiquidTag))   return SurfaceType.CupLiquid;
        if (collider.CompareTag(CupSurfaceTag))  return SurfaceType.CupSurface;
        return SurfaceType.None;
    }

    bool MatchesSurfaceTag(Collider2D collider)
    {
        if (collider == null || surfaceTags == null) return false;
        foreach (var tag in surfaceTags) if (!string.IsNullOrEmpty(tag) && collider.CompareTag(tag)) return true;
        return false;
    }

    void OnDestroy()
    {
        ClearLiquidSprites();
        if (collisionSpriteObject != null) { Destroy(collisionSpriteObject); collisionSpriteObject = null; }
        if (maskObject != null)            { Destroy(maskObject);            maskObject = null; }
    }
}
