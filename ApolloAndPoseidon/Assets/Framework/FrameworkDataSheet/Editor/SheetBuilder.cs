using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Framework.FrameworkDataSheet
{
    public enum ColumnKind
    {
        Group,
        Value,
        Reference
    }

    public class SheetColumn
    {
        public string Title;
        public string Tooltip;
        public string SubPath;
        public ColumnKind Kind;
        public bool RelativeToElement;
        public Type FieldType;
        public float DefaultWidth = 80f;

        public void InitDefaultWidth()
        {
            if (Kind == ColumnKind.Group) DefaultWidth = Title == "#" ? 36f : 120f;
            else if (Kind == ColumnKind.Reference) DefaultWidth = 150f;
            else DefaultWidth = 80f;
        }
    }

    public class SheetRow
    {
        public UnityEngine.Object Target;
        public SerializedObject SObj;
        public string GroupKey;
        public int ElementIndex = -1;
        public string ElementPath = "";
        public bool IsPlaceholder;
        public List<SerializedProperty> Cells = new List<SerializedProperty>();
    }

    public class SheetModel
    {
        public Type RootType;
        public string Title;
        public bool IsSceneMode;
        public string ExpanderField;
        public List<UnityEngine.Object> Targets = new List<UnityEngine.Object>();
        public List<SheetColumn> Columns = new List<SheetColumn>();
        public List<SheetRow> Rows = new List<SheetRow>();

        public bool HasExpander { get { return !string.IsNullOrEmpty(ExpanderField); } }

        public static string BuildPath(SheetColumn col, SheetRow row)
        {
            if (col.RelativeToElement && !string.IsNullOrEmpty(row.ElementPath))
                return row.ElementPath + "." + col.SubPath;
            if (col.RelativeToElement) return null;
            return col.SubPath;
        }
    }

    public static class SheetBuilder
    {
        private class Scope
        {
            public SerializedObject SObj;
            public string BasePath;
        }

        private static readonly Dictionary<Type, List<FieldInfo>> s_fieldCache = new Dictionary<Type, List<FieldInfo>>();

        public static List<FieldInfo> GetFields(Type type)
        {
            List<FieldInfo> fields;
            if (s_fieldCache.TryGetValue(type, out fields)) return fields;
            fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => (f.IsPublic || f.IsDefined(typeof(SerializeField)))
                            && !f.IsNotSerialized
                            && !f.IsDefined(typeof(HideInInspector))
                            && !f.IsStatic)
                .ToList();
            s_fieldCache[type] = fields;
            return fields;
        }

        public static Type GetElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
            return null;
        }

        private static bool IsUserSerializable(Type type)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;
            if (type == typeof(string)) return false;
            if (type.IsPrimitive) return false;
            if (type.IsEnum) return false;
            if (type.IsValueType) return true;
            return type.IsClass && type.IsSerializable;
        }

        public static string FindExpanderField(Type type)
        {
            foreach (var field in GetFields(type))
            {
                var elemType = GetElementType(field.FieldType);
                if (elemType != null && IsUserSerializable(elemType)) return field.Name;
            }
            return null;
        }

        public static SheetModel BuildAssets(Type type, List<UnityEngine.Object> assets)
        {
            var model = new SheetModel
            {
                RootType = type,
                Title = type.Name,
                IsSceneMode = false,
                ExpanderField = FindExpanderField(type)
            };
            model.Targets.AddRange(assets);
            BuildColumns(model);
            BuildRows(model);
            return model;
        }

        public static SheetModel BuildScene(Component component)
        {
            var type = component.GetType();
            var model = new SheetModel
            {
                RootType = type,
                Title = component.gameObject.name + " : " + type.Name,
                IsSceneMode = true,
                ExpanderField = FindExpanderField(type)
            };
            model.Targets.Add(component);
            BuildColumns(model);
            BuildRows(model);
            return model;
        }

        private static void BuildColumns(SheetModel model)
        {
            var type = model.RootType;
            var nameCol = new SheetColumn { Title = "资产", Kind = ColumnKind.Group, SubPath = null };
            nameCol.InitDefaultWidth();
            model.Columns.Add(nameCol);
            if (model.HasExpander)
            {
                var idxCol = new SheetColumn { Title = "#", Kind = ColumnKind.Group, SubPath = null };
                idxCol.InitDefaultWidth();
                model.Columns.Add(idxCol);
            }

            var scopes = new List<Scope>();
            foreach (var target in model.Targets)
            {
                var so = new SerializedObject(target);
                scopes.Add(new Scope { SObj = so, BasePath = "" });
            }

            foreach (var field in GetFields(type))
            {
                if (model.HasExpander && field.Name == model.ExpanderField)
                {
                    var elemType = GetElementType(field.FieldType);
                    var elemScopes = new List<Scope>();
                    foreach (var scope in scopes)
                    {
                        var arr = scope.SObj.FindProperty(field.Name);
                        if (arr == null) continue;
                        for (int i = 0; i < arr.arraySize; i++)
                            elemScopes.Add(new Scope { SObj = scope.SObj, BasePath = field.Name + ".Array.data[" + i + "]" });
                    }
                    foreach (var sub in GetFields(elemType))
                        AddFieldColumns(sub, sub.Name, sub.Name, elemScopes, true, model);
                }
                else
                {
                    AddFieldColumns(field, field.Name, field.Name, scopes, false, model);
                }
            }
        }

        private static void AddFieldColumns(FieldInfo field, string subPath, string title, List<Scope> scopes, bool relativeToElement, SheetModel model)
        {
            var fieldType = field.FieldType;
            var elemType = GetElementType(fieldType);

            if (elemType != null)
            {
                int max = 0;
                foreach (var scope in scopes)
                {
                    var prop = scope.SObj.FindProperty(JoinPath(scope.BasePath, subPath));
                    if (prop != null) max = Mathf.Max(max, prop.arraySize);
                }
                if (max <= 0) return;

                if (IsUserSerializable(elemType))
                {
                    foreach (var sub in GetFields(elemType))
                        for (int i = 0; i < max; i++)
                            AddFieldColumns(sub,
                                subPath + ".Array.data[" + i + "]." + sub.Name,
                                title + "[" + i + "]." + sub.Name,
                                scopes, relativeToElement, model);
                }
                else
                {
                    for (int i = 0; i < max; i++)
                        AddSimpleColumn(field,
                            subPath + ".Array.data[" + i + "]",
                            title + "[" + i + "]",
                            relativeToElement, model);
                }
            }
            else if (IsUserSerializable(fieldType))
            {
                foreach (var sub in GetFields(fieldType))
                    AddFieldColumns(sub,
                        subPath + "." + sub.Name,
                        title + "." + sub.Name,
                        scopes, relativeToElement, model);
            }
            else
            {
                AddSimpleColumn(field, subPath, title, relativeToElement, model);
            }
        }

        private static void AddSimpleColumn(FieldInfo field, string subPath, string title, bool relativeToElement, SheetModel model)
        {
            var tooltipAttr = field.GetCustomAttribute<TooltipAttribute>();
            var col = new SheetColumn
            {
                Title = title,
                Tooltip = tooltipAttr != null ? tooltipAttr.tooltip : null,
                SubPath = subPath,
                Kind = typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) ? ColumnKind.Reference : ColumnKind.Value,
                RelativeToElement = relativeToElement,
                FieldType = field.FieldType
            };
            col.InitDefaultWidth();
            model.Columns.Add(col);
        }

        private static void BuildRows(SheetModel model)
        {
            foreach (var target in model.Targets)
            {
                var so = new SerializedObject(target);
                string key = target.name;

                if (model.HasExpander)
                {
                    var arr = so.FindProperty(model.ExpanderField);
                    int count = arr != null ? arr.arraySize : 0;
                    if (count == 0)
                    {
                        model.Rows.Add(new SheetRow
                        {
                            Target = target, SObj = so, GroupKey = key,
                            ElementIndex = -1, ElementPath = "", IsPlaceholder = true
                        });
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            model.Rows.Add(new SheetRow
                            {
                                Target = target, SObj = so, GroupKey = key,
                                ElementIndex = i,
                                ElementPath = model.ExpanderField + ".Array.data[" + i + "]"
                            });
                    }
                }
                else
                {
                    model.Rows.Add(new SheetRow
                    {
                        Target = target, SObj = so, GroupKey = key,
                        ElementIndex = -1, ElementPath = ""
                    });
                }
            }

            foreach (var row in model.Rows)
            {
                foreach (var col in model.Columns)
                {
                    if (col.Kind == ColumnKind.Group) { row.Cells.Add(null); continue; }
                    string path = SheetModel.BuildPath(col, row);
                    row.Cells.Add(path != null ? row.SObj.FindProperty(path) : null);
                }
            }
        }

        private static string JoinPath(string basePath, string subPath)
        {
            if (string.IsNullOrEmpty(basePath)) return subPath;
            return basePath + "." + subPath;
        }
    }
}
