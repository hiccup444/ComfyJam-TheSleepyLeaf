using UnityEngine;
using TMPro;
using System.Collections;

public class UIClock : MonoBehaviour
{
    [SerializeField] Transform clockFace;

    [Header("Rotation Settings")]
    [SerializeField] float startRotation = 9f;
    [SerializeField] float endRotation = -177f;

    [Header("Money UI")]
    [SerializeField] TextMeshProUGUI moneyText1;
    [SerializeField] TextMeshProUGUI moneyText2;
    [SerializeField] float moneyLerpDuration = 0.5f; // how fast to count up

    private Coroutine moneyRoutine;
    private float displayedMoney = 0f;

    void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayTimeChanged += UpdateClockRotation;
            GameManager.Instance.OnMoneyChanged += AnimateMoneyTo;
            
            // initialize values
            UpdateClockRotation(GameManager.Instance.GetNormalizedDayTime());
            displayedMoney = GameManager.Instance.GetMoney();
            SetMoneyText(displayedMoney);
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError("UIClock: GameManager.Instance is null!");
#endif
        }
    }

    void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayTimeChanged -= UpdateClockRotation;
            GameManager.Instance.OnMoneyChanged -= AnimateMoneyTo;
        }
    }

    void UpdateClockRotation(float normalizedTime)
    {
        if (clockFace == null) return;

        float currentZRotation = Mathf.Lerp(startRotation, endRotation, normalizedTime);
        clockFace.localRotation = Quaternion.Euler(0f, 0f, currentZRotation);
    }

    // Called when money changes in GameManager
    void AnimateMoneyTo(float newMoneyValue)
    {
        if (moneyRoutine != null)
            StopCoroutine(moneyRoutine);

        moneyRoutine = StartCoroutine(AnimateMoney(displayedMoney, newMoneyValue));
    }

    IEnumerator AnimateMoney(float startValue, float endValue)
    {
        float elapsed = 0f;
        displayedMoney = startValue;

        while (elapsed < moneyLerpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moneyLerpDuration);
            displayedMoney = Mathf.Lerp(startValue, endValue, t);

            SetMoneyText(displayedMoney);
            yield return null;
        }

        // Snap exactly to end value at finish
        displayedMoney = endValue;
        SetMoneyText(displayedMoney);
        moneyRoutine = null;
    }

    // Format as $1,234,567 (no decimals). Use "C2" for cents.
    void SetMoneyText(float value)
    {
        string formatted = value.ToString("C0");

        if (moneyText1 != null)
            moneyText1.text = formatted;

        if (moneyText2 != null)
            moneyText2.text = formatted;
    }
}
