using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio; // ← mixer types

namespace JamesKJamKit.Services.Music
{
    /// <summary>
    /// Music tracks that can be cycled/shuffled.
    /// Each track can optionally specify an AudioMixerGroup override;
    /// otherwise the playlist's default group is used.
    /// </summary>
    [CreateAssetMenu(fileName = "MusicPlaylist", menuName = "Audio/Music Playlist")]
    public sealed class MusicPlaylist : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public AudioClip clip;

            [Tooltip("Optional MIDI data for synced visual notes")]
            public MidiTrackData midiData; // can be null

            [Tooltip("Optional per-track mixer group (routes this clip when not null)")]
            public AudioMixerGroup mixerGroupOverride; // can be null
        }

        [Header("Routing")]
        [Tooltip("Default mixer group used when a track doesn't provide an override")]
        [SerializeField] private AudioMixerGroup defaultMixerGroup;

        [Header("Playback")]
        [SerializeField] private bool shuffle;

        [SerializeField] private List<Entry> tracks = new();

        public bool HasTracks => tracks.Count > 0;

        public AudioClip GetTrackAtIndex(int index)
        {
            if (index < 0 || index >= tracks.Count) return null;
            return tracks[index].clip;
        }

        // MIDI access
        public MidiTrackData GetMidiDataAtIndex(int index)
        {
            if (index < 0 || index >= tracks.Count) return null;
            return tracks[index].midiData;
        }

        // Mixer routing for a given index (choose override or fallback)
        public AudioMixerGroup GetMixerGroupAtIndex(int index)
        {
            if (index < 0 || index >= tracks.Count) return defaultMixerGroup;
            var ov = tracks[index].mixerGroupOverride;
            return ov ? ov : defaultMixerGroup;
        }

        // Next track (updates index) and returns its clip
        public AudioClip GetNext(ref int index, System.Random rng)
        {
            if (tracks.Count == 0) return null;

            if (shuffle)
            {
                index = rng.Next(tracks.Count);
                return tracks[index].clip;
            }

            index = (index + 1) % tracks.Count;
            return tracks[index].clip;
        }
    }
}
