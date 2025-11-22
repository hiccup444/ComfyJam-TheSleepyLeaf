using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JamesKJamKit.Services.Audio;

public class TeaPacket : MonoBehaviour, IRespawnable
{
    [Header("Child Objects")]
    [Tooltip("The main sprite object (has collider and DragItem2D)")]
    [SerializeField] private string mainSpriteName = "teaPacketMain";

    [Tooltip("The tear corner object (has small collider for ripping)")]
    [SerializeField] private string tearCornerObjectName = "tearCorner";

    [Header("Sprites")]
    [SerializeField] private Sprite closedPacketSprite;
    [SerializeField] private Sprite rippedPacketSprite;

    [Header("Tear Settings")]
    [Tooltip("Size of the grabbable tear corner area (box collider)")]
    [SerializeField] private Vector2 tearCornerSize = new Vector2(0.3f, 0.3f);

    [Tooltip("Minimum distance and direction to drag before tearing")]
    [SerializeField] private Vector2 tearDirection = new Vector2(1f, 0f);
    
    [Tooltip("Offset for the tear corner grab point")]
    [SerializeField] private Vector2 tearCornerOffset = new Vector2(0.5f, 0.5f);

    [Header("Teabag")]
    [SerializeField] private GameObject teabag;
    [SerializeField] private Transform teabagStringTop;
    [Tooltip("Teabag prefab to instantiate if packet doesn't have one (fallback)")]
    [SerializeField] private GameObject teabagPrefab;

    [Header("Tea Definition")]
    [SerializeField] public TeaDefinition teaDefinition;

    [Header("Teabag Position")]
    [SerializeField] private Vector2 stringTopStartOffset = new Vector2(0, 0.3f);
    [SerializeField] private Vector2 teabagBodyStartOffset = new Vector2(0, -0.3f);
    [SerializeField] private float pullOutHeight = 0.5f;

    [Header("Fade & Respawn")]
    [Tooltip("Time to fade out after ripping (seconds)")]
    [SerializeField] private float fadeOutDuration = 1f;
    [Tooltip("Delay before starting fade (seconds)")]
    [SerializeField] private float fadeDelay = 0.5f;
    [Tooltip("If true, respawn a new packet at the original position")]
    [SerializeField] private bool respawnAfterDestroy = true;
    [Tooltip("Reference to the prefab to spawn (leave null to clone this object)")]
    [SerializeField] private GameObject teaPacketPrefab;

    [Header("Audio (Tear)")]
    [Tooltip("Played once when the packet is successfully ripped open.")]
    [SerializeField] private SFXEvent sfxTearOpen;
    [Tooltip("0 = fully 2D, 1 = fully 3D for tear SFX.")]
    [Range(0f, 1f)]
    [SerializeField] private float tearSpatialBlend = 0f;
    [Tooltip("Small randomization for natural variation")]
    [SerializeField] private Vector2 tearPitchJitter = new Vector2(0.98f, 1.02f);
    [SerializeField] private Vector2 tearVolumeJitter = new Vector2(0.97f, 1.00f);

    // Component references
    private SpriteRenderer mainRenderer;
    private Transform mainTransform;
    private Collider2D tearCornerCollider;
    private DragItem2D dragItem;
    private Teabag teabagScript;

    // State
    private bool isRipped = false;
    public bool IsRipped => isRipped;
    public event System.Action OnTearOpen;

    private bool isDraggingTear = false;
    private Vector3 tearCornerWorldPos;
    private Vector3 tearStartPos;
    private Vector3 originalLocalPosition;
    private Vector3 originalWorldPosition; // Fallback if parent is lost
    private Quaternion originalLocalRotation;
    private Vector3 originalLocalScale;
    private Transform originalParent;
    private string originalParentName; // Store name to help find it
    private GameObject cachedTeaPacketPrefab; // Store original prefab reference
    private AudioSource tearAudioSource;
    private static readonly Dictionary<TeaType, GameObject> TeabagTemplateCache = new();
    private TeaType TeabagTypeKey => teaDefinition != null ? teaDefinition.teaType : TeaType.None;

