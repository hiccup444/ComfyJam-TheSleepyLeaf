using System;
using System.IO;
using UnityEngine;

namespace JamesKJamKit.Services
{
    /// <summary>
    /// Persists simple audio and localization settings to disk. Emits an event whenever a setting
    /// changes so listeners can refresh their state immediately.
    /// </summary>
    [DefaultExecutionOrder(-9999)]
    public sealed class SaveSettings : MonoBehaviour
    {
        private const string FileName = "AppSettings.json";

        [Serializable]
        public sealed class SettingsData
        {
            public float masterDb = 0f;
            public float musicDb = -18f;
            public float sfxDb = 0f;
            public string localeCode = "en";
        }

        public static SaveSettings Instance { get; private set; }

        public event Action<SettingKind> OnSettingsChanged;

        public SettingsData Data { get; private set; } = new SettingsData();

        private string _settingsPath;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _settingsPath = Path.Combine(Application.persistentDataPath, FileName);
            Load();
            // Notify listeners of the initial state so volumes/locales are applied on boot.
            RaiseAll();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetMasterDb(float value)
        {
            if (Mathf.Approximately(Data.masterDb, value))
            {
                return;
            }

            Data.masterDb = value;
            Save();
            Raise(SettingKind.MasterVolume);
        }

        public void SetMusicDb(float value)
        {
            if (Mathf.Approximately(Data.musicDb, value))
            {
                return;
            }

            Data.musicDb = value;
            Save();
            Raise(SettingKind.MusicVolume);
        }

        public void SetSfxDb(float value)
        {
            if (Mathf.Approximately(Data.sfxDb, value))
            {
                return;
            }

            Data.sfxDb = value;
            Save();
            Raise(SettingKind.SfxVolume);
        }

        public void SetLocale(string localeCode)
        {
            if (string.Equals(Data.localeCode, localeCode, StringComparison.Ordinal))
            {
                return;
            }

            Data.localeCode = localeCode;
            Save();
            Raise(SettingKind.Locale);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return;
                }

                var json = File.ReadAllText(_settingsPath);
                if (!string.IsNullOrEmpty(json))
                {
                    JsonUtility.FromJsonOverwrite(json, Data);
                }
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"Failed to load app settings. Using defaults. Exception: {ex}");
#endif
            }
        }

        private void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonUtility.ToJson(Data, true);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"Failed to save app settings: {ex}");
#endif
            }
        }

        private void Raise(SettingKind kind)
        {
            OnSettingsChanged?.Invoke(kind);
        }

        private void RaiseAll()
        {
            Raise(SettingKind.MasterVolume);
            Raise(SettingKind.MusicVolume);
            Raise(SettingKind.SfxVolume);
            Raise(SettingKind.Locale);
        }
    }
}
