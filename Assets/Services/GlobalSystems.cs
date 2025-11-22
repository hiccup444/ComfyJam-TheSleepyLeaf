using UnityEngine;

namespace JamesKJamKit.Services
{
    [DefaultExecutionOrder(-10000)]
    public sealed class GlobalSystems : MonoBehaviour
    {
        private static GlobalSystems _instance;

        private void Awake()
        {
            if (_instance && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }
}
