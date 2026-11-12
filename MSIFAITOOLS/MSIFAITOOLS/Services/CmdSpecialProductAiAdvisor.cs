using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 一个特注品文件的 AI 补足结果：补充了多少字段 / 新行 / 特注寸法 + 提示。
    /// 由 <see cref="CmdSpecialProductAiAdvisor.Enrich"/> 生成，供 <see cref="CmdSpecialProductCsvExporter"/> 汇总日志。
    /// </summary>
    internal sealed class SpecialProductAiResult
    {
        public SpecialProductAiResult()
        {
            Notes = new List<string>();
        }

        /// <summary>实际调用 AI 的次数（正常 = 2：先补缺行/缺字段，再补特注寸法）。</summary>
        public int AiCallCount { get; set; }

        /// <summary>AI 补上的空白字段个数（Excel 已有值不算）。</summary>
        public int FilledFieldCount { get; set; }

        /// <summary>AI 补上的新行条数（Excel 部品表里没有的部品）。</summary>
        public int AddedRowCount { get; set; }

        /// <summary>AI 补上的「追加対応特注寸法」条数。</summary>
        public int FilledSpecialSizeCount { get; set; }

        /// <summary>提示（由调用方写进日志）。</summary>
        public List<string> Notes { get; private set; }
    }

    /// <summary>
    /// 【CMD 分析】最前面 · 特注品 CSV 生成的「AI 补足」：对文件名第 5 位是 <c>Z</c> 的文件，
    /// 用 AI 读该図番的 PDF 图面文字（<c>AiAppData\&lt;図番&gt;\plu_table.txt</c>），
    /// <b>只补 Excel 读不到的内容</b>（Excel 已有的值一律不动）。
    ///
    /// <para>分两次调用 AI（提示语见 <see cref="SpecialProductPromptStore"/>）：</para>
    /// <list type="number">
    ///   <item><b>先补缺行 / 缺字段</b>：部品表里空着的 品種 / 部品名 / 枠区分 / 部品コード / 員数，
    ///         以及图面上有、部品表里没有的部品（作为新行追加）；</item>
    ///   <item><b>再补特注寸法</b>：给「追加対応特注寸法」还空着的行，从图面文字里找出特注尺寸原文
    ///         （<c>2199 ≦Ｗ</c> 这种写法），第 4 步据此算 H / W 上下限。</item>
    /// </list>
    ///
    /// <para>
    /// <b>不改写、不猜</b>：AI 给的字段只写进空白单元格；行号对不上（超出范围 / 指向表头）的行直接丢弃；
    /// 新行至少要有「品種」或「部品名」，且与已有行重复（品種 + 部品名 + 部品コード）时不再追加。
    /// </para>
    /// </summary>
    internal sealed class CmdSpecialProductAiAdvisor
    {
        /// <summary>PDF 分解出来的图面文字文件名（【开始分解】生成）。</summary>
        public const string PdfTableFileName = "plu_table.txt";

        /// <summary>送给 AI 的图面文字字符上限（超出截断，避免提示词过长）。</summary>
        private const int MaxPdfTextChars = 30000;

        /// <summary>一次最多拼进来的 plu_table*.txt 个数（多页 PDF 第 2 页起是 _P2）。</summary>
        private const int MaxPdfFileCount = 6;

        /// <summary>AI 给的单元格文本长度上限（去掉换行后截断）。</summary>
        private const int MaxCellLength = 60;

        /// <summary>AI 回复解析失败时，日志里显示的原文长度。</summary>
        private const int ShortTextLength = 120;

        /// <summary>空单元格的 JSON 键（前 5 列，与 CSV 前 5 列一一对应）。</summary>
        private static readonly string[] FieldKeys = new string[]
        {
            "品種", "部品名", "枠区分", "部品コード", "員数"
        };

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly JavaScriptSerializer Serializer = CreateSerializer();

        private readonly ChatCompletionClient _client;

        private readonly SpecialProductPromptSet _prompts;

        public CmdSpecialProductAiAdvisor(AiApiOptions options, SpecialProductPromptSet prompts)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            if (!options.IsConfigured)
            {
                throw new InvalidOperationException(
                    "未配置 AI 接口密钥：请在程序内【AI 设置】中填写 API Key（特注品 AI 补足需要联网）。");
            }

            _client = new ChatCompletionClient(options);
            _prompts = prompts == null ? SpecialProductPromptStore.Load() : prompts;
        }

        /// <summary>本次使用的提示语（来源说明写进日志）。</summary>
        public SpecialProductPromptSet Prompts
        {
            get { return _prompts; }
        }

        /// <summary>
        /// 对一个図番做 AI 补足：直接修改 <paramref name="rows"/>（下标 0 是表头，数据行从 1 起）。
        /// </summary>
        /// <param name="drawingNo">図番（PDF / xlsx 文件名去扩展名）。</param>
        /// <param name="drawingOutputDirectory">
        /// 该図番的分解输出目录（<c>AiAppData\&lt;図番&gt;</c>），里面找 <c>plu_table*.txt</c>。
        /// </param>
        /// <param name="rows">CSV 行（表头 + 明细），会被原地补足 / 追加。</param>
        public SpecialProductAiResult Enrich(string drawingNo, string drawingOutputDirectory,
            IList<string[]> rows)
        {
            SpecialProductAiResult result = new SpecialProductAiResult();

            if (rows == null || rows.Count == 0)
            {
                result.Notes.Add("部品表没有表头，跳过 AI 补足");

                return result;
            }

            string pdfText = ReadPdfText(drawingOutputDirectory, result.Notes);

            if (pdfText.Length == 0)
            {
                result.Notes.Add("没有 PDF 图面文字（" + PdfTableFileName + "），跳过 AI 补足" +
                    "（先在【开始分解】里分解该図番的 PDF）");

                return result;
            }

            // 第一个任务：补缺行 / 缺字段（Excel 已有的值不动）
            string fieldUserPrompt = BuildUserPrompt(_prompts.FieldTemplate, drawingNo,
                BuildFieldTable(rows), pdfText);
            string fieldContent = _client.Complete(_prompts.SystemPrompt, fieldUserPrompt);
            result.AiCallCount++;
            ApplyFields(fieldContent, rows, result);

            // 第二个任务：补「追加対応特注寸法」（用补过字段 / 新行之后的部品表）
            string sizeUserPrompt = BuildUserPrompt(_prompts.SpecialSizeTemplate, drawingNo,
                BuildSizeTable(rows), pdfText);
            string sizeContent = _client.Complete(_prompts.SystemPrompt, sizeUserPrompt);
            result.AiCallCount++;
            ApplySpecialSizes(sizeContent, rows, result);

            if (result.FilledFieldCount == 0 && result.AddedRowCount == 0 &&
                result.FilledSpecialSizeCount == 0)
            {
                result.Notes.Add("AI 没有给出可补的内容（图面上没有可读出的信息）");
            }

            return result;
        }

        /// <summary>
        /// 读该図番的图面文字：<paramref name="directory"/> 下所有 <c>plu_table*.txt</c>
        /// （第 1 页 <c>plu_table.txt</c>，第 2 页起 <c>plu_table_P2.txt</c> …）按文件名顺序拼接，
        /// 超过 <see cref="MaxPdfTextChars"/> 截断。一个都没有时返回空串（调用方跳过 AI 补足）。
        /// </summary>
        private static string ReadPdfText(string directory, IList<string> notes)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return string.Empty;
            }

            List<string> files = new List<string>();

            try
            {
                foreach (string path in Directory.GetFiles(directory, "plu_table*.txt",
                    SearchOption.TopDirectoryOnly))
                {
                    files.Add(path);
                }
            }
            catch (Exception ex)
            {
                notes.Add("查找 " + PdfTableFileName + " 失败：" + ex.Message);

                return string.Empty;
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);

            StringBuilder builder = new StringBuilder();
            int used = 0;

            foreach (string path in files)
            {
                if (used >= MaxPdfFileCount)
                {
                    break;
                }

                string text;

                try
                {
                    text = File.ReadAllText(path, Utf8NoBom);
                }
                catch (Exception ex)
                {
                    notes.Add(Path.GetFileName(path) + "：读取失败（" + ex.Message + "）");

                    continue;
                }

                if (text.Trim().Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }

                builder.Append("--- ").Append(Path.GetFileName(path)).AppendLine(" ---");
                builder.Append(text);
                used++;

                if (builder.Length >= MaxPdfTextChars)
                {
                    break;
                }
            }

            string all = builder.ToString().Trim();

            if (all.Length > MaxPdfTextChars)
            {
                all = all.Substring(0, MaxPdfTextChars) + "\n（图面文字已截断）";
            }

            if (all.Length > 0)
            {
                notes.Add(string.Format("AI 输入：图面文字 {0} 个文件 / {1} 字符", used, all.Length));
            }

            return all;
        }

        /// <summary>第一个任务的表格：line（= CSV 行号）+ 前 5 列（空着的列由 AI 补）。</summary>
        private static string BuildFieldTable(IList<string[]> rows)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("| line | 品種 | 部品名 | 枠区分 | 部品コード | 員数 |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- |");

            for (int index = 1; index < rows.Count; index++)
            {
                string[] cells = rows[index];
                builder.Append("| ").Append((index + 1).ToString(CultureInfo.InvariantCulture));

                for (int column = 0; column < FieldKeys.Length; column++)
                {
                    builder.Append(" | ").Append(EscapeCell(Cell(cells, column)));
                }

                builder.AppendLine(" |");
            }

            return builder.ToString().Trim();
        }

        /// <summary>第二个任务的表格：line（= CSV 行号）+ 品種 / 部品名 / 部品コード + 现在的特注寸法。</summary>
        private static string BuildSizeTable(IList<string[]> rows)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("| line | 品種 | 部品名 | 部品コード | 追加対応特注寸法 |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");

            for (int index = 1; index < rows.Count; index++)
            {
                string[] cells = rows[index];
                builder.Append("| ").Append((index + 1).ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(EscapeCell(Cell(cells, 0)))
                    .Append(" | ").Append(EscapeCell(Cell(cells, 1)))
                    .Append(" | ").Append(EscapeCell(Cell(cells, 3)))
                    .Append(" | ").Append(EscapeCell(Cell(cells, 5)))
                    .AppendLine(" |");
            }

            return builder.ToString().Trim();
        }

        /// <summary>把模板里的占位符换成实际内容。</summary>
        private static string BuildUserPrompt(string template, string drawingNo, string excelTable,
            string pdfText)
        {
            string text = template ?? string.Empty;

            text = text.Replace("{{DRAWING_NO}}", drawingNo ?? string.Empty);
            text = text.Replace("{{EXCEL_TABLE}}", excelTable ?? string.Empty);
            text = text.Replace("{{PDF_TEXT}}", pdfText ?? string.Empty);

            return text;
        }

        /// <summary>
        /// 第一个任务的返回：<c>{"rows":[…],"new_rows":[…]}</c>。
        /// <c>rows</c> 按 line 补空白字段（已有值不动），<c>new_rows</c> 追加成新行（重复行不加）。
        /// </summary>
        private static void ApplyFields(string content, IList<string[]> rows, SpecialProductAiResult result)
        {
            Dictionary<string, object> root = ParseObject(content, result.Notes);

            if (root == null)
            {
                return;
            }

            object rowsValue;

            if (TryGetValue(root, "rows", out rowsValue))
            {
                foreach (object item in AsArray(rowsValue))
                {
                    Dictionary<string, object> map = item as Dictionary<string, object>;

                    if (map == null)
                    {
                        continue;
                    }

                    // line = 表格里给出的行号（1 = 表头）；指向表头 / 超出范围的行丢弃
                    int index = ReadInt(map, "line") - 1;

                    if (index <= 0 || index >= rows.Count)
                    {
                        continue;
                    }

                    string[] cells = rows[index];
                    int filled = 0;

                    for (int column = 0; column < FieldKeys.Length && column < cells.Length; column++)
                    {
                        if (!string.IsNullOrWhiteSpace(cells[column]))
                        {
                            continue;
                        }

                        string value = ReadText(map, FieldKeys[column]);

                        if (value.Length == 0)
                        {
                            continue;
                        }

                        cells[column] = value;
                        filled++;
                    }

                    result.FilledFieldCount += filled;
                }
            }

            object newRowsValue;

            if (!TryGetValue(root, "new_rows", out newRowsValue))
            {
                return;
            }

            foreach (object item in AsArray(newRowsValue))
            {
                Dictionary<string, object> map = item as Dictionary<string, object>;

                if (map == null)
                {
                    continue;
                }

                string[] cells = new string[CmdSpecialProductCsvExporter.CsvColumns.Length];

                for (int column = 0; column < cells.Length; column++)
                {
                    cells[column] = ReadText(map, CmdSpecialProductCsvExporter.CsvColumns[column]);
                }

                // 至少要有「品種」或「部品名」，否则不是一条部品
                if (cells[0].Length == 0 && cells[1].Length == 0)
                {
                    continue;
                }

                if (IsDuplicateRow(rows, cells))
                {
                    continue;
                }

                rows.Add(cells);
                result.AddedRowCount++;
            }
        }

        /// <summary>
        /// 第二个任务的返回：<c>{"special_sizes":[{"line":2,"追加対応特注寸法":"2199 ≦Ｗ"}]}</c>。
        /// 只补第 6 列还空着的行（Excel / 第一个任务已经写了的都不动）。
        /// </summary>
        private static void ApplySpecialSizes(string content, IList<string[]> rows,
            SpecialProductAiResult result)
        {
            Dictionary<string, object> root = ParseObject(content, result.Notes);

            if (root == null)
            {
                return;
            }

            object sizesValue;

            if (!TryGetValue(root, "special_sizes", out sizesValue))
            {
                return;
            }

            foreach (object item in AsArray(sizesValue))
            {
                Dictionary<string, object> map = item as Dictionary<string, object>;

                if (map == null)
                {
                    continue;
                }

                int index = ReadInt(map, "line") - 1;

                if (index <= 0 || index >= rows.Count)
                {
                    continue;
                }

                string[] cells = rows[index];

                if (cells.Length <= 5 || !string.IsNullOrWhiteSpace(cells[5]))
                {
                    continue;
                }

                string value = ReadText(map, "追加対応特注寸法");

                if (value.Length == 0)
                {
                    continue;
                }

                cells[5] = value;
                result.FilledSpecialSizeCount++;
            }
        }

        /// <summary>
        /// 新行是否与已有行重复：品種 + 部品名 + 部品コード 相同即算重复；
        /// 部品コード 为空时退化成 品種 + 部品名 + 枠区分（避免同一部品重复登录）。
        /// </summary>
        private static bool IsDuplicateRow(IList<string[]> rows, string[] cells)
        {
            string key = BuildRowKey(cells);

            if (key.Length == 0)
            {
                return false;
            }

            for (int index = 1; index < rows.Count; index++)
            {
                if (string.Equals(BuildRowKey(rows[index]), key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>生成比较用的行键（归一化：去空白 + 大写）。</summary>
        private static string BuildRowKey(string[] cells)
        {
            if (cells == null)
            {
                return string.Empty;
            }

            string variety = NormalizeKey(Cell(cells, 0));
            string partName = NormalizeKey(Cell(cells, 1));
            string frameKind = NormalizeKey(Cell(cells, 2));
            string partCode = NormalizeKey(Cell(cells, 3));

            if (variety.Length == 0 && partName.Length == 0)
            {
                return string.Empty;
            }

            return partCode.Length > 0
                ? variety + "|" + partName + "|" + partCode
                : variety + "|" + partName + "|" + frameKind;
        }

        /// <summary>比较用的归一化：去空白 + 转大写（与 CSV 写出的原文无关）。</summary>
        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char ch in value.Trim())
            {
                if (!char.IsWhiteSpace(ch))
                {
                    builder.Append(char.ToUpperInvariant(ch));
                }
            }

            return builder.ToString();
        }

        /// <summary>把 AI 回复解析成 JSON 对象；没有 JSON / 解析失败时记一条提示并返回 null。</summary>
        private static Dictionary<string, object> ParseObject(string content, IList<string> notes)
        {
            string json = AiPartResponseParser.ExtractJson(content);

            if (json.Length == 0)
            {
                notes.Add("AI 回复里没有 JSON（前 " + ShortTextLength + " 字：" + Shorten(content) + "）");

                return null;
            }

            try
            {
                return Serializer.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                notes.Add("AI 返回的 JSON 无法解析（" + ex.Message + "；前 " + ShortTextLength +
                    " 字：" + Shorten(content) + "）");

                return null;
            }
        }

        /// <summary>共享的 JSON 反序列化器（不限长度：图面部品表可能很大）。</summary>
        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;

            return serializer;
        }

        /// <summary>把 JSON 值转成数组（不是数组时返回空表）。</summary>
        private static IList<object> AsArray(object value)
        {
            object[] array = value as object[];

            return array == null ? new object[0] : array;
        }

        /// <summary>按键名取 JSON 值（先精确、再忽略大小写）。</summary>
        private static bool TryGetValue(Dictionary<string, object> map, string key, out object value)
        {
            value = null;

            if (map == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (map.TryGetValue(key, out value))
            {
                return true;
            }

            foreach (KeyValuePair<string, object> pair in map)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;

                    return true;
                }
            }

            return false;
        }

        /// <summary>取字段文本：空 / 对象 / 数组都按空串处理，其余去换行 + 去空白 + 限长。</summary>
        private static string ReadText(Dictionary<string, object> map, string key)
        {
            object value;

            if (!TryGetValue(map, key, out value) || value == null)
            {
                return string.Empty;
            }

            if (value is Dictionary<string, object> || value is object[])
            {
                return string.Empty;
            }

            return NormalizeCell(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        /// <summary>取行号（不是数字时返回 0，调用方按越界丢弃）。</summary>
        private static int ReadInt(Dictionary<string, object> map, string key)
        {
            string text = ReadText(map, key);

            if (text.Length == 0)
            {
                return 0;
            }

            int parsed;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return 0;
        }

        /// <summary>取一行里的单元格文本（null / 列号越界都返回空串）。</summary>
        private static string Cell(string[] cells, int index)
        {
            if (cells == null || index < 0 || index >= cells.Length || cells[index] == null)
            {
                return string.Empty;
            }

            return cells[index].Trim();
        }

        /// <summary>Markdown 表格用：竖线换成全角，换行压成空格。</summary>
        private static string EscapeCell(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                if (ch == '|')
                {
                    builder.Append('｜');
                }
                else if (ch == '\r' || ch == '\n')
                {
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary>压缩成一段短文本（日志用，不截断到 <see cref="MaxCellLength"/>）。</summary>
        private static string Shorten(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(content.Length);

            foreach (char ch in content)
            {
                builder.Append(ch == '\r' || ch == '\n' || ch == '\t' ? ' ' : ch);
            }

            string text = builder.ToString().Trim();

            return text.Length <= ShortTextLength
                ? text
                : text.Substring(0, ShortTextLength) + "…";
        }

        /// <summary>
        /// 单元格归一化：换行 / 制表符压成空格 → 去首尾空白 → 截断到 <see cref="MaxCellLength"/>。
        /// （CSV 是逐行解析的，字段里不能留换行。）
        /// </summary>
        private static string NormalizeCell(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                builder.Append(ch == '\r' || ch == '\n' || ch == '\t' ? ' ' : ch);
            }

            string text = builder.ToString().Trim();

            if (text.Length > MaxCellLength)
            {
                text = text.Substring(0, MaxCellLength).Trim();
            }

            return text;
        }
    }
}





