using System.Collections;
using UnityEngine;

public class HotChocolatePourer : MonoBehaviour, IRespawnable
{
    [Header("Child Objects")]
    [Tooltip("The main sprite object (has collider and DragItem2D)")]
    [SerializeField] private string mainSpriteName = "hotChocoMain";
    
    [Tooltip("The tear corner object (has small collider for ripping)")]
    [SerializeField] private string tearCornerObjectName = "tearCorner";
    
    [Header("Sprites")]
    [SerializeField] private Sprite closedSprite;
    [SerializeField] private Sprite openSprite;
    
    [Header("Tear Settings")]
    [Tooltip("Size of the grabbable tear corner area (box collider)")]
    [SerializeField] private Vector2 tearCornerSize = new Vector2(0.3f, 0.3f);
    
    [Tooltip("Minimum distance and direction to drag before tearing (e.g., (1, 0) = drag 1 unit right)")]
    [SerializeField] private Vector2 tearDirection = new Vector2(1f, 0f);
    
    [Tooltip("Offset for the tear corner grab point")]
    [SerializeField] private Vector2 tearCornerOffset = new Vector2(0.5f, 0.5f);
    
    [Header("Tilt Settings")]
    [Tooltip("Angle to start pouring (only works when open)")]
    [SerializeField] private float pourThreshold = -45f;

    [Header("Auto-Tilt Settings")]
    [Tooltip("Radius to detect nearby cups for auto-tilting")]
    [SerializeField] private float autoTiltRadius = 0.6f;

    [Tooltip("Maximum tilt angle when cup is very close (should match pourThreshold direction)")]
    [SerializeField] private float maxTiltAngle = -45f;

    [Tooltip("Speed of auto-tilt rotation (degrees per second)")]
    [SerializeField] private float autoTiltSpeed = 720f;

    [Tooltip("Exponent for distance-to-tilt mapping (higher = more aggressive close-range tilt)")]
    [SerializeField] private float tiltAggression = 2f;

    [Tooltip("Normalized distance (0-1) at which to snap to max angle")]
    [SerializeField] private float snapDistance01 = 0.18f;
    
    [Header("Powder Stream")]
    [Tooltip("GameObject containing the powder stream visual (child object)")]
    [SerializeField] private string powderStreamName = "powderStream";
    
    [Tooltip("GameObject containing the powder particle effect (child object)")]
    [SerializeField] private string powderParticleName = "powderParticle";
    
    [Tooltip("Where the pour point is (raycast origin) relative to packet")]
    [SerializeField] private Vector2 pourPointOffset = new Vector2(0.3f, -0.2f);
    
    [Tooltip("Where the stream sprite sits relative to the pour point (adjust Y to position top of sprite at pour point)")]
    [SerializeField] private Vector2 streamSpriteOffset = new Vector2(0f, -0.5f);
    
    [Tooltip("Rotation offset applied to powder stream sprite (0 = down, 180 = up)")]
    [SerializeField] private float streamRotationOffset = 0f;
    
    [Tooltip("Maximum distance to raycast downward for cup detection")]
    [SerializeField] private float raycastDistance = 10f;
    
    [Tooltip("Width of BoxCast for cup detection (wider = more forgiving)")]
    [SerializeField] private float raycastWidth = 0.3f;
    
    [Tooltip("How long to pour before powder is applied to cup (seconds)")]
    [SerializeField] private float pourDuration = 1f;
    
    [Header("Sorting")]
    [SerializeField] private int streamSortingOrder = 8;
    [SerializeField] private int particleSortingOrder = 9;

    [Header("Fade & Respawn")]
    [Tooltip("Time to fade out after successful pour (seconds)")]
    [SerializeField] private float fadeOutDuration = 1f;
    [Tooltip("Delay before starting fade (seconds)")]
    [SerializeField] private float fadeDelay = 0.5f;
    [Tooltip("If true, respawn a new packet at the original position")]
    [SerializeField] private bool respawnAfterDestroy = true;
    [Tooltip("Reference to the prefab to spawn")]
    [SerializeField] private GameObject hotChocolatePrefab;

    // Accepted tags for cup detection
    private static readonly string[] CupTags = { "CupLiquid", "CupSurface", "Cup" };
    