    // Lifecycle auditing
    private int respawnRetryCount = 0;
    private const int MAX_RESPAWN_RETRIES = 3;

    public void RespawnBeforeDestroy()
    {
        RespawnPacket();
    }

    void Awake()
    {
        // AudioSource for tear SFX
        tearAudioSource = GetComponent<AudioSource>();
        if (tearAudioSource == null) tearAudioSource = gameObject.AddComponent<AudioSource>();
        tearAudioSource.playOnAwake = false;
        tearAudioSource.loop = false;
        tearAudioSource.spatialBlend = tearSpatialBlend;

        // Store original local/world info
        originalLocalPosition = transform.localPosition;
        originalWorldPosition = transform.position; // Fallback
        originalLocalRotation = transform.localRotation;
        originalLocalScale = transform.localScale;
        originalParent = transform.parent;
        originalParentName = originalParent != null ? originalParent.name : "";

        // Cache prefab reference
        if (teaPacketPrefab != null && !teaPacketPrefab.name.Contains("Clone"))
            cachedTeaPacketPrefab = teaPacketPrefab;

        // Find main sprite child
        Transform mainSpriteTransform = transform.Find(mainSpriteName);
        if (mainSpriteTransform != null)
        {
            mainTransform = mainSpriteTransform;
            mainRenderer = mainSpriteTransform.GetComponent<SpriteRenderer>();
            dragItem = mainSpriteTransform.GetComponent<DragItem2D>();

            if (mainRenderer != null && closedPacketSprite != null)
                mainRenderer.sprite = closedPacketSprite;
            else if (mainRenderer == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"TeaPacket: '{mainSpriteName}' needs a SpriteRenderer component!");
#endif
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError($"TeaPacket: Could not find child '{mainSpriteName}'!");
#endif
        }

        // Find or create tear corner object (lives under mainTransform if present)
        Transform tearCornerTransform = transform.Find(tearCornerObjectName);
        if (tearCornerTransform == null && mainTransform != null)
            tearCornerTransform = mainTransform.Find(tearCornerObjectName);

        if (tearCornerTransform == null)
        {
            GameObject tearCornerObj = new GameObject(tearCornerObjectName);
            tearCornerTransform = tearCornerObj.transform;
            tearCornerTransform.SetParent(mainTransform != null ? mainTransform : transform);
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

        // Register tear corner collider with DragItem2D so it doesn't start dragging
        if (dragItem != null && tearCornerCollider != null)
        {
            if (dragItem.ignoreColliders == null || dragItem.ignoreColliders.Length == 0)
            {
                dragItem.ignoreColliders = new Collider2D[] { tearCornerCollider };
            }
            else
            {
                var ignoreList = new System.Collections.Generic.List<Collider2D>(dragItem.ignoreColliders);
                if (!ignoreList.Contains(tearCornerCollider))
                {
                    ignoreList.Add(tearCornerCollider);
                    dragItem.ignoreColliders = ignoreList.ToArray();
                }
            }
        }

        // Init tear-corner world pos
        UpdateTearCornerPosition();

        // -------- Robust teabag setup --------
        teabag = null;
        teabagStringTop = null;
        teabagScript = null;

        // Find nested/inactive teabag anywhere under this packet
        var foundBag = GetComponentInChildren<Teabag>(true);
        bool teabagSetup = false;

        if (foundBag != null && foundBag.gameObject != gameObject)
        {
            teabag = foundBag.gameObject;
            teabagScript = foundBag;
            teabag.SetActive(false);
            teabagStringTop = teabag.transform.Find("StringTop");
            if (teabagStringTop == null)
#if UNITY_EDITOR
                Debug.LogWarning($"TeaPacket: Could not find StringTop child in teabag {teabag.name}");
#endif
            if (teabagScript != null && teaDefinition != null)
                teabagScript.SetTeaDefinition(teaDefinition);
            CacheTeabagTemplate(teabag);
#if UNITY_EDITOR
            Debug.Log($"TeaPacket [{gameObject.GetInstanceID()}]: Found teabag '{teabag.name}' (ID: {teabag.GetInstanceID()})");
#endif
            teabagSetup = true;
        }
        else if (TryInstantiateCachedTeabag())
        {
            teabagSetup = true;
        }
        else if (teabagPrefab != null)
        {
#if UNITY_EDITOR
            Debug.Log($"TeaPacket [{gameObject.GetInstanceID()}]: No teabag child found, instantiating from prefab");
#endif
            teabagSetup = InstantiateTeabagFromPrefab(teabagPrefab);
        }

        if (!teabagSetup)
        {
#if UNITY_EDITOR
            Debug.LogError($"TeaPacket [{gameObject.GetInstanceID()}]: NO TEABAG FOUND and no teabagPrefab assigned!");
#endif
        }
        // ------------------------------------

        // LIFECYCLE AUDIT: Validate initialization and attempt self-heal if needed
        if (!ValidateInitialization())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[TeaPacket] Initialization validation failed for {gameObject.name}, attempting self-heal...");
#endif

            if (AttemptSelfHeal())
            {
                // Re-validate after healing
                if (ValidateInitialization())
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[TeaPacket] Self-heal successful for {gameObject.name}!");
#endif
                }
                else
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError($"[TeaPacket] Self-heal FAILED for {gameObject.name} - packet may not function correctly!");
#endif
                }
            }
            else
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[TeaPacket] Could not self-heal {gameObject.name} - packet may not function correctly!");
#endif
            }
        }
    }

    void Start()
    {
        if (originalParent != null)
            originalLocalPosition = transform.localPosition;

        originalWorldPosition = transform.position;

        if (mainRenderer != null)
        {
            Color c = mainRenderer.color; c.a = 1f;
            mainRenderer.color = c;
        }
    }

    private void CacheTeabagTemplate(GameObject bag)
    {
        TeaType key = TeabagTypeKey;
        if (bag == null || TeabagTemplateCache.ContainsKey(key)) return;

        GameObject template = Instantiate(bag);
        template.name = bag.name + "_Template";
        template.SetActive(false);
        template.hideFlags = HideFlags.HideAndDontSave;
        template.transform.SetParent(null);
        DontDestroyOnLoad(template);
        TeabagTemplateCache[key] = template;
    }

    private bool TryInstantiateCachedTeabag()
    {
        GameObject template = GetCachedTeabagTemplate();
        if (template == null) return false;
#if UNITY_EDITOR
        Debug.Log($"TeaPacket [{gameObject.GetInstanceID()}]: No teabag child found, using cached template '{template.name}'");
#endif
        return InstantiateTeabagFromPrefab(template);
    }

    private GameObject GetCachedTeabagTemplate()
    {
        TeabagTemplateCache.TryGetValue(TeabagTypeKey, out var template);
        return template;
    }

    // ==================== LIFECYCLE AUDITING ====================

    /// <summary>
    /// Logs detailed initialization state for debugging packet setup issues
    /// </summary>
    private void LogInitializationState(string context)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogError($"[TeaPacket] {context} - Initialization State Dump:");
        Debug.LogError($"  GameObject: {gameObject.name} (ID: {gameObject.GetInstanceID()})");
        Debug.LogError($"  mainTransform: {(mainTransform != null ? mainTransform.name : "NULL")}");
        Debug.LogError($"  mainRenderer: {(mainRenderer != null ? "OK" : "NULL")}");
        Debug.LogError($"  dragItem: {(dragItem != null ? "OK" : "NULL")}");
        Debug.LogError($"  tearCornerCollider: {(tearCornerCollider != null ? "OK" : "NULL")}");
        Debug.LogError($"  teabag: {(teabag != null ? $"{teabag.name} (ID: {teabag.GetInstanceID()})" : "NULL")}");

        if (teabag != null)
        {
            bool isSceneInstance = teabag.scene.name != null;
            Debug.LogError($"    └─ Is Scene Instance: {isSceneInstance} (scene: {teabag.scene.name ?? "PREFAB ASSET"})");
        }

        Debug.LogError($"  teabagStringTop: {(teabagStringTop != null ? "OK" : "NULL")}");
        Debug.LogError($"  teabagScript: {(teabagScript != null ? "OK" : "NULL")}");
        Debug.LogError($"  teaDefinition: {(teaDefinition != null ? teaDefinition.name : "NULL")}");
        Debug.LogError($"  teaPacketPrefab: {(teaPacketPrefab != null ? teaPacketPrefab.name : "NULL")}");
        Debug.LogError($"  cachedTeaPacketPrefab: {(cachedTeaPacketPrefab != null ? cachedTeaPacketPrefab.name : "NULL")}");

        Debug.LogError($"  Children ({transform.childCount}):");
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            Debug.LogError($"    [{i}] {child.name}");
        }

        Debug.LogError($"  Parent: {(transform.parent != null ? transform.parent.name : "NULL (root)")}");
