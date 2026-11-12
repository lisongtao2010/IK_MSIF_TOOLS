using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CadPartSplitter
{
    /// <summary>
    /// 「非 Excel 特注品文件」的 AI 判定通道：把文件摊成的文本交给 AI，判出部品明细后
    /// <b>逐字段回正文核对</b>（值必须在正文里出现过），核对不上的丢掉。
    ///
    /// <para>
    /// 与 <see cref="TzAiExtractor"/> 的区别只有「核对方式」：Excel 能按行号回原表逐格核对，
    /// 非 Excel 没有行列结构，只能核对「这个值在正文里出现过没有」——
    /// 所以这条通道宁可少收，也不把 AI 编的内容写进特注品.json。
    /// </para>
    ///
    /// <para>
    /// 提示语见 <see cref="TzFileAiPromptStore"/>（默认 <c>Prompts\TzJsonFileAi.Prompt.txt</c>）。
    /// 写出的明细 <c>source = ai-file</c>、<c>sheet = (ファイル)</c>、<c>row = 0</c>
    /// （没有行号可写；下游只按那五个字段取数，不认识这几个字段也没影响）。
    /// </para>
    /// </summary>
    internal sealed class TzFileAiExtractor
    {
        /// <summary>特注品.json 里 <c>source</c> 字段表示「非 Excel 文件由 AI 判出」的值。</summary>
        public const string SourceAiFile = "ai-file";

        /// <summary>非 Excel 文件写进 <c>sheet</c> 的占位值（没有工作表这个概念）。</summary>
        public const string SheetName = "(ファイル)";

        /// <summary>一次最多采纳的明细条数（防止模型复读整篇文件刷爆特注品.json）。</summary>
        private const int MaxItemsPerCall = 400;

        private readonly ChatCompletionClient _client;

        private readonly int _maxChars;

        private readonly Action<string> _log;

        public TzFileAiExtractor(ChatCompletionClient client, int maxChars, Action<string> log)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            _client = client;
            _maxChars = maxChars < 2000 ? 2000 : maxChars;
            _log = log;
        }

        /// <summary>本次的接口配置（日志里打印模型 / 来源用）。</summary>
        public AiApiOptions Options
        {
            get { return _client.Options; }
        }

        /// <summary>
        /// 调一次 AI 判一个非 Excel 文件，返回核对通过的明细（失败时返回空表并往 <paramref name="stats"/>
        /// 记原因，不抛异常 —— AI 是补足，不该让整批 特注品.json 失败）。
        /// </summary>
        /// <param name="filePath">源文件全路径（只是日志 / JSON 用，内容已在 <paramref name="source"/> 里）。</param>
        /// <param name="source">摊平后的正文（见 <see cref="TzSourceTextReader"/>）。</param>
        /// <param name="stats">统计（调用次数 / 采纳条数 / 丢弃条数等）。</param>
        public List<TzEntry> Extract(string filePath, TzSourceText source, TzAiStats stats)
        {
            var entries = new List<TzEntry>();

            if (stats == null)
            {
                stats = new TzAiStats();
            }

            if (string.IsNullOrWhiteSpace(filePath) || source == null ||
                string.IsNullOrWhiteSpace(source.Text))
            {
                return entries;
            }

            string fileName = Path.GetFileName(filePath);
            string drawingNumber = Path.GetFileNameWithoutExtension(filePath);

            TzFileAiPromptSet prompts = TzFileAiPromptStore.Load();

            if (prompts == null || !prompts.IsValid)
            {
                stats.Notes.Add("AI 提示语不可用（" + TzFileAiPromptStore.LastLoadError + "）");
                return entries;
            }

            string truncatedNote = source.Truncated
                ? "（正文超过 " + _maxChars.ToString() + " 字，后面的内容已省略；只能根据看到的部分判断）"
                : string.Empty;

            stats.CallCount++;

            string content;

            try
            {
                content = _client.Complete(prompts.SystemPrompt,
                    prompts.BuildUserPrompt(drawingNumber, fileName, source.Kind, truncatedNote,
                        source.Text));
            }
            catch (Exception ex)
            {
                stats.Notes.Add("AI 调用失败（" + fileName + "）：" + ex.Message);
                return entries;
            }

            IList<TzAiItem> items;

            try
            {
                items = TzAiResponseParser.Parse(content, stats.Notes);
            }
            catch (Exception ex)
            {
                stats.Notes.Add("AI 回复解析失败（" + fileName + "）：" + ex.Message);
                return entries;
            }

            stats.ParsedItemCount += items.Count;

            // 核对基准：整篇正文去空格 / 换行后的样子（与 CsvFile.Normalize 的口径一致）
            string normalizedText = CsvFile.Normalize(source.Text);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (TzAiItem item in items)
            {
                if (entries.Count >= MaxItemsPerCall)
                {
                    break;
                }

                string variety = ResolveField(item.Variety, normalizedText, stats);
                string partName = ResolveField(item.PartName, normalizedText, stats);
                string frameKind = ResolveField(item.FrameKind, normalizedText, stats);
                string partCode = ResolveField(item.PartCode, normalizedText, stats);
                string specialSize = ResolveField(item.SpecialSize, normalizedText, stats);

                // 五个字段全空（AI 说的值在正文里一个都找不到）→ 不是明细
                if (variety.Length == 0 && partName.Length == 0 && partCode.Length == 0 &&
                    specialSize.Length == 0)
                {
                    stats.RejectedItemCount++;
                    continue;
                }

                string key = CsvFile.Normalize(variety) + "|" + CsvFile.Normalize(partName) + "|" +
                             CsvFile.Normalize(frameKind) + "|" + CsvFile.Normalize(partCode) + "|" +
                             CsvFile.Normalize(specialSize);

                if (!seen.Add(key))
                {
                    continue;
                }

                entries.Add(new TzEntry
                {
                    File = fileName,
                    DrawingNumber = drawingNumber,
                    Sheet = SheetName,
                    Row = 0,        // 非 Excel 没有行号
                    Variety = variety,
                    PartName = partName,
                    FrameKind = frameKind,
                    PartCode = partCode,
                    SpecialSize = specialSize,
                    Source = SourceAiFile
                });

                stats.AcceptedItemCount++;
            }

            if (entries.Count > 0 && _log != null)
            {
                _log("[INFO] 特注品.json：AI 判定 " + fileName + "（" + source.Kind + "）→ "
                     + entries.Count + " 条明细（模型 " + _client.Options.Model + "）。");
            }

            return entries;
        }

        /// <summary>
        /// 把一个字段值回正文核对：值（去空格 / 换行后）在正文里出现过就用 AI 给的原文，
        /// 否则视为 AI 编造、丢弃该字段并计数。
        /// </summary>
        private static string ResolveField(string value, string normalizedText, TzAiStats stats)
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

            if (normalizedText.IndexOf(normalized, StringComparison.Ordinal) >= 0)
            {
                return trimmed;
            }

            stats.RejectedValueCount++;

            return string.Empty;
        }
    }
}
