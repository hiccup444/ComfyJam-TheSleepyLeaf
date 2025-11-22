using UnityEngine;
using System.Collections;

public class Teabag : MonoBehaviour
{
    public Transform stringTop;
    public Transform teabagBody;
    public LineRenderer stringLine;

    [Header("String Physics")]
    public int stringSegments = 15;
    public float stringLength = 2f;
    public float gravity = 9.81f;

    private bool gravityEnabled = true;
    private int skipPhysicsFrames = 0;
    private TeaPacket packetContainer = null;

    [SerializeField] public TeaDefinition teaDefinition;

    [Header("Attachment Points")]
    public Vector2 topAttachmentOffset = new Vector2(0, -0.5f);
    public Vector2 bagAttachmentOffset = new Vector2(0, 0.5f);

    private bool initializedFromPacket = false;
    private bool readyToSimulate = false;
    private bool isDragging = false;
    private bool isDraggingTop = false;
    private bool hasBeenGrabbedOnce = false;

    private Vector3 lastStringTopPosition;
    private Rigidbody2D teabagRb;
    private Rigidbody2D topRb;

    private Vector3[] stringPoints;
    private Vector3[] oldStringPoints;
    private float segmentLength;

    [Header("Dip Visual Feedback")]
    [SerializeField] private int currentDips = 0;
    [SerializeField] private int maxDips = 3;
    [SerializeField] private Color[] dipTints = new Color[]
    {
        Color.white,                         // 0 dips - original color
        new Color(0.85f, 0.85f, 0.75f, 1f), // 1 dip - slight darkening
        new Color(0.7f, 0.65f, 0.5f, 1f),   // 2 dips - medium darkening
        new Color(0.55f, 0.5f, 0.35f, 1f)   // 3 dips - dark brown/used
    };

    private SpriteRenderer teabagSpriteRenderer;

    [Header("String Rendering")]
    [SerializeField] private string stringSortingLayer = "Default";
    [SerializeField] private int stringSortingOrder = 5;
    [SerializeField, Min(0f)] private float stringWidth = 0.08f;
    [Tooltip("Optional. If null, a Sprites/Default material will be created at runtime.")]
    [SerializeField] private Material stringMaterial;

    void Start()
    {
        SetupPhysics();

        // Get the sprite renderer from the teabag body
        if (teabagBody != null)
        {
            teabagSpriteRenderer = teabagBody.GetComponent<SpriteRenderer>();
        }

        // Always initialize the arrays
        stringPoints = new Vector3[stringSegments];
        oldStringPoints = new Vector3[stringSegments];

        // Apply initial tint (should be white/no tint)
        UpdateTeabagTint();
    }

    void SetupPhysics()
    {
        topRb = stringTop.GetComponent<Rigidbody2D>();
        if (topRb == null) topRb = stringTop.gameObject.AddComponent<Rigidbody2D>();
        topRb.bodyType = RigidbodyType2D.Kinematic;

        if (stringTop.GetComponent<Collider2D>() == null)
        {
            _ = stringTop.gameObject.AddComponent<BoxCollider2D>();
        }

        teabagRb = teabagBody.GetComponent<Rigidbody2D>();
        if (teabagRb == null) teabagRb = teabagBody.gameObject.AddComponent<Rigidbody2D>();
        teabagRb.mass = 0.5f;
        teabagRb.linearDamping = 1f;
        teabagRb.bodyType = RigidbodyType2D.Kinematic;

        if (teabagBody.GetComponent<Collider2D>() == null)
        {
            _ = teabagBody.gameObject.AddComponent<PolygonCollider2D>();
        }
    }

    public Transform TeabagBodyTransform => teabagBody;

    public bool TryGetTeaDetails(out TeaType type, out Color targetColor, out bool milkRequired)
    {
        if (teaDefinition == null)
        {
            type = TeaType.None;
            targetColor = Color.clear;
            milkRequired = false;
            return false;
        }
        type = teaDefinition.teaType;
        targetColor = teaDefinition.targetColor;
        milkRequired = teaDefinition.milkRequired;
        return true;
    }

    /// <summary>Called when the teabag completes a dip in the liquid. Darkens the teabag sprite.</summary>
    public void RegisterDip()
    {
        if (currentDips >= maxDips) return;
        currentDips++;
        UpdateTeabagTint();
    }

    /// <summary>Updates the teabag sprite color based on number of dips.</summary>
    private void UpdateTeabagTint()
    {
        if (teabagSpriteRenderer == null) return;
        int tintIndex = Mathf.Clamp(currentDips, 0, dipTints.Length - 1);
        teabagSpriteRenderer.color = dipTints[tintIndex];
    }

    public void SetGravityEnabled(bool enabled) => gravityEnabled = enabled;
    public void SetPacketContainer(TeaPacket packet) => packetContainer = packet;
    public void SetTeaDefinition(TeaDefinition definition) => teaDefinition = definition;