#endif
    }

    /// <summary>
    /// Attempts to fix common initialization issues automatically
    /// </summary>
    private bool AttemptSelfHeal()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[TeaPacket] Attempting self-heal for {gameObject.name}...");
#endif
        bool healed = false;

        // Try to find missing mainTransform
        if (mainTransform == null)
        {
            Transform found = transform.Find(mainSpriteName);
            if (found != null)
            {
                mainTransform = found;
                mainRenderer = found.GetComponent<SpriteRenderer>();
                dragItem = found.GetComponent<DragItem2D>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[TeaPacket] HEALED: Found mainTransform '{mainSpriteName}'");
#endif
                healed = true;
            }
        }

        // Try to recreate tear corner if missing
        if (tearCornerCollider == null && mainTransform != null)
        {
            Transform tearCornerTransform = transform.Find(tearCornerObjectName);
            if (tearCornerTransform == null && mainTransform != null)
                tearCornerTransform = mainTransform.Find(tearCornerObjectName);

            if (tearCornerTransform == null)
            {
                GameObject tearCornerObj = new GameObject(tearCornerObjectName);
                tearCornerTransform = tearCornerObj.transform;
                tearCornerTransform.SetParent(mainTransform);
                tearCornerTransform.localPosition = tearCornerOffset;

                BoxCollider2D boxCol = tearCornerObj.AddComponent<BoxCollider2D>();
                boxCol.size = tearCornerSize;
                boxCol.isTrigger = false;
                tearCornerCollider = boxCol;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[TeaPacket] HEALED: Recreated tearCorner collider");
#endif
                healed = true;
            }
            else
            {
                tearCornerCollider = tearCornerTransform.GetComponent<Collider2D>();
                if (tearCornerCollider != null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[TeaPacket] HEALED: Found existing tearCorner collider");
#endif
                    healed = true;
                }
            }
        }

        // Try to fix missing teabag from cache or prefab
        if (teabag == null || teabagScript == null)
        {
            if (TryInstantiateCachedTeabag())
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[TeaPacket] HEALED: Instantiated teabag from cache");
#endif
                healed = true;
            }
            else if (teabagPrefab != null && InstantiateTeabagFromPrefab(teabagPrefab))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[TeaPacket] HEALED: Instantiated teabag from prefab");
