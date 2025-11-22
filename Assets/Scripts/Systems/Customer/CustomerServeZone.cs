using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Detects mugs entering the ServeZone trigger and notifies the active ServeHandoff.
/// </summary>
[RequireComponent(typeof(Collider2D), typeof(Rigidbody2D))]
public sealed class CustomerServeZone : MonoBehaviour
{
    [SerializeField] private Customer owner;

    private readonly HashSet<int> cupsInside = new HashSet<int>();
    private static readonly HashSet<int> globallyRegisteredCups = new HashSet<int>();
    private bool hasLoggedMissingHandoff;
    private Rigidbody2D zoneRigidbody;

    private void Reset()
    {
        ConfigureCollider();
        ConfigureRigidbody();
    }

    private void Awake()
    {
        if (owner == null)
            owner = GetComponentInParent<Customer>();

        ConfigureCollider();
        ConfigureRigidbody();
    }

    private void ConfigureCollider()
    {
        var collider = GetComponent<Collider2D>();
        if (collider != null && !collider.isTrigger)
            collider.isTrigger = true;
    }

    private void ConfigureRigidbody()
    {
        if (zoneRigidbody == null)
            zoneRigidbody = GetComponent<Rigidbody2D>();

        if (zoneRigidbody == null)
            return;

        zoneRigidbody.bodyType = RigidbodyType2D.Kinematic;
        zoneRigidbody.simulated = true;
        zoneRigidbody.gravityScale = 0f;
        zoneRigidbody.useFullKinematicContacts = false;
        zoneRigidbody.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var cup = ResolveCup(other);
        if (cup == null)
            return;

        int cupId = cup.GetInstanceID();
        if (!cupsInside.Add(cupId))
            return;

        lock (globallyRegisteredCups)
        {
            globallyRegisteredCups.Add(cupId);
        }

        TryServeCup(cup, cupId);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        var cup = ResolveCup(other);
        if (cup == null)
            return;

        int cupId = cup.GetInstanceID();
        if (!cupsInside.Contains(cupId))
            return;

        TryServeCup(cup, cupId);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var cup = ResolveCup(other);
        if (cup == null)
            return;

        int cupId = cup.GetInstanceID();
        cupsInside.Remove(cupId);

        lock (globallyRegisteredCups)
        {
            globallyRegisteredCups.Remove(cupId);
        }
    }

    private GameObject ResolveCup(Collider2D collider)
    {
        if (collider == null)
            return null;

        var snapper = collider.GetComponentInParent<CupSnapper>();
        if (snapper != null)
            return snapper.gameObject;

        var state = collider.GetComponentInParent<CupState>();
        if (state != null)
            return state.gameObject;

        if (collider.CompareTag("Cup"))
            return collider.gameObject;

        var parent = collider.transform.parent;
        while (parent != null)
        {
            if (parent.CompareTag("Cup"))
                return parent.gameObject;

            parent = parent.parent;
        }

        return null;
    }

    private bool IsOwnerReadyToServe()
    {
        if (owner == null)
            return false;

        return owner.IsAtCounter() && owner.CanOrder() && !owner.hasReceivedOrder;
    }

    private bool IsCupDragging(GameObject cup)
    {
        var drag = cup.GetComponentInChildren<DragItem2D>();
        return drag != null && drag.dragging;
    }

    private void TryServeCup(GameObject cup, int cupId)
    {
        if (cup == null)
            return;

        if (IsCupDragging(cup))
            return;

        if (!IsOwnerReadyToServe())
        {
            cupsInside.Remove(cupId);
            return;
        }

        var coordinator = ServeCoordinator.Instance;
        if (coordinator == null)
        {
            if (!hasLoggedMissingHandoff)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[ServeZone] No ServeCoordinator instance found.", this);
#endif
                hasLoggedMissingHandoff = true;
            }

            cupsInside.Remove(cupId);
            return;
        }

        cupsInside.Remove(cupId);
        lock (globallyRegisteredCups)
        {
            globallyRegisteredCups.Remove(cupId);
        }
        coordinator.TryServe(cup, owner);
    }

    public static bool IsCupRegistered(int cupId)
    {
        lock (globallyRegisteredCups)
        {
            return globallyRegisteredCups.Contains(cupId);
        }
    }
}