    public void InitializeInPacket(Vector3 topPosition, Vector3 bagPosition)
    {
        initializedFromPacket = true;

        // Force Z to 0
        topPosition.z = 0;
        bagPosition.z = 0;

        // Make sure arrays exist
        if (stringPoints == null || stringPoints.Length != stringSegments)
        {
            stringPoints = new Vector3[stringSegments];
            oldStringPoints = new Vector3[stringSegments];
        }

        // Just in case transforms haven't updated yet
        Vector3 actualTopPos = stringTop.position;
        Vector3 actualBagPos = teabagBody.position;
        actualTopPos.z = 0;
        actualBagPos.z = 0;

        Vector3 topAttach = actualTopPos + (Vector3)topAttachmentOffset;
        topAttach.z = 0;
        Vector3 bagAttach = actualBagPos + (Vector3)bagAttachmentOffset;
        bagAttach.z = 0;

        float actualDistance = Vector3.Distance(topAttach, bagAttach);
        segmentLength = stringLength / (stringSegments - 1);

        for (int i = 0; i < stringSegments; i++)
        {
            float t = i / (float)(stringSegments - 1);
            Vector3 pos = Vector3.Lerp(topAttach, bagAttach, t);
            pos.z = 0;
            stringPoints[i] = pos;
            oldStringPoints[i] = pos;
        }

        // Setup line renderer here
        SetupLineRenderer();

        // Store initial stringTop position for movement detection
        lastStringTopPosition = stringTop.position;

        // Skip the next 2 physics frames to let everything settle
        skipPhysicsFrames = 2;
        readyToSimulate = true;
    }

    void CreateStringSegments()
    {
        segmentLength = stringLength / (stringSegments - 1);
        Vector3 startPos = stringTop.position + (Vector3)topAttachmentOffset;

        for (int i = 0; i < stringSegments; i++)
        {
            float t = i / (float)(stringSegments - 1);
            Vector3 newPos = startPos + Vector3.down * (stringLength * t);
            stringPoints[i] = newPos;
            oldStringPoints[i] = newPos;
        }
    }

    void SetupLineRenderer()
    {
        if (stringLine == null)
            stringLine = gameObject.AddComponent<LineRenderer>();

        stringLine.positionCount = stringSegments;
        stringLine.useWorldSpace = true;

        // width
        stringLine.startWidth = stringWidth;
        stringLine.endWidth = stringWidth;

        // material
        if (stringMaterial != null)
        {
            stringLine.material = stringMaterial;
        }
        else if (stringLine.material == null || stringLine.material.shader == null)
        {
            stringLine.material = CreateDefaultStringMaterial();
        }

        // ✅ Inspector-driven sorting
        stringLine.sortingLayerName = stringSortingLayer;
        stringLine.sortingOrder = stringSortingOrder;
    }

    private Material CreateDefaultStringMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Unlit/Color");

        if (shader == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("Unable to resolve a dedicated line shader, falling back to Standard.");
#endif
            shader = Shader.Find("Standard");
        }

        var lineMat = new Material(shader)
        {
            color = Color.white
        };

        if (lineMat.HasProperty("_BaseColor"))
        {
            lineMat.SetColor("_BaseColor", Color.white);
        }
        else if (lineMat.HasProperty("_Color"))
        {
            lineMat.SetColor("_Color", Color.white);
        }

