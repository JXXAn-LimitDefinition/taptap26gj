using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Framework.FrameworkDataSheet
{
    public class DataSheetWindow : EditorWindow
    {
        [MenuItem("Tools/数据表编辑器")]
        private static void Open()
        {
            var window = GetWindow<DataSheetWindow>();
            window.titleContent = new GUIContent("数据表编辑器");
            window.minSize = new Vector2(760f, 360f);
            window.RefreshTypes();
        }

        private class TypeEntry
        {
            public Type Type;
            public int Count;
        }

        private static readonly Color RowAltBg = new Color(0f, 0f, 0f, 0.06f);
        private static readonly Color RowSelBg = new Color(0.3f, 0.55f, 0.95f, 0.25f);
        private static readonly Color GroupLineColor = new Color(0f, 0f, 0f, 0.35f);

        private List<TypeEntry> typeEntries = new List<TypeEntry>();
        private Vector2 leftScroll, rightScroll;
        private string typeFilter = "";
        private Type selectedType;

        private UnityEngine.Object scenePicker;
        private List<Component> goComponents;
        private string[] goCompNames;
        private int goCompIndex = -1;
        private Component sceneComponent;

        private SheetModel current;
        private readonly List<float> widths = new List<float>();
        private int selectedRow = -1;
        private int resizingCol = -1;
        private float resizeStartX, resizeStartW;
        private string status = "";

        private static GUIStyle headerStyle, groupStyle, rowHeadStyle;
        private static bool stylesInit;

        private void OnEnable()
        {
            RefreshTypes();
        }

        private void OnGUI()
        {
            try
            {
                InitStyles();
                DrawToolbar();
                GUILayout.BeginHorizontal();
                DrawLeftPanel();
                DrawTableArea();
                GUILayout.EndHorizontal();
                DrawStatusBar();
            }
            catch (Exception e)
            {
                if (e.GetType().Name == "ExitGUIException") throw;
                Debug.LogException(e);
            }
        }

        private void InitStyles()
        {
            if (stylesInit) return;
            headerStyle = new GUIStyle("ToolbarButton")
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            groupStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 2, 0, 0)
            };
            rowHeadStyle = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleCenter };
            stylesInit = true;
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton))
            {
                RefreshTypes();
                RebuildCurrent();
            }

            GUILayout.Space(8f);
            GUI.enabled = current != null;
            if (GUILayout.Button("导出CSV", EditorStyles.toolbarButton)) DoExport();
            if (GUILayout.Button("导入CSV", EditorStyles.toolbarButton)) DoImport();
            GUILayout.Space(8f);
            if (GUILayout.Button("加行", EditorStyles.toolbarButton, GUILayout.Width(46f))) AddRowOrAsset();
            if (GUILayout.Button("删行", EditorStyles.toolbarButton, GUILayout.Width(46f))) RemoveRowOrAsset();
            GUI.enabled = true;

            if (GUILayout.Button("导出全部", EditorStyles.toolbarButton))
            {
                try
                {
                    var paths = CsvExporter.ExportAll();
                    SetStatus("已导出 " + paths.Count + " 个 CSV 到 " + CsvExporter.RootFolder + "（清单见 Console）");
                    Debug.Log("[数据表] 导出清单:\n" + string.Join("\n", paths.ToArray()));
                }
                catch (Exception e)
                {
                    ShowIoError("导出失败", e);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            GUILayout.BeginVertical(GUILayout.Width(240f));
            typeFilter = EditorGUILayout.TextField(typeFilter, EditorStyles.toolbarSearchField);

            leftScroll = GUILayout.BeginScrollView(leftScroll, GUI.skin.box);

            EditorGUILayout.LabelField("ScriptableObject 类型", EditorStyles.boldLabel);
            foreach (var entry in typeEntries)
            {
                if (!string.IsNullOrEmpty(typeFilter) &&
                    entry.Type.Name.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool selected = selectedType == entry.Type;
                Color prev = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.35f, 0.6f, 0.95f);
                if (GUILayout.Button(entry.Type.Name + " (" + entry.Count + ")", EditorStyles.miniButton, GUILayout.Height(20f)))
                    SelectType(entry.Type);
                GUI.backgroundColor = prev;
            }

            GUILayout.Space(10f);
            EditorGUILayout.LabelField("场景 / 预制体数据", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("把场景物体或预制体拖到下面，选择组件后即可表格化编辑它的数据。", MessageType.None);

            var picked = EditorGUILayout.ObjectField("对象", scenePicker, typeof(UnityEngine.Object), true);
            if (!ReferenceEquals(picked, scenePicker)) OnScenePicked(picked);

            if (goComponents != null && goComponents.Count > 0 && goCompNames != null)
            {
                int idx = EditorGUILayout.Popup("组件", goCompIndex, goCompNames);
                if (idx != goCompIndex)
                {
                    goCompIndex = idx;
                    SelectSceneComponent(goComponents[idx]);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawTableArea()
        {
            if (current == null)
            {
                EditorGUILayout.HelpBox(
                    "从左侧选择一个 ScriptableObject 类型（表格里每行是一个资产），\n" +
                    "或拖入场景物体选择组件（列表字段自动展开为多行）。\n" +
                    "单元格直接编辑（支持 Undo）；引用列（模型/子弹等）只在 Unity 内编辑，CSV 中只读。",
                    MessageType.Info);
                return;
            }

            var soSet = new HashSet<SerializedObject>();
            foreach (var row in current.Rows) soSet.Add(row.SObj);
            foreach (var so in soSet) so.Update();

            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(current.Title, EditorStyles.boldLabel);
            GUILayout.Label(current.Rows.Count + " 行 × " + current.Columns.Count + " 列", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(current.IsSceneMode ? "场景数据" : "资产数据", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();

            const float headerH = 24f, rowH = 22f, headW = 36f;
            EnsureWidths();
            float totalW = headW + widths.Sum();
            float totalH = headerH + current.Rows.Count * rowH + 6f;

            rightScroll = GUILayout.BeginScrollView(rightScroll, false, true);
            Rect rect = GUILayoutUtility.GetRect(totalW, totalH, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            GUI.Label(new Rect(rect.x, rect.y, headW, headerH), "#", headerStyle);
            float x = rect.x + headW;
            for (int c = 0; c < current.Columns.Count; c++)
            {
                var col = current.Columns[c];
                var hRect = new Rect(x, rect.y, widths[c], headerH);
                GUI.Label(hRect, new GUIContent(col.Title, col.Tooltip), headerStyle);
                DrawResizeHandle(hRect, c);
                x += widths[c];
            }

            for (int r = 0; r < current.Rows.Count; r++)
            {
                float y = rect.y + headerH + r * rowH;
                var row = current.Rows[r];
                var rowRect = new Rect(rect.x, y, totalW, rowH);

                if (r % 2 == 1) EditorGUI.DrawRect(rowRect, RowAltBg);
                if (r == selectedRow) EditorGUI.DrawRect(rowRect, RowSelBg);
                if (r + 1 < current.Rows.Count && current.Rows[r + 1].Target != row.Target)
                    EditorGUI.DrawRect(new Rect(rect.x, y + rowH - 1f, totalW, 1f), GroupLineColor);

                if (GUI.Button(new Rect(rect.x, y, headW, rowH), (r + 1).ToString(), rowHeadStyle))
                {
                    selectedRow = r;
                    Repaint();
                }

                x = rect.x + headW;
                for (int c = 0; c < current.Columns.Count; c++)
                {
                    DrawCell(new Rect(x, y, widths[c], rowH), row, c);
                    x += widths[c];
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawResizeHandle(Rect headerRect, int col)
        {
            var handle = new Rect(headerRect.xMax - 5f, headerRect.y, 10f, headerRect.height);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && handle.Contains(evt.mousePosition) && evt.button == 0)
            {
                resizingCol = col;
                resizeStartX = evt.mousePosition.x;
                resizeStartW = widths[col];
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && resizingCol == col)
            {
                widths[col] = Mathf.Max(28f, resizeStartW + evt.mousePosition.x - resizeStartX);
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp && resizingCol == col)
            {
                resizingCol = -1;
                SaveWidths();
                evt.Use();
            }
        }

        private void DrawCell(Rect cellRect, SheetRow row, int c)
        {
            var col = current.Columns[c];
            if (col.Kind == ColumnKind.Group)
            {
                string text = col.Title == "#"
                    ? (row.IsPlaceholder ? "-" : (row.ElementIndex + 1).ToString())
                    : row.GroupKey + (row.IsPlaceholder ? " (空)" : "");
                using (new EditorGUI.DisabledScope(true))
                    GUI.Label(cellRect, text, groupStyle);
                return;
            }

            var prop = row.Cells[c];
            if (prop == null)
            {
                GUI.Label(cellRect, "—", groupStyle);
                return;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(cellRect, prop, GUIContent.none);
            if (EditorGUI.EndChangeCheck())
            {
                row.SObj.ApplyModifiedProperties();
                MarkDirty(row.Target);
                Repaint();
            }
        }

        private void DrawStatusBar()
        {
            GUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(20f));
            GUILayout.Label(status, EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        private void RefreshTypes()
        {
            typeEntries = DataSheetTypes.GetUserSoTypes()
                .Select(t => new TypeEntry { Type = t, Count = DataSheetTypes.FindAssetPaths(t).Count })
                .ToList();
        }

        private void SelectType(Type type)
        {
            selectedType = type;
            sceneComponent = null;
            RebuildCurrent();
        }

        private void OnScenePicked(UnityEngine.Object picked)
        {
            scenePicker = picked;
            goComponents = null;
            goCompNames = null;
            goCompIndex = -1;

            var comp = picked as Component;
            if (comp != null)
            {
                SelectSceneComponent(comp);
                return;
            }

            var go = picked as GameObject;
            if (go != null)
            {
                goComponents = go.GetComponents<Component>()
                    .Where(cq => cq != null && SheetBuilder.GetFields(cq.GetType()).Count > 0)
                    .ToList();
                if (goComponents.Count == 0)
                {
                    SetStatus("该对象没有带可序列化数据的组件");
                    return;
                }
                goCompNames = goComponents.Select(cq => cq.GetType().Name).ToArray();
                goCompIndex = 0;
                SelectSceneComponent(goComponents[0]);
                return;
            }

            sceneComponent = null;
            if (current != null && current.IsSceneMode)
            {
                current = null;
                widths.Clear();
                selectedRow = -1;
                SetStatus("已清除场景数据选择");
            }
        }

        private void SelectSceneComponent(Component comp)
        {
            sceneComponent = comp;
            selectedType = null;
            current = SheetBuilder.BuildScene(comp);
            widths.Clear();
            selectedRow = -1;
            SetStatus("已加载 " + current.Title + "（" + current.Rows.Count + " 行）");
        }

        private void RebuildCurrent()
        {
            if (selectedType != null)
            {
                var assets = DataSheetTypes.LoadInstances(selectedType);
                current = SheetBuilder.BuildAssets(selectedType, assets);
                widths.Clear();
                SetStatus("已加载 " + current.Title + "：" + assets.Count + " 个资产 / " + current.Rows.Count + " 行");
            }
            else if (sceneComponent != null)
            {
                current = SheetBuilder.BuildScene(sceneComponent);
                widths.Clear();
                SetStatus("已加载 " + current.Title + "（" + current.Rows.Count + " 行）");
            }
            else
            {
                current = null;
            }
            if (selectedRow >= 0 && (current == null || selectedRow >= current.Rows.Count))
                selectedRow = -1;
        }

        private void DoExport()
        {
            try
            {
                string path = CsvExporter.Export(current);
                SetStatus("已导出: " + path);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            }
            catch (Exception e)
            {
                ShowIoError("导出失败", e);
            }
        }

        private void DoImport()
        {
            string dir = Application.dataPath + "/DataSheets";
            if (!System.IO.Directory.Exists(dir)) dir = Application.dataPath;
            string full = EditorUtility.OpenFilePanel("选择要导入的 CSV", dir, "csv");
            if (string.IsNullOrEmpty(full)) return;
            try
            {
                string report = CsvImporter.Import(current, full);
                SetStatus(report);
                RebuildCurrent();
            }
            catch (Exception e)
            {
                ShowIoError("导入失败", e);
            }
        }

        private static void ShowIoError(string title, Exception e)
        {
            bool locked = e is System.IO.IOException || e is UnauthorizedAccessException;
            string msg = locked
                ? "无法写入文件，通常是因为 CSV 还在 Excel 中打开着（Excel 会锁定文件）。\n请先关闭 Excel 里的该文件，再重试。\n\n" + e.Message
                : e.GetType().Name + ": " + e.Message;
            EditorUtility.DisplayDialog(title, msg, "知道了");
            Debug.LogException(e);
        }

        private void AddRowOrAsset()
        {
            if (current == null) return;

            if (!current.IsSceneMode)
            {
                var type = current.RootType;
                string folder = "Assets";
                if (current.Targets.Count > 0)
                {
                    string existing = AssetDatabase.GetAssetPath(current.Targets[0]);
                    if (!string.IsNullOrEmpty(existing))
                        folder = System.IO.Path.GetDirectoryName(existing).Replace('\\', '/');
                }
                var asset = ScriptableObject.CreateInstance(type);
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/New" + type.Name + ".asset");
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                RebuildCurrent();
                var row = current.Rows.FirstOrDefault(r => r.Target == asset);
                if (row != null) selectedRow = current.Rows.IndexOf(row);
                EditorGUIUtility.PingObject(asset);
                SetStatus("已创建 " + path);
                return;
            }

            if (!current.HasExpander)
            {
                SetStatus("该组件没有可展开的列表字段，请在 Inspector 里添加");
                return;
            }

            var so = new SerializedObject(current.Targets[0]);
            var arr = so.FindProperty(current.ExpanderField);
            arr.arraySize++;
            so.ApplyModifiedProperties();
            MarkDirty(current.Targets[0]);
            RebuildCurrent();
            selectedRow = current.Rows.Count - 1;
            SetStatus("已添加 1 行（" + current.ExpanderField + "）");
        }

        private void RemoveRowOrAsset()
        {
            if (current == null || selectedRow < 0 || selectedRow >= current.Rows.Count)
            {
                SetStatus("先点击行首数字选中一行");
                return;
            }

            var row = current.Rows[selectedRow];

            if (!current.IsSceneMode)
            {
                string path = AssetDatabase.GetAssetPath(row.Target);
                if (!EditorUtility.DisplayDialog("删除资产",
                        "确定删除资产 " + row.Target.name + " ？\n" + path, "删除", "取消"))
                    return;
                AssetDatabase.DeleteAsset(path);
                selectedRow = -1;
                RebuildCurrent();
                SetStatus("已删除资产");
                return;
            }

            if (row.ElementIndex < 0)
            {
                SetStatus("该行是占位行（列表为空），无法删除");
                return;
            }

            var so = new SerializedObject(row.Target);
            var arr = so.FindProperty(current.ExpanderField);
            arr.DeleteArrayElementAtIndex(row.ElementIndex);
            so.ApplyModifiedProperties();
            MarkDirty(row.Target);
            selectedRow = -1;
            RebuildCurrent();
            SetStatus("已删除第 " + (row.ElementIndex + 1) + " 行");
        }

        private static void MarkDirty(UnityEngine.Object target)
        {
            if (target == null) return;
            if (EditorUtility.IsPersistent(target))
            {
                EditorUtility.SetDirty(target);
            }
            else if (target is Component comp && comp.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
            }
        }

        private string WidthKey()
        {
            string prefix = current.IsSceneMode ? "S_" : "A_";
            return "DataSheetWidth_" + prefix + current.RootType.FullName;
        }

        private void EnsureWidths()
        {
            if (widths.Count == current.Columns.Count) return;
            widths.Clear();
            string[] parts = EditorPrefs.GetString(WidthKey(), "").Split(',');
            for (int i = 0; i < current.Columns.Count; i++)
            {
                float w;
                if (i < parts.Length && float.TryParse(parts[i], out w) && w >= 28f) widths.Add(w);
                else widths.Add(current.Columns[i].DefaultWidth);
            }
        }

        private void SaveWidths()
        {
            if (current == null || widths.Count != current.Columns.Count) return;
            EditorPrefs.SetString(WidthKey(), string.Join(",", widths.Select(w => ((int)w).ToString()).ToArray()));
        }

        private void SetStatus(string message)
        {
            status = message ?? "";
        }
    }
}