#endif
                healed = true;
            }
        }

        // Re-register tear corner with dragItem if needed
        if (dragItem != null && tearCornerCollider != null)
        {
            bool needsRegistration = true;
            if (dragItem.ignoreColliders != null)
            {
                foreach (var col in dragItem.ignoreColliders)
                {
                    if (col == tearCornerCollider)
                    {
                        needsRegistration = false;
                        break;
                    }
                }
            }

            if (needsRegistration)
            {
                var ignoreList = new System.Collections.Generic.List<Collider2D>(dragItem.ignoreColliders ?? new Collider2D[0]);
                ignoreList.Add(tearCornerCollider);
                dragItem.ignoreColliders = ignoreList.ToArray();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[TeaPacket] HEALED: Registered tearCorner with DragItem2D");
#endif
                healed = true;
            }
        }

        return healed;
    }

    /// <summary>
    /// Validates that all critical components are properly initialized
    /// </summary>
    private bool ValidateInitialization()
    {
        bool valid = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        System.Text.StringBuilder errors = new System.Text.StringBuilder();
#endif

        if (mainTransform == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ mainTransform is null (missing '{mainSpriteName}' child)");
#endif
            valid = false;
        }

        if (mainRenderer == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ mainRenderer is null (missing SpriteRenderer on '{mainSpriteName}')");
#endif
            valid = false;
        }

        if (dragItem == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ dragItem is null (missing DragItem2D on '{mainSpriteName}')");
#endif
            valid = false;
        }

        if (tearCornerCollider == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ tearCornerCollider is null (missing tear corner setup)");
#endif
            valid = false;
        }

        if (teabag == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ teabag is null (missing teabag child or failed instantiation)");
#endif
            valid = false;
        }
        else if (teabag.scene.name == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ teabag points to PREFAB ASSET instead of scene instance");
#endif
            valid = false;
        }

        if (teabagStringTop == null && teabag != null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ teabagStringTop is null (missing 'StringTop' child in teabag)");
#endif
            valid = false;
        }

        if (teabagScript == null && teabag != null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ teabagScript is null (missing Teabag component)");
#endif
            valid = false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (teaDefinition == null)
        {
            errors.AppendLine($"  ⚠ teaDefinition is null (tea type won't be set - assign in inspector!)");
            // Not marking as invalid - packet will still work, just won't make tea
        }

        if (teaPacketPrefab == null && cachedTeaPacketPrefab == null)
        {
            errors.AppendLine($"  ⚠ teaPacketPrefab is null (cannot respawn after destroy)");
            // Not marking as invalid - only matters on respawn
        }
        else if (teaPacketPrefab != null && teaPacketPrefab.name.Contains("Clone"))
        {
            errors.AppendLine($"  ⚠ teaPacketPrefab points to instance '{teaPacketPrefab.name}' instead of prefab");
        }

        if (!valid)
        {
            Debug.LogError($"[TeaPacket] VALIDATION FAILED for {gameObject.name}:\n{errors}");
            LogInitializationState("Validation Failed");
        }
        else if (errors.Length > 0)
        {
            Debug.LogWarning($"[TeaPacket] Validation warnings for {gameObject.name}:\n{errors}");
        }
#endif

        return valid;
    }

    /// <summary>
    /// Validates a newly respawned packet and attempts retry if invalid
    /// </summary>
    private bool ValidateRespawnedPacket(GameObject newPacket)
    {
        if (newPacket == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError($"[TeaPacket] ValidateRespawnedPacket: newPacket is NULL!");
#endif
            return false;
        }

        TeaPacket newPacketScript = newPacket.GetComponent<TeaPacket>();
        if (newPacketScript == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError($"[TeaPacket] ValidateRespawnedPacket: newPacket missing TeaPacket component!");
#endif
            return false;
        }

        // Give the new packet a frame to run its Awake() before validation
        // (Note: This method is called synchronously, so Awake() has already run)

        bool valid = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        System.Text.StringBuilder errors = new System.Text.StringBuilder();
#endif

        if (newPacketScript.teabag == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ Respawned packet missing teabag reference");
#endif
            valid = false;
        }

        if (newPacketScript.teaDefinition == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ Respawned packet missing teaDefinition");
#endif
            valid = false;
        }

        if (newPacketScript.mainTransform == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ Respawned packet missing mainTransform");
#endif
            valid = false;
        }

        if (newPacketScript.tearCornerCollider == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            errors.AppendLine($"  ✗ Respawned packet missing tearCornerCollider");
#endif
            valid = false;
        }

        if (!valid)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError($"[TeaPacket] Respawned packet validation FAILED:\n{errors}");
#endif

            // Attempt self-heal before giving up
            if (newPacketScript.AttemptSelfHeal())
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[TeaPacket] Self-heal successful, re-validating...");
#endif

                // Re-check after healing
                if (newPacketScript.teabag != null &&
                    newPacketScript.mainTransform != null &&
                    newPacketScript.tearCornerCollider != null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[TeaPacket] Respawned packet healed successfully!");
#endif
                    return true;
                }
            }

            return false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[TeaPacket] Respawned packet '{newPacket.name}' validated successfully");
#endif
        return true;
    }

    private bool InstantiateTeabagFromPrefab(GameObject prefabSource)
    {
        if (prefabSource == null) return false;

        GameObject newTeabag = Instantiate(prefabSource, transform);
        newTeabag.name = prefabSource.name;
        teabag = newTeabag;
        teabagScript = newTeabag.GetComponent<Teabag>();
        teabagStringTop = teabag.transform.Find("StringTop");
        teabag.SetActive(false);

        if (teabagScript != null && teaDefinition != null)
            teabagScript.SetTeaDefinition(teaDefinition);

        if (teabagPrefab == null)
            teabagPrefab = prefabSource;

#if UNITY_EDITOR
        Debug.Log($"TeaPacket: Successfully instantiated teabag '{teabag.name}' (ID: {teabag.GetInstanceID()})");
#endif
        return teabag != null && teabagScript != null;
    }

    void UpdateTearCornerPosition()
    {
        if (tearCornerCollider != null)
            tearCornerWorldPos = tearCornerCollider.transform.position;
        else
            tearCornerWorldPos = transform.position + (Vector3)tearCornerOffset;

        tearCornerWorldPos.z = 0;
    }

    void LateUpdate()
    {
        if (!isRipped) UpdateTearCornerPosition();
    }

    void Update()
    {
        if (isRipped) return;

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0;
            Collider2D hit = Physics2D.OverlapPoint(mousePos);
            if (hit == tearCornerCollider)
            {
                isDraggingTear = true;
                tearStartPos = mousePos;
#if UNITY_EDITOR
                Debug.Log("Started dragging tear corner");
#endif
            }
        }

        if (isDraggingTear)
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0;
            Vector3 dragVector = mousePos - tearStartPos;
            float distance = Vector3.Dot(dragVector, tearDirection.normalized);
            if (distance >= tearDirection.magnitude)
                RipOpen();
        }

        if (Input.GetMouseButtonUp(0))
        {
#if UNITY_EDITOR
            if (isDraggingTear) Debug.Log("Released tear corner");
#endif
            isDraggingTear = false;
        }
    }

    void RipOpen()
    {
        isRipped = true;
        isDraggingTear = false;
#if UNITY_EDITOR
        Debug.Log("Tea packet ripped open!");
#endif

        // Play tear open SFX
        PlayTearOpenSFX();
        OnTearOpen?.Invoke();

        if (rippedPacketSprite != null && mainRenderer != null)
        {
            mainRenderer.sprite = rippedPacketSprite;
            mainRenderer.flipX = true;
        }

        // If we have a usable teabag setup, deploy it; otherwise skip gracefully
        if (teabag != null && teabagScript != null && teabagStringTop != null && mainTransform != null)
        {
            // Guard: ensure it's a scene instance, not a prefab asset
            if (teabag.scene.name == null)
            {
#if UNITY_EDITOR
                Debug.LogError("TeaPacket: Teabag reference points to a prefab asset, not a scene instance! Check your teabag assignment in the inspector.");
#endif
            }
            else
            {
                Vector3 topPos = mainTransform.position + (Vector3)stringTopStartOffset;
                topPos.z = 0;
                Vector3 bagPos = mainTransform.position + (Vector3)teabagBodyStartOffset;
                bagPos.z = 0;

                teabag.transform.SetParent(null);
                teabag.SetActive(true);

                teabagStringTop.position = topPos;
                teabagScript.teabagBody.position = bagPos;

                teabagScript.InitializeInPacket(topPos, bagPos);
                teabagScript.SetGravityEnabled(false);
                teabagScript.SetPacketContainer(this);

                if (teaDefinition != null)
                    teabagScript.SetTeaDefinition(teaDefinition);

#if UNITY_EDITOR
                Debug.Log("Teabag activated and unparented successfully");
#endif
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning("TeaPacket: Missing teabag references for ripping! Skipping teabag deploy.");
#endif
        }

        StartCoroutine(FadeOutAndDestroy());
    }

    void PlayTearOpenSFX()
    {
        if (sfxTearOpen == null || tearAudioSource == null) return;

        var clip = sfxTearOpen.GetRandomClip();
        if (clip == null) return;

        float originalPitch = tearAudioSource.pitch;

        float evtPitch = Random.Range(Mathf.Min(sfxTearOpen.pitchRange.x, sfxTearOpen.pitchRange.y), Mathf.Max(sfxTearOpen.pitchRange.x, sfxTearOpen.pitchRange.y));
        float jitterPitch = Random.Range(Mathf.Min(tearPitchJitter.x, tearPitchJitter.y), Mathf.Max(tearPitchJitter.x, tearPitchJitter.y));
        tearAudioSource.pitch = evtPitch * jitterPitch;

        float evtVolume = Mathf.Clamp01(sfxTearOpen.volume);
        float jitterVolume = Random.Range(Mathf.Min(tearVolumeJitter.x, tearVolumeJitter.y), Mathf.Max(tearVolumeJitter.x, tearVolumeJitter.y));
        float finalVolume = Mathf.Clamp01(evtVolume * Mathf.Clamp01(jitterVolume));

        tearAudioSource.spatialBlend = Mathf.Clamp01(sfxTearOpen.spatialBlend);
        tearAudioSource.rolloffMode = sfxTearOpen.rolloff;
        tearAudioSource.minDistance = Mathf.Max(0.01f, sfxTearOpen.minDistance);
        tearAudioSource.maxDistance = Mathf.Max(tearAudioSource.minDistance + 0.01f, sfxTearOpen.maxDistance);
        tearAudioSource.dopplerLevel = Mathf.Max(0f, sfxTearOpen.dopplerLevel);

        tearAudioSource.PlayOneShot(clip, finalVolume);
        tearAudioSource.pitch = originalPitch;
    }

    IEnumerator FadeOutAndDestroy()
    {
        yield return new WaitForSeconds(fadeDelay);

        SpriteRenderer[] packetSprites = mainTransform != null ?
            mainTransform.GetComponentsInChildren<SpriteRenderer>(true) :
            new SpriteRenderer[0];

        float elapsedTime = 0f;
        Color[] originalColors = new Color[packetSprites.Length];
        for (int i = 0; i < packetSprites.Length; i++)
            if (packetSprites[i] != null) originalColors[i] = packetSprites[i].color;

        while (elapsedTime < fadeOutDuration)
        {
            elapsedTime += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsedTime / fadeOutDuration);
            for (int i = 0; i < packetSprites.Length; i++)
            {
                if (packetSprites[i] != null)
                {
                    Color newColor = originalColors[i];
                    newColor.a = alpha;
                    packetSprites[i].color = newColor;
                }
            }
            yield return null;
        }

        if (respawnAfterDestroy) RespawnPacket();

        if (teabagScript != null)
            teabagScript.SetGravityEnabled(true);

        Destroy(gameObject);
    }

    void RespawnPacket()
    {
        GameObject prefabToUse = cachedTeaPacketPrefab != null ? cachedTeaPacketPrefab : teaPacketPrefab;
        if (prefabToUse == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("TeaPacket: No prefab reference set for respawning! Please assign teaPacketPrefab in the inspector.");
#endif
            return;
        }
        if (prefabToUse.name.Contains("Clone"))
        {
#if UNITY_EDITOR
            Debug.LogWarning($"TeaPacket: Prefab reference contains an instance ('{prefabToUse.name}'), not a prefab asset! This will cause issues.");
#endif
        }

        // Sanity: warn if prefab lacks a Teabag descendant and no teabagPrefab is set on it
        var hasBag = prefabToUse.GetComponentInChildren<Teabag>(true) != null;
        if (!hasBag && teabagPrefab == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"TeaPacket: Respawn prefab '{prefabToUse.name}' does not contain a Teabag. Also no teabagPrefab fallback set—packet will log an error on Awake.");
#endif
        }

