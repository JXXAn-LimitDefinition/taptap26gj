using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Framework.FrameworkDataSheet
{
    public static class DataSheetTypes
    {
        public const string UserAssembly = "Assembly-CSharp";

        public static List<Type> GetUserSoTypes()
        {
            return TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .Where(t => t.Assembly.GetName().Name == UserAssembly)
                .Where(t => !t.IsAbstract && !t.IsGenericType && t.IsVisible)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> FindAssetPaths(Type type)
        {
            return AssetDatabase.FindAssets("t:" + type.Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<UnityEngine.Object> LoadInstances(Type type)
        {
            return FindAssetPaths(type)
                .Select(p => AssetDatabase.LoadAssetAtPath(p, type))
                .Where(o => o != null)
                .ToList();
        }

        public static bool HasEditableFields(Type type)
        {
            return SheetBuilder.GetFields(type).Count > 0;
        }
    }

    public static class CsvValue
    {
        private static readonly NumberStyles kNumStyle = NumberStyles.Float;
        private static readonly CultureInfo kInv = CultureInfo.InvariantCulture;

        public static string ToString(SerializedProperty p)
        {
            if (p == null) return "";
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    return p.intValue.ToString(kInv);
                case SerializedPropertyType.Boolean:
                    return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:
                    return p.floatValue.ToString("R", kInv);
                case SerializedPropertyType.String:
                    return p.stringValue;
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length ? p.enumNames[p.enumValueIndex] : "";
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue != null ? p.objectReferenceValue.name : "";
                case SerializedPropertyType.Color:
                    return Format4(p.colorValue.r, p.colorValue.g, p.colorValue.b, p.colorValue.a);
                case SerializedPropertyType.Vector2:
                    return Format2(p.vector2Value.x, p.vector2Value.y);
                case SerializedPropertyType.Vector3:
                    return Format3(p.vector3Value.x, p.vector3Value.y, p.vector3Value.z);
                case SerializedPropertyType.Vector4:
                    return Format4(p.vector4Value.x, p.vector4Value.y, p.vector4Value.z, p.vector4Value.w);
                default:
                    return "";
            }
        }

        public static bool CanWrite(SerializedPropertyType type)
        {
            switch (type)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                    return true;
                default:
                    return false;
            }
        }

        public static bool TrySet(SerializedProperty p, string s)
        {
            if (p == null) return false;
            s = s ?? "";
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    int iv;
                    if (int.TryParse(s, kNumStyle, kInv, out iv)) { p.intValue = iv; return true; }
                    return false;
                case SerializedPropertyType.Boolean:
                    bool bv;
                    if (bool.TryParse(s, out bv)) { p.boolValue = bv; return true; }
                    if (s == "1") { p.boolValue = true; return true; }
                    if (s == "0") { p.boolValue = false; return true; }
                    return false;
                case SerializedPropertyType.Float:
                    float fv;
                    if (float.TryParse(s, kNumStyle, kInv, out fv)) { p.floatValue = fv; return true; }
                    return false;
                case SerializedPropertyType.String:
                    p.stringValue = s;
                    return true;
                case SerializedPropertyType.Enum:
                    for (int i = 0; i < p.enumNames.Length; i++)
                    {
                        if (string.Equals(p.enumNames[i], s, StringComparison.OrdinalIgnoreCase))
                        {
                            p.enumValueIndex = i;
                            return true;
                        }
                    }
                    int ev;
                    if (int.TryParse(s, kNumStyle, kInv, out ev)) { p.intValue = ev; return true; }
                    return false;
                case SerializedPropertyType.Color:
                    float[] cv;
                    if (ParseFloats(s, 4, out cv) || ParseFloats(s, 3, out cv))
                    {
                        p.colorValue = cv.Length == 4 ? new Color(cv[0], cv[1], cv[2], cv[3]) : new Color(cv[0], cv[1], cv[2], 1f);
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Vector2:
                    float[] v2;
                    if (ParseFloats(s, 2, out v2)) { p.vector2Value = new Vector2(v2[0], v2[1]); return true; }
                    return false;
                case SerializedPropertyType.Vector3:
                    float[] v3;
                    if (ParseFloats(s, 3, out v3)) { p.vector3Value = new Vector3(v3[0], v3[1], v3[2]); return true; }
                    return false;
                case SerializedPropertyType.Vector4:
                    float[] v4;
                    if (ParseFloats(s, 4, out v4)) { p.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]); return true; }
                    return false;
                default:
                    return false;
            }
        }

        private static bool ParseFloats(string s, int count, out float[] result)
        {
            result = null;
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(',');
            if (parts.Length != count) return false;
            var values = new float[count];
            for (int i = 0; i < count; i++)
            {
                float v;
                if (!float.TryParse(parts[i].Trim(), kNumStyle, kInv, out v)) return false;
                values[i] = v;
            }
            result = values;
            return true;
        }

        private static string Format2(float a, float b) { return a.ToString("R", kInv) + "," + b.ToString("R", kInv); }
        private static string Format3(float a, float b, float c) { return Format2(a, b) + "," + c.ToString("R", kInv); }
        private static string Format4(float a, float b, float c, float d) { return Format3(a, b, c) + "," + d.ToString("R", kInv); }
    }

    public static class CsvExporter
    {
        public const string RootFolder = "Assets/DataSheets";
        public const string SceneFolder = "Assets/DataSheets/Scene";

        public static string GetCsvPath(SheetModel model)
        {
            if (model.IsSceneMode)
            {
                var comp = model.Targets.Count > 0 ? model.Targets[0] as Component : null;
                string baseName = comp != null ? comp.gameObject.name + "_" + comp.GetType().Name : model.RootType.Name;
                return SceneFolder + "/" + Sanitize(baseName) + ".csv";
            }
            return RootFolder + "/" + model.RootType.Name + ".csv";
        }

        public static string Export(SheetModel model)
        {
            return Export(model, true);
        }

        public static string Export(SheetModel model, bool refresh)
        {
            var rows = new List<string[]> { model.Columns.Select(c => c.Title).ToArray() };

            foreach (var row in model.Rows)
            {
                var cells = new string[model.Columns.Count];
                for (int i = 0; i < model.Columns.Count; i++)
                {
                    var col = model.Columns[i];
                    if (col.Kind == ColumnKind.Group)
                    {
                        cells[i] = col.Title == "#" ? (row.ElementIndex >= 0 ? row.ElementIndex.ToString() : "") : row.GroupKey;
                    }
                    else
                    {
                        var prop = row.Cells[i];
                        cells[i] = prop != null ? CsvValue.ToString(prop) : "";
                    }
                }
                rows.Add(cells);
            }

            string path = GetCsvPath(model);
            EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            CsvSerializer.Write(path, rows);
            if (refresh) AssetDatabase.Refresh();
            return path;
        }

        public static List<string> ExportAll()
        {
            var paths = new List<string>();
            foreach (var type in DataSheetTypes.GetUserSoTypes())
            {
                var assets = DataSheetTypes.LoadInstances(type);
                if (assets.Count == 0) continue;
                var model = SheetBuilder.BuildAssets(type, assets);
                paths.Add(Export(model, false));
            }
            AssetDatabase.Refresh();
            return paths;
        }

        public static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;
            var parts = assetFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string Sanitize(string fileName)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');
            return fileName;
        }
    }
}
