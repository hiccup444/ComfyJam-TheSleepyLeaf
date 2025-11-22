using System;
using System.Collections.Generic;
using UnityEngine;

namespace JamesKJamKit.Services.Music
{
    [Serializable]
    public struct MidiNote
    {
        public float timeSeconds;      // when to spawn relative to track start
        public int spawnPointIndex;    // which spawn point to use (mapped from MIDI pitch)
        public int noteTypeIndex;      // which sprite to show (mapped from MIDI channel or note range)
        public float intensity;        // velocity mapped to 0-1 range (for scale/brightness)
        
        public MidiNote(float time, int spawnPoint, int noteType, float noteIntensity)
        {
            timeSeconds = time;
            spawnPointIndex = spawnPoint;
            noteTypeIndex = noteType;
            intensity = noteIntensity;
        }
    }

    [CreateAssetMenu(fileName = "MidiTrackData", menuName = "Audio/MIDI Track Data")]
    public class MidiTrackData : ScriptableObject
{
    [Header("Audio Reference")]
    [Tooltip("The audio clip this MIDI data syncs with")]
    public AudioClip audioClip;
    
    [Header("MIDI Data")]
    [Tooltip("Duration of the track in seconds")]
    public float trackDurationSeconds;
    
    [Tooltip("All notes parsed from MIDI, sorted by time")]
    public List<MidiNote> notes = new List<MidiNote>();
    
    [Header("Mapping Settings (Editor Only)")]
    [Tooltip("How many spawn points are available for mapping")]
    public int spawnPointCount = 5;
    
    [Tooltip("MIDI note range to map across spawn points")]
    public int minMidiNote = 60; // Middle C
    public int maxMidiNote = 84; // 2 octaves up
    
    [Header("Debug Info")]
    [Tooltip("Source MIDI file name")]
    public string sourceMidiFile;
    
    [Tooltip("When this data was last parsed")]
    public string parseDate;
    
    // quickly check if we have any notes
    public bool HasNotes => notes != null && notes.Count > 0;
    
    // get total note count
    public int NoteCount => notes?.Count ?? 0;
    
    // helper to get notes within a time range (for efficient lookup)
    public List<MidiNote> GetNotesInRange(float startTime, float endTime)
    {
        if (!HasNotes) return new List<MidiNote>();
        
        List<MidiNote> result = new List<MidiNote>();
        
        // notes should be sorted by time, but do linear search for safety
        // could optimize with binary search later if needed
        foreach (var note in notes)
        {
            if (note.timeSeconds >= startTime && note.timeSeconds <= endTime)
            {
                result.Add(note);
            }
            else if (note.timeSeconds > endTime)
            {
                break; // stop early since sorted
            }
        }
        
        return result;
    }
    
    // validation helper
    void OnValidate()
    {
        // ensure min is less than max
        if (minMidiNote >= maxMidiNote)
        {
            maxMidiNote = minMidiNote + 12; // at least an octave
        }
        
        // keep spawn point count reasonable
        spawnPointCount = Mathf.Clamp(spawnPointCount, 1, 20);
    }
}
}