#if UNITY_EDITOR
        Debug.Log($"Instantiating prefab: {prefabToUse.name} (Retry: {respawnRetryCount}/{MAX_RESPAWN_RETRIES})");
#endif

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
                Debug.Log($"TeaPacket: Found parent '{originalParentName}' in scene by name");
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
                Debug.Log($"TeaPacket: Using TeaHolder found in scene as parent");
#endif
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"TeaPacket: Could not find parent! Spawning at world position.");
#endif
            }
        }

        GameObject newPacket = Instantiate(prefabToUse);
        newPacket.name = prefabToUse.name;

        Transform newMainTransform = newPacket.transform.Find(mainSpriteName);
        if (newMainTransform != null)
        {
            newMainTransform.localPosition = Vector3.zero;
            newMainTransform.localRotation = Quaternion.identity;
            // no sorting or extra changes here
        }

        if (parentToUse != null)
        {
            newPacket.transform.SetParent(parentToUse);
            newPacket.transform.localPosition = originalLocalPosition;
            newPacket.transform.localRotation = originalLocalRotation;
            newPacket.transform.localScale = originalLocalScale;
#if UNITY_EDITOR
            Debug.Log($"Respawned tea packet in '{parentToUse.name}' at local pos {originalLocalPosition} with scale {originalLocalScale}");
#endif
        }
        else
        {
            newPacket.transform.position = originalWorldPosition;
            newPacket.transform.rotation = originalLocalRotation;
            newPacket.transform.localScale = originalLocalScale;
#if UNITY_EDITOR
            Debug.Log($"Respawned tea packet at world pos {originalWorldPosition}");
#endif
        }

        // LIFECYCLE AUDIT: Validate respawned packet
        if (!ValidateRespawnedPacket(newPacket))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError($"[TeaPacket] Respawned packet validation FAILED!");
