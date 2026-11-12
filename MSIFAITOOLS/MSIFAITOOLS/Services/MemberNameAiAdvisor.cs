using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MSIFAITOOLS.Services
{
    /// <summary>单个図番的【AI 分析】（部材名 AI 推荐）结果。</summary>
    internal sealed class MemberNameAiFileResult
    {
        public MemberNameAiFileResult(string drawingNo)
        {
            DrawingNo = drawingNo ?? string.Empty;
        }

        /// <summary>図番（PDF 文件名去扩展名）。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>需要判定的行数（数据来源 = CMD分析・アッセンブリ図 且 部材名里含 | 的行）。</summary>
        public int TargetRowCount { get; set; }

        /// <summary>AI 给出推荐的行数。</summary>
        public int SuggestedRowCount { get; set; }

        /// <summary>实际写进数据库的行数。</summary>
        public int SavedRowCount { get; set; }

        /// <summary>跳过 / 失败原因（正常且全部判定成功时为空）。</summary>
        public string Message { get; set; }

        /// <summary>是否因为异常中断（其它図番继续处理）。</summary>
        public bool Failed { get; set; }
    }

    /// <summary>整批【AI 分析】（部材名 AI 推荐）的汇总。</summary>
    internal sealed class MemberNameAiSummary
    {
        public MemberNameAiSummary(string databasePath)
        {
            DatabasePath = databasePath ?? string.Empty;
            Files = new List<MemberNameAiFileResult>();
        }

        /// <summary>部品基本信息数据库路径。</summary>
        public string DatabasePath { get; private set; }

        /// <summary>每个「アッセンブリ図」PDF 的结果。</summary>
        public IList<MemberNameAiFileResult> Files { get; private set; }

        /// <summary>目标 PDF 数（【分类结果】里「アッセンブリ図」的数量）。</summary>
        public int TargetDrawingCount { get; set; }

        /// <summary>实际有待判定行的図番数。</summary>
        public int ProcessedDrawingCount { get; set; }

        /// <summary>需要判定的行数合计。</summary>
        public int TargetRowCount { get; set; }

        /// <summary>AI 给出推荐的行数合计。</summary>
        public int SuggestedRowCount { get; set; }

        /// <summary>写库成功的行数合计。</summary>
        public int SavedRowCount { get; set; }

        /// <summary>跳过的行数合计（没有 parts_info.json / 没有候选 / AI 未给出）。</summary>
        public int SkippedRowCount { get; set; }

        /// <summary>异常失败的図番数。</summary>
        public int FailedDrawingCount { get; set; }

        /// <summary>实际调用 AI 的次数（每页一次）。</summary>
        public int AiCallCount { get; set; }

        /// <summary>部材マスタ（部材部品 数据库，首次运行时由 部材部品.csv 导入）的路径；没读到时为空串。</summary>
        public string MemberCatalogPath { get; set; }

        /// <summary>部材マスタ 里的部材名 件数（去重后）。</summary>
        public int MemberCatalogMemberCount { get; set; }

        /// <summary>部材マスタ 里的部品名 件数（去重后）。</summary>
        public int MemberCatalogPartCount { get; set; }

        /// <summary>提示语来源（外部文件 / 内置资源 / 内置默认值）。</summary>
        public string PromptSource { get; set; }
    }

    /// <summary>
    /// 【AI 分析】：判定 / 修正「部材名」，结果写进「部材名AI推荐」列。
    ///
    /// <para>
    /// <b>为什么要判</b>：【CMD 分析】第 1 步用「番号」到 <c>parts_info.json</c> 里找圈号
    /// （<c>callouts</c>），再把圈号所在零件图的<b>全部</b> <c>texts</c> 去掉数字 / <c>XX部</c> 后用
    /// <c>|</c> 连起来（<see cref="PartsInfoIndex.ResolveCalloutMemberName(int)"/>）。这里有三个问题：
    /// ① 两个番号（如 1 和 3）在图面上离得很近、又落进同一张零件图时，代码分不清谁是谁，
    /// 只能两个都挂上 → <c>鴨居|上レール</c>；
    /// ② 图上的文字不一定是部材名（<c>部品ｾｯﾄ</c> / <c>取説</c> / <c>鬼目ﾅｯﾄ</c> 是部品，不是部材）；
    /// ③ 图纸上根本可能没写部材名，只写部品名。
    /// </para>
    ///
    /// <para>
    /// <b>本类做什么</b>：处理「数据来源 = <see cref="PartSourceKind.CmdAssembly"/>」的<b>全部</b>行
    /// （部材名为空 / 已经是「その他」/ 单个部材名 / 含 <c>|</c> 的行都要判），把
    /// ①该番号的圈号坐标、②同一零件图上其它番号的圈号坐标、③零件图上各个文字（含部材名候选）的坐标、
    /// ④部材マスタ（<see cref="MemberPartRepository"/> 的「部材部品」表：部材名 一览 + 部品名 → 部材名，
    /// 原 <c>部材部品\部材部品.csv</c>，第一次运行时导入，见 <see cref="MemberPartCatalog"/>
    /// 见 <see cref="MemberPartCatalog"/>）
    /// 拼成提示词交给 AI，让 AI 判断每个番号到底属于哪个部材；
    /// 图纸上没写部材名时允许从部材マスタ 的部材名里选，判断不出部材时回答「その他」。
    /// 结果写进 <see cref="PartBasicInfo.MemberNameAiSuggestion"/>（数据库列「部材名AI推荐」）。
    /// <b>不覆盖「部材名」，不改其它任何列</b>（<see cref="PartBasicInfoRepository.UpdateMemberNameSuggestions"/>）。
    /// </para>
    ///
    /// <para>
    /// 提示语来自 exe 目录 <c>Prompts\MemberNameAdvisor.Prompt.txt</c>（运行时可改，
    /// 见 <see cref="MemberNamePromptStore"/>）；每页调用一次 AI。
    /// </para>
    /// </summary>
    internal sealed class MemberNameAiAdvisor
    {
        /// <summary>「部材名」里区分多个部材名的分隔符（由 CMD 分析写入；AI 判定时只留正确的那一个）。</summary>
        public const char MemberNameSeparator = '|';

        /// <summary>一次最多送给 AI 的零件图文字条数（超出截断，避免提示词过长）。</summary>
        private const int MaxTextRowsPerPage = 240;

        /// <summary>一次最多送给 AI 的圈号条数。</summary>
        private const int MaxCalloutRowsPerPage = 120;

        /// <summary>部材マスタ 表里每个部材名 最多列出的部品名 条数（超出用「…」省略）。</summary>
        private const int MaxPartNamesPerMember = 16;

        private readonly ChatCompletionClient _client;

        private readonly string _databasePath;

        public MemberNameAiAdvisor(AiApiOptions options, string databasePath)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // 未配置密钥时构造函数直接抛出（与【AI 分析】原来的行为一致）
            _client = new ChatCompletionClient(options);
            _databasePath = string.IsNullOrWhiteSpace(databasePath)
                ? PartBasicInfoRepository.ResolveDatabasePath()
                : databasePath.Trim();
        }

        /// <summary>本次使用的数据库路径。</summary>
        public string DatabasePath
        {
            get { return _databasePath; }
        }

        /// <summary>
        /// 这一行是否需要让 AI 判定部材名：
        /// 数据来源 = <see cref="PartSourceKind.CmdAssembly"/>（CMD 分析・アッセンブリ図）。
        /// <para>
        /// 以前只处理「部材名」里含 <see cref="MemberNameSeparator"/> 的行；现在为了让 AI 判断
        /// 「取到的部材名到底是不是部材」，该来源的行<b>全部</b>都要判定
        /// （部材名为空 / 已经是「その他」/ 单个部材名 / 含 <c>|</c> 的行都一样）。
        /// </para>
        /// </summary>
        public static bool NeedsAiMemberName(PartBasicInfo part)
        {
            if (part == null)
            {
                return false;
            }

            return string.Equals(part.SourceKind, PartSourceKind.CmdAssembly, StringComparison.Ordinal);
        }

        /// <summary>给定行里需要 AI 判定的行数。</summary>
        public static int CountTargets(IList<PartBasicInfo> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return 0;
            }

            int count = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (NeedsAiMemberName(part))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 批量处理【分类结果】里「アッセンブリ図」的 PDF：对该図番里「数据来源 = CMD分析・アッセンブリ図」的
        /// 全部行调 AI 判定部材名（参照 <c>部材部品\部材部品.csv</c>），结果写进「部材名AI推荐」列。
        /// 单个図番失败只记日志，不影响其它図番。
        /// </summary>
        /// <param name="classifications">一览（分类结果）数据。</param>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="onLog">日志回调（kind, message）。</param>
        public MemberNameAiSummary AnalyzeAndSave(IList<PdfClassification> classifications,
            string outputDirectory, Action<LogKind, string> onLog)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                throw new DirectoryNotFoundException(
                    "找不到分解输出目录（请先执行【开始分解】）：" + outputDirectory);
            }

            MemberNameAiSummary summary = new MemberNameAiSummary(_databasePath);
            MemberNamePromptSet prompts = MemberNamePromptStore.Load();
            summary.PromptSource = prompts.Source;

            // 部材マスタ（部材部品 数据库；第一次运行时由 部材部品\部材部品.csv 导入）：
            // 部材名 一览 + 部品名 → 部材名。
            // 图纸上不一定写着部材名，用它给 AI 一份「可以回答的部材名」和「部品 → 部材」的对照。
            List<string> catalogNotes = new List<string>();
            MemberPartCatalog catalog = MemberPartCatalogLoader.LoadFor(outputDirectory, catalogNotes);
            summary.MemberCatalogPath = catalog.Path;
            summary.MemberCatalogMemberCount = catalog.MemberNameCount;
            summary.MemberCatalogPartCount = catalog.PartNameCount;

            ReportLog(onLog, LogKind.Info,
                "部材名AI推荐：处理「数据来源 = " + PartSourceKind.CmdAssembly +
                "」的全部行（部材名 为空 / その他 / 含 " + MemberNameSeparator +
                " 的行都要判）→ 结果写进「" + PartBasicInfoRepository.MemberNameAiColumnName + "」列");
            ReportLog(onLog, LogKind.Info, "AI 提示语：" + prompts.Source);

            if (catalog.IsEmpty)
            {
                ReportLog(onLog, LogKind.Warning,
                    "部材マスタ 不可用：本次只用图纸上的文字判定部材名（部材部品 数据库 " +
                    MemberPartRepository.ResolveDatabasePath() + " 里还没有数据，也没读到 " +
                    MemberPartCatalogLoader.FolderName + "\\" + MemberPartCatalogLoader.FileName + "）");
            }
            else
            {
                ReportLog(onLog, LogKind.Info, string.Format(
                    "部材マスタ：{0}（部材名 {1} 件 / 部品名 {2} 件）", catalog.Path,
                    catalog.MemberNameCount, catalog.PartNameCount));
            }

            foreach (string note in catalogNotes)
            {
                ReportLog(onLog, LogKind.Warning, "  " + note);
            }

            ReportLog(onLog, LogKind.Info, "部品基本信息数据库：" + _databasePath);

            if (!string.IsNullOrEmpty(MemberNamePromptStore.LastLoadError))
            {
                ReportLog(onLog, LogKind.Warning, "  " + MemberNamePromptStore.LastLoadError);
            }

            List<PdfClassification> targets = PartAnalysisService.SelectAssemblyTargets(classifications);
            summary.TargetDrawingCount = targets.Count;

            if (targets.Count == 0)
            {
                ReportLog(onLog, LogKind.Warning,
                    "一览中没有分类为「" + PdfCategoryNames.Assembly + "」的 PDF，无需 AI 分析。");

                return summary;
            }

            using (PartBasicInfoRepository repository = new PartBasicInfoRepository(_databasePath))
            {
                foreach (PdfClassification target in targets)
                {
                    string drawingNo = PartBasicInfoRepository.ResolveDrawingNo(target.FilePath);
                    MemberNameAiFileResult fileResult = new MemberNameAiFileResult(drawingNo);
                    summary.Files.Add(fileResult);

                    if (drawingNo.Length == 0)
                    {
                        fileResult.Message = "无法从 PDF 文件名解析出図番";
                        ReportLog(onLog, LogKind.Warning, "  跳过：" + fileResult.Message);

                        continue;
                    }

                    try
                    {
                        ProcessDrawing(repository, drawingNo, outputDirectory, prompts, catalog, fileResult,
                            summary, onLog);
                    }
                    catch (Exception ex)
                    {
                        fileResult.Failed = true;
                        fileResult.Message = ex.Message;
                        summary.FailedDrawingCount++;
                        ReportLog(onLog, LogKind.Error,
                            "  " + drawingNo + "：AI 分析失败（" + ex.Message + "）");
                    }
                }
            }

            return summary;
        }

        /// <summary>把汇总结果写成一段可显示 / 可记日志的文字。</summary>
        public static string BuildSummaryText(MemberNameAiSummary summary)
        {
            if (summary == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();

            builder.Append("部材名 AI 推荐完成：目标図番 ").Append(summary.TargetDrawingCount).Append(" 个");
            builder.Append("，有待判定行的図番 ").Append(summary.ProcessedDrawingCount).Append(" 个");
            builder.Append("，需要判定 ").Append(summary.TargetRowCount).Append(" 行");
            builder.Append("，AI 推荐 ").Append(summary.SuggestedRowCount).Append(" 行");
            builder.Append("，写库 ").Append(summary.SavedRowCount).Append(" 行");
            builder.Append("，跳过 ").Append(summary.SkippedRowCount).Append(" 行");
            builder.Append("，调用 AI ").Append(summary.AiCallCount).Append(" 次");

            if (!string.IsNullOrEmpty(summary.MemberCatalogPath))
            {
                builder.Append("，部材マスタ 部材名 ").Append(summary.MemberCatalogMemberCount)
                    .Append(" 件 / 部品名 ").Append(summary.MemberCatalogPartCount).Append(" 件");
            }
            else
            {
                builder.Append("，部材マスタ 未找到（只用图纸文字）");
            }

            if (summary.FailedDrawingCount > 0)
            {
                builder.Append("，失败 ").Append(summary.FailedDrawingCount).Append(" 个図番");
            }

            return builder.ToString();
        }

        /// <summary>
        /// 处理一个図番：从数据库取出全部行 → 筛出「数据来源 = CMD分析・アッセンブリ図」的行 →
        /// 按「页码」分组（每页一份 <c>parts_info.json</c>）→ 每页调一次 AI →
        /// 只把推荐值写进「部材名AI推荐」列。
        /// </summary>
        private void ProcessDrawing(PartBasicInfoRepository repository, string drawingNo, string outputDirectory,
            MemberNamePromptSet prompts, MemberPartCatalog catalog, MemberNameAiFileResult fileResult,
            MemberNameAiSummary summary, Action<LogKind, string> onLog)
        {
            List<PartBasicInfo> rows = new List<PartBasicInfo>();

            foreach (PartBasicInfo part in repository.Query(drawingNo))
            {
                if (NeedsAiMemberName(part))
                {
                    rows.Add(part);
                }
            }

            fileResult.TargetRowCount = rows.Count;

            if (rows.Count == 0)
            {
                fileResult.Message = "没有「数据来源 = " + PartSourceKind.CmdAssembly + "」的行，无需 AI 分析";

                return;
            }

            summary.ProcessedDrawingCount++;
            summary.TargetRowCount += rows.Count;
            ReportLog(onLog, LogKind.Info,
                string.Format("  {0}：需要判定 {1} 行（部材名 {2}）", drawingNo, rows.Count,
                    DescribeMemberNameStats(rows)));

            // 按页码分组（parts_info.json：第 1 页在 <図番>\，第 2 页起在 <図番>_P2\）
            List<int> pages = new List<int>();
            Dictionary<int, List<PartBasicInfo>> byPage = new Dictionary<int, List<PartBasicInfo>>();

            foreach (PartBasicInfo part in rows)
            {
                int page = part.Page <= 0 ? 1 : part.Page;
                List<PartBasicInfo> pageRows;

                if (!byPage.TryGetValue(page, out pageRows))
                {
                    pageRows = new List<PartBasicInfo>();
                    byPage.Add(page, pageRows);
                    pages.Add(page);
                }

                pageRows.Add(part);
            }

            pages.Sort();

            List<PartBasicInfo> changed = new List<PartBasicInfo>();

            foreach (int page in pages)
            {
                List<PartBasicInfo> pageRows = byPage[page];
                string infoPath = ResolvePartsInfoPath(outputDirectory, drawingNo, page,
                    GetSourcePdfPath(pageRows));
                PartsInfoIndex index = PartsInfoIndex.Load(infoPath);

                if (index == null || !string.IsNullOrEmpty(index.LoadError))
                {
                    summary.SkippedRowCount += pageRows.Count;
                    fileResult.Message = AppendMessage(fileResult.Message,
                        string.Format("第 {0} 页读不到 {1}", page, PartsInfoIndex.FileName));
                    ReportLog(onLog, LogKind.Warning,
                        string.Format("    第 {0} 页：读不到 {1}，跳过 {2} 行（可重新执行【开始分解】生成）（{3}）", page,
                            PartsInfoIndex.FileName, pageRows.Count,
                            index == null ? infoPath : index.LoadError));

                    continue;
                }

                if (!HasAnyCallout(index, pageRows))
                {
                    summary.SkippedRowCount += pageRows.Count;
                    fileResult.Message = AppendMessage(fileResult.Message,
                        string.Format("第 {0} 页的番号在 {1} 里找不到圈号", page, PartsInfoIndex.FileName));
                    ReportLog(onLog, LogKind.Warning,
                        string.Format("    第 {0} 页：番号在 {1} 里找不到圈号（callouts），跳过 {2} 行",
                            page, PartsInfoIndex.FileName, pageRows.Count));

                    continue;
                }

                string userPrompt = BuildUserPrompt(prompts, drawingNo, page, index, pageRows, catalog);
                string response = _client.Complete(prompts.SystemPrompt, userPrompt);
                summary.AiCallCount++;

                // 程序先按坐标距离算一份「建议分配」：既写进提示词，也用于 AI 判不了 / 判得不稳时的兜底
                List<CandidateDistance> distances = CollectCandidateDistances(index, pageRows);
                Dictionary<int, string> suggested = BuildDistanceAssignments(distances);

                ApplyResponse(response, page, pageRows, index, catalog, distances, suggested, summary,
                    fileResult, changed, onLog);
            }

            if (changed.Count == 0)
            {
                return;
            }

            int saved = repository.UpdateMemberNameSuggestions(drawingNo, changed);
            fileResult.SavedRowCount = saved;
            summary.SavedRowCount += saved;
            ReportLog(onLog, LogKind.Success,
                string.Format("    {0}：已写入「{1}」{2} 行", drawingNo,
                    PartBasicInfoRepository.MemberNameAiColumnName, saved));
        }

        /// <summary>
        /// 拼一页的用户提示词：部材マスタ 表 + 圈号位置表 + 零件图文字表 + 距离表 + 待判定的行表。
        /// 只把「待判定番号所在零件图」的数据给 AI，但<b>同一张图上其它番号的坐标也一并给出</b>，
        /// AI 才能比较远近、判断「哪个番号贴哪个部材名」。
        /// </summary>
        private static string BuildUserPrompt(MemberNamePromptSet prompts, string drawingNo, int page,
            PartsInfoIndex index, IList<PartBasicInfo> rows, MemberPartCatalog catalog)
        {
            List<string> numbers = new List<string>();
            HashSet<string> seenNumbers = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> partIndexes = new HashSet<int>();

            foreach (PartBasicInfo part in rows)
            {
                string numberText = part.Number.ToString(CultureInfo.InvariantCulture);

                if (seenNumbers.Add(numberText))
                {
                    numbers.Add(numberText);
                }

                foreach (PartMarkPoint point in index.GetCalloutPoints(numberText))
                {
                    if (point != null)
                    {
                        partIndexes.Add(point.PartIndex);
                    }
                }
            }

            string text = prompts.UserTemplate;

            text = ReplaceToken(text, "DRAWING_NO", drawingNo);
            text = ReplaceToken(text, "PAGE", page.ToString(CultureInfo.InvariantCulture));
            text = ReplaceToken(text, "MEMBER_TABLE", BuildMemberTable(catalog));
            text = ReplaceToken(text, "CALLOUT_TABLE", BuildCalloutTable(index, partIndexes, numbers));
            text = ReplaceToken(text, "TEXT_TABLE", BuildTextTable(index, partIndexes));
            text = ReplaceToken(text, "TARGET_TABLE", BuildTargetTable(index, rows, catalog));
            text = ReplaceToken(text, "DISTANCE_TABLE", BuildDistanceTable(index, rows));

            return text;
        }

        /// <summary>「圈号（番号）在图纸上的位置」表：只列待判定行所在零件图里的圈号。</summary>
        private static string BuildCalloutTable(PartsInfoIndex index, ICollection<int> partIndexes,
            IList<string> targetNumbers)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("### 圈号（番号）在图纸上的位置");
            builder.AppendLine("| 番号 | 零件图 | x(mm) | y(mm) | 需要判定 |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");

            int written = 0;
            bool truncated = false;

            foreach (string number in index.CalloutNumbers)
            {
                bool needed = ContainsNumber(targetNumbers, number);

                foreach (PartMarkPoint point in index.GetCalloutPoints(number))
                {
                    if (point == null || !partIndexes.Contains(point.PartIndex))
                    {
                        continue;
                    }

                    if (written >= MaxCalloutRowsPerPage)
                    {
                        truncated = true;

                        break;
                    }

                    builder.Append("| ").Append(number)
                        .Append(" | ").Append(point.PartIndex.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(FormatCoordinate(point.X))
                        .Append(" | ").Append(FormatCoordinate(point.Y))
                        .Append(" | ").Append(needed ? "是" : "否")
                        .AppendLine(" |");
                    written++;
                }

                if (truncated)
                {
                    break;
                }
            }

            if (written == 0)
            {
                builder.AppendLine("（该页没有可用的圈号坐标）");
            }
            else if (truncated)
            {
                builder.AppendLine();
                builder.AppendLine("（圈号过多，只列出前 " + MaxCalloutRowsPerPage + " 条）");
            }

            return builder.ToString().Trim();
        }

        /// <summary>「零件图上的文字（含坐标）」表：部材名候选与位置标记（<c>A部</c>）都列出来。</summary>
        private static string BuildTextTable(PartsInfoIndex index, ICollection<int> partIndexes)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("### 零件图上的文字（含坐标）");
            builder.AppendLine("| 零件图 | 文字 | 类别 | x(mm) | y(mm) |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");

            List<int> ordered = new List<int>(partIndexes);
            ordered.Sort();

            int written = 0;
            bool truncated = false;

            foreach (int partIndex in ordered)
            {
                foreach (MemberNameCandidate item in index.GetPartTexts(partIndex))
                {
                    if (written >= MaxTextRowsPerPage)
                    {
                        truncated = true;

                        break;
                    }

                    builder.Append("| ").Append(partIndex.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(EscapeCell(item.Name))
                        .Append(" | ").Append(DescribeTextKind(item.Name))
                        .Append(" | ").Append(FormatCoordinate(item.X))
                        .Append(" | ").Append(FormatCoordinate(item.Y))
                        .AppendLine(" |");
                    written++;
                }

                if (truncated)
                {
                    break;
                }
            }

            if (written == 0)
            {
                builder.AppendLine("（该页没有可用的文字坐标）");
            }
            else if (truncated)
            {
                builder.AppendLine();
                builder.AppendLine("（文字过多，只列出前 " + MaxTextRowsPerPage + " 条）");
            }

            return builder.ToString().Trim();
        }

        /// <summary>「需要判定的行」表：番号 + 现在的部材名 + 图上候选部材名 + 部材マスタ线索。</summary>
        private static string BuildTargetTable(PartsInfoIndex index, IList<PartBasicInfo> rows,
            MemberPartCatalog catalog)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("### 需要判定的行");
            builder.AppendLine("| 番号 | 现在的部材名 | 图上候选部材名（优先从这里选） | 部材マスタ线索 |");
            builder.AppendLine("| --- | --- | --- | --- |");

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (PartBasicInfo part in rows)
            {
                if (part == null)
                {
                    continue;
                }

                string number = part.Number.ToString(CultureInfo.InvariantCulture);

                // 同一番号可能有多行（不同部品コード），只列一次
                if (!seen.Add(number))
                {
                    continue;
                }

                string candidates = BuildCandidateList(index, number);
                string hint = BuildCatalogHint(catalog, index, number, part.MemberName, part.PartName);

                builder.Append("| ").Append(number)
                    .Append(" | ").Append(EscapeCell(part.MemberName))
                    .Append(" | ").Append(candidates.Length == 0 ? "（无候选）" : candidates)
                    .Append(" | ").Append(hint.Length == 0 ? "-" : EscapeCell(hint))
                    .AppendLine(" |");
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// 「部材マスタ」表：可以回答的部材名 一览 + 每个部材名 下属的部品名。
        /// 图纸上不一定写着部材名（只写部品名 / 只写尺寸），AI 靠这张表把部品换成它所属的部材，
        /// 或直接回答一个图上没印出来的部材名。
        /// </summary>
        private static string BuildMemberTable(MemberPartCatalog catalog)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("### 部材マスタ（" + MemberPartCatalogLoader.FolderName + "\\" +
                MemberPartCatalogLoader.FileName + "）");

            if (catalog == null || catalog.IsEmpty)
            {
                builder.AppendLine("（没有可用的部材マスタ：只能从图上的文字里选，判断不出部材时回答「" +
                    PartBasicInfo.MemberNameFallback + "」）");

                return builder.ToString().Trim();
            }

            builder.AppendLine("可以回答的部材名：" + catalog.DescribeMemberNames());
            builder.AppendLine();
            builder.AppendLine("| 部材名 | 该部材下属的部品名（图上的文字可能是这些部品名） |");
            builder.AppendLine("| --- | --- |");

            foreach (string memberName in catalog.MemberNames)
            {
                List<string> partNames = new List<string>();

                foreach (string partName in catalog.GetPartNames(memberName))
                {
                    if (partNames.Count >= MaxPartNamesPerMember)
                    {
                        partNames.Add("…");
                        break;
                    }

                    partNames.Add(partName);
                }

                builder.Append("| ").Append(EscapeCell(memberName))
                    .Append(" | ").Append(partNames.Count == 0 ? "-" : EscapeCell(string.Join(" / ",
                        partNames.ToArray())))
                    .AppendLine(" |");
            }

            builder.AppendLine();
            builder.Append("这张表是「部品 → 部材」的对照，用在图上没写部材名、或图上只写了部品名的时候；");
            builder.Append("图上已经有部材名时以图上文字为准。");
            builder.Append("不属于任何部材的部品（部品セット / 取説 / ラベル 等）属于「" +
                PartBasicInfo.MemberNameFallback + "」。");

            return builder.ToString().Trim();
        }

        /// <summary>
        /// 「部材マスタ线索」列：<b>该行部品名在マスタ里属于哪些部材</b> + 现在的部材名 是部材还是部品 +
        /// 图上候选文字里哪些能在マスタ 里对上。没有线索时返回空串（提示词里显示 <c>-</c>）。
        /// </summary>
        private static string BuildCatalogHint(MemberPartCatalog catalog, PartsInfoIndex index, string numberText,
            string currentMemberName, string partName)
        {
            if (catalog == null || catalog.IsEmpty)
            {
                return string.Empty;
            }

            List<string> hints = new List<string>();

            // ① 该行「部品名」→ 部材名（1:N のときは全部並べる）。
            //    AI はこの一覧の中から選ぶ（图上の文字は「どれか」を決めるための根拠）
            IList<string> owners = catalog.GetMemberNamesForPart(partName);

            if (owners.Count > 0)
            {
                string ownerText = string.Join(" / ", new List<string>(owners).ToArray());
                hints.Add("该行部品名「" + partName.Trim() + "」在部材マスタ里属于：" + ownerText +
                    (owners.Count > 1
                        ? "（" + owners.Count + " 个部材都有这个部品 → 按图上文字 / 位置 / 区分选其中一个）"
                        : "（回答必须是这个部材名）"));
            }

            string member;

            if (string.IsNullOrWhiteSpace(currentMemberName))
            {
                hints.Add("现在的部材名（空）→ 需要判断");
            }
            else if (catalog.TryGetMemberName(currentMemberName, out member))
            {
                hints.Add("现在的部材名「" + member + "」= 部材マスタの部材名");
            }
            else if (catalog.TryResolvePartName(currentMemberName, out member))
            {
                hints.Add("现在的部材名「" + currentMemberName.Trim() + "」是部品名 → 部材名「" + member + "」");
            }

            foreach (MemberNameCandidate item in index.GetMemberNameCandidates(numberText))
            {
                string candidate = item == null ? string.Empty : item.Name;

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (catalog.TryGetMemberName(candidate, out member))
                {
                    continue;
                }

                if (!catalog.TryResolvePartName(candidate, out member))
                {
                    continue;
                }

                string hint = "文字「" + candidate.Trim() + "」是部品名 → 部材名「" + member + "」";

                if (!ContainsText(hints, hint))
                {
                    hints.Add(hint);
                }
            }

            return string.Join("；", hints.ToArray());
        }

        /// <summary>列表里是否已有完全相同的一条文字。</summary>
        private static bool ContainsText(IList<string> items, string text)
        {
            if (items == null || items.Count == 0 || string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (string item in items)
            {
                if (string.Equals(item, text, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>某个番号的候选部材名（按零件图顺序、归一化去重，用 <c> / </c> 连接）。</summary>
        private static string BuildCandidateList(PartsInfoIndex index, string numberText)
        {
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (MemberNameCandidate item in index.GetMemberNameCandidates(numberText))
            {
                if (seen.Add(NormalizeForCompare(item.Name)))
                {
                    names.Add(item.Name);
                }
            }

            return string.Join(" / ", names.ToArray());
        }

        /// <summary>
        /// 「圈号 → 候选部材名」的距离表（程序算好的精确距离）+ 程序建议分配。
        /// 让 AI 不必自己算距离（大模型对数值距离不稳定），只做取舍。
        /// </summary>
        private static string BuildDistanceTable(PartsInfoIndex index, IList<PartBasicInfo> rows)
        {
            List<CandidateDistance> distances = CollectCandidateDistances(index, rows);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("### 圈号到候选部材名的距离（程序已精确计算，单位 mm，越小 = 该文字离该番号的圈号越近）");
            builder.AppendLine("| 番号 | 候选部材名 | 零件图 | 距离(mm) | 该番号内第几近 |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");

            if (distances.Count == 0)
            {
                builder.AppendLine("（没有可比较的距离数据）");

                return builder.ToString().Trim();
            }

            List<int> numbers = new List<int>();
            Dictionary<int, List<CandidateDistance>> byNumber = new Dictionary<int, List<CandidateDistance>>();

            foreach (CandidateDistance item in distances)
            {
                List<CandidateDistance> items;

                if (!byNumber.TryGetValue(item.Number, out items))
                {
                    items = new List<CandidateDistance>();
                    byNumber.Add(item.Number, items);
                    numbers.Add(item.Number);
                }

                items.Add(item);
            }

            numbers.Sort();

            foreach (int number in numbers)
            {
                List<CandidateDistance> items = byNumber[number];

                items.Sort(delegate(CandidateDistance left, CandidateDistance right)
                {
                    int byDistance = left.Distance.CompareTo(right.Distance);

                    if (byDistance != 0)
                    {
                        return byDistance;
                    }

                    int byPartIndex = left.PartIndex.CompareTo(right.PartIndex);

                    return byPartIndex != 0 ? byPartIndex : string.CompareOrdinal(left.Name, right.Name);
                });

                for (int index2 = 0; index2 < items.Count; index2++)
                {
                    builder.Append("| ").Append(number.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(EscapeCell(items[index2].Name))
                        .Append(" | ").Append(items[index2].PartIndex.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(FormatCoordinate(items[index2].Distance))
                        .Append(" | ").Append((index2 + 1).ToString(CultureInfo.InvariantCulture))
                        .AppendLine(" |");
                }
            }

            Dictionary<int, string> assignments = BuildDistanceAssignments(distances);

            if (assignments.Count > 0)
            {
                builder.AppendLine();
                builder.Append("程序按「全局距离最小、一处文字只配一个番号」算出的建议分配：");

                List<int> assigned = new List<int>(assignments.Keys);
                assigned.Sort();

                for (int index3 = 0; index3 < assigned.Count; index3++)
                {
                    if (index3 > 0)
                    {
                        builder.Append('；');
                    }

                    builder.Append("番号 ").Append(assigned[index3].ToString(CultureInfo.InvariantCulture))
                        .Append(" → ").Append(assignments[assigned[index3]]);
                }

                builder.Append('。');
            }

            return builder.ToString().Trim();
        }

        /// <summary>「番号 → 候选部材名」的一条距离记录（程序算好的几何距离）。</summary>
        private sealed class CandidateDistance
        {
            public int Number { get; set; }

            public string Name { get; set; }

            public int PartIndex { get; set; }

            public double Distance { get; set; }
        }

        /// <summary>
        /// 该番号「最近候选」比「次近候选」近这个比例以上时，视为几何上已能确定
        /// （AI 给出别的答案时以距离为准，保证同一份数据每次结果一致）。
        /// </summary>
        private const double ConfidentGapRatio = 0.10;

        /// <summary>
        /// 算出每个待判定番号「各个候选部材名」到它圈号的距离（只比较同一零件图内的文字），
        /// 同一个候选名在多个零件图出现时只保留最近的那一处。
        /// </summary>
        private static List<CandidateDistance> CollectCandidateDistances(PartsInfoIndex index,
            IList<PartBasicInfo> rows)
        {
            List<CandidateDistance> result = new List<CandidateDistance>();

            if (index == null || rows == null)
            {
                return result;
            }

            Dictionary<int, IList<PartMarkPoint>> callouts = new Dictionary<int, IList<PartMarkPoint>>();

            // key = 番号|规范化文字 → 该番号下这个名字最近的一处（同一名字只算一个候选）
            Dictionary<string, CandidateDistance> byName = new Dictionary<string, CandidateDistance>(
                StringComparer.Ordinal);
            List<string> order = new List<string>();

            foreach (PartBasicInfo part in rows)
            {
                if (part == null)
                {
                    continue;
                }

                string numberText = part.Number.ToString(CultureInfo.InvariantCulture);
                IList<PartMarkPoint> points;

                if (!callouts.TryGetValue(part.Number, out points))
                {
                    points = index.GetCalloutPoints(numberText);

                    if (points == null)
                    {
                        points = new List<PartMarkPoint>();
                    }

                    callouts.Add(part.Number, points);
                }

                if (points.Count == 0)
                {
                    continue;
                }

                foreach (MemberNameCandidate candidate in index.GetMemberNameCandidates(numberText))
                {
                    if (candidate == null || string.IsNullOrEmpty(candidate.Name))
                    {
                        continue;
                    }

                    double nearest = double.MaxValue;

                    foreach (PartMarkPoint point in points)
                    {
                        if (point == null || point.PartIndex != candidate.PartIndex)
                        {
                            continue;
                        }

                        double dx = point.X - candidate.X;
                        double dy = point.Y - candidate.Y;
                        double distance = Math.Sqrt(dx * dx + dy * dy);

                        if (distance < nearest)
                        {
                            nearest = distance;
                        }
                    }

                    if (nearest == double.MaxValue)
                    {
                        continue;
                    }

                    string key = numberText + "|" + NormalizeForCompare(candidate.Name);
                    CandidateDistance existing;

                    if (byName.TryGetValue(key, out existing))
                    {
                        // 同一名字出现在多个零件图：只保留最近的那一处
                        if (nearest < existing.Distance)
                        {
                            existing.Distance = nearest;
                            existing.PartIndex = candidate.PartIndex;
                        }

                        continue;
                    }

                    byName.Add(key, new CandidateDistance
                    {
                        Number = part.Number,
                        Name = candidate.Name,
                        PartIndex = candidate.PartIndex,
                        Distance = nearest
                    });
                    order.Add(key);
                }
            }

            foreach (string key in order)
            {
                result.Add(byName[key]);
            }

            return result;
        }

        /// <summary>
        /// 按「全局距离从小到大、一个番号配一个文字、一处文字只配一个番号」贪心分配，
        /// 得到程序建议的「番号 → 部材名」（AI 没给答案 / 几何上明显更近时用它）。
        /// </summary>
        private static Dictionary<int, string> BuildDistanceAssignments(IList<CandidateDistance> distances)
        {
            Dictionary<int, string> assignments = new Dictionary<int, string>();

            if (distances == null || distances.Count == 0)
            {
                return assignments;
            }

            List<CandidateDistance> ordered = new List<CandidateDistance>(distances);

            ordered.Sort(delegate(CandidateDistance left, CandidateDistance right)
            {
                int byDistance = left.Distance.CompareTo(right.Distance);

                if (byDistance != 0)
                {
                    return byDistance;
                }

                int byNumber = left.Number.CompareTo(right.Number);

                return byNumber != 0 ? byNumber : string.CompareOrdinal(left.Name, right.Name);
            });

            HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (CandidateDistance item in ordered)
            {
                string nameKey = NormalizeForCompare(item.Name);

                if (assignments.ContainsKey(item.Number) || usedNames.Contains(nameKey))
                {
                    continue;
                }

                assignments.Add(item.Number, item.Name);
                usedNames.Add(nameKey);
            }

            return assignments;
        }

        /// <summary>
        /// 某番号「最近候选」相对「次近候选」的距离优势（0.10 = 近 10%）。
        /// 只有一个候选或没有候选时返回 <see cref="double.MaxValue"/>（视为已确定）。
        /// </summary>
        private static double GetDistanceGapRatio(IList<CandidateDistance> distances, int number)
        {
            double best = double.MaxValue;
            double second = double.MaxValue;

            if (distances != null)
            {
                foreach (CandidateDistance item in distances)
                {
                    if (item.Number != number)
                    {
                        continue;
                    }

                    if (item.Distance < best)
                    {
                        second = best;
                        best = item.Distance;
                    }
                    else if (item.Distance < second)
                    {
                        second = item.Distance;
                    }
                }
            }

            if (best == double.MaxValue || second == double.MaxValue || best <= 0)
            {
                return double.MaxValue;
            }

            return (second - best) / best;
        }

        /// <summary>
        /// 该页待判定的行里，是否至少有一个番号能在 <c>parts_info.json</c> 里找到圈号。
        /// 一个都找不到时没有可交给 AI 的坐标，直接跳过这一页（省一次 AI 调用）。
        /// </summary>
        private static bool HasAnyCallout(PartsInfoIndex index, IList<PartBasicInfo> rows)
        {
            foreach (PartBasicInfo part in rows)
            {
                if (part == null)
                {
                    continue;
                }

                if (index.GetCalloutPoints(part.Number.ToString(CultureInfo.InvariantCulture)).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 该番号的候选部材名只有一个时直接返回它（不需要 AI 判断）；多个或没有时返回空串。
        /// AI 没给结果 / 给了候选外的文字时用它兜底。
        /// </summary>
        private static string GetSingleCandidate(PartsInfoIndex index, int number)
        {
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (MemberNameCandidate item in
                index.GetMemberNameCandidates(number.ToString(CultureInfo.InvariantCulture)))
            {
                if (seen.Add(NormalizeForCompare(item.Name)))
                {
                    names.Add(item.Name);
                }
            }

            return names.Count == 1 ? names[0] : string.Empty;
        }

        /// <summary>
        /// 解析 AI 回应（<c>{"1":"鴨居","3":"上レール","5":"その他"}</c>），逐行写进
        /// <see cref="PartBasicInfo.MemberNameAiSuggestion"/>：
        /// 只接受「该番号候选集合里真实存在的文字」/ 部材マスタ 的部材名（含「その他」），
        /// 其它一律丢弃并计入跳过（防 AI 编造）。
        /// <para>
        /// 取值顺序：①AI 的答案；②AI 没给 → 坐标距离的建议分配；③候选只有一个 → 用它；
        /// ④部材マスタ 能判定的（现在的部材名 是部品名 → 它所属的部材名）→ 用它；
        /// ⑤都不行 → 跳过（不写推荐）。
        /// </para>
        /// </summary>
        private static void ApplyResponse(string response, int page, IList<PartBasicInfo> rows,
            PartsInfoIndex index, MemberPartCatalog catalog, IList<CandidateDistance> distances,
            IDictionary<int, string> suggested, MemberNameAiSummary summary,
            MemberNameAiFileResult fileResult, IList<PartBasicInfo> changed, Action<LogKind, string> onLog)
        {
            IDictionary<int, string> decisions = ParseResponse(response);
            string pageText = page.ToString(CultureInfo.InvariantCulture);

            if (decisions.Count == 0)
            {
                // AI 没给出可用 JSON：不整页放弃，仍按坐标距离 / 候选唯一来填
                fileResult.Message = AppendMessage(fileResult.Message,
                    "第 " + pageText + " 页 AI 未给出可用的 JSON");
                ReportLog(onLog, LogKind.Warning,
                    "    第 " + pageText + " 页：AI 未给出可用的 JSON（回应：" + Shorten(response) +
                    "），改为按坐标距离 / 候选唯一判定");
            }

            foreach (PartBasicInfo part in rows)
            {
                if (part == null)
                {
                    continue;
                }

                string value;
                string matched = string.Empty;

                if (decisions.TryGetValue(part.Number, out value))
                {
                    matched = MatchCandidate(index, part.Number, value, catalog);

                    if (matched.Length == 0)
                    {
                        ReportLog(onLog, LogKind.Warning,
                            string.Format(
                                "    第 {0} 页 番号 {1}：AI 给出的「{2}」既不在图上候选里、也不在部材マスタ里，已丢弃",
                                page, part.Number, value));
                    }
                    else
                    {
                        matched = PreferNearest(index, part.Number, matched, distances, suggested, page, onLog);
                    }
                }

                if (matched.Length == 0)
                {
                    // AI 没给 / 给的不可用：先用坐标距离的建议
                    matched = FindSuggested(index, part.Number, suggested, catalog);

                    if (matched.Length > 0)
                    {
                        ReportLog(onLog, LogKind.Info,
                            string.Format("    第 {0} 页 番号 {1}：AI 未给出，按坐标距离判定 → {2}", page,
                                part.Number, matched));
                    }
                }

                if (matched.Length == 0)
                {
                    // 仍没有：候选只有一个时它就是答案
                    matched = GetSingleCandidate(index, part.Number);
                }

                if (matched.Length == 0)
                {
                    // 还没有：用部材マスタ 判断现在的部材名（是部品名 → 换成它所属的部材名）
                    matched = ResolveFromCatalog(catalog, part.MemberName);

                    if (matched.Length > 0)
                    {
                        ReportLog(onLog, LogKind.Info,
                            string.Format("    第 {0} 页 番号 {1}：AI 未给出，按部材マスタ判定 → {2}", page,
                                part.Number, matched));
                    }
                }

                if (matched.Length == 0)
                {
                    summary.SkippedRowCount++;

                    continue;
                }

                part.MemberNameAiSuggestion = matched;
                changed.Add(part);
                summary.SuggestedRowCount++;
                fileResult.SuggestedRowCount++;
                ReportLog(onLog, LogKind.Info,
                    string.Format("    第 {0} 页 番号 {1}：{2} → {3}", page, part.Number,
                        DescribeMemberName(part.MemberName), matched));
            }
        }

        /// <summary>
        /// 从 AI 回应里取出「番号 → 部材名」。主要格式是扁平对象 <c>{"1":"鴨居"}</c>；
        /// 也容忍包一层数组的写法（<c>{"decisions":[{"番号":1,"部材名":"鴨居"}]}</c>）。
        /// </summary>
        /// <summary>
        /// AI 给出的答案若与「坐标距离上明显更近的候选」冲突（距离优势 ≥ <see cref="ConfidentGapRatio"/>），
        /// 改用距离最近者并记日志 —— 保证同一份数据每次点击结果一致。
        /// <para>
        /// 答案来自部材マスタ（图上没印这个文字，如「上枠」「その他」）时没有距离数据，
        /// 直接采用 AI 的判断，不用距离覆盖。
        /// </para>
        /// </summary>
        private static string PreferNearest(PartsInfoIndex index, int number, string matched,
            IList<CandidateDistance> distances, IDictionary<int, string> suggested, int page,
            Action<LogKind, string> onLog)
        {
            if (!IsTextCandidate(index, number, matched))
            {
                return matched;
            }

            string nearest;

            if (suggested == null || !suggested.TryGetValue(number, out nearest)
                || string.IsNullOrEmpty(nearest))
            {
                return matched;
            }

            if (string.Equals(NormalizeForCompare(nearest), NormalizeForCompare(matched),
                StringComparison.Ordinal))
            {
                return matched;
            }

            double gap = GetDistanceGapRatio(distances, number);

            if (gap < ConfidentGapRatio)
            {
                return matched;
            }

            string replaced = MatchCandidate(index, number, nearest, null);

            if (replaced.Length == 0)
            {
                return matched;
            }

            ReportLog(onLog, LogKind.Warning,
                string.Format("    第 {0} 页 番号 {1}：AI 判为「{2}」，但「{3}」在坐标上近 {4}%，按距离判定",
                    page, number, matched, replaced, Math.Round(gap * 100)));

            return replaced;
        }

        /// <summary>取「程序按坐标距离给出的建议值」，并确认它确实在该番号的候选里。</summary>
        private static string FindSuggested(PartsInfoIndex index, int number,
            IDictionary<int, string> suggested, MemberPartCatalog catalog)
        {
            string name;

            if (suggested == null || !suggested.TryGetValue(number, out name)
                || string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return MatchCandidate(index, number, name, catalog);
        }

        /// <summary>
        /// AI 没给出答案时的兜底：用部材マスタ 判断现在的部材名 ——
        /// 本身就是部材名 → 照原样；是部品名 → 换成它所属的部材名；マスタ 里没有 → 空串（跳过）。
        /// </summary>
        private static string ResolveFromCatalog(MemberPartCatalog catalog, string memberName)
        {
            if (catalog == null || catalog.IsEmpty)
            {
                return string.Empty;
            }

            return catalog.ResolveMemberNameForText(memberName);
        }

        private static IDictionary<int, string> ParseResponse(string response)
        {
            Dictionary<int, string> decisions = new Dictionary<int, string>();
            string json = AiPartResponseParser.ExtractJson(response);

            if (json.Length == 0)
            {
                return decisions;
            }

            object parsed;

            try
            {
                parsed = new JavaScriptSerializer().DeserializeObject(json);
            }
            catch (Exception)
            {
                // AI 偶尔会输出非法 JSON：当作「没给出结果」处理，由调用方记日志
                return decisions;
            }

            Dictionary<string, object> map = parsed as Dictionary<string, object>;

            if (map != null)
            {
                foreach (KeyValuePair<string, object> pair in map)
                {
                    int number;
                    string name = ReadDecisionValue(pair.Value);

                    if (name.Length > 0 && TryReadNumber(pair.Key, out number))
                    {
                        decisions[number] = name;

                        continue;
                    }

                    object[] nested = pair.Value as object[];

                    if (nested != null)
                    {
                        CollectArrayDecisions(nested, decisions);
                    }
                }

                return decisions;
            }

            object[] array = parsed as object[];

            if (array != null)
            {
                CollectArrayDecisions(array, decisions);
            }

            return decisions;
        }

        /// <summary>
        /// 把「数组形式的判定」收进字典，兼容 AI 换成
        /// <c>[{"番号":1,"部材名":"鴨居"}]</c> 或 <c>[{"1":"鴨居"}]</c> 的写法。
        /// </summary>
        private static void CollectArrayDecisions(object[] array, IDictionary<int, string> decisions)
        {
            if (array == null)
            {
                return;
            }

            foreach (object item in array)
            {
                int number;
                string direct = ReadDecisionValue(item);

                if (direct.Length > 0 && TryReadNumber(direct, out number))
                {
                    continue;
                }

                Dictionary<string, object> row = item as Dictionary<string, object>;

                if (row == null)
                {
                    continue;
                }

                int rowNumber = 0;
                string rowName = string.Empty;

                foreach (KeyValuePair<string, object> pair in row)
                {
                    string key = NormalizeForCompare(pair.Key);
                    int parsed;

                    if (key == "番号" || key == "NUMBER" || key == "NO")
                    {
                        if (TryReadNumber(ReadDecisionValue(pair.Value), out parsed))
                        {
                            rowNumber = parsed;
                        }
                    }
                    else if (key == "部材名" || key == "部材名AI推荐" || key == "MEMBERNAME" ||
                             key == "NAME" || key == "VALUE")
                    {
                        rowName = ReadDecisionValue(pair.Value);
                    }
                    else if (TryReadNumber(key, out parsed) && rowName.Length == 0)
                    {
                        rowNumber = parsed;
                        rowName = ReadDecisionValue(pair.Value);
                    }
                }

                if (rowNumber > 0 && rowName.Length > 0)
                {
                    decisions[rowNumber] = rowName;
                }
            }
        }

        /// <summary>
        /// 把 AI 给的答案对成可写入的部材名（归一化比较，忽略半角 / 全角、大小写与空白）：
        /// ①该番号在图纸上的候选文字 → 用图上的写法；
        /// ②部材マスタ 的部材名（含「その他」）→ 用マスタ 的写法（图纸上可能根本没印）；
        /// ③部材マスタ 的部品名 → 换成它所属的部材名。
        /// 都对不上时返回空串 —— AI 编造的文字一律不采用。
        /// </summary>
        private static string MatchCandidate(PartsInfoIndex index, int number, string value,
            MemberPartCatalog catalog)
        {
            string wanted = NormalizeForCompare(value);

            if (wanted.Length == 0)
            {
                return string.Empty;
            }

            IList<MemberNameCandidate> candidates =
                index.GetMemberNameCandidates(number.ToString(CultureInfo.InvariantCulture));

            foreach (MemberNameCandidate item in candidates)
            {
                if (string.Equals(NormalizeForCompare(item.Name), wanted, StringComparison.Ordinal))
                {
                    return item.Name;
                }
            }

            // 图上没印这个文字也没关系：部材マスタ 的部材名 / 部品名 也认
            string memberName;

            if (catalog != null)
            {
                if (catalog.TryGetMemberName(value, out memberName))
                {
                    return memberName;
                }

                if (catalog.TryResolvePartName(value, out memberName))
                {
                    return memberName;
                }
            }

            return string.Empty;
        }

        /// <summary>该番号在图纸上的候选文字里是否有 <paramref name="name"/>（有才谈得上「按距离覆盖」）。</summary>
        private static bool IsTextCandidate(PartsInfoIndex index, int number, string name)
        {
            string wanted = NormalizeForCompare(name);

            if (wanted.Length == 0)
            {
                return false;
            }

            foreach (MemberNameCandidate item in
                index.GetMemberNameCandidates(number.ToString(CultureInfo.InvariantCulture)))
            {
                if (string.Equals(NormalizeForCompare(item.Name), wanted, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>取该页行里「来源PDF」列的路径（第一个非空值）；都没有时返回空串。</summary>
        private static string GetSourcePdfPath(IList<PartBasicInfo> rows)
        {
            if (rows == null)
            {
                return string.Empty;
            }

            foreach (PartBasicInfo row in rows)
            {
                if (row != null && !string.IsNullOrWhiteSpace(row.PdfFile))
                {
                    return row.PdfFile.Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 该页 <c>parts_info.json</c> 的完整路径。查找顺序（与【CMD 分析】共用同一份数据）：
        /// <list type="number">
        /// <item>「来源PDF」列所在目录 → <c>&lt;PDF 文件夹&gt;\AiAppData\&lt;図番&gt;[_P&lt;页码&gt;]\parts_info.json</c>
        /// （PDF 换过文件夹时也能找到）；</item>
        /// <item>分解输出根目录 → <c>&lt;输出根&gt;\&lt;図番&gt;[_P&lt;页码&gt;]\parts_info.json</c>；</item>
        /// <item>以上都不存在时，在上述两个根里按目录名（不区分大小写）找 <c>&lt;図番&gt;</c> / <c>&lt;図番&gt;_P&lt;页码&gt;</c>。</item>
        /// </list>
        /// 第 1 页在 <c>&lt;図番&gt;\</c>，第 2 页起在 <c>&lt;図番&gt;_P2\</c>。
        /// </summary>
        /// <param name="outputDirectory">分解输出根目录（<c>&lt;PDF 文件夹&gt;\AiAppData</c>）。</param>
        /// <param name="drawingNo">図番。</param>
        /// <param name="page">页码（从 1 开始）。</param>
        /// <param name="pdfPath">「来源PDF」列的路径（可为空）。</param>
        private static string ResolvePartsInfoPath(string outputDirectory, string drawingNo, int page,
            string pdfPath)
        {
            int pageNumber = page <= 1 ? 1 : page;
            string folder = pageNumber == 1
                ? drawingNo
                : drawingNo + "_P" + pageNumber.ToString(CultureInfo.InvariantCulture);

            // 搜索根目录（去重、去空）：先「来源PDF」所在文件夹，再分解输出根
            List<string> roots = new List<string>();

            string pdfDir = GetDirectoryOrEmpty(pdfPath);

            if (pdfDir.Length > 0)
            {
                roots.Add(pdfDir);

                string pdfAppData = Path.Combine(pdfDir, PdfSplitService.OutputRootFolderName);

                if (!roots.Exists(delegate(string item)
                {
                    return string.Equals(item, pdfAppData, StringComparison.OrdinalIgnoreCase);
                }))
                {
                    roots.Add(pdfAppData);
                }
            }

            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                string outputRoot = outputDirectory.Trim();

                if (!roots.Exists(delegate(string item)
                {
                    return string.Equals(item, outputRoot, StringComparison.OrdinalIgnoreCase);
                }))
                {
                    roots.Add(outputRoot);
                }
            }

            // ① 先按标准目录名直接拼路径
            string firstCandidate = string.Empty;

            foreach (string root in roots)
            {
                string candidate = Path.Combine(root, folder, PartsInfoIndex.FileName);

                if (firstCandidate.Length == 0)
                {
                    firstCandidate = candidate;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // ② 目录名不标准（大小写 / 多出后缀）时，按名字扫描一级子目录
            foreach (string root in roots)
            {
                string found = FindPartsInfoUnder(root, folder);

                if (found.Length > 0)
                {
                    return found;
                }
            }

            return firstCandidate;
        }

        /// <summary>在 <paramref name="root"/> 的一级子目录里找名为 <paramref name="folder"/> 的
        /// <c>parts_info.json</c>（不区分大小写）；找不到返回空串。</summary>
        private static string FindPartsInfoUnder(string root, string folder)
        {
            try
            {
                if (root.Length == 0 || !Directory.Exists(root))
                {
                    return string.Empty;
                }

                foreach (string directory in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(directory);

                    if (!string.Equals(name, folder, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(directory, PartsInfoIndex.FileName);

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        /// <summary>取路径所在目录；路径为空 / 非法时返回空串。</summary>
        private static string GetDirectoryOrEmpty(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string directory = Path.GetDirectoryName(path.Trim());

                return string.IsNullOrWhiteSpace(directory) ? string.Empty : directory;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        /// <summary>日志回调为空时安全跳过。</summary>
        private static void ReportLog(Action<LogKind, string> onLog, LogKind kind, string message)
        {
            if (onLog != null)
            {
                onLog(kind, message);
            }
        }

        /// <summary>把一条原因追加到「跳过 / 失败原因」后面（没有时直接返回）。</summary>
        private static string AppendMessage(string current, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return current ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(current) ? message : current + "；" + message;
        }

        /// <summary>替换 <c>{{NAME}}</c> 形式的占位符。</summary>
        private static string ReplaceToken(string text, string token, string value)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            return text.Replace("{{" + token + "}}", value ?? string.Empty);
        }

        /// <summary>
        /// 比较用的归一化：NFKC（半角カナ → 全角カナ、全角英数 → 半角）+ 去掉全部空白（含全角空格）+ 统一大写。
        /// 与部材マスタ 的比较共用同一套规则（<see cref="MemberPartCatalogLoader.NormalizeKey"/>），
        /// 所以图上的 <c>上ﾚｰﾙ</c> 和 AI 回的 <c>上レール</c> 能对上。
        /// </summary>
        private static string NormalizeForCompare(string value)
        {
            return MemberPartCatalogLoader.NormalizeKey(value);
        }

        /// <summary>日志里显示待判定行的部材名 分布（空 / その他 / 含 <c>|</c> / 单个名字）。</summary>
        private static string DescribeMemberNameStats(IList<PartBasicInfo> rows)
        {
            int empty = 0;
            int fallback = 0;
            int multiple = 0;
            int single = 0;

            foreach (PartBasicInfo part in rows)
            {
                if (part == null)
                {
                    continue;
                }

                string memberName = part.MemberName == null ? string.Empty : part.MemberName.Trim();

                if (memberName.Length == 0)
                {
                    empty++;
                }
                else if (string.Equals(memberName, PartBasicInfo.MemberNameFallback, StringComparison.Ordinal))
                {
                    fallback++;
                }
                else if (memberName.IndexOf(MemberNameSeparator) >= 0)
                {
                    multiple++;
                }
                else
                {
                    single++;
                }
            }

            return string.Format("空 {0} 行 / {1} {2} 行 / 含 {3} {4} 行 / 单个名字 {5} 行", empty,
                PartBasicInfo.MemberNameFallback, fallback, MemberNameSeparator, multiple, single);
        }

        /// <summary>坐标显示（mm，最多 2 位小数；没有坐标时显示 <c>-</c>）。</summary>
        private static string FormatCoordinate(double value)
        {
            return double.IsNaN(value) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>文字的类别（提示词里给 AI 看）：部材名候选 / 位置标记 / 其它（含数字）。</summary>
        private static string DescribeTextKind(string word)
        {
            if (PartsInfoIndex.IsMemberNameWord(word))
            {
                return "部材名候选";
            }

            string normalized = (word ?? string.Empty).Trim();

            return normalized.EndsWith("部", StringComparison.Ordinal) ? "位置标记" : "其它（含数字）";
        }

        /// <summary>日志里显示部材名（空值写成「（空）」）。</summary>
        private static string DescribeMemberName(string memberName)
        {
            return string.IsNullOrWhiteSpace(memberName) ? "（空）" : memberName.Trim();
        }

        /// <summary>Markdown 表格单元格：竖线换成全角、换行压成空格，避免破坏表格。</summary>
        private static string EscapeCell(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "（空）";
            }

            return value.Replace('|', '｜').Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>列表里是否有这个番号。</summary>
        private static bool ContainsNumber(IList<string> numbers, string number)
        {
            if (numbers == null)
            {
                return false;
            }

            foreach (string item in numbers)
            {
                if (string.Equals(item, number, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>取字符串里的第一段数字（<c>"番号1"</c> → 1）；没有数字时返回 false。</summary>
        private static bool TryReadNumber(string value, out int number)
        {
            number = 0;

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            StringBuilder digits = new StringBuilder();

            foreach (char ch in value)
            {
                if (ch >= '0' && ch <= '9')
                {
                    digits.Append(ch);
                }
                else if (digits.Length > 0)
                {
                    break;
                }
            }

            return digits.Length > 0 &&
                   int.TryParse(digits.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                       out number);
        }

        /// <summary>把 JSON 里的值转成文本（null → 空串）。</summary>
        private static string ReadDecisionValue(object value)
        {
            return value == null
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        /// <summary>截断 AI 回应，便于写进日志。</summary>
        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "（空）";
            }

            string singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();

            return singleLine.Length <= 200 ? singleLine : singleLine.Substring(0, 200) + "...";
        }
    }
}
