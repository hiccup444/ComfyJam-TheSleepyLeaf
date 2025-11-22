using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using JamesKJamKit.Services.Music;
using System.Collections.Generic;

namespace JamesKJamKit.Services
{
    /// <summary>
    /// Controls background music playback, swapping playlists based on the current game state and
    /// crossfading between tracks. Supports routing per-track to a specific AudioMixerGroup.
    /// </summary>
    [DefaultExecutionOrder(-9994)]
    public sealed class MusicDirector : MonoBehaviour
    {
        private const string MasterParam = "MASTER_VOL";
        private const string MusicParam  = "MUSIC_VOL";

        public enum MusicState
        {
            Menu,
            Game,
            Paused,
            Cutscene,
            Credits
        }

        [Header("Mixer & Routing")]
        [SerializeField] private AudioMixer mixer;
        [SerializeField] private AudioMixerGroup musicGroup; // global fallback if playlist/track doesn't specify

        [Header("Playlists")]
        [SerializeField] private MusicPlaylist menuPlaylist;
        [SerializeField] private MusicPlaylist gamePlaylist;
        [SerializeField] private MusicPlaylist cutscenePlaylist;
        [SerializeField] private MusicPlaylist creditsPlaylist;

        [Header("Behavior")]
        [SerializeField] private float crossfadeSeconds = 2f;

        [Header("Startup")]
        [SerializeField] private string startupSnapshotName = "Startup";

        public static MusicDirector Instance { get; private set; }

        public MusicState CurrentState { get; private set; } = MusicState.Menu;

        // event fired when a track actually starts playing
        public event Action OnTrackChanged;

        // public accessors for MIDI extension
        public AudioSource ActiveSource   => _activeSource;
        public MusicPlaylist ActivePlaylist => _activePlaylist;
        public MusicPlaylist GamePlaylist => gamePlaylist; // for reference comparison in extensions
        public int PlaylistIndex          => _playlistIndex;

        private readonly System.Random _rng = new();
        private AudioSource _primary;
        private AudioSource _secondary;
        private AudioSource _activeSource;
        private MusicPlaylist _activePlaylist;
        private int _playlistIndex = -1;
        private bool _paused;
        private MusicState _baseState = MusicState.Menu;
        private float _targetMusicDb;
        private bool _missingMasterParamLogged;
        private bool _missingMusicParamLogged;
        private bool _startupSnapshotMissingLogged;
        private AudioMixerSnapshot _cachedStartupSnapshot;
        private List<int> _playHistory = new List<int>(MaxHistorySize); // track indices we've played (pre-allocate capacity)
        private int _historyPosition = -1; // current position in history (-1 = at latest)
        private const int MaxHistorySize = 20; // prevent unlimited memory growth

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _cutsceneGuardLogged; // prevent per-frame logging spam
        #endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            CreateSources();
            ApplyVolumes(true);
        }

