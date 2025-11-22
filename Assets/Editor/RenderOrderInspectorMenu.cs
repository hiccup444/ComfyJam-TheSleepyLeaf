// Assets/Editor/RenderOrderInspector.cs
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RenderOrderInspectorMenu
{
    [MenuItem("Tools/Render Order Inspector…")]
    public static void Open() => RenderOrderInspector.ShowWindow();
}

public sealed class RenderOrderInspector : EditorWindow
{
    // Row model
    private sealed class Row
    {
        public Renderer renderer;
        public bool isMask;
        public string namePath;

        // Common (for render-order sort)
        public int layerIndex;      // from renderer.sortingLayerID
        public int sortingOrder;    // from renderer.sortingOrder
        public float z;

        // SpriteMask-specific editable range
        public int backLayerIndex;   // from SpriteMask.backSortingLayerID
        public int backOrder;        // from SpriteMask.backSortingOrder
        public int frontLayerIndex;  // from SpriteMask.frontSortingLayerID
        public int frontOrder;       // from SpriteMask.frontSortingOrder
    }

    // UI state
    private Vector2 _scroll;
    private string _search = "";
    private bool _includeInactive = true;
    private bool _activeSceneOnly = false;
    private bool _autoRefresh = true;
    private float _zNudgeStep = 0.1f;
    private bool _wrapNames = false;
    private bool _includeSpriteMasks = true; // NEW

    // Column widths (name column auto-fits)
    private const float COL_LAYER = 160f;
    private const float COL_ORDER = 70f;
    private const float COL_Z     = 100f;
    private const float COL_ACT   = 300f; // widened to accommodate extra mask buttons
    private const float COL_MARGIN = 40f;

    // Styles
    private GUIStyle _nameStyleSingle;
    private GUIStyle _nameStyleWrap;
    private GUIStyle _miniLabel;

    // Layer data
    private string[] _layerNames = Array.Empty<string>();
    private int[] _layerIds = Array.Empty<int>();
    private Dictionary<int, int> _layerIdToIndex = new();

    // Data
    private List<Row> _rows = new();
    private double _nextAutoRefresh;
    private const double AutoRefreshInterval = 0.5;

    public static void ShowWindow()
    {
        var win = GetWindow<RenderOrderInspector>("Render Order");
        win.minSize = new Vector2(760, 340);
        win.InitStyles();
        win.RefreshLayers();
        win.RefreshRows();
        win.Show();
    }

    private void OnEnable()
    {
        InitStyles();
        RefreshLayers();
        RefreshRows();
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        EditorApplication.update -= OnEditorUpdate;
    }