        return lineMat;
    }

    void Update()
    {
        // Check if stringTop has moved 0.5 units from its initial position
        if (!hasBeenGrabbedOnce && initializedFromPacket)
        {
            float distanceFromStart = Vector3.Distance(stringTop.position, lastStringTopPosition);
            if (distanceFromStart >= 0.5f)
                hasBeenGrabbedOnce = true;
        }

        HandleDragging();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        TryApplyTea(other);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null) return;
        TryApplyTea(collision.collider);
    }

    void FixedUpdate()
    {
        if (skipPhysicsFrames > 0)
        {
            skipPhysicsFrames--;

            // During skip frames, force everything to stay in place
            if (stringPoints != null && stringPoints.Length > 0 && stringLine != null)
            {
                // Recalculate string positions from current sprite positions
                Vector3 topAttach = stringTop.position + (Vector3)topAttachmentOffset;
                Vector3 bagAttach = teabagBody.position + (Vector3)bagAttachmentOffset;

                for (int i = 0; i < stringSegments; i++)
                {
                    float t = i / (float)(stringSegments - 1);
                    Vector3 pos = Vector3.Lerp(topAttach, bagAttach, t);
                    pos.z = 0;
                    stringPoints[i] = pos;
                    oldStringPoints[i] = pos; // Keep them identical
                }

                // Update visual
                for (int i = 0; i < stringSegments; i++)
                {
                    stringLine.SetPosition(i, stringPoints[i]);
                }
            }
            return;
        }

        // Only simulate if we're ready
        if (readyToSimulate && stringPoints != null && stringPoints.Length > 0)
        {
            SimulateString();
        }
        else if (!initializedFromPacket && !readyToSimulate)
        {
            // First frame for non-packet teabags - initialize now
            CreateStringSegments();
            SetupLineRenderer();
            readyToSimulate = true;
        }
    }

    void SimulateString()
    {
        float deltaTime = Time.fixedDeltaTime;

        // Enable gravity once the stringTop has been moved
        if (packetContainer != null && !gravityEnabled && hasBeenGrabbedOnce)
        {
            gravityEnabled = true;
        }

        // Verlet integration for middle points AND the bag endpoint
        for (int i = 1; i < stringSegments; i++)
        {
            Vector3 velocity = stringPoints[i] - oldStringPoints[i];
            oldStringPoints[i] = stringPoints[i];

            // Only apply gravity if enabled
            if (gravityEnabled)
            {
                velocity += Vector3.down * gravity * deltaTime * deltaTime;
            }

            // Moderate damping on upward movement only
            if (velocity.y > 0)
                velocity.y *= 0.92f;

            velocity *= 0.98f;
            stringPoints[i] += velocity;
        }

        // Constraint iterations
        for (int iteration = 0; iteration < 8; iteration++)
        {
            stringPoints[0] = stringTop.position + (Vector3)topAttachmentOffset;

            for (int i = 0; i < stringSegments - 1; i++)
            {
                Vector3 direction = stringPoints[i + 1] - stringPoints[i];
                float distance = direction.magnitude;
                if (distance > 0.0001f)
                {
                    float difference = (segmentLength - distance) / distance;
                    Vector3 offset = direction * difference * 0.5f;

                    if (i == 0)
                    {
                        stringPoints[i + 1] += offset * 2f;
                    }
                    else
                    {
                        stringPoints[i] -= offset;
                        stringPoints[i + 1] += offset;
                    }
                }
            }
        }

        Vector3 targetBagPos = stringPoints[stringSegments - 1] - (Vector3)bagAttachmentOffset;

        // Apply extra downward force if bag is above stringTop (instead of hard clamping)
        Vector3 stringTopWorldPos = stringTop.position + (Vector3)topAttachmentOffset;
        if (targetBagPos.y > stringTopWorldPos.y)
        {
            float overshoot = targetBagPos.y - stringTopWorldPos.y;
            Vector3 correctionForce = Vector3.down * overshoot * 0.1f;
            stringPoints[stringSegments - 1] += correctionForce;
            targetBagPos = stringPoints[stringSegments - 1] - (Vector3)bagAttachmentOffset;
        }

        if (isDragging)
        {
            stringPoints[stringSegments - 1] = teabagBody.position + (Vector3)bagAttachmentOffset;
            oldStringPoints[stringSegments - 1] = stringPoints[stringSegments - 1];
        }
        else if (hasBeenGrabbedOnce)
        {
            // After first grab, always update position from string physics
            teabagBody.position = targetBagPos;
        }
        else
        {
            // Before first grab, keep teabag locked in place
            stringPoints[stringSegments - 1] = teabagBody.position + (Vector3)bagAttachmentOffset;
            oldStringPoints[stringSegments - 1] = stringPoints[stringSegments - 1];
        }

        // Update line renderer
        for (int i = 0; i < stringSegments; i++)
        {
            stringLine.SetPosition(i, stringPoints[i]);
        }
    }

    void TryApplyTea(Collider2D collider)
    {
        if (collider == null) return;

        var beverage = ResolveBeverageState(collider);
        if (beverage == null) return;
        if (beverage.HasTea) return;

        if (TryGetTeaDetails(out var type, out var targetColor, out var requiresMilk))
            beverage.SetTeaType(type, targetColor, requiresMilk);
    }

    MugBeverageState ResolveBeverageState(Collider2D collider)
    {
        if (collider == null) return null;

        if (collider.CompareTag("CupLiquid"))
            return GetBeverage(collider);

        if (collider.CompareTag("CupSurface"))
        {
            var cupLiquid = FindCupLiquidCollider(collider.transform);
            if (cupLiquid != null)
                return GetBeverage(cupLiquid);
            return GetBeverage(collider);
        }

        var fallback = FindCupLiquidCollider(collider.transform);
        if (fallback != null)
            return GetBeverage(fallback);

        return null;
    }

    static MugBeverageState GetBeverage(Component component)
    {
        if (component == null) return null;
        var beverage = component.GetComponentInParent<MugBeverageState>();
        if (beverage == null)
            beverage = component.GetComponent<MugBeverageState>();
        return beverage;
    }

    static Collider2D FindCupLiquidCollider(Transform origin)
    {
        Transform current = origin;
        while (current != null)
        {
            var colliders = current.GetComponentsInChildren<Collider2D>(true);
            foreach (var candidate in colliders)
            {
                if (candidate != null && candidate.CompareTag("CupLiquid"))
                    return candidate;
            }
            if (current.CompareTag("CupSurface"))
                break;
            current = current.parent;
        }
        return null;
    }

    void HandleDragging()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);
            if (hit.collider != null)
            {
                if (hit.collider.transform == stringTop)
                {
                    isDraggingTop = true;
                    hasBeenGrabbedOnce = true;
                }
                else if (hit.collider.transform == teabagBody)
                {
                    isDragging = true;
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            isDraggingTop = false;
        }

        if (isDraggingTop || isDragging)
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0;

            if (isDraggingTop)
            {
                stringTop.position = mousePos;
            }
            else if (isDragging)
            {
                teabagBody.position = mousePos;
            }
        }
    }
}