        public void SkipToNextTrack()
        {
            // Block skipping while on cutscene playlist (FIX for Radio Next)
            if (IsOnCutscenePlaylist)
            {
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MusicDirector] Blocked Next on Cutscene playlist.");
                #endif
                return;
            }
            // AUDIT LOG
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[MusicDirector] SkipToNextTrack() requested | ActivePlaylist={GetActivePlaylistLabel()} | _playlistIndex={_playlistIndex} | historyCount={_playHistory?.Count} | historyPos={_historyPosition}");
            #endif
            if (_activeSource != null && _activeSource.isPlaying)
            {
                _activeSource.Stop();
            }
            PlayNextTrack(true);
        }

        public void SkipToPreviousTrack()
        {
            // Block skipping while on cutscene playlist (FIX for Radio Prev)
            if (IsOnCutscenePlaylist)
            {
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MusicDirector] Blocked Prev on Cutscene playlist.");
                #endif
                return;
            }
            // AUDIT LOG
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[MusicDirector] SkipToPreviousTrack() requested | ActivePlaylist={GetActivePlaylistLabel()} | _playlistIndex={_playlistIndex} | historyCount={_playHistory?.Count} | historyPos={_historyPosition}");
            #endif
            // can't go back if no history
            if (_playHistory.Count == 0)
            {
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MusicDirector] No previous track in history");
                #endif
                return;
            }

            // move back in history
            if (_historyPosition == -1)
            {
                // currently at latest, go back one (current track is at end of list)
                _historyPosition = _playHistory.Count - 2; // skip current, go to previous
            }
            else
            {
                // already going back, go back one more
                _historyPosition = Mathf.Max(0, _historyPosition - 1);
            }

            // clamp to valid range
            if (_historyPosition < 0 || _historyPosition >= _playHistory.Count)
            {
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MusicDirector] No previous track available");
                #endif
                return;
            }

            // play the track at this history position
            int trackIndex = _playHistory[_historyPosition];
            PlayTrackAtIndex(trackIndex);
        }

        private void PlayTrackAtIndex(int index)
        {
            if (_activePlaylist == null || !_activePlaylist.HasTracks) return;

            var clip = _activePlaylist.GetTrackAtIndex(index);
            if (clip == null) return;

            var nextSource = _activeSource == _primary ? _secondary : _primary;
            var routeGroup = ResolveMixerGroupForIndex(index);
            PrepareSource(nextSource, clip, routeGroup);
            nextSource.Play();

            if (crossfadeSeconds > 0f && _activeSource != null && _activeSource.isPlaying)
            {
                StartCoroutine(CrossfadeRoutine(_activeSource, nextSource, crossfadeSeconds));
            }
            else
            {
                if (_activeSource != null) _activeSource.Stop();
                nextSource.volume = 1f;
                _activeSource = nextSource;
            }

            OnTrackChanged?.Invoke();
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[MusicDirector] Playing track at index {index} via group {(routeGroup ? routeGroup.name : "NULL")}");
            #endif
        }

        private void OnEnable()
        {
            if (SaveSettings.Instance != null)
            {
                SaveSettings.Instance.OnSettingsChanged += HandleSettingsChanged;
            }

            if (SceneRouter.Instance != null)
            {
                SceneRouter.Instance.OnSceneChanged += HandleSceneChanged;
            }

            if (PauseController.Instance != null)
            {
                PauseController.Instance.OnPauseChanged += HandlePauseChanged;
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnDayStarted += HandleDayStarted;
            }
        }

        private void Start()
        {
            // Ensure GameManager event subscription (in case it wasn't ready during OnEnable)
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnDayStarted -= HandleDayStarted; // Remove first to avoid duplicates
                GameManager.Instance.OnDayStarted += HandleDayStarted;
            }

            // Kick off music based on the active scene.
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            HandleSceneChanged(activeScene);
        }

        private void OnDisable()
        {
            if (SaveSettings.Instance != null)
            {
                SaveSettings.Instance.OnSettingsChanged -= HandleSettingsChanged;
            }

            if (SceneRouter.Instance != null)
            {
                SceneRouter.Instance.OnSceneChanged -= HandleSceneChanged;
            }

            if (PauseController.Instance != null)
            {
                PauseController.Instance.OnPauseChanged -= HandlePauseChanged;
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnDayStarted -= HandleDayStarted;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_activePlaylist == null) return;

            // Don't auto-play next track if we're on the cutscene playlist
            if (_activePlaylist == cutscenePlaylist)
            {
                // Optimization: Guard debug log to prevent per-frame string allocation on WebGL
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Only log once to avoid spam
                if (!_cutsceneGuardLogged)
                {
                    Debug.Log("[MusicDirector] Auto-play guard active: on Cutscene playlist; skipping auto next.");
                    _cutsceneGuardLogged = true;
                }
                #endif
                return;
            }

            if (_activeSource != null && !_activeSource.isPlaying && !_paused)
            {
                PlayNextTrack();
            }
        }

        public void SetState(MusicState state)
        {
            // AUDIT LOG (before state change)
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            var prevState = CurrentState;
            var baseStateSnapshot = _baseState;
            var activeClipName = _activeSource != null && _activeSource.clip != null ? _activeSource.clip.name : "NULL";
            var isPlaying = _activeSource != null && _activeSource.isPlaying;
            Debug.Log($"[MusicDirector] SetState() enter | from={prevState} to={state} | _baseState={baseStateSnapshot} | ActivePlaylist={GetActivePlaylistLabel()} | ActiveClip={activeClipName} | isPlaying={isPlaying}");
            #endif
            if (_paused && state != MusicState.Paused)
            {
                _baseState = state;
                return;
            }

            CurrentState = state;
            switch (state)
            {
                case MusicState.Menu:
                    SwitchPlaylist(menuPlaylist);
                    break;
                case MusicState.Game:
                    SwitchPlaylist(gamePlaylist);
                    break;
                case MusicState.Cutscene:
                    SwitchPlaylist(cutscenePlaylist);
                    break;
                case MusicState.Credits:
                    SwitchPlaylist(creditsPlaylist);
                    break;
                case MusicState.Paused:
                    // Keep playing current playlist with unchanged volume.
                    ApplyVolumes();
                    break;
            }
            // AUDIT LOG (after state change)
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            activeClipName = _activeSource != null && _activeSource.clip != null ? _activeSource.clip.name : "NULL";
            isPlaying = _activeSource != null && _activeSource.isPlaying;
            Debug.Log($"[MusicDirector] SetState() exit | now={CurrentState} | _baseState={_baseState} | ActivePlaylist={GetActivePlaylistLabel()} | ActiveClip={activeClipName} | isPlaying={isPlaying}");
            #endif
        }

        private void HandleSceneChanged(string sceneName)
        {
            TryApplyStartupSnapshotForScene(sceneName);

            // If entering the Game scene, check if we should play cutscene music first
            if (string.Equals(sceneName, "Game", StringComparison.OrdinalIgnoreCase))
            {
                // Check if intro cutscene needs to play
                if (GameManager.Instance != null && !GameManager.Instance.HasPlayedIntroCutscene())
                {
                    // Start with cutscene music - CutsceneManager will switch to Game music when done
                    _baseState = MusicState.Cutscene;
                    if (!_paused)
                    {
                        SetState(MusicState.Cutscene);
                    }
                    return;
                }

                // Cutscene already played, use game music
                _baseState = MusicState.Game;
                if (!_paused)
                {
                    SetState(MusicState.Game);
                }
            }
            else
            {
                // Non-game scenes default to menu music
                _baseState = MusicState.Menu;
                if (!_paused)
                {
                    SetState(MusicState.Menu);
                }
            }
        }

        private bool IsMainMenuScene(string sceneName)
            => string.Equals(sceneName, GetMainMenuSceneName(), StringComparison.OrdinalIgnoreCase);

        private string GetMainMenuSceneName()
            => SceneRouter.Instance?.MainMenuSceneName ?? "MainMenu";

        private void TryApplyStartupSnapshotForScene(string sceneName)
        {
            if (!IsMainMenuScene(sceneName)) return;
            var snapshot = GetStartupSnapshot();
            if (snapshot == null)
            {
                if (!_startupSnapshotMissingLogged)
                {
                    #if UNITY_EDITOR
                    Debug.LogWarning($"[MusicDirector] Unable to find startup snapshot '{startupSnapshotName}'.", mixer);
                    #endif
                    _startupSnapshotMissingLogged = true;
                }
                return;
            }
            snapshot.TransitionTo(0f);
        }

        private AudioMixerSnapshot GetStartupSnapshot()
        {
            if (_cachedStartupSnapshot != null) return _cachedStartupSnapshot;
            if (mixer == null || string.IsNullOrEmpty(startupSnapshotName)) return null;
            _cachedStartupSnapshot = mixer.FindSnapshot(startupSnapshotName);
            return _cachedStartupSnapshot;
        }

        private void HandlePauseChanged(bool paused)
        {
            _paused = paused;
            // AUDIT LOG
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            var activeClipName = _activeSource != null && _activeSource.clip != null ? _activeSource.clip.name : "NULL";
            var isPlaying = _activeSource != null && _activeSource.isPlaying;
            Debug.Log($"[MusicDirector] HandlePauseChanged(paused={paused}) | CurrentState={CurrentState} | _baseState={_baseState} | ActivePlaylist={GetActivePlaylistLabel()} | ActiveClip={activeClipName} | isPlaying={isPlaying}");
            #endif
            if (paused)
            {
                SetState(MusicState.Paused);
                ApplyDuckVolume();
            }
            else
            {
                // If the intro has been marked played, never return to Cutscene on resume
                if (_baseState == MusicState.Cutscene && GameManager.Instance != null && GameManager.Instance.HasPlayedIntroCutscene())
                {
                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log("[MusicDirector] Resume override: intro already played → forcing _baseState=Game");
                    #endif
                    _baseState = MusicState.Game;
                }
                ApplyVolumes();
                SetState(_baseState);
            }
        }

        private void HandleDayStarted()
        {
            // Only auto-skip track when on the game playlist
            // Don't interfere with menu or cutscene music
            if (_activePlaylist != gamePlaylist)
            {
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[MusicDirector] New day started, but not on game playlist (current: {GetActivePlaylistLabel()}) - skipping track change");
                #endif
                return;
            }

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MusicDirector] New day started - skipping to next track");
            #endif
            SkipToNextTrack();
        }

        // AUDIT helper for readable playlist label in logs
        private string GetActivePlaylistLabel()
        {
            if (_activePlaylist == null) return "NULL";
            if (_activePlaylist == menuPlaylist) return "Menu";
            if (_activePlaylist == gamePlaylist) return "Game";
            if (_activePlaylist == cutscenePlaylist) return "Cutscene";
            if (_activePlaylist == creditsPlaylist) return "Credits";
            return _activePlaylist.name;
        }

        // Convenience property to detect Cutscene playlist
        public bool IsOnCutscenePlaylist => _activePlaylist == cutscenePlaylist;

        // Ensure game playlist is active and clear any skip/back history after cutscene
        public void EnsureGamePlaylistAndClearHistory()
        {
            _baseState = MusicState.Game; // ensure resume goes to Game
            SetState(MusicState.Game);
            if (_playHistory != null) _playHistory.Clear();
            _historyPosition = -1;
        }

        private void HandleSettingsChanged(SettingKind kind)
        {
            if (kind is SettingKind.MasterVolume or SettingKind.MusicVolume)
            {
                ApplyVolumes();
            }
        }

        private void SwitchPlaylist(MusicPlaylist playlist)
        {
            if (playlist == null || !playlist.HasTracks)
            {
                StopAllMusic();
                _activePlaylist = null;
                return;
            }

            if (_activePlaylist != playlist)
            {
                _activePlaylist = playlist;
                _playlistIndex = -1;
                PlayNextTrack(true);
            }
            else if (_activeSource == null || !_activeSource.isPlaying)
            {
                PlayNextTrack();
            }
        }

        private void PlayNextTrack(bool immediate = false)
        {
            if (_activePlaylist == null) return;

            var clip = _activePlaylist.GetNext(ref _playlistIndex, _rng);
            if (clip == null) return;

            // add to history (only if not navigating back)
            if (_historyPosition == -1)
            {
                _playHistory.Add(_playlistIndex);

                // trim history if too long (optimization: use RemoveRange to avoid multiple shifts)
                if (_playHistory.Count > MaxHistorySize)
                {
                    int removeCount = _playHistory.Count - MaxHistorySize;
                    _playHistory.RemoveRange(0, removeCount);
                }
            }
            else
            {
                // if we were going back and now going forward, reset position
                _historyPosition = -1;
            }

            var nextSource = _activeSource == _primary ? _secondary : _primary;
            var routeGroup = ResolveMixerGroupForIndex(_playlistIndex);
            PrepareSource(nextSource, clip, routeGroup);
            nextSource.Play();

            if (!immediate && crossfadeSeconds > 0f && _activeSource != null && _activeSource.isPlaying)
            {
                StartCoroutine(CrossfadeRoutine(_activeSource, nextSource, crossfadeSeconds));
            }
            else
            {
                if (_activeSource != null) _activeSource.Stop();
                nextSource.volume = 1f;
                _activeSource = nextSource;
            }

            OnTrackChanged?.Invoke();
        }

        private IEnumerator CrossfadeRoutine(AudioSource from, AudioSource to, float duration)
        {
            _activeSource = to;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var normalized = Mathf.Clamp01(t / duration);
                if (from != null) from.volume = 1f - normalized;
                to.volume = normalized;
                yield return null;
            }

            if (from != null)
            {
                from.Stop();
                from.volume = 1f;
            }

            to.volume = 1f;
        }

        private void StopAllMusic()
        {
            if (_primary   != null) _primary.Stop();
            if (_secondary != null) _secondary.Stop();
            _activeSource = null;
        }

        private void CreateSources()
        {
            _primary   = CreateSource("Music Source A");
            _secondary = CreateSource("Music Source B");
            _activeSource = _primary;
        }

        private AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.outputAudioMixerGroup = musicGroup; // default; may be overridden per-track
            return source;
        }

        private void PrepareSource(AudioSource source, AudioClip clip, AudioMixerGroup routeGroup)
        {
            source.Stop();
            source.clip = clip;
            source.volume = 0f;
            source.loop = false;
            // Route this specific track
            source.outputAudioMixerGroup = routeGroup != null ? routeGroup : musicGroup;
        }

        private AudioMixerGroup ResolveMixerGroupForIndex(int index)
        {
            // Playlist per-track override → playlist default → serialized fallback
            if (_activePlaylist != null)
            {
                var g = _activePlaylist.GetMixerGroupAtIndex(index);
                if (g != null) return g;
            }
            return musicGroup;
        }

        private void ApplyDuckVolume() => ApplyVolumes(true);

        private void ApplyVolumes(bool force = false)
        {
            if (mixer == null || SaveSettings.Instance == null) return;

            var data = SaveSettings.Instance.Data;
            // Do not alter music volume when paused; keep user setting.
            var desired = data.musicDb;
            if (!force && Mathf.Approximately(_targetMusicDb, desired)) return;

            _targetMusicDb = desired;
            if (mixer.SetFloat(MasterParam, data.masterDb))
            {
                _missingMasterParamLogged = false;
            }
            else if (!_missingMasterParamLogged)
            {
                #if UNITY_EDITOR
                Debug.LogWarning($"[MusicDirector] Mixer missing exposed parameter '{MasterParam}'. Volume will use default value.", mixer);
                #endif
                _missingMasterParamLogged = true;
            }

            if (mixer.SetFloat(MusicParam, desired))
            {
                _missingMusicParamLogged = false;
            }
            else if (!_missingMusicParamLogged)
            {
                #if UNITY_EDITOR
                Debug.LogWarning($"[MusicDirector] Mixer missing exposed parameter '{MusicParam}'. Volume will use default value.", mixer);
                #endif
                _missingMusicParamLogged = true;
            }
        }
    }
}
