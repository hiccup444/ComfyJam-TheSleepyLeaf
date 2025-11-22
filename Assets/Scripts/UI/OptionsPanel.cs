using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using JamesKJamKit.Services;
using JamesKJamKit.Services.Localization;

namespace JamesKJamKit.UI
{
    /// <summary>
    /// Reusable options panel shared by the main menu and pause menu.
    /// </summary>
    public sealed class OptionsPanel : MonoBehaviour
    {
        private const float MinVolumeDb = -80f;
        private const float MaxVolumeDb = 0f;
        private const float SliderCurve = 1f / 3f;

        [Header("Audio")]
        [SerializeField]
        private Slider masterSlider;

        [SerializeField]
        private Slider musicSlider;

        [SerializeField]
        private Slider sfxSlider;

        [Header("Localization")]
        [SerializeField]
        private TMP_Dropdown languageDropdown;

        [SerializeField]
        private GameObject root;

        [SerializeField]
        private MainMenuScreen mainMenuScreen;

        private readonly List<string> _locales = new();
        private bool _isPopulated;

        private void OnEnable()
        {
            if (SaveSettings.Instance != null)
            {
                SaveSettings.Instance.OnSettingsChanged += HandleSettingsChanged;
            }

            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.OnLocaleChanged += HandleLocaleChanged;
            }

            RegisterUiCallbacks();
            PopulateLocales();
            SyncFromSettings();
        }

        private void OnDisable()
        {
            if (SaveSettings.Instance != null)
            {
                SaveSettings.Instance.OnSettingsChanged -= HandleSettingsChanged;
            }

            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.OnLocaleChanged -= HandleLocaleChanged;
            }

            UnregisterUiCallbacks();
            _isPopulated = false;
        }

        public void Close()
        {
            // If attached to MainMenuScreen, use its animation system
            if (mainMenuScreen != null)
            {
                mainMenuScreen.OnOptionsPanelClosed();
            }
            else
            {
                // Fallback for pause menu or other contexts
                AudioManager.Instance?.PlayUiClick();
                var target = root != null ? root : gameObject;
                target.SetActive(false);
            }
        }

        private void RegisterUiCallbacks()
        {
            if (masterSlider != null)
            {
                masterSlider.onValueChanged.AddListener(OnMasterChanged);
            }

            if (musicSlider != null)
            {
                musicSlider.onValueChanged.AddListener(OnMusicChanged);
            }

            if (sfxSlider != null)
            {
                sfxSlider.onValueChanged.AddListener(OnSfxChanged);
            }

            if (languageDropdown != null)
            {
                languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
            }
        }

        private void UnregisterUiCallbacks()
        {
            if (masterSlider != null)
            {
                masterSlider.onValueChanged.RemoveListener(OnMasterChanged);
            }

            if (musicSlider != null)
            {
                musicSlider.onValueChanged.RemoveListener(OnMusicChanged);
            }

            if (sfxSlider != null)
            {
                sfxSlider.onValueChanged.RemoveListener(OnSfxChanged);
            }

            if (languageDropdown != null)
            {
                languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
            }
        }

        private void PopulateLocales()
        {
            if (LocalizationService.Instance == null || languageDropdown == null)
            {
                return;
            }

            _locales.Clear();
            _locales.AddRange(LocalizationService.Instance.GetAvailableLocales());
            if (_locales.Count == 0)
            {
                var fallback = LocalizationService.Instance.CurrentLocale;
                if (string.IsNullOrEmpty(fallback))
                {
                    fallback = "en";
                }

                _locales.Add(fallback);
            }
            languageDropdown.ClearOptions();
            languageDropdown.AddOptions(_locales);
            _isPopulated = true;
        }

        private void SyncFromSettings()
        {
            var settings = SaveSettings.Instance;
            if (settings == null)
            {
                return;
            }

            var data = settings.Data;
            if (masterSlider != null)
            {
                masterSlider.SetValueWithoutNotify(DbToSlider(data.masterDb));
            }

            if (musicSlider != null)
            {
                musicSlider.SetValueWithoutNotify(DbToSlider(data.musicDb));
            }

            if (sfxSlider != null)
            {
                sfxSlider.SetValueWithoutNotify(DbToSlider(data.sfxDb));
            }

            if (languageDropdown != null && _isPopulated)
            {
                var index = _locales.IndexOf(data.localeCode);
                if (index < 0)
                {
                    index = 0;
                }

                languageDropdown.SetValueWithoutNotify(index);
            }
        }

        private void HandleSettingsChanged(SettingKind kind)
        {
            if (kind == SettingKind.MasterVolume ||
                kind == SettingKind.MusicVolume ||
                kind == SettingKind.SfxVolume)
            {
                SyncFromSettings();
            }
            else if (kind == SettingKind.Locale)
            {
                SyncFromSettings();
            }
        }

        private void HandleLocaleChanged(string locale)
        {
            if (languageDropdown != null && _isPopulated)
            {
                var index = _locales.IndexOf(locale);
                if (index >= 0)
                {
                    languageDropdown.SetValueWithoutNotify(index);
                }
            }
        }

        private void OnMasterChanged(float value)
        {
            SaveSettings.Instance?.SetMasterDb(SliderToDb(value));
        }

        private void OnMusicChanged(float value)
        {
            SaveSettings.Instance?.SetMusicDb(SliderToDb(value));
        }

        private void OnSfxChanged(float value)
        {
            SaveSettings.Instance?.SetSfxDb(SliderToDb(value));
        }

        private void OnLanguageChanged(int index)
        {
            if (!_isPopulated || index < 0 || index >= _locales.Count)
            {
                return;
            }

            LocalizationService.Instance?.SetLocale(_locales[index]);
        }

        private static float SliderToDb(float sliderValue)
        {
            var curved = Mathf.Pow(Mathf.Clamp01(sliderValue), SliderCurve);
            return Mathf.Lerp(MinVolumeDb, MaxVolumeDb, curved);
        }

        private static float DbToSlider(float dbValue)
        {
            var curved = Mathf.InverseLerp(MinVolumeDb, MaxVolumeDb, Mathf.Clamp(dbValue, MinVolumeDb, MaxVolumeDb));
            return Mathf.Pow(curved, 1f / SliderCurve);
        }
    }
}
