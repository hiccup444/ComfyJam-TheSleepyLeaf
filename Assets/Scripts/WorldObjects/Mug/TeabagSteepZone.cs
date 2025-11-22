using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class TeabagSteepZone : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Beverage state belonging to this mug.")]
    [SerializeField] private MugBeverageState beverageState;

    [Tooltip("Collider used to detect teabag dips.")]
    [SerializeField] private Collider2D zoneCollider;

    readonly HashSet<Teabag> _immersedTeabags = new HashSet<Teabag>();

    void Awake()
    {
        ResolveDependencies();
    }

    void Reset()
    {
        ResolveDependencies();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ResolveDependencies();
    }
#endif

    void OnTriggerEnter2D(Collider2D other)
    {
        #if UNITY_EDITOR
        Debug.Log($"[Zone] Enter {other.name}");
        #endif

        if (!TryGetTeabag(other, out var teabag))
            return;

        if (!_immersedTeabags.Add(teabag))
            return;

        if (beverageState == null || !beverageState.HasWater)
            return;

        EnsureTeaType(teabag);
        beverageState.RegisterSteep();
        
        // Notify the teabag to darken its visual
        teabag.RegisterDip();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!TryGetTeabag(other, out var teabag))
            return;

        _immersedTeabags.Remove(teabag);
    }

    bool TryGetTeabag(Collider2D collider, out Teabag teabag)
    {
        teabag = null;
        if (collider == null)
            return false;

        teabag = collider.GetComponent<Teabag>();
        if (teabag == null)
            teabag = collider.GetComponentInParent<Teabag>();
        if (teabag == null)
            return false;

        var body = teabag.TeabagBodyTransform;
        if (body != null && collider.transform != body)
            return false;

        return true;
    }

    void EnsureTeaType(Teabag teabag)
    {
        if (beverageState == null || beverageState.HasTea)
            return;

        if (teabag != null && teabag.TryGetTeaDetails(out var type, out var color, out var requiresMilk))
            beverageState.SetTeaType(type, color, requiresMilk);
    }

    void ResolveDependencies()
    {
        if (beverageState == null)
            beverageState = GetComponentInParent<MugBeverageState>();

        if (zoneCollider == null)
            zoneCollider = GetComponent<Collider2D>();

        if (zoneCollider != null)
            zoneCollider.isTrigger = true;
    }
}