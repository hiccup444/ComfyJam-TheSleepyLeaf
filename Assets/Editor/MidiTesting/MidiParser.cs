using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using JamesKJamKit.Services.Music;

#if UNITY_EDITOR
using UnityEditor;

public class MidiParser
{
    // MIDI file format constants
    private const string HEADER_CHUNK_ID = "MThd";
    private const string TRACK_CHUNK_ID = "MTrk";
    
    // MIDI message types
    private const byte NOTE_OFF = 0x80;
    private const byte NOTE_ON = 0x90;
    private const byte META_EVENT = 0xFF;
    private const byte SET_TEMPO = 0x51;
    
    public class ParseResult
    {
        public List<MidiNote> notes = new List<MidiNote>();
        public float durationSeconds;
        public int noteCount;
        public string errorMessage;
        public bool success;
    }
    
    public static ParseResult ParseMidiFile(string filePath, int spawnPointCount, int minNote, int maxNote)
    {
        ParseResult result = new ParseResult();
        
        try
        {
            if (!File.Exists(filePath))
            {
                result.errorMessage = $"File not found: {filePath}";
                return result;
            }
            
            byte[] data = File.ReadAllBytes(filePath);
            
            using (MemoryStream stream = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                // parse header chunk
                if (!ReadHeaderChunk(reader, out int format, out int trackCount, out int ticksPerQuarter))
                {
                    result.errorMessage = "Invalid MIDI header";
                    return result;
                }
                
                Debug.Log($"[MidiParser] Format: {format}, Tracks: {trackCount}, Ticks/Quarter: {ticksPerQuarter}");
                
                // default tempo (120 BPM = 500,000 microseconds per quarter note)
                int microsecondsPerQuarter = 500000;
                
                // collect all note events with their delta times
                List<RawNoteEvent> rawEvents = new List<RawNoteEvent>();
                
                // parse each track
                for (int track = 0; track < trackCount; track++)
                {
                    ParseTrack(reader, rawEvents, ref microsecondsPerQuarter);
                }
                
                // convert delta ticks to absolute seconds
                ConvertToAbsoluteTime(rawEvents, ticksPerQuarter, microsecondsPerQuarter);
                
                // map to spawn points and create MidiNotes
                result.notes = MapNotesToSpawnPoints(rawEvents, spawnPointCount, minNote, maxNote);
                result.noteCount = result.notes.Count;
                
                // calculate duration (find last note time)
                if (result.notes.Count > 0)
                {
                    result.durationSeconds = 0f;
                    foreach (var note in result.notes)
                    {
                        if (note.timeSeconds > result.durationSeconds)
                            result.durationSeconds = note.timeSeconds;
                    }
                }
                
                result.success = true;
            }
        }
        catch (Exception e)
        {
            result.errorMessage = $"Parse error: {e.Message}";
            Debug.LogError($"[MidiParser] {result.errorMessage}\n{e.StackTrace}");
        }
        
        return result;
    }
    
    private static bool ReadHeaderChunk(BinaryReader reader, out int format, out int trackCount, out int ticksPerQuarter)
    {
        format = 0;
        trackCount = 0;
        ticksPerQuarter = 0;
        
        // read "MThd"
        string chunkId = new string(reader.ReadChars(4));
        if (chunkId != HEADER_CHUNK_ID)
            return false;
        
        // header size (should be 6)
        int headerSize = ReadInt32BigEndian(reader);
        
        // format type (0, 1, or 2)
        format = ReadInt16BigEndian(reader);
        
        // number of tracks
        trackCount = ReadInt16BigEndian(reader);
        
        // ticks per quarter note
        ticksPerQuarter = ReadInt16BigEndian(reader);
        
        return true;
    }
    
