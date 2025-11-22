using UnityEngine;

// TODO: Ensure Customer prefab has OrderIconSimple on the order bubble and iconRenderer assigned.
public sealed class OrderIconSimple : MonoBehaviour
{
    [SerializeField] private SpriteRenderer iconRenderer;
    [SerializeField] private bool hideWhenNull = true;

    public void SetRecipe(RecipeSO recipe)
    {
        if (iconRenderer == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[OrderIconSimple] No SpriteRenderer assigned.", this);
#endif
            return;
        }

        var sprite = recipe != null ? recipe.icon : null;
        iconRenderer.sprite = sprite;
        iconRenderer.enabled = !hideWhenNull || sprite != null;
        gameObject.SetActive(!hideWhenNull || sprite != null);
#if UNITY_EDITOR
        Debug.Log($"[OrderIcon] Set to {(sprite ? sprite.name : "NULL")} for recipe {(recipe ? recipe.displayName : "NULL")}", this);
#endif
    }

    public void Clear()
    {
        if (!iconRenderer)
        {
            return;
        }

        iconRenderer.sprite = null;
        iconRenderer.enabled = !hideWhenNull;
        gameObject.SetActive(!hideWhenNull);
#if UNITY_EDITOR
        Debug.Log("[OrderIcon] Cleared", this);
#endif
    }
}
