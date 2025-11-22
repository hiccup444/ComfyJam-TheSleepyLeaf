using UnityEngine;

public sealed class ClickToFillRelay : MonoBehaviour
{
    [SerializeField] private DispenserController controller;
    [SerializeField] private MonoBehaviour audioBridgeSource;
    [SerializeField] private bool isHotButton;

    IDispenserAudio audioBridge;

    void Reset()
    {
        if (!controller) controller = FindFirstObjectByType<DispenserController>();
        CacheAudioBridge(autoAssign: true);
    }

    void Awake() => CacheAudioBridge();

    void OnValidate() => CacheAudioBridge();

    void OnMouseDown()
    {
        var temp = isHotButton ? WaterTemp.Hot : WaterTemp.Cold;
        audioBridge?.HandleButtonPressed(temp);

        if (!controller) return;
        if (controller.IsBusy) return;

        if (isHotButton) controller.TryFillHot();
        else controller.TryFillCold();
    }

    void CacheAudioBridge(bool autoAssign = false)
    {
        if (autoAssign && audioBridgeSource == null)
        {
            audioBridgeSource = controller ? FindBridge(controller) : null;
            if (audioBridgeSource == null)
            {
                audioBridgeSource = FindBridgeInParents();
            }
        }

        audioBridge = audioBridgeSource as IDispenserAudio;
    }

    MonoBehaviour FindBridge(Component component)
    {
        if (component == null) return null;
        foreach (var behaviour in component.GetComponents<MonoBehaviour>())
        {
            if (behaviour is IDispenserAudio) return behaviour;
        }
        return null;
    }

    MonoBehaviour FindBridgeInParents()
    {
        foreach (var behaviour in GetComponentsInParent<MonoBehaviour>(true))
        {
            if (behaviour is IDispenserAudio) return behaviour;
        }
        return null;
    }
}
