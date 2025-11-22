using System;
using System.Collections.Generic;
using UnityEngine;

namespace JamesKJamKit.Services.Localization
{
    /// <summary>
    /// Provides localized strings based on the active locale and keeps UI updated when the locale
    /// changes.
    /// </summary>
    [DefaultExecutionOrder(-9996)]
    public sealed class LocalizationService : MonoBehaviour
    {
        [SerializeField]
        private string defaultLocale = "en";

        [SerializeField]
        private List<LocalizationTable> tables = new();

        public static LocalizationService Instance { get; private set; }

        public event Action<string> OnLocaleChanged;

        public string CurrentLocale { get; private set; }

        private readonly Dictionary<string, LocalizationTable> _lookup =
            new(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BuildLookup();
            ApplySavedLocale();
        }

        private void OnEnable()
        {
            if (SaveSettings.Instance != null)
            {
                SaveSettings.Instance.OnSettingsChanged += HandleSettingsChanged;
            }
        }

        private void OnValidate()
        {
            BuildLookup();
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(CurrentLocale))
            {
                SetLocaleInternal(defaultLocale, false);
            }
        }

        private void OnDisable()
        {
            if (SaveSettings.Instance != null)
            {
                SaveSettings.Instance.OnSettingsChanged -= HandleSettingsChanged;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetLocale(string localeCode)
        {
            SetLocaleInternal(localeCode, true);
        }

        public string Get(string key)
        {
            if (!_lookup.TryGetValue(CurrentLocale, out var table))
            {
                if (!_lookup.TryGetValue(defaultLocale, out table))
                {
                    return key;
                }
            }

            return table.Get(key);
        }

        public IReadOnlyList<string> GetAvailableLocales()
        {
            var locales = new List<string>(_lookup.Keys);
            if (locales.Count == 0 && !string.IsNullOrEmpty(defaultLocale))
            {
                locales.Add(defaultLocale);
            }

            return locales;
        }

        private void BuildLookup()
        {
            _lookup.Clear();
            foreach (var table in tables)
            {
                if (table == null)
                {
                    continue;
                }

                _lookup[table.LocaleCode] = table;
            }
        }

        private void ApplySavedLocale()
        {
            var settings = SaveSettings.Instance;
            if (settings != null && !string.IsNullOrEmpty(settings.Data.localeCode))
            {
                SetLocaleInternal(settings.Data.localeCode, false);
            }
        }

        private void HandleSettingsChanged(SettingKind kind)
        {
            if (kind == SettingKind.Locale)
            {
                ApplySavedLocale();
            }
        }

        private void SetLocaleInternal(string localeCode, bool persist)
        {
            if (string.IsNullOrEmpty(localeCode))
            {
                localeCode = defaultLocale;
            }

            LocalizationTable table = null;
            if (!_lookup.TryGetValue(localeCode, out table))
            {
                if (!_lookup.TryGetValue(defaultLocale, out table))
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"Localization table for locale '{localeCode}' not found.");
#endif
                }
            }

            var normalized = table != null ? table.LocaleCode : localeCode;
            if (string.Equals(CurrentLocale, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CurrentLocale = normalized;
            if (persist && SaveSettings.Instance != null)
            {
                SaveSettings.Instance.SetLocale(CurrentLocale);
            }

            OnLocaleChanged?.Invoke(CurrentLocale);
        }
    }
}
