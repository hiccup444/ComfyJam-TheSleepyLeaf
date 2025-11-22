using TMPro;
using UnityEngine;
using UnityEngine.UI;
using JamesKJamKit.Services.Localization;

namespace JamesKJamKit.UI
{
    /// <summary>
    /// Updates a text label whenever the active locale changes.
    /// </summary>
    [ExecuteAlways]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField]
        private string localizationKey;

        [SerializeField]
        private bool toUpper;

        private TMP_Text _tmpText;
        private Text _legacyText;

        private void Awake()
        {
            CacheComponents();
            Refresh();
        }

        private void OnEnable()
        {
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.OnLocaleChanged += HandleLocaleChanged;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.OnLocaleChanged -= HandleLocaleChanged;
            }
        }

        private void OnValidate()
        {
            CacheComponents();
            Refresh();
        }

        private void CacheComponents()
        {
            if (_tmpText == null)
            {
                _tmpText = GetComponent<TMP_Text>();
            }

            if (_legacyText == null)
            {
                _legacyText = GetComponent<Text>();
            }
        }

        private void HandleLocaleChanged(string locale)
        {
            Refresh();
        }

        public void Refresh()
        {
            var value = LocalizationService.Instance != null
                ? LocalizationService.Instance.Get(localizationKey)
                : localizationKey;

            if (toUpper)
            {
                value = value.ToUpperInvariant();
            }

            if (_tmpText != null)
            {
                _tmpText.text = value;
            }
            else if (_legacyText != null)
            {
                _legacyText.text = value;
            }
        }
    }
}
