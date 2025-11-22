using UnityEngine;
using UnityEditor;
using System.Linq;
using JamesKJamKit.Services.Music;
using System.Collections.Generic;

#if UNITY_EDITOR
[CustomEditor(typeof(MidiTrackData))]
public class MidiTrackDataEditor : Editor
{
    private bool showAllNotes = false;
    private int previewNoteCount = 20;
    private Vector2 scrollPosition;
    
    public override void OnInspectorGUI()
    {
        MidiTrackData data = (MidiTrackData)target;
        
        // default inspector for main fields
        DrawDefaultInspector();
        
        GUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Space(10);
        
        // statistics section
        EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Total Notes: {data.NoteCount}");
        EditorGUILayout.LabelField($"Duration: {data.trackDurationSeconds:F2} seconds");
        
        if (data.HasNotes)
        {
            // calculate average notes per second
            float notesPerSecond = data.NoteCount / Mathf.Max(0.1f, data.trackDurationSeconds);
            EditorGUILayout.LabelField($"Avg Notes/Second: {notesPerSecond:F2}");
            
            // find note distribution across spawn points
            var spawnPointCounts = data.notes.GroupBy(n => n.spawnPointIndex)
                .OrderBy(g => g.Key)
                .Select(g => new { Point = g.Key, Count = g.Count() })
                .ToList();
            
            GUILayout.Space(5);
            EditorGUILayout.LabelField("Spawn Point Distribution:", EditorStyles.boldLabel);
            foreach (var point in spawnPointCounts)
            {
                float percentage = (point.Count / (float)data.NoteCount) * 100f;
                EditorGUILayout.LabelField($"  Point {point.Point}: {point.Count} notes ({percentage:F1}%)");
            }
            
            // intensity range
            if (data.notes.Count > 0)
            {
                float minIntensity = data.notes.Min(n => n.intensity);
                float maxIntensity = data.notes.Max(n => n.intensity);
                float avgIntensity = data.notes.Average(n => n.intensity);
                
                GUILayout.Space(5);
                EditorGUILayout.LabelField($"Intensity Range: {minIntensity:F2} - {maxIntensity:F2} (avg: {avgIntensity:F2})");
            }
        }
        
        GUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Space(10);
        
        // note preview section
        if (data.HasNotes)
        {
            EditorGUILayout.LabelField("Note Preview", EditorStyles.boldLabel);
            
            showAllNotes = EditorGUILayout.Toggle("Show All Notes", showAllNotes);
            
            if (!showAllNotes)
            {
                previewNoteCount = EditorGUILayout.IntSlider("Preview Count", previewNoteCount, 5, 100);
            }
            
            GUILayout.Space(5);
            
            int displayCount = showAllNotes ? data.notes.Count : Mathf.Min(previewNoteCount, data.notes.Count);
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // header
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Time", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("Spawn", EditorStyles.boldLabel, GUILayout.Width(50));
            EditorGUILayout.LabelField("Type", EditorStyles.boldLabel, GUILayout.Width(50));
            EditorGUILayout.LabelField("Intensity", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("", GUILayout.Width(60)); // delete button column
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            
            // notes
            List<int> notesToDelete = new List<int>();
            
            for (int i = 0; i < displayCount; i++)
            {
                var note = data.notes[i];
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{note.timeSeconds:F3}s", GUILayout.Width(60));
                EditorGUILayout.LabelField($"{note.spawnPointIndex}", GUILayout.Width(50));
                EditorGUILayout.LabelField($"{note.noteTypeIndex}", GUILayout.Width(50));
                
                // visual intensity bar
                Rect rect = GUILayoutUtility.GetRect(60, 18);
                EditorGUI.ProgressBar(rect, note.intensity, $"{note.intensity:F2}");
                
                // delete button
                if (GUILayout.Button("X", GUILayout.Width(30)))
                {
                    notesToDelete.Add(i);
                }
                
                EditorGUILayout.EndHorizontal();
            }
            
            // delete notes after iteration to avoid modifying collection during iteration
            if (notesToDelete.Count > 0)
            {
                Undo.RecordObject(data, "Delete MIDI Notes");
                
                // delete in reverse order to maintain indices
                for (int i = notesToDelete.Count - 1; i >= 0; i--)
                {
                    data.notes.RemoveAt(notesToDelete[i]);
                }
                
                EditorUtility.SetDirty(data);
                Debug.Log($"[MidiTrackDataEditor] Deleted {notesToDelete.Count} note(s)");
            }
            
            if (!showAllNotes && data.notes.Count > previewNoteCount)
            {
                EditorGUILayout.LabelField($"... and {data.notes.Count - previewNoteCount} more notes");
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
            
            GUILayout.Space(10);
            
            // bulk deletion options
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Delete All Notes", GUILayout.Height(25)))
            {
                if (EditorUtility.DisplayDialog("Delete All Notes", 
                    $"Are you sure you want to delete all {data.notes.Count} notes?", 
                    "Delete", "Cancel"))
                {
                    Undo.RecordObject(data, "Delete All MIDI Notes");
                    data.notes.Clear();
                    EditorUtility.SetDirty(data);
                    Debug.Log("[MidiTrackDataEditor] Deleted all notes");
                }
            }
            
            if (GUILayout.Button("Delete by Time Range", GUILayout.Height(25)))
            {
                ShowDeleteByTimeRangeDialog(data);
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Delete by Spawn Point", GUILayout.Height(25)))
            {
                ShowDeleteBySpawnPointDialog(data);
            }
            
            if (GUILayout.Button("Delete by Note Type", GUILayout.Height(25)))
            {
                ShowDeleteByNoteTypeDialog(data);
            }
            
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.HelpBox("No notes in this MIDI data. Use Tools > MIDI Parser to parse a MIDI file.", MessageType.Info);
        }
        
        GUILayout.Space(10);
        
        // reparse button
        if (GUILayout.Button("Open MIDI Parser"))
        {
            MidiParserWindow.ShowWindow();
        }
    }
    
    void ShowDeleteByTimeRangeDialog(MidiTrackData data)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Delete First Second"), false, () => DeleteTimeRange(data, 0f, 1f));
        menu.AddItem(new GUIContent("Delete Last Second"), false, () => DeleteTimeRange(data, data.trackDurationSeconds - 1f, data.trackDurationSeconds));
        menu.AddItem(new GUIContent("Delete First Half"), false, () => DeleteTimeRange(data, 0f, data.trackDurationSeconds * 0.5f));
        menu.AddItem(new GUIContent("Delete Second Half"), false, () => DeleteTimeRange(data, data.trackDurationSeconds * 0.5f, data.trackDurationSeconds));
        menu.ShowAsContext();
    }
    
    void ShowDeleteBySpawnPointDialog(MidiTrackData data)
    {
        GenericMenu menu = new GenericMenu();
        
        // get all unique spawn points
        var spawnPoints = data.notes.Select(n => n.spawnPointIndex).Distinct().OrderBy(x => x).ToList();
        
        foreach (int spawnPoint in spawnPoints)
        {
            int count = data.notes.Count(n => n.spawnPointIndex == spawnPoint);
            menu.AddItem(new GUIContent($"Delete Spawn Point {spawnPoint} ({count} notes)"), 
                false, 
                () => DeleteBySpawnPoint(data, spawnPoint));
        }
        
        menu.ShowAsContext();
    }
    
    void ShowDeleteByNoteTypeDialog(MidiTrackData data)
    {
        GenericMenu menu = new GenericMenu();
        
        // get all unique note types
        var noteTypes = data.notes.Select(n => n.noteTypeIndex).Distinct().OrderBy(x => x).ToList();
        
        string[] typeNames = { "Type 0 (Bass/Low)", "Type 1 (Mid)", "Type 2 (Treble/High)" };
        
        foreach (int noteType in noteTypes)
        {
            int count = data.notes.Count(n => n.noteTypeIndex == noteType);
            string typeName = noteType >= 0 && noteType < typeNames.Length ? typeNames[noteType] : $"Type {noteType}";
            menu.AddItem(new GUIContent($"Delete {typeName} ({count} notes)"), 
                false, 
                () => DeleteByNoteType(data, noteType));
        }
        
        menu.ShowAsContext();
    }
    
    void DeleteTimeRange(MidiTrackData data, float startTime, float endTime)
    {
        int countBefore = data.notes.Count;
        
        Undo.RecordObject(data, "Delete MIDI Notes by Time Range");
        data.notes.RemoveAll(n => n.timeSeconds >= startTime && n.timeSeconds <= endTime);
        EditorUtility.SetDirty(data);
        
        int deleted = countBefore - data.notes.Count;
        Debug.Log($"[MidiTrackDataEditor] Deleted {deleted} note(s) between {startTime:F2}s and {endTime:F2}s");
    }
    
    void DeleteBySpawnPoint(MidiTrackData data, int spawnPoint)
    {
        int countBefore = data.notes.Count;
        
        Undo.RecordObject(data, "Delete MIDI Notes by Spawn Point");
        data.notes.RemoveAll(n => n.spawnPointIndex == spawnPoint);
        EditorUtility.SetDirty(data);
        
        int deleted = countBefore - data.notes.Count;
        Debug.Log($"[MidiTrackDataEditor] Deleted {deleted} note(s) from spawn point {spawnPoint}");
    }
    
    void DeleteByNoteType(MidiTrackData data, int noteType)
    {
        int countBefore = data.notes.Count;
        
        Undo.RecordObject(data, "Delete MIDI Notes by Note Type");
        data.notes.RemoveAll(n => n.noteTypeIndex == noteType);
        EditorUtility.SetDirty(data);
        
        int deleted = countBefore - data.notes.Count;
        string[] typeNames = { "Bass/Low", "Mid", "Treble/High" };
        string typeName = noteType >= 0 && noteType < typeNames.Length ? typeNames[noteType] : $"Type {noteType}";
        Debug.Log($"[MidiTrackDataEditor] Deleted {deleted} note(s) of type {noteType} ({typeName})");
    }
}
#endif