    private static void ParseTrack(BinaryReader reader, List<RawNoteEvent> events, ref int microsecondsPerQuarter)
    {
        // read "MTrk"
        string chunkId = new string(reader.ReadChars(4));
        if (chunkId != TRACK_CHUNK_ID)
        {
            Debug.LogWarning($"[MidiParser] Expected MTrk, got {chunkId}");
            return;
        }
        
        int trackSize = ReadInt32BigEndian(reader);
        long trackEnd = reader.BaseStream.Position + trackSize;
        
        int absoluteTicks = 0;
        byte lastStatus = 0;
        
        while (reader.BaseStream.Position < trackEnd)
        {
            // read delta time (variable length)
            int deltaTime = ReadVariableLength(reader);
            absoluteTicks += deltaTime;
            
            // read status byte
            byte statusByte = reader.ReadByte();
            
            // handle running status (reuse last status if < 0x80)
            if (statusByte < 0x80)
            {
                // running status: this byte is actually data, not status
                reader.BaseStream.Position--; // step back
                statusByte = lastStatus;
            }
            else
            {
                lastStatus = statusByte;
            }
            
            byte messageType = (byte)(statusByte & 0xF0);
            byte channel = (byte)(statusByte & 0x0F);
            
            // handle note events
            if (messageType == NOTE_ON || messageType == NOTE_OFF)
            {
                byte note = reader.ReadByte();
                byte velocity = reader.ReadByte();
                
                // note on with velocity 0 is treated as note off
                if (messageType == NOTE_ON && velocity > 0)
                {
                    events.Add(new RawNoteEvent
                    {
                        ticks = absoluteTicks,
                        note = note,
                        velocity = velocity,
                        channel = channel
                    });
                }
            }
            // handle meta events
            else if (statusByte == META_EVENT)
            {
                byte metaType = reader.ReadByte();
                int length = ReadVariableLength(reader);
                
                // set tempo event
                if (metaType == SET_TEMPO)
                {
                    microsecondsPerQuarter = ReadInt24BigEndian(reader);
                    Debug.Log($"[MidiParser] Tempo change: {60000000.0 / microsecondsPerQuarter} BPM");
                }
                else
                {
                    // skip other meta events
                    reader.BaseStream.Position += length;
                }
            }
            // handle other channel messages
            else if (messageType >= 0x80 && messageType <= 0xE0)
            {
                // most channel messages have 2 data bytes
                reader.ReadByte();
                if (messageType != 0xC0 && messageType != 0xD0) // program change and channel pressure have only 1 data byte
                {
                    reader.ReadByte();
                }
            }
            // handle system exclusive
            else if (statusByte == 0xF0 || statusByte == 0xF7)
            {
                int length = ReadVariableLength(reader);
                reader.BaseStream.Position += length;
            }
        }
    }
    
    private static void ConvertToAbsoluteTime(List<RawNoteEvent> events, int ticksPerQuarter, int microsecondsPerQuarter)
    {
        double microsecondsPerTick = (double)microsecondsPerQuarter / ticksPerQuarter;
        
        foreach (var evt in events)
        {
            evt.timeSeconds = (float)(evt.ticks * microsecondsPerTick / 1000000.0);
        }
    }
    
    private static List<MidiNote> MapNotesToSpawnPoints(List<RawNoteEvent> rawEvents, int spawnPointCount, int minNote, int maxNote)
    {
        List<MidiNote> result = new List<MidiNote>();
        
        int noteRange = maxNote - minNote;
        if (noteRange <= 0) noteRange = 1; // safety
        
        foreach (var raw in rawEvents)
        {
            // skip notes outside our range
            if (raw.note < minNote || raw.note > maxNote)
                continue;
            
            // map note pitch to spawn point (linear mapping)
            float normalizedPitch = (float)(raw.note - minNote) / noteRange;
            int spawnPoint = Mathf.Clamp(Mathf.RoundToInt(normalizedPitch * (spawnPointCount - 1)), 0, spawnPointCount - 1);
            
            // map velocity to intensity (0-127 -> 0-1)
            float intensity = raw.velocity / 127f;
            
            // choose sprite based on pitch range (divide range into thirds)
            int noteType = Mathf.Clamp((int)(normalizedPitch * 3f), 0, 2);
            
            result.Add(new MidiNote(raw.timeSeconds, spawnPoint, noteType, intensity));
        }
        
        // sort by time for efficient lookup later
        result.Sort((a, b) => a.timeSeconds.CompareTo(b.timeSeconds));
        
        return result;
    }
    
    // helper to read variable-length quantity (MIDI format)
    private static int ReadVariableLength(BinaryReader reader)
    {
        int value = 0;
        byte b;
        
        do
        {
            b = reader.ReadByte();
            value = (value << 7) | (b & 0x7F);
        } while ((b & 0x80) != 0);
        
        return value;
    }
    
    // big-endian integer readers
    private static int ReadInt32BigEndian(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        Array.Reverse(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }
    
    private static int ReadInt24BigEndian(BinaryReader reader)
    {
        byte[] bytes = new byte[4];
        bytes[1] = reader.ReadByte();
        bytes[2] = reader.ReadByte();
        bytes[3] = reader.ReadByte();
        Array.Reverse(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }
    
    private static int ReadInt16BigEndian(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(2);
        Array.Reverse(bytes);
        return BitConverter.ToInt16(bytes, 0);
    }
    
    // internal structure for raw MIDI events before conversion
    private class RawNoteEvent
    {
        public int ticks;
        public float timeSeconds;
        public byte note;
        public byte velocity;
        public byte channel;
    }
}
#endif