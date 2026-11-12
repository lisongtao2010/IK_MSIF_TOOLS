using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CadPartSplitter
{
    /// <summary>AI 判出的一条部品明细（<b>未经回原表核对</b>）。</summary>
    internal sealed class TzAiItem
    {
        /// <summary>明细所在行号（# 后面的数字，1 起）。0 = AI 没给。</summary>
        public int Row { get; set; }

        /// <summary>品種。</summary>
        public string Variety { get; set; }

        /// <summary>部品名。</summary>
        public string PartName { get; set; }

        /// <summary>枠区分。</summary>
        public string FrameKind { get; set; }

        /// <summary>部品コード。</summary>
        public string PartCode { get; set; }

        /// <summary>追加対応特注寸法。</summary>
        public string SpecialSize { get; set; }
    }

    /// <summary>
    /// 解析 AI 返回的「Excel 部品表明细」JSON。
    /// 期望格式：<c>{"items":[{"row":12,"品種":"…","部品名":"…","枠区分":"…","部品コード":"…","追加対応特注寸法":"…"}],"notes":"…"}</c>
    /// 同时兼容顶层直接是数组、以及字段名使用英文别名的情形（与 <c>MSIFAITOOLS</c> 的
    /// <c>AiPartResponseParser</c> 同思路：容忍模型夹带 Markdown 代码块与解释文字）。
    /// </summary>
    internal static class TzAiResponseParser
    {
        private static readonly string[] RowKeys = { "row", "行", "行号", "line", "行番号", "行番" };

        private static readonly string[] VarietyKeys =
            { "品種", "品種名", "品種区分", "variety", "kind" };

        private static readonly string[] PartNameKeys =
            { "部品名", "品名", "partName", "part_name", "name" };

        private static readonly string[] FrameKindKeys =
            { "枠区分", "区分", "frameKind", "frame_kind", "frame" };

        private static readonly string[] PartCodeKeys =
            { "部品コード", "コード", "partCode", "part_code", "code" };

        private static readonly string[] SpecialSizeKeys =
        {
            "追加対応特注寸法", "特注寸法", "追加特注寸法", "特注",
            "specialSize", "special_size", "size"
        };

        private static readonly string[] NotesKeys = { "notes", "note", "备注", "说明", "reason" };

        private static readonly string[] ItemArrayKeys =
        {
            "items", "rows", "明细", "明細", "部品明细", "部品明細", "parts"
        };

        private static readonly Regex NumberPattern = new Regex(@"-?\d+", RegexOptions.Compiled);

        /// <summary>
        /// 把 AI 回复正文解析成明细列表（解析失败时抛 <see cref="InvalidOperationException"/>）。
        /// </summary>
        /// <param name="content">AI 回复的正文（可能带 Markdown 代码块）。</param>
        /// <param name="notes">收集解析过程中的提示信息（由调用方写进日志）。</param>
        public static IList<TzAiItem> Parse(string content, IList<string> notes)
        {
            List<TzAiItem> items = new List<TzAiItem>();

            if (notes == null)
            {
                notes = new List<string>();
            }

            string json = ExtractJson(content);

            if (json.Length == 0)
            {
                throw new InvalidOperationException(
                    "AI 回复中没有找到 JSON 内容（前 200 字：" + Shorten(content) + "）");
            }

            object parsed;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                parsed = serializer.DeserializeObject(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "AI 返回的 JSON 无法解析（前 200 字：" + Shorten(content) + "）", ex);
            }

            Dictionary<string, object> root = parsed as Dictionary<string, object>;

            if (root != null)
            {
                string rootNotes = ReadString(root, NotesKeys);

                if (rootNotes.Length > 0)
                {
                    notes.Add("AI 判版式：" + rootNotes);
                }

                object itemsValue;

                if (TryGetValueIgnoreCase(root, ItemArrayKeys, out itemsValue))
                {
                    AppendItems(items, itemsValue);

                    return items;
                }

                notes.Add("AI 返回的 JSON 中没有 items / rows 字段");

                return items;
            }

            object[] topLevel = parsed as object[];

            if (topLevel != null)
            {
                AppendItems(items, topLevel);

                return items;
            }

            notes.Add("AI 返回的 JSON 既不是对象也不是数组");

            return items;
        }

        /// <summary>
        /// 从 AI 回复中截取 JSON 主体（去掉 Markdown 代码块与前后解释文字）：
        /// 取最外层 <c>{…}</c> 或 <c>[…]</c>，按括号配对找到结束位置。
        /// </summary>
        internal static string ExtractJson(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            int objectStart = content.IndexOf('{');
            int arrayStart = content.IndexOf('[');
            bool useObject = objectStart >= 0 && (arrayStart < 0 || objectStart < arrayStart);
            int start = useObject ? objectStart : arrayStart;

            if (start < 0)
            {
                return string.Empty;
            }

            char openChar = useObject ? '{' : '[';
            char closeChar = useObject ? '}' : ']';
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < content.Length; i++)
            {
                char ch = content[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == openChar)
                {
                    depth++;
                    continue;
                }

                if (ch == closeChar)
                {
                    depth--;

                    if (depth == 0)
                    {
                        return content.Substring(start, i - start + 1);
                    }
                }
            }

            // 括号没配对（回复被截断）时返回从起点到末尾，交给解析器报错
            return content.Substring(start);
        }

        /// <summary>把数组里的每一项读成 <see cref="TzAiItem"/>（非对象项跳过）。</summary>
        private static void AppendItems(List<TzAiItem> items, object value)
        {
            foreach (object element in AsArray(value))
            {
                Dictionary<string, object> map = element as Dictionary<string, object>;

                if (map == null)
                {
                    continue;
                }

                TzAiItem item = new TzAiItem();
                item.Row = ReadInt(map, RowKeys);
                item.Variety = ReadString(map, VarietyKeys);
                item.PartName = ReadString(map, PartNameKeys);
                item.FrameKind = ReadString(map, FrameKindKeys);
                item.PartCode = ReadString(map, PartCodeKeys);
                item.SpecialSize = ReadString(map, SpecialSizeKeys);

                items.Add(item);
            }
        }

        /// <summary>把 object 统一成可遍历的集合。</summary>
        private static IEnumerable<object> AsArray(object value)
        {
            List<object> items = new List<object>();

            if (value == null)
            {
                return items;
            }

            object[] array = value as object[];

            if (array != null)
            {
                return array;
            }

            IEnumerable<object> enumerable = value as IEnumerable<object>;

            if (enumerable == null)
            {
                return items;
            }

            foreach (object item in enumerable)
            {
                items.Add(item);
            }

            return items;
        }

        /// <summary>取出文本内容（去首尾空白，null 视为空字符串）。</summary>
        private static string ReadString(Dictionary<string, object> map, string[] keys)
        {
            object value;

            if (map == null || !TryGetValueIgnoreCase(map, keys, out value) || value == null)
            {
                return string.Empty;
            }

            if (value is Dictionary<string, object> || value is object[])
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        /// <summary>按候选键名取行号（取不到或不是数字时返回 0）。</summary>
        private static int ReadInt(Dictionary<string, object> map, string[] keys)
        {
            string text = ReadString(map, keys);

            if (text.Length == 0)
            {
                return 0;
            }

            Match match = NumberPattern.Match(text);

            if (!match.Success)
            {
                return 0;
            }

            int parsed;

            if (int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return 0;
        }

        /// <summary>按候选键名逐个查找（先精确、再忽略大小写）。</summary>
        private static bool TryGetValueIgnoreCase(Dictionary<string, object> map, string[] keys,
            out object value)
        {
            value = null;

            if (map == null || keys == null)
            {
                return false;
            }

            foreach (string key in keys)
            {
                if (map.TryGetValue(key, out value))
                {
                    return true;
                }
            }

            foreach (string key in keys)
            {
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = pair.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>截断过长的文本（错误信息里用）。</summary>
        private static string Shorten(string text)
        {
            const int MaxLength = 200;

            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= MaxLength ? text : text.Substring(0, MaxLength) + "…";
        }
    }
}
