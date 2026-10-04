using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Framework.FrameworkDataSheet
{
    public static class CsvImporter
    {
        private static readonly Regex ArraySegRegex = new Regex(@"^(.*)\.Array\.data\[(\d+)\]", RegexOptions.Compiled);

        public static string Import(SheetModel model, string csvPath)
        {
            if (model == null) return "没有打开的数据表";
            List<string[]> rows;
            try { rows = CsvSerializer.Read(csvPath); }
            catch (Exception e) { return "读取 CSV 失败: " + e.Message; }

            if (rows.Count < 2) return "CSV 为空或只有表头";

            var header = rows[0];
            var colToCsv = new Dictionary<int, int>();
            var unknownHeaders = new List<string>();
            for (int c = 0; c < model.Columns.Count; c++)
            {
                int csvIdx = -1;
                for (int h = 0; h < header.Length; h++)
                {
                    if (string.Equals(header[h].Trim(), model.Columns[c].Title, StringComparison.OrdinalIgnoreCase))
                    {
                        csvIdx = h;
                        break;
                    }
                }
                colToCsv[c] = csvIdx;
            }
            for (int h = 0; h < header.Length; h++)
            {
                bool known = model.Columns.Any(col => string.Equals(col.Title, header[h].Trim(), StringComparison.OrdinalIgnoreCase));
                if (!known && !string.IsNullOrEmpty(header[h].Trim())) unknownHeaders.Add(header[h].Trim());
            }

            int nameCsvIdx = -1;
            for (int h = 0; h < header.Length; h++)
                if (string.Equals(header[h].Trim(), "资产", StringComparison.OrdinalIgnoreCase)) { nameCsvIdx = h; break; }
            if (!model.IsSceneMode && nameCsvIdx < 0) return "CSV 缺少「资产」列，无法匹配数据对象（若表头里中文显示乱码，说明保存编码不是 UTF-8，请在 Excel 里另存为「CSV UTF-8」格式后重试）";

            var warnings = new List<string>();
            int appliedCells = 0, skippedCells = 0, refCols = 0;

            var targets = new List<UnityEngine.Object>();
            var rowGroups = new List<List<string[]>>();

            if (model.IsSceneMode)
            {
                targets.Add(model.Targets[0]);
                rowGroups.Add(rows.Skip(1).ToList());
            }
            else
            {
                var groupMap = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows.Skip(1))
                {
                    string name = nameCsvIdx < row.Length ? row[nameCsvIdx].Trim() : "";
                    if (string.IsNullOrEmpty(name)) { warnings.Add("跳过一行：资产名为空"); continue; }
                    List<string[]> list;
                    if (!groupMap.TryGetValue(name, out list)) { list = new List<string[]>(); groupMap[name] = list; }
                    list.Add(row);
                }
                foreach (var kv in groupMap)
                {
                    var target = model.Targets.FirstOrDefault(t => string.Equals(t.name, kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (target == null) { warnings.Add("CSV 中的资产在 Unity 里找不到: " + kv.Key); continue; }
                    targets.Add(target);
                    rowGroups.Add(kv.Value);
                }
            }

            if (targets.Count == 0)
                return "没有匹配到任何数据对象（0 个）。请检查 CSV「资产」列的名称是否与 Unity 里的资产名一致；若名称是乱码，说明保存编码不是 UTF-8，请另存为「CSV UTF-8」后重试";

            for (int t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                var groupRows = rowGroups[t];
                var so = new SerializedObject(target);

                int elementCount = 0;
                if (model.HasExpander)
                {
                    int idxCsv = -1;
                    for (int h = 0; h < header.Length; h++)
                        if (string.Equals(header[h].Trim(), "#", StringComparison.OrdinalIgnoreCase)) { idxCsv = h; break; }

                    elementCount = 0;
                    foreach (var row in groupRows)
                    {
                        string idxStr = idxCsv >= 0 && idxCsv < row.Length ? row[idxCsv].Trim() : "";
                        if (idxStr.Length > 0) elementCount++;
                    }
                    if (elementCount == 0 && groupRows.Count > 1)
                        elementCount = groupRows.Count;

                    var arr = so.FindProperty(model.ExpanderField);
                    if (arr != null && arr.arraySize != elementCount)
                        arr.arraySize = elementCount;
                }

                GrowNestedArrays(model, so, groupRows);

                for (int c = 0; c < model.Columns.Count; c++)
                {
                    var col = model.Columns[c];
                    if (col.Kind == ColumnKind.Group) continue;
                    if (colToCsv[c] < 0) continue;

                    if (col.RelativeToElement)
                    {
                        if (!model.HasExpander) continue;
                        for (int r = 0; r < elementCount && r < groupRows.Count; r++)
                        {
                            var prop = so.FindProperty(model.ExpanderField + ".Array.data[" + r + "]." + col.SubPath);
                            if (prop == null) { warnings.Add(target.name + " 找不到字段: " + col.Title); continue; }
                            if (!CsvValue.CanWrite(prop.propertyType)) { refCols++; continue; }
                            string value = GetString(groupRows[r], colToCsv[c]);
                            if (CsvValue.TrySet(prop, value)) appliedCells++;
                            else if (!string.IsNullOrEmpty(value)) { skippedCells++; warnings.Add(target.name + "." + col.Title + " 无法解析: \"" + value + "\""); }
                        }
                    }
                    else
                    {
                        var prop = so.FindProperty(col.SubPath);
                        if (prop == null) { warnings.Add(target.name + " 找不到字段: " + col.Title); continue; }
                        if (!CsvValue.CanWrite(prop.propertyType)) { refCols++; continue; }
                        string value = GetString(groupRows[0], colToCsv[c]);
                        if (CsvValue.TrySet(prop, value)) appliedCells++;
                        else if (!string.IsNullOrEmpty(value)) { skippedCells++; warnings.Add(target.name + "." + col.Title + " 无法解析: \"" + value + "\""); }
                    }
                }

                so.ApplyModifiedProperties();
                if (EditorUtility.IsPersistent(target)) EditorUtility.SetDirty(target);
                else if (target is Component comp) EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
            }

            var report = "已导入 " + targets.Count + " 个对象 / " + appliedCells + " 个值";
            if (refCols > 0) report += "（引用列 " + refCols + " 个仅限 Unity 内编辑，已跳过）";
            if (skippedCells > 0) report += "，解析失败 " + skippedCells + " 个";
            if (unknownHeaders.Count > 0) report += "，忽略未知列: " + string.Join("、", unknownHeaders.ToArray());
            if (warnings.Count > 0)
            {
                report += "。警告 " + warnings.Count + " 条（详见 Console）";
                Debug.LogWarning("[数据表] 导入警告:\n" + string.Join("\n", warnings.ToArray()));
            }
            return report;
        }

        private static string GetString(string[] row, int idx)
        {
            return idx >= 0 && idx < row.Length ? row[idx] : "";
        }

        private static void GrowNestedArrays(SheetModel model, SerializedObject so, List<string[]> groupRows)
        {
            var needed = new Dictionary<string, int>();
            for (int c = 0; c < model.Columns.Count; c++)
            {
                var col = model.Columns[c];
                if (col.Kind == ColumnKind.Group) continue;
                if (string.IsNullOrEmpty(col.SubPath) || !col.SubPath.Contains(".Array.data[")) continue;

                int count = 1;
                if (col.RelativeToElement && model.HasExpander)
                {
                    var arr = so.FindProperty(model.ExpanderField);
                    count = arr != null ? arr.arraySize : 0;
                }
                for (int r = 0; r < count; r++)
                {
                    string full = model.ExpanderField + ".Array.data[" + r + "]." + col.SubPath;
                    var m = ArraySegRegex.Match(full);
                    if (!m.Success) continue;
                    string root = m.Groups[1].Value;
                    int idx = int.Parse(m.Groups[2].Value);
                    int cur;
                    needed.TryGetValue(root, out cur);
                    if (idx + 1 > cur) needed[root] = idx + 1;
                }
            }
            foreach (var kv in needed)
            {
                var prop = so.FindProperty(kv.Key);
                if (prop != null && prop.isArray && prop.arraySize < kv.Value)
                    prop.arraySize = kv.Value;
            }
        }
    }
}
