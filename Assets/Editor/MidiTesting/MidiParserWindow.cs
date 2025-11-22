using UnityEngine;
using UnityEditor;
using System.IO;
using JamesKJamKit.Services.Music;

#if UNITY_EDITOR
public class MidiParserWindow : EditorWindow
{
    private string midiFilePath = "";
    private AudioClip targetAudioClip;
    private int spawnPointCount = 5;
    private int minMidiNote = 60; // Middle C
    private int maxMidiNote = 84; // 2 octaves
    
    private string lastParseResult = "";
    private bool lastParseSuccess = false;
    
    [MenuItem("Tools/MIDI Parser")]
    public static void ShowWindow()
    {
        GetWindow<MidiParserWindow>("MIDI Parser");
    }
    
    void OnGUI()
    {
        GUILayout.Label("MIDI to Unity Parser", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        // MIDI file selection
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("MIDI File:", GUILayout.Width(100));
        midiFilePath = EditorGUILayout.TextField(midiFilePath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFilePanel("Select MIDI File", "", "mid,midi");
            if (!string.IsNullOrEmpty(path))
            {
                midiFilePath = path;
            }
        }
        EditorGUILayout.EndHorizontal();
        
        // audio clip reference
        targetAudioClip = (AudioClip)EditorGUILayout.ObjectField("Audio Clip", targetAudioClip, typeof(AudioClip), false);
        
        GUILayout.Space(10);
        GUILayout.Label("Mapping Settings", EditorStyles.boldLabel);
        
        // spawn point count
        spawnPointCount = EditorGUILayout.IntSlider("Spawn Point Count", spawnPointCount, 1, 20);
        
        // note range
        minMidiNote = EditorGUILayout.IntSlider("Min MIDI Note", minMidiNote, 0, 127);
        maxMidiNote = EditorGUILayout.IntSlider("Max MIDI Note", maxMidiNote, 0, 127);
        
        // ensure min < max
        if (minMidiNote >= maxMidiNote)
        {
            EditorGUILayout.HelpBox("Min note must be less than max note!", MessageType.Warning);
        }
        
        GUILayout.Space(10);
        
        // info box
        EditorGUILayout.HelpBox(
            "This will parse the MIDI file and create a MidiTrackData asset.\n\n" +
            "- Notes outside the MIDI range will be ignored\n" +
            "- Note pitch maps to spawn point position\n" +
            "- Note velocity maps to intensity (0-1)\n" +
            "- Sprite type is chosen based on pitch range",
            MessageType.Info);
        
        GUILayout.Space(10);
        
        // parse button
        GUI.enabled = !string.IsNullOrEmpty(midiFilePath) && File.Exists(midiFilePath);
        if (GUILayout.Button("Parse MIDI and Create Asset", GUILayout.Height(30)))
        {
            ParseAndCreateAsset();
        }
        GUI.enabled = true;
        
        // show last result
        if (!string.IsNullOrEmpty(lastParseResult))
        {
            GUILayout.Space(10);
            MessageType msgType = lastParseSuccess ? MessageType.Info : MessageType.Error;
            EditorGUILayout.HelpBox(lastParseResult, msgType);
        }
    }
    
    void ParseAndCreateAsset()
    {
        // validate
        if (string.IsNullOrEmpty(midiFilePath) || !File.Exists(midiFilePath))
        {
            lastParseResult = "Invalid MIDI file path!";
            lastParseSuccess = false;
            return;
        }
        
        if (minMidiNote >= maxMidiNote)
        {
            lastParseResult = "Min MIDI note must be less than max MIDI note!";
            lastParseSuccess = false;
            return;
        }
        
        // parse the MIDI file
        var parseResult = MidiParser.ParseMidiFile(midiFilePath, spawnPointCount, minMidiNote, maxMidiNote);
        
        if (!parseResult.success)
        {
            lastParseResult = $"Parse failed: {parseResult.errorMessage}";
            lastParseSuccess = false;
            return;
        }
        
        // create the ScriptableObject asset
        MidiTrackData asset = ScriptableObject.CreateInstance<MidiTrackData>();
        asset.audioClip = targetAudioClip;
        asset.notes = parseResult.notes;
        asset.trackDurationSeconds = parseResult.durationSeconds;
        asset.spawnPointCount = spawnPointCount;
        asset.minMidiNote = minMidiNote;
        asset.maxMidiNote = maxMidiNote;
        asset.sourceMidiFile = Path.GetFileName(midiFilePath);
        asset.parseDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        // determine save path
        string defaultName = targetAudioClip != null 
            ? $"{targetAudioClip.name}_MidiData" 
            : Path.GetFileNameWithoutExtension(midiFilePath) + "_MidiData";
        
        string savePath = EditorUtility.SaveFilePanelInProject(
            "Save MIDI Track Data",
            defaultName,
            "asset",
            "Choose where to save the MidiTrackData asset");
        
        if (string.IsNullOrEmpty(savePath))
        {
            lastParseResult = "Save cancelled.";
            lastParseSuccess = false;
            return;
        }
        
        // save the asset
        AssetDatabase.CreateAsset(asset, savePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        // select the created asset
        EditorGUIUtility.PingObject(asset);
        Selection.activeObject = asset;
        
        // success message
        lastParseResult = $"Success! Created asset with {parseResult.noteCount} notes.\n" +
                         $"Duration: {parseResult.durationSeconds:F2} seconds\n" +
                         $"Saved to: {savePath}";
        lastParseSuccess = true;
        
        Debug.Log($"[MidiParserWindow] {lastParseResult}");
    }
    
    // helper to show note name from MIDI number
    private string GetNoteName(int midiNote)
    {
        string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int octave = (midiNote / 12) - 1;
        int noteIndex = midiNote % 12;
        return noteNames[noteIndex] + octave;
    }
}
#endif