#endif

            // Attempt retry if we haven't exceeded max retries
            if (respawnRetryCount < MAX_RESPAWN_RETRIES)
            {
                respawnRetryCount++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"[TeaPacket] Destroying failed packet and retrying respawn (Attempt {respawnRetryCount}/{MAX_RESPAWN_RETRIES})...");
#endif

                Destroy(newPacket);

                // Try alternate prefab source on retry
                if (respawnRetryCount == 2 && cachedTeaPacketPrefab != null && teaPacketPrefab != null)
                {
                    // Switch to the other prefab source
                    GameObject temp = cachedTeaPacketPrefab;
                    cachedTeaPacketPrefab = teaPacketPrefab;
                    teaPacketPrefab = temp;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[TeaPacket] Swapping prefab sources for retry");
#endif
                }

                RespawnPacket(); // Recursive retry
            }
            else
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[TeaPacket] Max respawn retries ({MAX_RESPAWN_RETRIES}) exceeded! Packet '{newPacket.name}' may not function correctly.");
                LogInitializationState("Max Respawn Retries Exceeded");
#endif
            }
        }
        else
        {
            // Validation successful - reset retry counter for next time
            respawnRetryCount = 0;
        }
    }

    public bool IsBagPulledOut(Vector3 bagPosition)
    {
        if (mainTransform == null) return false;
        float bagHeight = bagPosition.y;
        float packetOpeningHeight = mainTransform.position.y + stringTopStartOffset.y;
        return bagHeight > packetOpeningHeight + pullOutHeight;
    }

    void OnDrawGizmosSelected()
    {
        if (mainTransform == null && transform.Find(mainSpriteName) != null)
            mainTransform = transform.Find(mainSpriteName);

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

        if (!Application.isPlaying)
        {
            Vector3 basePos = mainTransform != null ? mainTransform.position : transform.position;
            Gizmos.color = Color.green;
            Vector3 stringPos = basePos + (Vector3)stringTopStartOffset;
            Gizmos.DrawWireSphere(stringPos, 0.2f);
            Gizmos.color = Color.blue;
            Vector3 bagBodyPos = basePos + (Vector3)teabagBodyStartOffset;
            Gizmos.DrawWireSphere(bagBodyPos, 0.3f);
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(stringPos + Vector3.up * 0.3f, "String Top");
            UnityEditor.Handles.Label(bagBodyPos + Vector3.down * 0.4f, "Teabag Body");
            #endif
        }
    }

    void OnDrawGizmos()
    {
        if (mainTransform == null && transform.Find(mainSpriteName) != null)
            mainTransform = transform.Find(mainSpriteName);

        Vector3 cornerPos = Application.isPlaying ? tearCornerWorldPos : transform.position + (Vector3)tearCornerOffset;
        cornerPos.z = 0;
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireCube(cornerPos, tearCornerSize);
    }
}