    private SpriteRenderer mainRenderer;
    private Transform mainTransform;
    private SpriteRenderer powderStreamRenderer;
    private Transform powderStreamTransform;
    private SpriteRenderer powderParticleRenderer;
    private Transform powderParticleTransform;
    private Collider2D tearCornerCollider;
    private DragItem2D dragItem;
    private bool isPouringActive = false;
    private bool isOpen = false;
    private bool isDraggingTear = false;
    private Vector3 tearCornerWorldPos;
    private Vector3 tearStartPos;
    private float currentPourTime = 0f;
    private MugBeverageState currentTargetCup = null;

    // Performance optimization caches
    private Collider2D[] _overlapBuffer = new Collider2D[10];
    private MugBeverageState _cachedBeverage = null;
    
    // Respawn state
    private Vector3 originalLocalPosition;
    private Vector3 originalWorldPosition;
    private Quaternion originalLocalRotation;
    private Vector3 originalLocalScale;
    private Transform originalParent;
    private string originalParentName;
    private GameObject cachedHotChocolatePrefab;
    private bool isFading = false;

    public void RespawnBeforeDestroy()
    {
        RespawnPacket();
    }
    void Awake()
    {
        // Store original transform for respawning
        originalLocalPosition = transform.localPosition;
        originalWorldPosition = transform.position;
        originalLocalRotation = transform.localRotation;
        originalLocalScale = transform.localScale;
        originalParent = transform.parent;
        originalParentName = originalParent != null ? originalParent.name : "";
        
        // Cache the prefab reference
        if (hotChocolatePrefab != null && !hotChocolatePrefab.name.EndsWith("(Clone)"))
        {
            cachedHotChocolatePrefab = hotChocolatePrefab;
        }
        
        // Find main sprite
        Transform mainSpriteTransform = transform.Find(mainSpriteName);
        if (mainSpriteTransform != null)
        {
            mainTransform = mainSpriteTransform;
            mainRenderer = mainSpriteTransform.GetComponent<SpriteRenderer>();
            dragItem = mainSpriteTransform.GetComponent<DragItem2D>();
            
            if (mainRenderer != null && closedSprite != null)
            {
                mainRenderer.sprite = closedSprite;
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError($"HotChocolatePourer: Could not find child '{mainSpriteName}'!");
#endif
        }
        
        // Find or create tear corner object
        Transform tearCornerTransform = transform.Find(tearCornerObjectName);
        if (tearCornerTransform == null && mainTransform != null)
        {
            tearCornerTransform = mainTransform.Find(tearCornerObjectName);
        }
        
        if (tearCornerTransform == null)
        {
            GameObject tearCornerObj = new GameObject(tearCornerObjectName);
            tearCornerTransform = tearCornerObj.transform;
            if (mainTransform != null) tearCornerTransform.SetParent(mainTransform);
            else tearCornerTransform.SetParent(transform);
            tearCornerTransform.localPosition = tearCornerOffset;
            tearCornerTransform.localRotation = Quaternion.identity;
            tearCornerTransform.localScale = Vector3.one;
            BoxCollider2D boxCol = tearCornerObj.AddComponent<BoxCollider2D>();
            boxCol.size = tearCornerSize;
            boxCol.isTrigger = false;
            tearCornerCollider = boxCol;
        }
        else
        {
            if (mainTransform != null && tearCornerTransform.parent != mainTransform)
                tearCornerTransform.SetParent(mainTransform);
            
            tearCornerCollider = tearCornerTransform.GetComponent<Collider2D>();
            if (tearCornerCollider == null)
            {
                BoxCollider2D boxCol = tearCornerTransform.gameObject.AddComponent<BoxCollider2D>();
                boxCol.size = tearCornerSize;
                boxCol.isTrigger = false;
                tearCornerCollider = boxCol;
            }
        }
        
        // Initialize tear corner world position
        UpdateTearCornerPosition();
        
        // Register tear corner with DragItem2D so it doesn't trigger dragging
        if (dragItem != null && tearCornerCollider != null)
        {
            if (dragItem.ignoreColliders == null || dragItem.ignoreColliders.Length == 0)
            {
                dragItem.ignoreColliders = new Collider2D[] { tearCornerCollider };
            }
            else
            {
                // Add to existing array using Array.Resize
                bool alreadyContains = false;
                for (int i = 0; i < dragItem.ignoreColliders.Length; i++)
                {
                    if (dragItem.ignoreColliders[i] == tearCornerCollider)
                    {
                        alreadyContains = true;
                        break;
                    }
                }
                if (!alreadyContains)
                {
                    int oldLength = dragItem.ignoreColliders.Length;
                    System.Array.Resize(ref dragItem.ignoreColliders, oldLength + 1);
                    dragItem.ignoreColliders[oldLength] = tearCornerCollider;
                }
            }
        }
        
        // Find powder stream sprite
        Transform streamTransform = mainTransform != null ? mainTransform.Find(powderStreamName) : null;
        if (streamTransform == null) streamTransform = transform.Find(powderStreamName);
        if (streamTransform != null)
        {
            powderStreamTransform = streamTransform;
            powderStreamRenderer = streamTransform.GetComponent<SpriteRenderer>();
            if (powderStreamRenderer != null)
            {
                powderStreamRenderer.sortingOrder = streamSortingOrder;
                powderStreamRenderer.enabled = false;
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning($"HotChocolatePourer: Could not find child '{powderStreamName}'!");
#endif
        }
        
        // Find powder particle sprite
        Transform particleTransform = mainTransform != null ? mainTransform.Find(powderParticleName) : null;
        if (particleTransform == null) particleTransform = transform.Find(powderParticleName);
        if (particleTransform != null)
        {
            powderParticleTransform = particleTransform;
            powderParticleRenderer = particleTransform.GetComponent<SpriteRenderer>();
            if (powderParticleRenderer != null)
            {
                powderParticleRenderer.sortingOrder = particleSortingOrder;
                powderParticleRenderer.enabled = false;
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning($"HotChocolatePourer: Could not find child '{powderParticleName}'!");
#endif
        }
    }
    
    void Start()
    {
        // Store position after Start to ensure we have final loaded position
        if (originalParent != null)
        {
            originalLocalPosition = transform.localPosition;
        }
        originalWorldPosition = transform.position;
        
        // Ensure sprite is visible (reset alpha in case it was faded)
        if (mainRenderer != null)
        {
            Color spriteColor = mainRenderer.color;
            spriteColor.a = 1f;
            mainRenderer.color = spriteColor;
        }
    }
    
    void UpdateTearCornerPosition()
    {
        if (tearCornerCollider != null) tearCornerWorldPos = tearCornerCollider.transform.position;
        else tearCornerWorldPos = transform.position + (Vector3)tearCornerOffset;
        tearCornerWorldPos.z = 0;
    }
    
    void LateUpdate()
    {
        if (!isOpen)
        {
            UpdateTearCornerPosition();
        }
        else if (isOpen && dragItem != null && dragItem.IsDragging)
        {
            UpdateAutoTilt();
        }
    }
    
    void Update()
    {
        if (isFading) return; // Don't process input while fading

        if (!isOpen)
        {
            if (Input.GetMouseButtonDown(0))
            {
                Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                mousePos.z = 0;

                Vector3 currentRipCorner = transform.position + (Vector3)tearCornerOffset;
                currentRipCorner.z = 0;
                tearCornerWorldPos = currentRipCorner;

                Collider2D hit = Physics2D.OverlapPoint(mousePos);
                if (hit == tearCornerCollider)
                {
                    isDraggingTear = true;
                    tearStartPos = mousePos;
                }
            }

            if (isDraggingTear)
            {
                Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                mousePos.z = 0;

                tearCornerWorldPos = mousePos;

                Vector3 dragVector = mousePos - tearStartPos;
                float distance = Vector3.Dot(dragVector, tearDirection.normalized);

                if (distance >= tearDirection.magnitude)
                {
                    TearOpen();
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (isDraggingTear && !isOpen)
                {
                    Vector3 resetPos = transform.position + (Vector3)tearCornerOffset;
                    resetPos.z = 0;
                    tearCornerWorldPos = resetPos;
                }
                isDraggingTear = false;
            }
        }
        else if (isOpen)
        {
            // Only check tilt if the packet is being dragged
            if (dragItem != null && dragItem.IsDragging)
            {
                CheckAndStartPouring();
            }
            else
            {
                // Not being dragged, stop pouring
                StopPouring();
            }
        }
    }
    
    void TearOpen()
    {
        isOpen = true;
        isDraggingTear = false;

        if (openSprite != null && mainRenderer != null)
        {
            mainRenderer.sprite = openSprite;
        }
    }

    void CheckAndStartPouring()
    {
        if (mainTransform == null) return;

        float currentTilt = mainTransform.localEulerAngles.z;
        if (currentTilt > 180f) currentTilt -= 360f;

        bool isTiltedCorrectly = (pourThreshold < 0) ? (currentTilt <= pourThreshold) : (currentTilt >= pourThreshold);

        if (isTiltedCorrectly)
        {
            if (!isPouringActive)
            {
                StartPouring();
            }
        }
        else
        {
            if (isPouringActive)
            {
                StopPouring();
            }
        }
    }
    
    void StartPouring()
    {
        if (isPouringActive) return;
        
        isPouringActive = true;
        
        Vector3 pourPoint = mainTransform.position + (Vector3)pourPointOffset;
        pourPoint.z = 0;
        
        if (powderStreamRenderer != null && powderStreamTransform != null)
        {
            powderStreamRenderer.gameObject.SetActive(true);
            powderStreamRenderer.enabled = true;
            
            Vector3 streamPos = pourPoint + (Vector3)streamSpriteOffset;
            streamPos.z = powderStreamTransform.position.z;
            powderStreamTransform.position = streamPos;
            powderStreamTransform.rotation = Quaternion.Euler(0, 0, streamRotationOffset);
        }
        
        if (powderParticleRenderer != null && powderParticleTransform != null)
        {
            powderParticleRenderer.gameObject.SetActive(true);
            powderParticleRenderer.enabled = true;
            
            Vector3 particlePos = pourPoint;
            particlePos.z = powderParticleTransform.position.z;
            powderParticleTransform.position = particlePos;
            powderParticleTransform.rotation = Quaternion.Euler(0, 0, streamRotationOffset);
        }
        
        StartCoroutine(ApplyHotChocolate());
    }
    
    void StopPouring()
    {
        if (!isPouringActive) return;

        isPouringActive = false;
        currentPourTime = 0f;
        currentTargetCup = null;
        _cachedBeverage = null; // Clear cache when stopping

        if (powderStreamRenderer != null)
        {
            powderStreamRenderer.enabled = false;
            powderStreamRenderer.gameObject.SetActive(false);
        }
        if (powderParticleRenderer != null)
        {
            powderParticleRenderer.enabled = false;
            powderParticleRenderer.gameObject.SetActive(false);
        }
        StopAllCoroutines();
    }
    
    System.Collections.IEnumerator ApplyHotChocolate()
    {
        while (isPouringActive)
        {
            Vector3 pourPoint = mainTransform.position + (Vector3)pourPointOffset;
            RaycastHit2D hit = Physics2D.BoxCast(
                pourPoint,
                new Vector2(raycastWidth, 0.1f),
                0f,
                Vector2.down,
                raycastDistance
            );

            if (hit.collider != null)
            {
                // Use cached beverage if available and still valid, otherwise search
                if (_cachedBeverage == null || _cachedBeverage.Equals(null))
                {
                    if (hit.collider.CompareTag("CupLiquid"))
                    {
                        _cachedBeverage = hit.collider.GetComponentInParent<MugBeverageState>()
                                       ?? hit.collider.GetComponent<MugBeverageState>();
                    }
                    else if (IsCupTag(hit.collider))
                    {
                        var cupLiquid = hit.collider.GetComponentInChildren<Collider2D>(true);
                        if (cupLiquid != null && cupLiquid.CompareTag("CupLiquid"))
                        {
                            _cachedBeverage = cupLiquid.GetComponentInParent<MugBeverageState>();
                        }
                        _cachedBeverage ??= hit.collider.GetComponentInParent<MugBeverageState>()
                                         ??  hit.collider.GetComponent<MugBeverageState>();
                    }
                }

                if (_cachedBeverage != null && _cachedBeverage.HasWater && !_cachedBeverage.HasTea)
                {
                    if (currentTargetCup == null || currentTargetCup != _cachedBeverage)
                    {
                        currentTargetCup = _cachedBeverage;
                        currentPourTime = 0f;
                    }

                    currentPourTime += Time.deltaTime;

                    if (currentPourTime >= pourDuration)
                    {
                        // Pre/post tint colors
                        Color prePowderColor = new Color32(0xA5, 0x6F, 0x3A, 0xFF); // Light brown powder
                        Color postPowderColor = new Color32(0x6F, 0x4E, 0x37, 0xFF); // Rich chocolate

                        _cachedBeverage.SetPowderType(TeaType.HotChocolate, prePowderColor, postPowderColor, true, true);
#if UNITY_EDITOR
                        Debug.Log($"Hot chocolate successfully applied! Starting fade and respawn.");
#endif

                        currentPourTime = 0f;
                        currentTargetCup = null;

                        // Trigger fade and respawn after successful pour
                        StopPouring();
                        StartCoroutine(FadeOutAndDestroy());
                        yield break; // Exit the coroutine
                    }
                }
                else
                {
                    currentPourTime = 0f;
                    currentTargetCup = null;
                    _cachedBeverage = null; // Clear cache if cup no longer valid
                }
            }
            else
            {
                currentPourTime = 0f;
                currentTargetCup = null;
                _cachedBeverage = null; // Clear cache if no hit
            }
            
            yield return null;
        }
    }
    
    IEnumerator FadeOutAndDestroy()
    {
        isFading = true;
        
        // Wait before starting fade
        yield return new WaitForSeconds(fadeDelay);
        
        // Get only the packet sprites (from mainTransform)
        SpriteRenderer[] packetSprites = mainTransform != null ? 
            mainTransform.GetComponentsInChildren<SpriteRenderer>(true) : 
            new SpriteRenderer[0];
        
        float elapsedTime = 0f;
        
        // Store original alpha values
        Color[] originalColors = new Color[packetSprites.Length];
        for (int i = 0; i < packetSprites.Length; i++)
        {
            if (packetSprites[i] != null)
            {
                originalColors[i] = packetSprites[i].color;
            }
        }
        
        // Fade out packet sprites
        Color tempColor; // Reuse this variable to avoid allocations
        while (elapsedTime < fadeOutDuration)
        {
            elapsedTime += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsedTime / fadeOutDuration);

            for (int i = 0; i < packetSprites.Length; i++)
            {
                if (packetSprites[i] != null)
                {
                    tempColor = originalColors[i];
                    tempColor.a = alpha;
                    packetSprites[i].color = tempColor;
                }
            }

            yield return null;
        }
        
        // Respawn if enabled
        if (respawnAfterDestroy)
        {
            RespawnPacket();
        }
        
        // Destroy this packet
        Destroy(gameObject);
    }
    
    void RespawnPacket()
    {
        // Use cached prefab if available
        GameObject prefabToUse = cachedHotChocolatePrefab != null ? cachedHotChocolatePrefab : hotChocolatePrefab;

        if (prefabToUse == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("HotChocolatePourer: No prefab reference set for respawning!");
#endif
            return;
        }

#if UNITY_EDITOR
        Debug.Log($"Instantiating prefab: {prefabToUse.name}");
#endif

        // Attempt to find parent using multiple fallback strategies
        Transform parentToUse = null;
        if (originalParent != null)
        {
            parentToUse = originalParent;
        }
        else if (!string.IsNullOrEmpty(originalParentName))
        {
            GameObject parentObj = GameObject.Find(originalParentName);
            if (parentObj != null)
            {
                parentToUse = parentObj.transform;
#if UNITY_EDITOR
                Debug.Log($"HotChocolatePourer: Found parent '{originalParentName}' in scene by name");
#endif
            }
        }
        if (parentToUse == null)
        {
            GameObject teaHolder = GameObject.Find("TeaHolder");
            if (teaHolder != null)
            {
                parentToUse = teaHolder.transform;
#if UNITY_EDITOR
                Debug.Log($"HotChocolatePourer: Using TeaHolder found in scene as parent");
#endif
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"HotChocolatePourer: Could not find parent! Spawning at world position.");
#endif
            }
        }

        // Spawn new packet
        GameObject newPacket = Instantiate(prefabToUse);
        newPacket.name = prefabToUse.name; // Remove (Clone) suffix

        // Reset all sprite alphas to 1 (in case they were faded)
        SpriteRenderer[] allSprites = newPacket.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (SpriteRenderer sprite in allSprites)
        {
            if (sprite != null)
            {
                Color color = sprite.color;
                color.a = 1f;
                sprite.color = color;
            }
        }

        // Reset hotChocoMain child position
        Transform newMainTransform = newPacket.transform.Find(mainSpriteName);
        if (newMainTransform != null)
        {
            newMainTransform.localPosition = Vector3.zero;
            newMainTransform.localRotation = Quaternion.identity;
        }

        // Set transform using found parent (or null for world root)
        if (parentToUse != null)
        {
            newPacket.transform.SetParent(parentToUse);
            newPacket.transform.localPosition = originalLocalPosition;
            newPacket.transform.localRotation = originalLocalRotation;
            newPacket.transform.localScale = originalLocalScale;
#if UNITY_EDITOR
            Debug.Log($"Respawned hot chocolate packet in '{parentToUse.name}' at local pos {originalLocalPosition} with scale {originalLocalScale}");
#endif
        }
        else
        {
            newPacket.transform.position = originalWorldPosition;
            newPacket.transform.rotation = originalLocalRotation;
            newPacket.transform.localScale = originalLocalScale;
#if UNITY_EDITOR
            Debug.Log($"Respawned hot chocolate packet at world pos {originalWorldPosition}");
#endif
        }
    }
    
    bool IsCupTag(Collider2D col)
    {
        foreach (string tag in CupTags)
        {
            if (col.CompareTag(tag)) return true;
        }
        return false;
    }

    bool TryFindCup(out Vector3 cupPosition, out float distance)
    {
        cupPosition = Vector3.zero;
        distance = float.MaxValue;

        if (mainTransform == null)
            return false;

        Vector3 packetPos = mainTransform.position;

        // Use the newer OverlapCircle API (non-allocating with result list)
        var contactFilter = new ContactFilter2D();
        contactFilter.useTriggers = true;
        contactFilter.SetLayerMask(Physics2D.AllLayers);

        int count = Physics2D.OverlapCircle(packetPos, autoTiltRadius, contactFilter, _overlapBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider2D col = _overlapBuffer[i];
            if (col == null) continue;

            if (IsCupTag(col) || col.CompareTag("CupLiquid"))
            {
                float dist = Vector3.Distance(packetPos, col.transform.position);
                if (dist < distance)
                {
                    distance = dist;
                    cupPosition = col.transform.position;
                }
            }
        }

        return distance < autoTiltRadius;
    }

    void UpdateAutoTilt()
    {
        if (mainTransform == null || !isOpen || dragItem == null || !dragItem.IsDragging)
            return;

        if (TryFindCup(out Vector3 cupPosition, out float distance))
        {
            // Calculate normalized distance (0 = touching, 1 = at edge of radius)
            float normalizedDistance = Mathf.Clamp01(distance / autoTiltRadius);

            // Snap to max when very close (matching MilkTiltVisual logic)
            bool snap = normalizedDistance <= snapDistance01;

            float desiredAngle;
            if (snap)
            {
                // Instant snap to max tilt when very close
                desiredAngle = maxTiltAngle;
            }
            else
            {
                // Use power curve for smooth falloff
                float closeness = 1f - normalizedDistance; // 0..1 (1 when touching)
                float weight = Mathf.Pow(Mathf.Clamp01(closeness), tiltAggression);
                // Lerp from 0 to maxTiltAngle based on weight
                desiredAngle = Mathf.Lerp(0f, maxTiltAngle, weight);
            }

            // Smoothly rotate toward desired angle (using LOCAL rotation like MilkTiltVisual)
            float currentAngle = mainTransform.localEulerAngles.z;
            if (currentAngle > 180f) currentAngle -= 360f;

            float newAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, autoTiltSpeed * Time.deltaTime);
            mainTransform.localEulerAngles = new Vector3(0f, 0f, newAngle);
        }
        else
        {
            // No cup nearby - relax back to upright
            float currentAngle = mainTransform.localEulerAngles.z;
            if (currentAngle > 180f) currentAngle -= 360f;

            float newAngle = Mathf.MoveTowardsAngle(currentAngle, 0f, autoTiltSpeed * Time.deltaTime);
            mainTransform.localEulerAngles = new Vector3(0f, 0f, newAngle);
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (mainTransform == null && transform.Find(mainSpriteName) != null)
        {
            mainTransform = transform.Find(mainSpriteName);
        }

        // Draw auto-tilt detection radius (when open or in editor)
        if (isOpen || !Application.isPlaying)
        {
            Vector3 packetPos = mainTransform != null ? mainTransform.position : transform.position;
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.3f);
            Gizmos.DrawWireSphere(packetPos, autoTiltRadius);

            #if UNITY_EDITOR
            UnityEditor.Handles.Label(packetPos + Vector3.right * autoTiltRadius, "Auto-Tilt Radius");
            #endif
        }

        // Draw tear corner
        Vector3 gizmoPos = Application.isPlaying ? tearCornerWorldPos : transform.position + (Vector3)tearCornerOffset;
        gizmoPos.z = 0;
        Gizmos.color = isDraggingTear ? Color.red : Color.yellow;
        Gizmos.DrawWireCube(gizmoPos, tearCornerSize);
        Gizmos.DrawSphere(gizmoPos, 0.08f);

        Vector3 tearEndPos = gizmoPos + (Vector3)tearDirection;
        Gizmos.color = isDraggingTear ? new Color(1f, 0.3f, 0.3f, 0.7f) : new Color(1f, 1f, 0f, 0.7f);
        Gizmos.DrawLine(gizmoPos, tearEndPos);
        Vector3 arrowDir = ((Vector3)tearDirection).normalized;
        Vector3 perpendicular = new Vector3(-arrowDir.y, arrowDir.x, 0);
        float arrowHeadSize = 0.15f;
        Gizmos.DrawLine(tearEndPos, tearEndPos - arrowDir * arrowHeadSize + perpendicular * arrowHeadSize * 0.5f);
        Gizmos.DrawLine(tearEndPos, tearEndPos - arrowDir * arrowHeadSize - perpendicular * arrowHeadSize * 0.5f);
        Gizmos.DrawWireCube(tearEndPos, Vector3.one * 0.1f);

        #if UNITY_EDITOR
        string directionText = $"({tearDirection.x:F1}, {tearDirection.y:F1})";
        UnityEditor.Handles.Label(gizmoPos + Vector3.up * 0.4f, "Tear Corner\nDrag " + directionText);
        #endif
        
        if (mainTransform != null)
        {
            Vector3 basePos = !Application.isPlaying ? transform.position : mainTransform.position;
            Vector3 pourPoint = basePos + (Vector3)pourPointOffset;
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(pourPoint, 0.15f);
            Gizmos.DrawLine(basePos, pourPoint);
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(pourPoint + Vector3.up * 0.3f, "Pour Point");
            #endif
            
            Vector3 spritePos = pourPoint + (Vector3)streamSpriteOffset;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(spritePos, Vector3.one * 0.2f);
            Gizmos.DrawLine(pourPoint, spritePos);
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(spritePos + Vector3.right * 0.3f, "Sprite Position");
            #endif
            
            if (isOpen || !Application.isPlaying)
            {
                Vector3 raycastEnd = pourPoint + Vector3.down * raycastDistance;
                Gizmos.color = isPouringActive ? Color.green : new Color(0f, 1f, 1f, 0.5f);
                Gizmos.DrawLine(pourPoint, raycastEnd);
                
                Vector3 halfWidth = Vector3.right * (raycastWidth * 0.5f);
                Gizmos.color = isPouringActive ? new Color(0f, 1f, 0f, 0.3f) : new Color(0f, 1f, 1f, 0.2f);
                Gizmos.DrawLine(pourPoint - halfWidth, pourPoint + halfWidth);
                Gizmos.DrawLine(raycastEnd - halfWidth, raycastEnd + halfWidth);
                Gizmos.DrawLine(pourPoint - halfWidth, raycastEnd - halfWidth);
                Gizmos.DrawLine(pourPoint + halfWidth, raycastEnd + halfWidth);
                
                Gizmos.color = isPouringActive ? Color.green : Color.cyan;
                Gizmos.DrawWireSphere(raycastEnd, 0.2f);
                #if UNITY_EDITOR
                UnityEditor.Handles.Label(raycastEnd + (Vector3)(Vector2.right * 0.3f), 
                    "BoxCast Detection\n(width: " + raycastWidth + ", dist: " + raycastDistance + ")");
                #endif
            }
        }
    }
    
    void OnDrawGizmos()
    {
        if (mainTransform == null && transform.Find(mainSpriteName) != null)
        {
            mainTransform = transform.Find(mainSpriteName);
        }
        Vector3 cornerPos = Application.isPlaying ? tearCornerWorldPos : transform.position + (Vector3)tearCornerOffset;
        cornerPos.z = 0;
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireCube(cornerPos, tearCornerSize);
    }
}