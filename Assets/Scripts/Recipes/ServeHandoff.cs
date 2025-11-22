using UnityEngine;

public sealed class ServeHandoff : MonoBehaviour
{
    public void OnCupDropped(GameObject cup)
    {
        ServeCoordinator.Instance?.TryServe(cup);
    }

    public void OnCupDropped(GameObject cup, Customer forcedCustomer)
    {
        ServeCoordinator.Instance?.TryServe(cup, forcedCustomer);
    }
}
