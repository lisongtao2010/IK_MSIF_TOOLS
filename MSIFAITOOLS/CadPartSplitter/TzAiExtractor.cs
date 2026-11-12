using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CadPartSplitter
{
    /// <summary>
    /// 「Excel 部品表 AI 判定」的统计与提示：调用次数、AI 返回条数、
    /// 通过回原表核对的条数、被丢弃的条数 / 字段数。
    /// 由 <see cref="TzAiExtractor.Extract"/> 填写，调用方汇总后写进日志。
    /// </summary>
    internal sealed class TzAiStats
    {
        public TzAiStats()
        {
            Notes = new List<string>();
        }

        /// <summary>实际调用 AI 的次数（一张表一次）。</summary>
        public int CallCount { get; set; }

        /// <summary>AI 返回的明细条数。</summary>
        public int ParsedItemCount { get; set; }

        /// <summary>通过回原表核对、被采用的条数。</summary>
        public int AcceptedItemCount { get; set; }

        /// <summary>被丢弃的条数（行号定位不到 / 一个字段都核对不上）。</summary>
        public int RejectedItemCount { get; set; }

        /// <summary>被丢弃的字段数（字段值在原表里找不到 —— 视为 AI 编造）。</summary>
        public int RejectedValueCount { get; set; }

        /// <summary>采用「AI 原文」的字段数（值夹在长格子里，不是整格内容）。</summary>
        public int AiTextValueCount { get; set; }

        /// <summary>过程中的提示（调用失败原因、AI 的版式判断等）。</summary>
        public List<string> Notes { get; private set; }

        /// <summary>把另一份（单表）统计累加进来。</summary>
        public void MergeFrom(TzAiStats other)
        {
            if (other == null)
            {
                return;
            }

            CallCount += other.CallCount;
            ParsedItemCount += other.ParsedItemCount;
            AcceptedItemCount += other.AcceptedItemCount;
            RejectedItemCount += other.RejectedItemCount;
            RejectedValueCount += other.RejectedValueCount;
            AiTextValueCount += other.AiTextValueCount;

            foreach (string note in other.Notes)
            {
                if (Notes.Count >= 40)
                {
                    break;
                }

                Notes.Add(note);
            }
        }
    }

    /// <summary>
    /// 让 AI 判「这张 Excel 表哪里是部品明细、每个字段是什么」，再把结果交回代码
    /// <b>逐字段回原表核对</b>，核对不上的丢掉 —— 宁可少收，也不把 AI 编的内容写进 tz.json。
    ///
    /// <para>
    /// 流程：
    /// <list type="number">
    ///   <item>把工作表摊成「<c>#12: C1=三方枠 | C2=片引戸 …</c>」的文本（只列非空格子，
    ///         超长按 <see cref="DetectorOptions.TzAiMaxGridChars"/> 截断）；</item>
    ///   <item>调一次 AI（提示语见 <see cref="TzAiPromptStore"/>，默认提示语在代码里兜底）；</item>
    ///   <item>解析 AI 的 JSON（<see cref="TzAiResponseParser"/>），每条必须带 <c>row</c>；</item>
    ///   <item>每个字段值回原表核对：<b>整格相等</b> → 用原表文字；<b>格子里包含这个值</b>
    ///         （例如「追加部品セット ： …（ MZA674 ）」） → 用 AI 给出的原文；
    ///         都找不到 → 该字段丢弃并计数；行号定位不到 → 整条丢弃；</item>
    ///   <item>去重后返回 <see cref="TzEntry"/>（<c>source = "ai"</c>）。</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class TzAiExtractor
    {
        /// <summary>tz.json 里 <c>source</c> 字段表示「AI 判定补足」的值。</summary>
        public const string SourceAi = "ai";

        /// <summary>一次最多采纳的明细条数（防止模型复读整张表刷爆 tz.json）。</summary>
        private const int MaxItemsPerCall = 400;

        /// <summary>合并单元格允许向上追溯的行数（品種 / 枠区分）。</summary>
        private const int MaxInheritRows = 3;

        private readonly ChatCompletionClient _client;

        private readonly TzAiMode _mode;

        private readonly int _maxGridChars;

        private readonly Action<string> _log;

        public TzAiExtractor(ChatCompletionClient client, TzAiMode mode, int maxGridChars,
            Action<string> log)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            _client = client;
            _mode = mode;
            _maxGridChars = maxGridChars < 2000 ? 2000 : maxGridChars;
            _log = log;
        }

        /// <summary>本次的触发方式。</summary>
        public TzAiMode Mode
        {
            get { return _mode; }
        }

        /// <summary>本次的接口配置（日志里打印模型 / 来源用）。</summary>
        public AiApiOptions Options
        {
            get { return _client.Options; }
        }

        /// <summary>
        /// 底下那个接口客户端：<see cref="TzFileAiExtractor"/>（非 Excel 文件那条通道）
        /// 复用它，不必再建一份（密钥 / 超时 / HttpClient 都一致）。
        /// </summary>
        internal ChatCompletionClient Client
        {
            get { return _client; }
        }

        /// <summary>
        /// 按 <see cref="DetectorOptions"/> 从环境变量 / MSIFAITOOLS 设置 / App.config 建一个实例。
        /// 未配置密钥时抛 <see cref="InvalidOperationException"/>（调用方记日志后跳过 AI）。
        /// </summary>
        public static TzAiExtractor Create(DetectorOptions o)
        {
            if (o == null)
            {
                throw new ArgumentNullException(nameof(o));
            }

            AiApiOptions options = AiApiOptions.FromConfiguration();

            if (!options.IsConfigured)
            {
                string detail = string.IsNullOrEmpty(AiApiOptions.LastConfigurationError)
                    ? string.Empty
                    : "（" + AiApiOptions.LastConfigurationError + "）";

                throw new InvalidOperationException(
                    "未配置 AI 接口密钥：请设置环境变量 " + AiApiOptions.ApiKeyEnvironmentKey +
                    "，或在 MSIFAITOOLS 的【AI 设置】里填写（" + AiApiOptions.SharedSettingsFilePath +
                    "），或在 App.config 里写 AiApiKey" + detail);
            }

            return new TzAiExtractor(new ChatCompletionClient(options), o.TzAiMode,
                o.TzAiMaxGridChars, o.Log);
        }

        /// <summary>
        /// 调一次 AI 判一张表，返回核对通过的明细（失败时返回空表并往 <paramref name="stats"/> 记原因，
        /// 不抛异常 —— AI 是补足，不该让整批 tz.json 失败）。
        /// </summary>
        /// <param name="grid">工作表内容（行 × 列，1 起算行号）。</param>
        /// <param name="fileName">来源 Excel 文件名（写进 JSON 的 file）。</param>
        /// <param name="drawingNo">图番（写进 JSON 的 drawingNo）。</param>
        /// <param name="sheetName">表名（写进 JSON 的 sheet）。</param>
        /// <param name="stats">统计（调用次数 / 采纳条数 / 丢弃条数等）。</param>
        public List<TzEntry> Extract(IList<string[]> grid, string fileName, string drawingNo,
            string sheetName, TzAiStats stats)
        {
            List<TzEntry> entries = new List<TzEntry>();

            if (stats == null)
            {
                stats = new TzAiStats();
            }

            if (grid == null || grid.Count == 0)
            {
                return entries;
            }

            string gridText = BuildGridText(grid);

            if (gridText.Length == 0)
            {
                return entries;
            }

            TzAiPromptSet prompts = TzAiPromptStore.Load();

            if (prompts == null || !prompts.IsValid)
            {
                stats.Notes.Add("AI 提示语不可用（" + TzAiPromptStore.LastLoadError + "）");
                return entries;
            }

            stats.CallCount++;

            string content;

            try
            {
                content = _client.Complete(prompts.SystemPrompt,
                    prompts.BuildUserPrompt(drawingNo, sheetName, gridText));
            }
            catch (Exception ex)
            {
                stats.Notes.Add("AI 调用失败（" + fileName + " / " + sheetName + "）：" + ex.Message);
                return entries;
            }

            IList<TzAiItem> items;

            try
            {
                items = TzAiResponseParser.Parse(content, stats.Notes);
            }
            catch (Exception ex)
            {
                stats.Notes.Add("AI 回复解析失败（" + fileName + " / " + sheetName + "）：" + ex.Message);
                return entries;
            }

            stats.ParsedItemCount += items.Count;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (TzAiItem item in items)
            {
                if (entries.Count >= MaxItemsPerCall)
                {
                    break;
                }

                int row = ResolveRow(grid, item);

                if (row <= 0)
                {
                    stats.RejectedItemCount++;
                    continue;
                }

                string[] line = grid[row - 1];

                string variety = ResolveField(grid, row, line, item.Variety, stats, true);
                string partName = ResolveField(grid, row, line, item.PartName, stats, false);
                string frameKind = ResolveField(grid, row, line, item.FrameKind, stats, true);
                string partCode = ResolveField(grid, row, line, item.PartCode, stats, false);
                string specialSize = ResolveField(grid, row, line, item.SpecialSize, stats, false);

                // 五个字段全空（AI 只给了一行行号）→ 这不是明细
                if (variety.Length == 0 && partName.Length == 0 && partCode.Length == 0 &&
                    specialSize.Length == 0)
                {
                    stats.RejectedItemCount++;
                    continue;
                }

                string key = row.ToString(CultureInfo.InvariantCulture) + "|" + CsvFile.Normalize(variety) + "|" +
                             CsvFile.Normalize(partName) + "|" + CsvFile.Normalize(partCode) + "|" +
                             CsvFile.Normalize(specialSize);

                if (!seen.Add(key))
                {
                    continue;
                }

                entries.Add(new TzEntry
                {
                    File = fileName,
                    DrawingNumber = drawingNo,
                    Sheet = sheetName,
                    Row = row,
                    Variety = variety,
                    PartName = partName,
                    FrameKind = frameKind,
                    PartCode = partCode,
                    SpecialSize = specialSize,
                    Source = SourceAi
                });

                stats.AcceptedItemCount++;
            }

            if (entries.Count > 0 && _log != null)
            {
                _log("[INFO] tz.json：AI 判定 " + fileName + " 的「" + sheetName + "」表 → "
                     + entries.Count + " 条明细（模型 " + _client.Options.Model + "）。");
            }

            return entries;
        }

        /// <summary>
        /// 定位这条明细在表里的行号：
        /// ① AI 给了 1..N 的行号 → 直接用；
        /// ② 行号缺失 / 越界 → 用部品コード / 部品名 / 特注寸法在表里找「唯一」出现的行；
        /// ③ 找不到或多行命中 → 返回 0（整条丢弃，不猜）。
        /// </summary>
        private static int ResolveRow(IList<string[]> grid, TzAiItem item)
        {
            if (item.Row >= 1 && item.Row <= grid.Count)
            {
                return item.Row;
            }

            string[] probes = { item.PartCode, item.PartName, item.SpecialSize };

            foreach (string probe in probes)
            {
                if (string.IsNullOrWhiteSpace(probe))
                {
                    continue;
                }

                string normalized = CsvFile.Normalize(probe);

                if (normalized.Length < 2)
                {
                    continue;
                }

                int found = 0;

                for (int r = 0; r < grid.Count; r++)
                {
                    if (!RowContains(grid[r], normalized))
                    {
                        continue;
                    }

                    if (found > 0)
                    {
                        found = -1;     // 多行命中 → 有歧义，不能用
                        break;
                    }

                    found = r + 1;
                }

                if (found > 0)
                {
                    return found;
                }
            }

            return 0;
        }

        /// <summary>该行有没有哪一格（整格或包含）等于 <paramref name="normalized"/>。</summary>
        private static bool RowContains(string[] line, string normalized)
        {
            return FindCell(line, normalized, true) != null ||
                   FindCell(line, normalized, false) != null;
        }

        /// <summary>
        /// 把一个字段值回原表核对：
        /// ① 本行某格「整格相等」→ 用原表文字；
        /// ② 本行某格「包含」这个值（值夹在长格子里）→ 用 AI 原文（字面确实在表里）；
        /// ③ 合并单元格（值写在上面几行）→ 往上找 <see cref="MaxInheritRows"/> 行；
        /// ④ 都找不到 → 视为 AI 编造，丢弃该字段并计数。
        /// </summary>
        private static string ResolveField(IList<string[]> grid, int row, string[] line, string value,
            TzAiStats stats, bool allowInherit)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            string normalized = CsvFile.Normalize(trimmed);

            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            string exact = FindCell(line, normalized, true);

            if (exact != null)
            {
                return exact;
            }

            if (normalized.Length >= 2 && FindCell(line, normalized, false) != null)
            {
                stats.AiTextValueCount++;

                return trimmed;
            }

            if (allowInherit)
            {
                for (int offset = 1; offset <= MaxInheritRows; offset++)
                {
                    int otherRow = row - offset;

                    if (otherRow < 1 || otherRow > grid.Count)
                    {
                        continue;
                    }

                    exact = FindCell(grid[otherRow - 1], normalized, true);

                    if (exact != null)
                    {
                        return exact;
                    }
                }
            }

            stats.RejectedValueCount++;

            return string.Empty;
        }

        /// <summary>
        /// 在一行里找与 <paramref name="normalized"/> 匹配的格子：
        /// <paramref name="exact"/> = true 要求整格相等，false 只要包含。
        /// 命中时返回该格原文（已 Trim），否则返回 null。
        /// </summary>
        private static string FindCell(string[] line, string normalized, bool exact)
        {
            if (line == null || normalized.Length == 0)
            {
                return null;
            }

            foreach (string cell in line)
            {
                if (string.IsNullOrWhiteSpace(cell))
                {
                    continue;
                }

                string cellText = CsvFile.Normalize(cell);

                if (cellText.Length == 0)
                {
                    continue;
                }

                if (exact)
                {
                    if (string.Equals(cellText, normalized, StringComparison.Ordinal))
                    {
                        return cell.Trim();
                    }
                }
                else if (cellText.IndexOf(normalized, StringComparison.Ordinal) >= 0)
                {
                    return cell.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// 把工作表摊成送 AI 的文本：每行 <c>#行号: C列号=值 | C列号=值</c>，
        /// 空行 / 空格子不写（省 token，行号不会因此错位）。
        /// 超过 <see cref="DetectorOptions.TzAiMaxGridChars"/> 时截断并注明。
        /// </summary>
        private string BuildGridText(IList<string[]> grid)
        {
            StringBuilder builder = new StringBuilder();

            for (int r = 0; r < grid.Count; r++)
            {
                string[] line = grid[r];

                if (line == null)
                {
                    continue;
                }

                StringBuilder rowBuilder = new StringBuilder();

                for (int c = 0; c < line.Length; c++)
                {
                    if (string.IsNullOrWhiteSpace(line[c]))
                    {
                        continue;
                    }

                    // 格子内的换行压成 ~（与 CSV 一致），避免把一行明细拆成多行送出去
                    string text = CsvFile.SingleLine(line[c]).Trim();

                    if (text.Length == 0)
                    {
                        continue;
                    }

                    if (rowBuilder.Length > 0)
                    {
                        rowBuilder.Append(" | ");
                    }

                    rowBuilder.Append('C').Append((c + 1).ToString(CultureInfo.InvariantCulture))
                        .Append('=').Append(text);
                }

                if (rowBuilder.Length == 0)
                {
                    continue;
                }

                string rowText = "#" + (r + 1).ToString(CultureInfo.InvariantCulture) + ": " + rowBuilder;

                if (builder.Length + rowText.Length + 1 > _maxGridChars)
                {
                    builder.Append("（表内容超过 ")
                        .Append(_maxGridChars.ToString(CultureInfo.InvariantCulture))
                        .Append(" 字，后面的行已省略）");
                    break;
                }

                builder.AppendLine(rowText);
            }

            return builder.ToString().Trim();
        }
    }
}
