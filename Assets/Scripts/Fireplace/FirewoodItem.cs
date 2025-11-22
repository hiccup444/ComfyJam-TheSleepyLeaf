using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tag component for draggable firewood.
/// Attach to the wood piece prefab that has a Collider2D (and optionally Rigidbody2D).
/// </summary>
[DisallowMultipleComponent]
public sealed class FirewoodItem : MonoBehaviour
{
    [Tooltip("If true, destroy this GameObject when consumed by the fire.")]
    public bool destroyOnConsume = true;

    [Tooltip("Optional visual/audio feedback hook when consumed.")]
    public GameObject consumeEffectPrefab;

    [Tooltip("Disable colliders immediately after the fire consumes this log.")]
    public bool disableCollidersOnConsume = true;

    [Tooltip("How long to fade the sprite renderers before destroying/disabling.")]
    public float fadeDuration = 0.5f;

    [Header("Hooks")]
    [Tooltip("Called right before this wood is destroyed/disabled.")]
    public UnityEvent onConsumed;

    private bool _consumed;
    private Coroutine _fadeRoutine;

    /// <summary>
    /// Called by FireZone when the wood is accepted by the fire.
    /// </summary>
    public void Consume()
    {
        if (_consumed)
            return;

        _consumed = true;

        if (consumeEffectPrefab)
        {
            Instantiate(consumeEffectPrefab, transform.position, Quaternion.identity);
        }

        if (disableCollidersOnConsume)
        {
            SetCollidersEnabled(false);
        }

        onConsumed?.Invoke();

        if (_fadeRoutine != null)
            StopCoroutine(_fadeRoutine);

        _fadeRoutine = StartCoroutine(FadeThenDestroy());
    }

    private void SetCollidersEnabled(bool enabled)
    {
        foreach (var col in GetComponentsInChildren<Collider2D>(true))
        {
            col.enabled = enabled;
        }
    }

    private IEnumerator FadeThenDestroy()
    {
        var renderers = GetComponentsInChildren<SpriteRenderer>(true);
        var originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            originalColors[i] = renderers[i].color;
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = fadeDuration > 0f ? Mathf.Clamp01(elapsed / fadeDuration) : 1f;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                var c = originalColors[i];
                renderers[i].color = new Color(c.r, c.g, c.b, Mathf.Lerp(c.a, 0f, t));
            }

            yield return null;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                var c = originalColors[i];
                renderers[i].color = new Color(c.r, c.g, c.b, 0f);
            }
        }

        if (destroyOnConsume)
        {
            Destroy(gameObject);
        }
        else
        {
            // Fallback: disable visuals/collider so it can't be reused
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr) sr.enabled = false;
            var col = GetComponentInChildren<Collider2D>();
            if (col) col.enabled = false;
            gameObject.SetActive(false);
        }
    }
}
