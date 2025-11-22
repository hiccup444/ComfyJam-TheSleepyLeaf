using System;
using UnityEngine;
using JamesKJamKit.Services.Music;

namespace JamesKJamKit.Services
{
    [RequireComponent(typeof(MusicDirector))]
    [DefaultExecutionOrder(-9993)] // run after MusicDirector
    public class MusicDirectorMidiExtension : MonoBehaviour
    {
        public static MusicDirectorMidiExtension Instance { get; private set; }
        
        // event fired when a new track starts playing
        public event Action<MidiTrackData, AudioSource> OnTrackStarted;
        
        // event fired when track changes or stops
        public event Action OnTrackStopped;
        
        MusicDirector musicDirector;
        MidiTrackData currentMidiData;
        AudioSource currentAudioSource;
        
        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            
            Instance = this;
            musicDirector = GetComponent<MusicDirector>();
        }
        
        void OnEnable()
        {
            // subscribe to music director's track changed event
            if (musicDirector != null)
            {
                musicDirector.OnTrackChanged += HandleTrackChanged;
                
                // check if a track is already playing when we enable
                // (handles case where MusicDirector started before us)
                if (musicDirector.ActiveSource != null && musicDirector.ActiveSource.isPlaying)
                {
                    // Debug.Log("[MusicDirectorMidiExtension] OnEnable: Track already playing, firing HandleTrackChanged");
                    HandleTrackChanged();
                }
            }
        }
        
        void OnDisable()
        {
            if (musicDirector != null)
            {
                musicDirector.OnTrackChanged -= HandleTrackChanged;
            }
        }
        
        void HandleTrackChanged()
        {
            // Block MIDI logic on non-game playlists (optimization: use reference comparison instead of string)
            var playlist = musicDirector.ActivePlaylist;
            if (playlist != musicDirector.GamePlaylist)
            {
                // Debug.Log("[MusicDirectorMidiExtension] Track changed but not GamePlaylist, ignoring");
                OnTrackStopped?.Invoke();
                currentMidiData = null;
                currentAudioSource = null;
                return;
            }
            
            AudioClip currentClip = musicDirector.ActiveSource?.clip;
            
            // Debug.Log($"[MusicDirectorMidiExtension] Track changed to: {(currentClip != null ? currentClip.name : "NULL")}");
            
            // always fire stop event when track changes
            OnTrackStopped?.Invoke();
            
            if (currentClip != null)
            {
                // Debug.Log("[MusicDirectorMidiExtension] New track started");
                
                // get the active audio source
                currentAudioSource = musicDirector.ActiveSource;
                currentMidiData = GetMidiDataForClip(currentClip);
                
                // Debug.Log($"[MusicDirectorMidiExtension] Retrieved MIDI data: {(currentMidiData != null ? $"{currentMidiData.name} ({currentMidiData.NoteCount} notes)" : "NULL")}");

                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (currentMidiData == null)
                {
                    Debug.LogWarning($"[MusicDirectorMidiExtension] GetMidiDataForClip returned NULL for clip: {currentClip.name}");
                    Debug.LogWarning($"[MusicDirectorMidiExtension] Playlist: {(musicDirector.ActivePlaylist != null ? musicDirector.ActivePlaylist.name : "NULL")}, Index: {musicDirector.PlaylistIndex}");
                }
                #endif

                // Debug.Log($"[MusicDirectorMidiExtension] Firing OnTrackStarted event (subscribers: {OnTrackStarted?.GetInvocationList()?.Length ?? 0})");
                OnTrackStarted?.Invoke(currentMidiData, currentAudioSource);

                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (currentMidiData != null)
                {
                    Debug.Log($"[MusicDirectorMidiExtension] ✓ Track started with MIDI data: {currentMidiData.NoteCount} notes");
                }
                else
                {
                    Debug.LogWarning($"[MusicDirectorMidiExtension] ✗ Track started WITHOUT MIDI data!");
                }
                #endif
            }
        }
        
        void Update()
        {
            // no longer needed - we listen to events instead
        }
        
        // find MIDI data for the given audio clip by searching the active playlist
        MidiTrackData GetMidiDataForClip(AudioClip clip)
        {
            if (clip == null)
                return null;
            
            MusicPlaylist playlist = musicDirector.ActivePlaylist;
            int index = musicDirector.PlaylistIndex;
            
            if (playlist != null && index >= 0)
            {
                return playlist.GetMidiDataAtIndex(index);
            }
            
            return null;
        }
        
        // public accessors
        public MidiTrackData CurrentMidiData => currentMidiData;
        public AudioSource CurrentAudioSource => currentAudioSource;
        public float CurrentTrackTime => currentAudioSource != null ? currentAudioSource.time : 0f;
        
        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}