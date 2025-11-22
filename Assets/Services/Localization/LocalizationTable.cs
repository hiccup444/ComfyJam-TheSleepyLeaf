using System;
using System.Collections.Generic;
using UnityEngine;

namespace JamesKJamKit.Services.Localization
{
    /// <summary>
    /// Simple key-value localization table that can be authored in the editor.
    /// </summary>
    [CreateAssetMenu(fileName = "LocalizationTable", menuName = "Localization/Table")]
    public sealed class LocalizationTable : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            public string key;
            [TextArea]
            public string value;
        }

        [SerializeField]
        private string localeCode = "en";

        [SerializeField]
        private List<Entry> entries = new();

        private Dictionary<string, string> _lookup;

        public string LocaleCode => localeCode;

        private void OnEnable()
        {
            BuildLookup();
        }

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            BuildLookup();
            return _lookup != null && _lookup.TryGetValue(key, out var value) ? value : key;
        }

        private void BuildLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.key))
                {
                    continue;
                }

                _lookup[entry.key] = entry.value ?? string.Empty;
            }
        }
    }
}
