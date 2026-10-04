using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Framework.FrameworkDataSheet
{
    public static class CsvSerializer
    {
        public static void Write(string path, List<string[]> rows)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                for (int i = 0; i < row.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Escape(row[i]));
                }
                sb.Append("\r\n");
            }
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static List<string[]> Read(string path)
        {
            return Parse(ReadText(path));
        }

        private static string ReadText(string path)
        {
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                foreach (var enc in GetAnsiFallbackEncodings())
                {
                    try
                    {
                        return enc.GetString(bytes);
                    }
                    catch
                    {
                    }
                }
                return new UTF8Encoding(false).GetString(bytes);
            }
        }

        private static IEnumerable<Encoding> GetAnsiFallbackEncodings()
        {
            int[] codepages = { 936, 54936 };
            string[] names = { "GBK", "GB18030", "GB2312" };
            foreach (int cp in codepages)
            {
                Encoding enc = null;
                try { enc = Encoding.GetEncoding(cp); } catch { }
                if (enc != null) yield return enc;
            }
            foreach (string name in names)
            {
                Encoding enc = null;
                try { enc = Encoding.GetEncoding(name); } catch { }
                if (enc != null) yield return enc;
            }
        }

        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;
            if (text[0] == '\uFEFF') text = text.Substring(1);

            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            int i = 0;
            int len = text.Length;

            while (i < len)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < len && text[i + 1] == '"') { field.Append('"'); i += 2; }
                        else { inQuotes = false; i++; }
                    }
                    else { field.Append(c); i++; }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                    i++;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    i++;
                }
                else if (c == '\r' || c == '\n')
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    if (c == '\r' && i + 1 < len && text[i + 1] == '\n') i += 2; else i++;
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
                    row = new List<string>();
                }
                else
                {
                    field.Append(c);
                    i++;
                }
            }
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());

            while (rows.Count > 0)
            {
                var last = rows[rows.Count - 1];
                if (last.Length == 1 && string.IsNullOrEmpty(last[0])) rows.RemoveAt(rows.Count - 1);
                else break;
            }
            return rows;
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            bool needQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