    private void InitStyles()
    {
        _nameStyleSingle = new GUIStyle(EditorStyles.label)
        {
            clipping = TextClipping.Clip,
            wordWrap = false,
            alignment = TextAnchor.MiddleLeft,
            richText = false
        };
        _nameStyleWrap = new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            clipping = TextClipping.Overflow,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft,
            richText = false
        };
        _miniLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
    }

    private void OnHierarchyChanged()
    {
        if (_autoRefresh) ScheduleAutoRefresh();
    }

    private void OnEditorUpdate()
    {
        if (_autoRefresh && EditorApplication.timeSinceStartup >= _nextAutoRefresh)
        {
            _nextAutoRefresh = double.MaxValue;
            RefreshRows();
            Repaint();
        }
    }

    private void ScheduleAutoRefresh() => _nextAutoRefresh = EditorApplication.timeSinceStartup + AutoRefreshInterval;

    private void RefreshLayers()
    {
        var layers = SortingLayer.layers;
        _layerNames = layers.Select(l => l.name).ToArray();
        _layerIds = layers.Select(l => l.id).ToArray();
        _layerIdToIndex = new Dictionary<int, int>(_layerIds.Length);
        for (int i = 0; i < _layerIds.Length; i++) _layerIdToIndex[_layerIds[i]] = i;
    }

    private static string GetPath(GameObject go)
    {
        var t = go.transform;
        var path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    private bool SceneFilter(GameObject go)
    {
        if (!_activeSceneOnly) return true;
        return go.scene == SceneManager.GetActiveScene();
    }

    private IEnumerable<Renderer> FindAllRenderers()
    {
        return Resources.FindObjectsOfTypeAll<Renderer>()
            .Where(r =>
            {
                if (EditorUtility.IsPersistent(r)) return false; // skip Project assets
                var go = r.gameObject;

                if (!_includeInactive && !go.activeInHierarchy) return false;
                if (!SceneFilter(go)) return false;
                if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) return false;

                if (!_includeSpriteMasks && r is SpriteMask) return false;

                return true;
            });
    }

    private void RefreshRows()
    {
        var list = new List<Row>(256);
        foreach (var r in FindAllRenderers())
        {
            int layerIdx = _layerIdToIndex.TryGetValue(r.sortingLayerID, out var idx) ? idx : int.MaxValue;

            var row = new Row
            {
                renderer = r,
                isMask = r is SpriteMask,
                namePath = $"{r.gameObject.scene.name} → {GetPath(r.gameObject)}",
                layerIndex = layerIdx,
                sortingOrder = r.sortingOrder,
                z = r.transform.position.z
            };

            if (row.isMask)
            {
                var m = (SpriteMask)r;
                row.backLayerIndex  = _layerIdToIndex.TryGetValue(m.backSortingLayerID, out var bi) ? bi : layerIdx;
                row.frontLayerIndex = _layerIdToIndex.TryGetValue(m.frontSortingLayerID, out var fi) ? fi : layerIdx;
                row.backOrder  = m.backSortingOrder;
                row.frontOrder = m.frontSortingOrder;
            }

            list.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var s = _search.Trim();
            list = list.Where(row =>
                    row.namePath.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.renderer.GetType().Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        // Sort by actual render order: SortingLayer order → Order in Layer → Z → name
        list.Sort((a, b) =>
        {
            int c = a.layerIndex.CompareTo(b.layerIndex);
            if (c != 0) return c;
            c = a.sortingOrder.CompareTo(b.sortingOrder);
            if (c != 0) return c;
            c = a.z.CompareTo(b.z);
            if (c != 0) return c;
            return string.Compare(a.namePath, b.namePath, StringComparison.OrdinalIgnoreCase);
        });

        _rows = list;
    }

    private float NameColWidth()
    {
        return Mathf.Max(260f, position.width - (COL_LAYER + COL_ORDER + COL_Z + COL_ACT + COL_MARGIN));
    }

    private void HeaderGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            _search = GUILayout.TextField(_search, GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(160));
            if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(22)))
            {
                _search = "";
                RefreshRows();
            }

            GUILayout.Space(8);
            GUILayout.Label("ΔZ", GUILayout.Width(22));
            _zNudgeStep = EditorGUILayout.FloatField(_zNudgeStep, GUILayout.Width(70));
            if (Mathf.Approximately(_zNudgeStep, 0f)) _zNudgeStep = 0.1f;

            GUILayout.Space(8);
            _wrapNames = GUILayout.Toggle(_wrapNames, "Wrap Names", EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();

            _includeSpriteMasks = GUILayout.Toggle(_includeSpriteMasks, "Include Sprite Masks", EditorStyles.toolbarButton); // NEW
            _includeInactive = GUILayout.Toggle(_includeInactive, "Include Inactive", EditorStyles.toolbarButton);
            _activeSceneOnly = GUILayout.Toggle(_activeSceneOnly, "Active Scene Only", EditorStyles.toolbarButton);
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto-Refresh", EditorStyles.toolbarButton);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                RefreshRows();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Renderer / Path", EditorStyles.boldLabel, GUILayout.Width(NameColWidth()));
            GUILayout.Label("Layer", EditorStyles.boldLabel, GUILayout.Width(COL_LAYER));
            GUILayout.Label("Order", EditorStyles.boldLabel, GUILayout.Width(COL_ORDER));
            GUILayout.Label("Z", EditorStyles.boldLabel, GUILayout.Width(COL_Z));
            GUILayout.FlexibleSpace();
            GUILayout.Label("Actions", EditorStyles.boldLabel, GUILayout.Width(COL_ACT));
        }
        EditorGUILayout.Space(2);
    }

    private void RowGUI(Row row)
    {
        var r = row.renderer;
        if (r == null) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            // Name/path button (auto-fit)
            var nameWidth = NameColWidth();
            var typeName = ObjectNames.NicifyVariableName(r.GetType().Name);
            var label = $"{typeName}  —  {row.namePath}";
            var content = new GUIContent(label, label);

            if (GUILayout.Button(content, _wrapNames ? _nameStyleWrap : _nameStyleSingle, GUILayout.Width(nameWidth)))
            {
                Selection.activeObject = r.gameObject;
                EditorGUIUtility.PingObject(r.gameObject);
            }

            if (row.isMask)
            {
                // SPRITEMASK UI — show editable back→front range
                var m = (SpriteMask)r;

                // Back (layer, order)
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(COL_LAYER + COL_ORDER)))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("Back", _miniLabel, GUILayout.Width(36));
                        EditorGUI.BeginChangeCheck();
                        int newBackLayerIdx = EditorGUILayout.Popup(row.backLayerIndex, _layerNames, GUILayout.Width(COL_LAYER - 40));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(m, "SpriteMask Back Layer");
                            m.backSortingLayerID = _layerIds[newBackLayerIdx];
                            row.backLayerIndex = newBackLayerIdx;
                            EditorUtility.SetDirty(m);
                        }

                        EditorGUI.BeginChangeCheck();
                        int newBackOrder = EditorGUILayout.IntField(row.backOrder, GUILayout.Width(COL_ORDER));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(m, "SpriteMask Back Order");
                            m.backSortingOrder = newBackOrder;
                            row.backOrder = newBackOrder;
                            EditorUtility.SetDirty(m);
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("Front", _miniLabel, GUILayout.Width(36));
                        EditorGUI.BeginChangeCheck();
                        int newFrontLayerIdx = EditorGUILayout.Popup(row.frontLayerIndex, _layerNames, GUILayout.Width(COL_LAYER - 40));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(m, "SpriteMask Front Layer");
                            m.frontSortingLayerID = _layerIds[newFrontLayerIdx];
                            row.frontLayerIndex = newFrontLayerIdx;
                            EditorUtility.SetDirty(m);
                        }

                        EditorGUI.BeginChangeCheck();
                        int newFrontOrder = EditorGUILayout.IntField(row.frontOrder, GUILayout.Width(COL_ORDER));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(m, "SpriteMask Front Order");
                            m.frontSortingOrder = newFrontOrder;
                            row.frontOrder = newFrontOrder;
                            EditorUtility.SetDirty(m);
                        }
                    }
                }

                // Z (editable, same as others)
                var t = r.transform;
                float currentZ = t.position.z;
                EditorGUI.BeginChangeCheck();
                float newZ = EditorGUILayout.FloatField(currentZ, GUILayout.Width(COL_Z));
                if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newZ, currentZ))
                {
                    Undo.RecordObject(t, "Change Z");
                    var p = t.position; p.z = newZ; t.position = p;
                    row.z = newZ;
                    if (_autoRefresh) ScheduleAutoRefresh();
                }
            }
            else
            {
                // NON-MASK renderer: Sorting Layer popup
                int currentLayerIndex = _layerIdToIndex.TryGetValue(r.sortingLayerID, out var li) ? li : -1;
                EditorGUI.BeginChangeCheck();
                int newLayerIndex = EditorGUILayout.Popup(currentLayerIndex, _layerNames, GUILayout.Width(COL_LAYER));
                if (EditorGUI.EndChangeCheck() && newLayerIndex >= 0 && newLayerIndex < _layerIds.Length)
                {
                    Undo.RecordObject(r, "Change Sorting Layer");
                    r.sortingLayerID = _layerIds[newLayerIndex];
                    EditorUtility.SetDirty(r);
                    row.layerIndex = newLayerIndex;
                    if (_autoRefresh) ScheduleAutoRefresh();
                }

                // Order in Layer
                EditorGUI.BeginChangeCheck();
                int newOrder = EditorGUILayout.IntField(r.sortingOrder, GUILayout.Width(COL_ORDER));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(r, "Change Order In Layer");
                    r.sortingOrder = newOrder;
                    EditorUtility.SetDirty(r);
                    row.sortingOrder = newOrder;
                    if (_autoRefresh) ScheduleAutoRefresh();
                }

                // Z (editable)
                var t = r.transform;
                float currentZ = t.position.z;
                EditorGUI.BeginChangeCheck();
                float newZ = EditorGUILayout.FloatField(currentZ, GUILayout.Width(COL_Z));
                if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newZ, currentZ))
                {
                    Undo.RecordObject(t, "Change Z");
                    var p = t.position; p.z = newZ; t.position = p;
                    row.z = newZ;
                    if (_autoRefresh) ScheduleAutoRefresh();
                }
            }

            GUILayout.FlexibleSpace();

            // Actions — common
            if (GUILayout.Button("−1", GUILayout.Width(36))) BumpOrder(r, -1, row);
            if (GUILayout.Button("+1", GUILayout.Width(36))) BumpOrder(r, +1, row);
            if (GUILayout.Button("Top", GUILayout.Width(44))) SetOrderExtreme(r, toTop: true, row);
            if (GUILayout.Button("Bottom", GUILayout.Width(60))) SetOrderExtreme(r, toTop: false, row);

            GUILayout.Space(8);

            if (GUILayout.Button("−ΔZ", GUILayout.Width(44))) BumpZ(r, -_zNudgeStep, row);
            if (GUILayout.Button("+ΔZ", GUILayout.Width(44))) BumpZ(r, +_zNudgeStep, row);
            if (GUILayout.Button("Front Z", GUILayout.Width(64))) SetZExtreme(r, front:true, row);
            if (GUILayout.Button("Back Z", GUILayout.Width(60))) SetZExtreme(r, front:false, row);

            GUILayout.Space(8);

            // Quick helper for SpriteMask: range → enclose this mask’s own layer/order
            if (row.isMask)
            {
                if (GUILayout.Button("Range: Enclose Here", GUILayout.Width(130)))
                {
                    var m = (SpriteMask)r;
                    Undo.RecordObject(m, "SpriteMask Range Enclose");
                    int curIdx = _layerIdToIndex.TryGetValue(r.sortingLayerID, out var idx) ? idx : 0;
                    m.backSortingLayerID = _layerIds[curIdx];
                    m.frontSortingLayerID = _layerIds[curIdx];
                    m.backSortingOrder = r.sortingOrder - 1;
                    m.frontSortingOrder = r.sortingOrder + 1;

                    row.backLayerIndex = curIdx;
                    row.frontLayerIndex = curIdx;
                    row.backOrder = m.backSortingOrder;
                    row.frontOrder = m.frontSortingOrder;
                    EditorUtility.SetDirty(m);
                }
            }

            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                Selection.activeObject = r.gameObject;
                EditorGUIUtility.PingObject(r.gameObject);
            }
        }
    }

    private void BumpOrder(Renderer r, int delta, Row row)
    {
        Undo.RecordObject(r, "Nudge Order In Layer");
        r.sortingOrder += delta;
        row.sortingOrder = r.sortingOrder;
        EditorUtility.SetDirty(r);
        if (_autoRefresh) ScheduleAutoRefresh();
    }

    private void SetOrderExtreme(Renderer r, bool toTop, Row row)
    {
        var layerId = r.sortingLayerID;
        int extreme = toTop ? int.MinValue : int.MaxValue;

        foreach (var rr in _rows)
        {
            var other = rr.renderer;
            if (other == null) continue;
            if (other.sortingLayerID != layerId) continue;
            extreme = toTop
                ? Mathf.Max(extreme, other.sortingOrder)
                : Mathf.Min(extreme, other.sortingOrder);
        }

        int target = toTop ? extreme + 1 : extreme - 1;

        Undo.RecordObject(r, toTop ? "Send To Top" : "Send To Bottom");
        r.sortingOrder = target;
        row.sortingOrder = target;
        EditorUtility.SetDirty(r);
        if (_autoRefresh) ScheduleAutoRefresh();
    }

    private void BumpZ(Renderer r, float delta, Row row)
    {
        var t = r.transform;
        Undo.RecordObject(t, "Nudge Z");
        var p = t.position; p.z += delta; t.position = p;
        row.z = p.z;
        if (_autoRefresh) ScheduleAutoRefresh();
    }

    // Jump Z to extreme among same Sorting Layer & Order (Z tiebreaker)
    private void SetZExtreme(Renderer r, bool front, Row row)
    {
        int layerId = r.sortingLayerID;
        int order = r.sortingOrder;

        float extreme = front ? float.NegativeInfinity : float.PositiveInfinity;

        foreach (var rr in _rows)
        {
            var other = rr.renderer;
            if (other == null) continue;
            if (other.sortingLayerID != layerId) continue;
            if (other.sortingOrder != order) continue;
            float oz = other.transform.position.z;
            extreme = front ? Mathf.Max(extreme, oz) : Mathf.Min(extreme, oz);
        }

        if (float.IsInfinity(extreme))
            extreme = r.transform.position.z;

        float epsilon = Mathf.Max(1e-4f, _zNudgeStep * 0.5f);
        float target = front ? extreme + epsilon : extreme - epsilon;

        var t = r.transform;
        Undo.RecordObject(t, front ? "Bring To Front (Z)" : "Send To Back (Z)");
        var p = t.position; p.z = target; t.position = p;
        row.z = target;
        if (_autoRefresh) ScheduleAutoRefresh();
    }

    // ===== EXPORT / CLIPBOARD =====
    private string BuildTsvForRows(IEnumerable<Row> rows)
    {
        // Columns:
        // idx,Type,ScenePath,SortingLayer,Order,Z,IsMask,BackLayer,BackOrder,FrontLayer,FrontOrder,InstanceId
        var sb = new StringBuilder(16 * 1024);
        sb.AppendLine("idx\tType\tScenePath\tSortingLayer\tOrder\tZ\tIsMask\tBackLayer\tBackOrder\tFrontLayer\tFrontOrder\tInstanceId");

        int i = 0;
        foreach (var row in rows)
        {
            var r = row.renderer;
            if (r == null) continue;

            string type = r.GetType().Name;
            string layerName = SafeLayerName(row.layerIndex);
            string backLayer = row.isMask ? SafeLayerName(row.backLayerIndex) : "";
            string frontLayer = row.isMask ? SafeLayerName(row.frontLayerIndex) : "";
            int instanceId = r.gameObject.GetInstanceID();

            sb.Append(i++).Append('\t')
              .Append(type).Append('\t')
              .Append(row.namePath).Append('\t')
              .Append(layerName).Append('\t')
              .Append(row.sortingOrder).Append('\t')
              .Append(row.z.ToString("0.###")).Append('\t')
              .Append(row.isMask ? "1" : "0").Append('\t')
              .Append(backLayer).Append('\t')
              .Append(row.isMask ? row.backOrder : 0).Append('\t')
              .Append(frontLayer).Append('\t')
              .Append(row.isMask ? row.frontOrder : 0).Append('\t')
              .Append(instanceId)
              .Append('\n');
        }
        return sb.ToString();
    }

    private string SafeLayerName(int idx)
    {
        if (idx < 0 || idx >= _layerNames.Length) return "?";
        return _layerNames[idx] ?? "?";
    }

    private void CopyListedToClipboard()
    {
        string tsv = BuildTsvForRows(_rows);
        // Clipboard write (UnityEngine.GUIUtility)
        GUIUtility.systemCopyBuffer = tsv;
        ShowNotification(new GUIContent($"Copied {_rows.Count} rows (TSV)"));
    }
    // ===== END EXPORT =====

    private void OnGUI()
    {
        if (_layerNames.Length == 0)
        {
            EditorGUILayout.HelpBox("No Sorting Layers found. Create some in Edit → Project Settings → Tags and Layers.", MessageType.Info);
            if (GUILayout.Button("Refresh Layers")) RefreshLayers();
            return;
        }

        HeaderGUI();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_rows.Count == 0)
        {
            EditorGUILayout.HelpBox("No renderers found with current filters.", MessageType.None);
        }
        else
        {
            foreach (var row in _rows)
                RowGUI(row);
        }
        EditorGUILayout.EndScrollView();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh Layers")) RefreshLayers();
            if (GUILayout.Button("Refresh List")) RefreshRows();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select All Listed"))
            {
                Selection.objects = _rows.Where(r => r.renderer != null)
                                         .Select(r => r.renderer.gameObject).ToArray();
            }
            if (GUILayout.Button("Copy Listed (TSV)", GUILayout.Width(140)))
            {
                CopyListedToClipboard();
            }
        }
        EditorGUILayout.Space(4);
    }
}
