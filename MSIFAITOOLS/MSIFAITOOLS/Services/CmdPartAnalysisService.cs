using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 【CMD 分析】编排服务：遍历一览中分类为「アッセンブリ図」的 PDF，
    /// 读取 CadPartSplitter 生成的 <c>01_アッセンブリ図_部品機能情報.csv</c>（旧名 <c>LT_STEP1.csv</c>，<b>不调用 AI</b>），
    /// 用 <c>xy.txt</c> 校验后登录到与【AI 分析】完全相同的 SQLite 表
    /// （<see cref="PartBasicInfoRepository.TableName"/>）。
    ///
    /// 与【AI 分析】（<see cref="PartAnalysisService"/>）的区别只在「数据从哪儿来」：
    ///   · AI 分析：plu_table.txt → 送 AI → 解析 AI 返回结果；
    ///   · CMD 分析：01_アッセンブリ図_部品機能情報.csv → 本工具自己的列名映射 → 部品明细。
    /// 入库方式、校验状态、部品信息画面的显示方式完全一致（同一 DB / 同一表）。
    ///
    /// 点一次【CMD 分析】依次执行 <b>最前面 1 步 + 第 1~6 步</b>（名字与日志前缀见 <see cref="CmdAnalysisSteps"/>，
    /// 详细说明见 <c>docs\CMD分析_步骤命名.md</c>）：
    /// <list type="number">
    ///   <item><b>最前面 特注品 CSV 生成</b>（<see cref="CmdSpecialProductCsvExporter"/>，不占步骤编号）：
    ///         遍历 PDF 文件夹下的 <c>*.xlsx</c>，文件名第 5 位是 <c>Z</c>（特注品）的读部品表，
    ///         写 <c>AiAppData\&lt;文件名&gt;\特注品.csv</c>。<b>不碰数据库</b>；</item>
    ///   <item><b>第 1 步 装配图部品入库</b>（旧称「阶段 A」；<see cref="CmdPartAnalyzer"/>）；</item>
    ///   <item><b>第 2 步 圈号拆分</b>（旧称「阶段 A2」；<see cref="CmdCalloutPartSplitter"/>）；</item>
    ///   <item><b>第 3 步 要領図部品追加</b>（旧称「阶段 B」；<see cref="CmdInstructionPartCollector"/>）；</item>
    ///   <item><b>第 4 步 tz.json 部品追加</b>（<see cref="CmdTzPartCollector"/>）。</item>
    ///   <item><b>第 5 步 部品コード正規化</b>（<see cref="CmdPartCodeNormalizer"/>，含同図番内の重複チェック）。</item>
    ///   <item><b>第 6 步 使い分け自動設定</b>（<see cref="CmdUsageKindCollector"/>）。</item>
    /// </list>
    /// 这 6 步之前还有 <b>第 0 步 分解与分类</b>（【开始分解】= CadPartSplitter 分割 + 本工具分解 +
    /// <c>classify\PdfInfoList.txt</c>），它不属于本按钮。
    ///
    /// 追加步骤（第 2 / 3 / 4 / 5 / 6 步，都只对 <c>classify\PdfInfoList.txt</c> 里「図面分类 = アッセンブリ図」的记录生效）：
    /// <list type="number">
    ///   <item><b>按圈号分割</b>（<see cref="CmdCalloutPartSplitter"/>）：对「来源标识 = アッセンブリ図」的行，
    ///         用「番号」在 <c>parts_info.json</c> 的 <c>callouts</c> 里找圈号（如 <c>text=4</c> / <c>display=④4</c>）；
    ///         圈号必须出现在 <b>index 不同</b>的零件图里，且这些零件图的 <c>texts</c> 整体互为包含（A ⊆ B / B ⊆ A），
    ///         才按坐标判排列方向（左右 / 上下）把方向加到部品名前并拆成多条；</item>
    ///   <item><b>追加要領図 部品</b>（<see cref="CmdInstructionPartCollector"/>）：顺着
    ///         <c>01_アッセンブリ図_要領図.csv</c> → 各要領図的 <c>02_要領図_部品機能情報.csv</c>，
    ///         把「品種命中分类依据」的部品行、以及「品種为空（所有品種共用）」的部品行
    ///         也登录到同一张表（図番 = アッセンブリ図、部品図番 = 要領図；备註 <c>品種：共通</c>）；</item>
    ///   <item><b>追加 tz.json 部品</b>（<see cref="CmdTzPartCollector"/>）：读 <c>classify\tz.json</c>（Excel 部品表），
    ///         把「品種」命中分类依据的明细（部品名 / 枠区分 / 部品コード / 追加対応特注寸法）
    ///         登录到同一张表（図番 = アッセンブリ図、备註 <c>品種：…；特注寸法：…</c>）。</item>
    ///   <item><b>正規化部品コード</b>（<see cref="CmdPartCodeNormalizer"/>）：读 <c>部品マスタ\Tbl_部品マスタ.csv</c>，
    ///         用「色」把部品コード 里的颜色换成 <c>□</c>（<c>MEACC429</c> → <c>MEA□429</c>），
    ///         再把连续的符号压成一个 <c>□</c>（<c>MEA□□405</c> → <c>MEA□405</c>，只改这一列）。
    ///         换完和同図番 / 同页码 / 同番号 的另一行撞键的行跳过（= 重複チェック，记警告）。</item>
    ///   <item><b>設定使い分け</b>（<see cref="CmdUsageKindCollector"/>）：同一図番内「区分 / 参照元 / 部品コード /
    ///         H下限 / H上限 / W下限 / W上限」全相同的 2 行以上，按「部品コード」查
    ///         <c>hiroidashiData</c> 里的 <c>*.xlsx</c> 第 4 列（部品ｺｰﾄﾞ/形材番号）相同的行，
    ///         把第 12 列（使い分け）的值按顺序填到这些行上（只改这一列）。
    ///         <b>放在部品コード 正規化（第 5 步）之后</b>：这时部品コード 已经是 <c>MEA□429</c> 这种写法，
    ///         和拾い出し表一致，查表最准。</item>
    /// </list>
    ///
    /// 执行范围由 <see cref="CmdAnalysisStepKind"/> 决定：主按钮【CMD 分析】传
    /// <see cref="CmdAnalysisStepKind.All"/>（一次跑完 最前面 1 步 + 第 1~6 步）；
    /// 界面上的六个子按钮各传一个值，只重跑其中一步（调试 / 单步重试用）；
    /// <see cref="CmdAnalysisStepKind.SpecialProductCsv"/> 没有界面按钮，供代码只跑「特注品 CSV 生成」。
    /// </summary>
    internal sealed class CmdPartAnalysisService
    {
        private readonly string _databasePath;

        public CmdPartAnalysisService(string databasePath)
        {
            _databasePath = string.IsNullOrWhiteSpace(databasePath)
                ? PartBasicInfoRepository.ResolveDatabasePath()
                : databasePath;
        }

        /// <summary>本次使用的数据库路径。</summary>
        public string DatabasePath
        {
            get { return _databasePath; }
        }

        /// <summary>
        /// 分析一览中「アッセンブリ図」的 PDF 并登录到 SQLite，返回汇总结果。
        /// 每个 PDF 的数据来源：&lt;输出根目录&gt;\&lt;図番号&gt;\01_アッセンブリ図_部品機能情報.csv
        /// （多页 PDF 第 2 页起为 &lt;図番号&gt;_P2\01_アッセンブリ図_部品機能情報.csv）。
        /// </summary>
        /// <param name="classifications">一览（分类结果）数据。</param>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="onLog">日志回调（kind, message）。</param>
        public PartAnalysisSummary AnalyzeAndSave(IList<PdfClassification> classifications,
            string outputDirectory, Action<LogKind, string> onLog)
        {
            return AnalyzeAndSave(classifications, outputDirectory, onLog, CmdAnalysisStepKind.All);
        }

        /// <summary>
        /// 同上，但可以选择只执行其中一步（界面上的六个子按钮）：
        /// <list type="bullet">
        ///   <item><see cref="CmdAnalysisStepKind.All"/>：依次跑 最前面 1 步 + 第 1~6 步（= 主按钮【CMD 分析】）；</item>
        ///   <item><see cref="CmdAnalysisStepKind.SpecialProductCsv"/>：只跑「特注品 CSV 生成」（不写数据库）；</item>
        ///   <item><see cref="CmdAnalysisStepKind.Assembly"/>：只跑第 1 步 装配图部品入库；</item>
        ///   <item><see cref="CmdAnalysisStepKind.CalloutSplit"/>：只跑第 2 步 圈号拆分；</item>
        ///   <item><see cref="CmdAnalysisStepKind.YoryoAppend"/>：只跑第 3 步 要領図部品追加；</item>
        ///   <item><see cref="CmdAnalysisStepKind.TzAppend"/>：只跑第 4 步 tz.json 部品追加。</item>
        ///   <item><see cref="CmdAnalysisStepKind.PartCode"/>：只跑第 5 步 部品コード正規化。</item>
        ///   <item><see cref="CmdAnalysisStepKind.UsageKind"/>：只跑第 6 步 使い分け自動設定。</item>
        /// </list>
        /// 第 2 / 3 / 4 / 5 / 6 步依赖第 1 步已经入库的行：数据库里没有该図番的行时只记一条警告，不做任何写库。
        /// </summary>
        /// <param name="classifications">一览（分类结果）数据。</param>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="onLog">日志回调（kind, message）。</param>
        /// <param name="stepKind">要执行的步骤（主按钮传 <see cref="CmdAnalysisStepKind.All"/>）。</param>
        public PartAnalysisSummary AnalyzeAndSave(IList<PdfClassification> classifications,
            string outputDirectory, Action<LogKind, string> onLog, CmdAnalysisStepKind stepKind)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                throw new DirectoryNotFoundException(
                    "找不到分解输出目录（请先执行【开始分解】）：" + outputDirectory);
            }

            PartAnalysisSummary summary = new PartAnalysisSummary(_databasePath);

            PartAnalysisService.ReportLog(onLog, LogKind.Info,
                "数据来源：" + CmdPartAnalyzer.LtStep1FileName + "（CadPartSplitter 生成，不调用 AI）");
            PartAnalysisService.ReportLog(onLog, LogKind.Info, "部品基本信息数据库：" + _databasePath);
            bool runAssembly = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.Assembly;
            bool runCalloutSplit = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.CalloutSplit;
            bool runYoryoAppend = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.YoryoAppend;
            bool runTzAppend = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.TzAppend;
            bool runUsageKind = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.UsageKind;
            bool runPartCode = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.PartCode;
            bool runSpecialProductCsv = stepKind == CmdAnalysisStepKind.All ||
                stepKind == CmdAnalysisStepKind.SpecialProductCsv;

            PartAnalysisService.ReportLog(onLog, LogKind.Info, CmdAnalysisSteps.DescribeRun(stepKind));

            // 最前面：文件名第 5 位是 Z（特注品）的 xlsx → AiAppData\<文件名>\特注品.csv
            // 不依赖数据库，所以在挑选「アッセンブリ図」之前执行（没有装配图也会生成 CSV）
            if (runSpecialProductCsv)
            {
                ExportSpecialProductCsv(outputDirectory, onLog);

                // 只跑这一步（分步执行）：到这里结束，不写数据库
                if (stepKind == CmdAnalysisStepKind.SpecialProductCsv)
                {
                    return summary;
                }
            }

            List<PdfClassification> targets = PartAnalysisService.SelectAssemblyTargets(classifications);

            if (targets.Count == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "一览中没有分类为「" + PdfCategoryNames.Assembly + "」的 PDF，无需分析。");

                return summary;
            }

            summary.TargetCount = targets.Count;

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format("待分析「{0}」PDF：{1} 个",
                PdfCategoryNames.Assembly, targets.Count));

            using (PartBasicInfoRepository repository = new PartBasicInfoRepository(_databasePath))
            {
                if (!runAssembly && !HasSavedRows(repository, targets))
                {
                    // 第 2 / 3 / 4 / 5 / 6 步都依赖第 1 步写入的行：一行都没有就只提示，不做任何写库
                    PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                        "数据库中还没有这些図番的部品行，请先执行 {0}。", CmdAnalysisSteps.AssemblyStep.Label));

                    return summary;
                }

                if (runAssembly)
                {
                    CmdPartAnalyzer analyzer = new CmdPartAnalyzer();

                    // 第 1 步：装配图部品入库（读 01_アッセンブリ図_部品機能情報.csv → 建行 → xy.txt 校验 → 入库）
                    PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
                    PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                        "{0}读 {1} → 建行 → {2} 校验 → 入库（{3} 个 PDF）",
                        CmdAnalysisSteps.AssemblyStep.LogHeader, CmdPartAnalyzer.LtStep1FileName,
                        PdfSplitService.XyFileName, targets.Count));

                    for (int index = 0; index < targets.Count; index++)
                    {
                        AnalyzeFile(targets[index], index + 1, targets.Count, outputDirectory, analyzer,
                            repository, summary, onLog);
                    }
                }

                // 第 2 步：按 parts_info.json 的圈号（callouts）把アッセンブリ図 的部品行按位置分割
                if (runCalloutSplit)
                {
                    SplitAssemblyParts(targets, outputDirectory, repository, summary, onLog);
                }

                // 第 3 步：要領図（02_要領図_部品機能情報.csv）的部品挂到アッセンブリ図 上
                if (runYoryoAppend)
                {
                    CollectInstructionParts(targets, outputDirectory, repository, summary, onLog);
                }

                // 第 4 步：classify\tz.json（Excel 部品表）里品種命中分类依据的明细挂到アッセンブリ図 上
                if (runTzAppend)
                {
                    AppendTzParts(targets, outputDirectory, repository, summary, onLog);
                }

                // 第 5 步：按部品マスタ 的「色」把部品コード 的颜色换成 □、连续符号压成一个 □
                //        （换完和同図番 / 同页码 / 同番号 的另一行撞键的行跳过 = 重複チェック）
                if (runPartCode)
                {
                    NormalizePartCodes(targets, outputDirectory, repository, summary, onLog);
                }

                // 第 6 步：同一図番内 7 个字段全同的多行 → 按（正規化後の）部品コード 查 hiroidashiData 的 xlsx → 填「使い分け」
                //        放在第 5 步之后：部品コード 已经统一成 MEA□429 这种写法，和拾い出し表一致
                if (runUsageKind)
                {
                    AssignUsageKinds(targets, outputDirectory, repository, summary, onLog);
                }
            }

            return summary;
        }

        /// <summary>
        /// 【最前面】特注品 CSV 生成：遍历 PDF 文件夹（&lt;outputDirectory&gt; 的上一级）下的 <c>*.xlsx</c>，
        /// 文件名第 5 位是 <c>Z</c>（特注品）的读部品表 → 写
        /// <c>&lt;outputDirectory&gt;\&lt;文件名&gt;\特注品.csv</c>（见 <see cref="CmdSpecialProductCsvExporter"/>）。
        /// <para>
        /// 配置了 AI 接口密钥时还会做<b>AI 补足</b>：让 AI 读该図番的 <c>plu_table.txt</c>，
        /// 先补 Excel 里空着的字段 / 缺的部品，再补「追加対応特注寸法」，
        /// 并把最终内容同步进 <c>classify\tz.json</c>（Excel 已有的值一律不动）。
        /// 没配密钥时与原来完全一样（不联网）。
        /// </para>
        /// 这一步<b>不碰数据库</b>，也不依赖第 1 步的结果。
        /// </summary>
        private static void ExportSpecialProductCsv(string outputDirectory, Action<LogKind, string> onLog)
        {
            string pdfDirectory = Path.GetDirectoryName(outputDirectory);
            string metaFolder = Path.GetFileName(outputDirectory);
            string relativePath = Path.Combine(metaFolder, "<文件名>",
                CmdSpecialProductCsvExporter.CsvFileName);

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "{0}扫描 {1} 下的 xlsx（文件名第 {2} 位是 {3} = 特注品）→ {4}",
                CmdAnalysisSteps.SpecialProductCsvStep.LogHeader, pdfDirectory,
                CmdSpecialProductRule.DrawingNoPosition, CmdSpecialProductRule.DrawingNoMark,
                relativePath));

            // AI 补足（可选）：没配置密钥时只写 Excel 读到的内容（保持原来不联网的行为）
            // 与【AI 设置】画面共用同一份配置（环境变量 → %APPDATA%\MSIFAITOOLS\ai-settings.json
            // → App.config → 内置默认值），读取方法是 FromConfiguration()。
            // 注意：本工程同时编译了 CadPartSplitter 的源码，那边也有一个同名 AiApiOptions 类，
            // 但它同样只有 FromConfiguration()（Load() 是它内部嵌套类 SharedAiSettings 的方法），
            // 且本文件在 namespace MSIFAITOOLS.Services 下，未限定名解析到的是本命名空间这一份——
            // 正是 CmdSpecialProductCsvExporter.Export(...) 第三个参数要的类型。
            AiApiOptions aiOptions = AiApiOptions.FromConfiguration();
            bool useAi = aiOptions != null && aiOptions.IsConfigured;

            if (useAi)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info,
                    "  AI 补足：启用（" + aiOptions.Describe() +
                    "）→ 只补 Excel 读不到的字段 / 部品 / 特注寸法");
                PartAnalysisService.ReportLog(onLog, LogKind.Info,
                    "  AI 提示语：" + SpecialProductPromptStore.Load().Source);
            }
            else
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info,
                    "  AI 补足：未配置 API Key，跳过（只写 Excel 读到的内容；可在【AI 设置】里配置）");
            }

            CmdSpecialProductCsvResult result = CmdSpecialProductCsvExporter.Export(pdfDirectory,
                outputDirectory, useAi ? aiOptions : null);

            if (useAi && !string.IsNullOrEmpty(SpecialProductPromptStore.LastLoadError))
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "  " + SpecialProductPromptStore.LastLoadError);
            }

            foreach (string note in result.Notes)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info, "  提示：" + note);
            }

            if (result.SpecialProductFileCount == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                    "  没有文件名第 {0} 位是 {1} 的 xlsx（扫描 {2} 个），未生成 {3}",
                    CmdSpecialProductRule.DrawingNoPosition, CmdSpecialProductRule.DrawingNoMark,
                    result.ScannedFileCount, CmdSpecialProductCsvExporter.CsvFileName));

                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Success, string.Format(
                "  特注品 xlsx {0} 个 → {1} {2} 个（明细 {3} 行）",
                result.SpecialProductFileCount, CmdSpecialProductCsvExporter.CsvFileName,
                result.WrittenFiles.Count, result.RowCount));

            if (result.AiCallCount > 0 || result.AiFailedFileCount > 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                    "  AI 补足：{0} 个文件有补充（字段 {1} 个 / 新行 {2} 条 / 特注寸法 {3} 条），" +
                    "调用 AI {4} 次{5}",
                    result.AiAppliedFileCount, result.AiFieldCount, result.AiNewRowCount,
                    result.AiSpecialSizeCount, result.AiCallCount,
                    result.AiFailedFileCount > 0
                        ? "，失败 " + result.AiFailedFileCount + " 个"
                        : string.Empty));
            }

            int shown = 0;

            foreach (string path in result.WrittenFiles)
            {
                if (shown >= 10)
                {
                    PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format("  …（其余 {0} 个略）",
                        result.WrittenFiles.Count - shown));

                    break;
                }

                PartAnalysisService.ReportLog(onLog, LogKind.Info, "  " + path);
                shown++;
            }
        }

        /// <summary>
        /// 数据库里是否已经有这些図番的部品行 —— 第 2 / 3 / 4 / 5 / 6 步单独执行前的前置检查。
        /// 任意一个図番有行即视为第 1 步执行过（各図番的处理互不影响）。
        /// </summary>
        private static bool HasSavedRows(PartBasicInfoRepository repository, IList<PdfClassification> targets)
        {
            if (repository == null || targets == null)
            {
                return false;
            }

            foreach (PdfClassification item in targets)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Stem))
                {
                    continue;
                }

                if (new List<PartBasicInfo>(repository.Query(item.Stem)).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>处理单个 PDF：定位 01_アッセンブリ図_部品機能情報.csv → 解析 → 入库。</summary>
        private static void AnalyzeFile(PdfClassification item, int index, int total, string outputDirectory,
            CmdPartAnalyzer analyzer, PartBasicInfoRepository repository, PartAnalysisSummary summary,
            Action<LogKind, string> onLog)
        {
            string stem = Path.GetFileNameWithoutExtension(item.FilePath);

            PartAnalysisService.ReportLog(onLog, LogKind.Info,
                string.Format("[{0}/{1}] CMD 分析：{2}", index, total, item.FileName));

            string pdfOutputDirectory = PdfSplitService.GetPdfOutputDirectory(outputDirectory, stem);
            List<CmdPartSource> sources = CmdPartAnalyzer.LocateSources(outputDirectory, stem);

            if (sources.Count == 0)
            {
                string expected = Path.Combine(pdfOutputDirectory, CmdPartAnalyzer.LtStep1FileName);

                summary.MissingTableFileCount++;
                summary.Files.Add(new PartAnalysisFileResult(item.FilePath, stem,
                    PartAnalysisFileStatus.MissingLtStep1File, 0, 0, "缺少数据文件：" + expected));

                PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  跳过：找不到 " + expected);
                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "        请在【开始分解】时勾选「先调用 CadPartSplitter」（并确认 " +
                    CadPartSplitterRunner.ExeFileName + " 已部署），或手工把该文件放到上面的路径。");

                return;
            }

            string xyPath = Path.Combine(pdfOutputDirectory, PdfSplitService.XyFileName);

            try
            {
                CmdPartAnalysisResult result = analyzer.Analyze(item.FilePath, sources, xyPath);

                foreach (string note in result.Notes)
                {
                    PartAnalysisService.ReportLog(onLog, LogKind.Info, "  提示：" + note);
                }

                if (result.Parts.Count == 0)
                {
                    summary.NoDataFileCount++;
                    summary.Files.Add(new PartAnalysisFileResult(item.FilePath, result.DrawingNo,
                        PartAnalysisFileStatus.NoPartData, 0, 0, "未从 01_アッセンブリ図_部品機能情報.csv 读到部品明细"));

                    PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                        "  未读到部品明细（表头可能不含「部品名 / 部品コード」列）");

                    return;
                }

                // 部材名：用「番号」在 parts_info.json 的 callouts 里找圈号，取该圈号所在零件图的
                // texts（去掉数字、去掉「XX部」）用 | 连接（例：番号 4 → 縦枠）
                int memberNamed = ApplyMemberNames(outputDirectory, stem, result.Parts);

                // 図番（PDF 文件名）第 5 位是 Z = 特注品：读到的行一律记「特注品」（已有值的行不覆盖）
                ReportSpecialProducts(CmdSpecialProductRule.MarkSpecialProducts(result.Parts, stem), stem, onLog);

                // 特注不是 1 / 特注品 的行：数量 / 切断寸法 一律清空（写库前统一套用）
                ReportClearedQuantity(ClearQuantityForNonSpecial(result.Parts), onLog);

                int saved = repository.Save(result.DrawingNo, result.Parts);

                summary.SavedFileCount++;
                summary.SavedRowCount += saved;
                summary.VerifiedPassedCount += PartAnalysisService.CountStatus(result.Parts,
                    PartVerifyStatus.Passed);
                summary.VerifiedFailedCount += PartAnalysisService.CountStatus(result.Parts,
                    PartVerifyStatus.Failed);
                summary.Files.Add(new PartAnalysisFileResult(item.FilePath, result.DrawingNo,
                    PartAnalysisFileStatus.Saved, result.Parts.Count, saved, string.Empty));

                PartAnalysisService.ReportLog(onLog, LogKind.Success,
                    string.Format("  登录成功：{0} 条（図番 {1}）", saved, result.DrawingNo));

                if (memberNamed > 0)
                {
                    PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                        "  部材名：{0} 行按「番号 → 圈号 → 零件图 texts」取自 {1}",
                        memberNamed, PartsInfoIndex.FileName));
                }

                PartAnalysisService.LogSampleRows(onLog, result.Parts);
            }
            catch (Exception ex)
            {
                summary.FailedFileCount++;
                summary.Files.Add(new PartAnalysisFileResult(item.FilePath, stem,
                    PartAnalysisFileStatus.Failed, 0, 0, ex.Message));

                PartAnalysisService.ReportLog(onLog, LogKind.Error, "  分析失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 【第 1 步】填「部材名」：用每行的「番号」到该页目录的 <c>parts_info.json</c> 找圈号
        /// （<c>"text": "4"</c> 且 <c>"display": "④4"</c> 的 <c>callouts</c>），
        /// 取该圈号所在零件图（<c>index</c>）的全部 <c>texts</c>，<b>去掉含数字的文字</b>、
        /// <b>去掉「XX部」形式的位置标记</b>，剩下的去重后用 <c>|</c> 连接
        /// （见 <see cref="PartsInfoIndex.ResolveCalloutMemberName(int)"/>）。
        /// 同一页的 <c>parts_info.json</c> 只读一次；读不到时该页的行留空（不报错、不影响入库）。
        /// </summary>
        /// <returns>填了部材名的行数。</returns>
        private static int ApplyMemberNames(string outputDirectory, string drawingNo, IList<PartBasicInfo> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return 0;
            }

            Dictionary<int, PartsInfoIndex> indexes = new Dictionary<int, PartsInfoIndex>();
            int named = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part == null || part.Number <= 0)
                {
                    continue;
                }

                PartsInfoIndex index = LoadMemberNameIndex(outputDirectory, drawingNo, part.Page, indexes);

                if (index == null)
                {
                    continue;
                }

                string memberName = index.ResolveCalloutMemberName(part.Number);

                if (memberName.Length == 0)
                {
                    continue;
                }

                part.MemberName = memberName;
                named++;
            }

            return named;
        }

        /// <summary>
        /// 读取该页目录的 <c>parts_info.json</c>（第 1 页 <c>&lt;根&gt;\&lt;図番&gt;</c>，
        /// 第 2 页起 <c>&lt;根&gt;\&lt;図番&gt;_P2</c>）；同一页只读一次，读不到时缓存 null。
        /// </summary>
        private static PartsInfoIndex LoadMemberNameIndex(string outputDirectory, string drawingNo, int page,
            Dictionary<int, PartsInfoIndex> cache)
        {
            PartsInfoIndex cached;

            if (cache.TryGetValue(page, out cached))
            {
                return cached;
            }

            int pageNumber = page <= 1 ? 1 : page;
            string folder = pageNumber == 1
                ? drawingNo
                : drawingNo + "_P" + pageNumber.ToString(CultureInfo.InvariantCulture);
            string path = Path.Combine(outputDirectory ?? string.Empty, folder, PartsInfoIndex.FileName);
            PartsInfoIndex index = PartsInfoIndex.Load(path);

            if (index != null && !string.IsNullOrEmpty(index.LoadError))
            {
                index = null;
            }

            cache[page] = index;

            return index;
        }

        /// <summary>
        /// 第 2 步「圈号拆分」（旧称「阶段 A2」）：按圈号（<c>parts_info.json</c> 的 <c>callouts</c>）
        /// 把アッセンブリ図 的部品行按位置分割。
        ///
        /// 只处理「参照元 = アッセンブリ図」的行（<see cref="CmdCalloutPartSplitter.IsAssemblyRow"/>）：
        /// 用行上的「番号」找 <c>text</c> / <c>display</c> 自洽的圈号，圈号必须出现在 <b>index 不同</b>、
        /// <b>texts 互为包含</b>的零件图里，才按坐标定方向（左右 / 上下）把方向加到部品名前并拆成多条。
        /// 有变化时整张覆盖写回（<see cref="PartBasicInfoRepository.Save"/> 先删后插，必须提交全部行）。
        /// </summary>
        private static void SplitAssemblyParts(IList<PdfClassification> targets, string outputDirectory,
            PartBasicInfoRepository repository, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            List<string> drawingNumbers = GetAssemblyDrawingNumbers(targets, outputDirectory);

            if (drawingNumbers.Count == 0)
            {
                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "{0}按 {1} 的圈号（callouts）把「{2}」的部品行按位置分割（{3} 个図番）",
                CmdAnalysisSteps.CalloutSplitStep.LogHeader,
                PartsInfoIndex.FileName, PdfCategoryNames.Assembly, drawingNumbers.Count));

            CmdCalloutPartSplitter splitter = new CmdCalloutPartSplitter();
            int processed = 0;

            foreach (string drawingNo in drawingNumbers)
            {
                if (string.IsNullOrWhiteSpace(drawingNo))
                {
                    continue;
                }

                try
                {
                    List<PartBasicInfo> existing = new List<PartBasicInfo>(repository.Query(drawingNo));

                    if (existing.Count == 0)
                    {
                        continue;
                    }

                    CmdCalloutSplitResult result = splitter.Split(outputDirectory, drawingNo, existing);

                    if (result.SplitGroupCount == 0 && result.BlockedCount == 0)
                    {
                        continue;
                    }

                    foreach (string note in result.Notes)
                    {
                        PartAnalysisService.ReportLog(onLog, LogKind.Info, "  " + note);
                    }

                    if (result.SplitGroupCount == 0)
                    {
                        continue;
                    }

                    // 図番（PDF 文件名）第 5 位是 Z = 特注品：整张表里没有「特注」的行一律记「特注品」
                    ReportSpecialProducts(CmdSpecialProductRule.MarkSpecialProducts(result.Rows, drawingNo),
                        drawingNo, onLog);

                    // 特注不是 1 / 特注品 的行：数量 / 切断寸法 一律清空（保存整张表前统一套用）
                    ReportClearedQuantity(ClearQuantityForNonSpecial(result.Rows), onLog);

                    repository.Save(drawingNo, result.Rows);

                    processed++;
                    summary.SplitDrawingCount = processed;
                    summary.SplitGroupCount += result.SplitGroupCount;
                    summary.SplitRowCount += result.AddedRowCount;

                    PartAnalysisService.ReportLog(onLog, LogKind.Success, string.Format(
                        "  按位置分割：{0} 条部品（図番 {1}，该図番合计 {2} 行）",
                        result.SplitGroupCount, drawingNo, result.Rows.Count));
                }
                catch (Exception ex)
                {
                    PartAnalysisService.ReportLog(onLog, LogKind.Error,
                        "  按位置分割失败（図番 " + drawingNo + "）：" + ex.Message);
                }
            }

            if (processed == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info, "  没有需要按位置分割的部品");
            }
        }

        /// <summary>
        /// 第 2 步「圈号拆分」的対象図番：<c>classify\PdfInfoList.txt</c> 里「图面分类 = アッセンブリ図」的记录，
        /// 再补上一览里的「アッセンブリ図」——清单不存在时靠后者，清单的「図番号」与 PDF 文件名不一致时
        /// 数据库里的図番（= 文件名去扩展名）也能被覆盖到。
        /// </summary>
        private static List<string> GetAssemblyDrawingNumbers(IList<PdfClassification> targets,
            string outputDirectory)
        {
            List<string> numbers = PdfClassifier.ReadDrawingNumbers(outputDirectory, PdfCategory.Assembly);

            if (targets == null)
            {
                return numbers;
            }

            foreach (PdfClassification item in targets)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Stem))
                {
                    continue;
                }

                if (!numbers.Contains(item.Stem))
                {
                    numbers.Add(item.Stem);
                }
            }

            return numbers;
        }

        /// <summary>
        /// 第 3 步「要領図部品追加」（旧称「阶段 B」）：按要領図清单把要領図的部品登录到アッセンブリ図 名下。
        ///
        /// 每个アッセンブリ図：
        /// <list type="number">
        ///   <item>先取该図番已有的明细，去掉上一次本步骤写入的行（来源表格 = 02_要領図_部品機能情報.csv）；</item>
        ///   <item>由 <see cref="CmdInstructionPartCollector"/> 收集新品種命中的行（番号接在已有行之后）；
        ///         番号不同即不会命中唯一索引（図番 + 页码 + 番号 + 部品コード）；</item>
        ///   <item>合并后一次写库（<see cref="PartBasicInfoRepository.Save"/> 会先清空该図番再插入，
        ///         所以必须把已有行一起提交，否则会把 01_アッセンブリ図_部品機能情報.csv 的明细冲掉）。</item>
        ///   <item>部品名与已有的「アッセンブリ図」行<b>相同</b>时，用装配图行的「区分」覆盖要領図行，
        ///         并把那条装配图行删掉（<see cref="MergeInstructionWithAssembly"/>）。</item>
        ///   <item>合并后「区分」还是空着的行（名字带位置前缀、对不上装配图行），
        ///         用同一个部品 / 同一个部材 的区分补上（<see cref="FillMissingFrameKind"/>）。</item>
        /// </list>
        /// 找不到 01 / 02 文件时只记日志，不影响上面已登录的数据。
        /// </summary>
        private static void CollectInstructionParts(IList<PdfClassification> targets, string outputDirectory,
            PartBasicInfoRepository repository, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "{0}{1} → 各要領図的 {2}，把品種命中分类依据的部品（含没有品種的共通部品）挂到「{3}」上",
                CmdAnalysisSteps.YoryoAppendStep.LogHeader,
                CmdInstructionPartCollector.YoryoListFileName,
                CmdInstructionPartCollector.YoryoPartFileName, PdfCategoryNames.Assembly));

            CmdInstructionPartCollector collector = new CmdInstructionPartCollector();
            int processed = 0;

            foreach (PdfClassification item in targets)
            {
                if (item == null)
                {
                    continue;
                }

                string drawingNo = item.Stem;

                try
                {
                    List<PartBasicInfo> existing = new List<PartBasicInfo>(repository.Query(drawingNo));

                    // 上一次【CMD 分析】追加过的要領図部品先剔掉，保证重复执行不产生重复行
                    int removed = existing.RemoveAll(IsInstructionRow);
                    int startNumber = NextNumber(existing);

                    CmdInstructionPartResult result = collector.Collect(item, outputDirectory, startNumber);

                    foreach (string note in result.Notes)
                    {
                        PartAnalysisService.ReportLog(onLog, LogKind.Info, "  " + note);
                    }

                    summary.InstructionMissingFileCount += result.MissingFileCount;

                    if (result.Parts.Count == 0 && removed == 0)
                    {
                        continue;
                    }

                    List<PartBasicInfo> merged = new List<PartBasicInfo>(existing);
                    merged.AddRange(result.Parts);

                    // 部品名与「アッセンブリ図」行相同时：区分 取装配图行的，装配图那条删掉（同部品只留要領図 一条）
                    MergeInstructionWithAssembly(merged, drawingNo, onLog);

                    // 名字带位置前缀（右 / 左）等理由对不上、区分 还空着的行：用同一个部品的区分补上
                    ReportFilledFrameKind(FillMissingFrameKind(merged, drawingNo, onLog), onLog);

                    // 図番（PDF 文件名）第 5 位是 Z = 特注品：合并后的整张表里没有「特注」的行一律记「特注品」
                    ReportSpecialProducts(CmdSpecialProductRule.MarkSpecialProducts(merged, drawingNo),
                        drawingNo, onLog);

                    // 特注不是 1 / 特注品 的行：数量 / 切断寸法 一律清空（保存整张表前统一套用）
                    ReportClearedQuantity(ClearQuantityForNonSpecial(merged), onLog);

                    repository.Save(drawingNo, merged);

                    processed++;
                    summary.InstructionDrawingCount = processed;
                    summary.InstructionFileCount += result.YoryoPartFileCount;
                    summary.InstructionRowCount += result.Parts.Count;

                    string sharedText = result.SharedRowCount > 0
                        ? "，其中无品種（共通）" + result.SharedRowCount + " 条"
                        : string.Empty;

                    PartAnalysisService.ReportLog(onLog, LogKind.Success, string.Format(
                        "  要領図部品登录：{0} 条（図番 {1}，该図番合计 {2} 行）{3}",
                        result.Parts.Count, drawingNo, merged.Count, sharedText));
                }
                catch (Exception ex)
                {
                    summary.FailedFileCount++;

                    PartAnalysisService.ReportLog(onLog, LogKind.Error,
                        "  要領図部品登录失败（図番 " + drawingNo + "）：" + ex.Message);
                }
            }

            if (processed == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info, "  没有需要追加的要領図部品");
            }
        }

        /// <summary>
        /// 第 4 步「tz.json 部品追加」（<b>步骤名沿用旧名</b>）：读 <c>classify\PdfInfoList.txt</c> 里
        /// 「图面分类 = アッセンブリ図」的分类依据，把<b>最前面那步生成的
        /// <c>AiAppData\&lt;文件名&gt;\特注品.csv</c></b>（Excel 部品表的 CSV 版，
        /// <see cref="CmdTzPartCollector.LoadCsvEntries"/>）中「品種」命中该依据的明细挂到该装配图名下
        /// —— <b>不再解析 Excel、也不再读 <c>classify\tz.json</c></b>
        /// （一个 CSV 都没有时才退回读 tz.json，并记一条警告）。
        ///
        /// 每个アッセンブリ図：先去掉上一次本步骤写入的行（来源标识 = tz.json），按分类依据重新收集，
        /// 与已有行合并后一次写库（<see cref="PartBasicInfoRepository.Save"/> 会先清空该図番再插入，
        /// 所以必须把已有行一起提交，否则会把第 1 / 3 步的明细冲掉）。
        /// 合并后为空（该図番只有上次的 tz 行、这次一条都不再命中）时改用
        /// <see cref="PartBasicInfoRepository.ReplaceByDrawing"/> 把该図番清空 ——
        /// <see cref="PartBasicInfoRepository.Save"/> 收到空表会直接返回，不清库。
        /// </summary>
        private static void AppendTzParts(IList<PdfClassification> targets, string outputDirectory,
            PartBasicInfoRepository repository, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "{0}读 {1}（图面分类 = 「{2}」的记录，取分类依据）→ 最前面生成的 {3} 中" +
                "「品種」命中分类依据的明细挂到「{2}」上（不再解析 Excel）",
                CmdAnalysisSteps.TzAppendStep.LogHeader, CmdTzPartCollector.PdfInfoListFileName,
                PdfCategoryNames.Assembly, CmdSpecialProductCsvExporter.CsvFileName));

            List<string> notes = new List<string>();
            Dictionary<string, string> evidence =
                CmdTzPartCollector.ReadAssemblyEvidence(outputDirectory, notes);

            foreach (string note in notes)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  " + note);
            }

            notes.Clear();

            int csvFileCount;
            List<CmdTzEntry> entries =
                CmdTzPartCollector.LoadCsvEntries(outputDirectory, notes, out csvFileCount);
            string sourceName = CmdSpecialProductCsvExporter.CsvFileName;
            int sourceFileCount = csvFileCount;

            if (entries.Count == 0)
            {
                // 没有 特注品.csv（例如只单步跑第 4 步、最前面那步没跑）：退回旧的 classify\tz.json
                List<string> legacyNotes = new List<string>();
                List<CmdTzEntry> legacy = CmdTzPartCollector.LoadEntries(outputDirectory, legacyNotes);

                if (legacy.Count > 0)
                {
                    entries = legacy;
                    sourceName = CmdTzPartCollector.FileName;
                    sourceFileCount = 1;
                    notes.Clear();
                    notes.Add(string.Format("没有 {0}，改用旧的 {1}\\{2}（建议先跑最前面的「{3}」）",
                        CmdSpecialProductCsvExporter.CsvFileName, CmdTzPartCollector.ClassifyFolderName,
                        CmdTzPartCollector.FileName, CmdAnalysisSteps.SpecialProductCsvName));
                }
            }

            foreach (string note in notes)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  " + note);
            }

            if (entries.Count == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "  没有读到明细，跳过 " + CmdAnalysisSteps.TzAppendStep.Label);

                return;
            }

            summary.TzEntryCount = entries.Count;

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "  {0}：{1} 个文件 / {2} 条明细；清单里「{3}」{4} 条",
                sourceName, sourceFileCount, entries.Count, PdfCategoryNames.Assembly, evidence.Count));

            CmdTzPartCollector collector = new CmdTzPartCollector();
            // 读 tz.json 时把它的路径写进「来源表格文件」（读 CSV 时每行用自己那个 CSV 的路径）
            string fallbackSourcePath = string.Equals(sourceName, CmdTzPartCollector.FileName,
                StringComparison.Ordinal)
                ? CmdTzPartCollector.GetFilePath(outputDirectory)
                : string.Empty;
            int processed = 0;

            foreach (PdfClassification item in targets)
            {
                if (item == null)
                {
                    continue;
                }

                string drawingNo = item.Stem;

                try
                {
                    // 分类依据来自 PdfInfoList.txt；清单里没有该図番时退回一览的 Evidence
                    string basis = FindEvidence(evidence, drawingNo, item.Evidence);

                    if (string.IsNullOrWhiteSpace(basis))
                    {
                        PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                            "  図番 {0}：没有分类依据（清单里不是「{1}」？），跳过", drawingNo,
                            PdfCategoryNames.Assembly));

                        continue;
                    }

                    List<PartBasicInfo> existing = new List<PartBasicInfo>(repository.Query(drawingNo));

                    // 去掉上一次本步骤写入的行，重新按分类依据收集（番号接在剩余行之后）
                    int removed = existing.RemoveAll(IsTzRow);
                    int startNumber = NextNumber(existing);

                    CmdTzPartResult result = collector.Collect(drawingNo, basis, fallbackSourcePath,
                        item.FilePath, entries, startNumber);

                    foreach (string note in result.Notes)
                    {
                        PartAnalysisService.ReportLog(onLog, LogKind.Info, "  " + note);
                    }

                    if (result.Parts.Count == 0 && removed == 0)
                    {
                        continue;
                    }

                    List<PartBasicInfo> merged = new List<PartBasicInfo>(existing);
                    merged.AddRange(result.Parts);

                    // 特注不是 1 / 特注品 的行：数量 / 切断寸法 一律清空（保存整张表前统一套用；
                    // tz.json 的行特注 = 1、図番 第 5 位是 Z 的行特注 = 特注品，都不受影响）
                    ReportClearedQuantity(ClearQuantityForNonSpecial(merged), onLog);

                    if (merged.Count == 0)
                    {
                        // 该図番原本只有上一次本步写入的 tz 行，这次一条都不再命中：
                        // Save 收到空表会直接返回（不清库），所以这里用 ReplaceByDrawing 把该図番清空。
                        repository.ReplaceByDrawing(drawingNo, merged);
                    }
                    else
                    {
                        repository.Save(drawingNo, merged);
                    }

                    processed++;
                    summary.TzDrawingCount = processed;
                    summary.TzRowCount += result.Parts.Count;

                    PartAnalysisService.ReportLog(onLog, LogKind.Success, string.Format(
                        "  {0} 部品登录：{1} 条（図番 {2}；该図番合计 {3} 行{4}）",
                        sourceName, result.Parts.Count, drawingNo, merged.Count,
                        removed > 0 ? string.Format("，已剔除上次 tz 行 {0} 条", removed) : string.Empty));
                }
                catch (Exception ex)
                {
                    summary.FailedFileCount++;

                    PartAnalysisService.ReportLog(onLog, LogKind.Error,
                        "  " + sourceName + " 部品登录失败（図番 " + drawingNo + "）：" + ex.Message);
                }
            }

            if (processed == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info,
                    "  没有需要追加的 " + sourceName + " 部品");
            }
        }

        /// <summary>
        /// 第 6 步「使い分け自動設定」：同一図番内「区分 / 参照元 / 部品コード /
        /// H下限 / H上限 / W下限 / W上限」全部相同的 2 行以上 = 同一个部品的多条，
        /// 按「部品コード」去 <c>hiroidashiData</c> 里的 <c>*.xlsx</c> 第 4 列
        /// （部品ｺｰﾄﾞ/形材番号）找相同的行，把第 12 列「使い分け」的值按顺序填到这些行上。
        ///
        /// <para>规则：</para>
        /// <list type="bullet">
        ///   <item>组内行按「页码 → 番号」排序，第 i 行取拾い出し表里该部品コード的第 i 个值；</item>
        ///   <item>找到的值比行数少时，多出来的行保持原样（不写空值）；</item>
        ///   <item>该行已经有「使い分け」（手工填过）时不覆盖，重复执行不会改掉人工数据；</item>
        ///   <item>7 个字段相同但只有 1 行时不算「两条数据都一样」，不处理。</item>
        /// </list>
        /// <para>
        /// 这一步<b>不建行、不删行</b>，只改「使い分け」一列（<see cref="PartBasicInfoRepository.UpdateUsageKinds"/>），
        /// 所以第 1 / 2 / 3 / 4 / 5 步写入的内容不会被冲掉。
        /// </para>
        /// <para>
        /// <b>执行顺序在第 5 步 部品コード正規化 之后</b>：这时部品コード 已经统一成
        /// <c>MEA□429</c> 这种写法（和拾い出し表第 4 列一致），查表最准；
        /// 正規化时撞键被跳过的行保持原样，这里按原写法照样能查（符号按通配处理）。
        /// 「重複チェック」本身在第 5 步做（撞键的行不写），所以本步看到的部品コード 都是唯一的。
        /// </para>
        /// </summary>
        private static void AssignUsageKinds(IList<PdfClassification> targets, string outputDirectory,
            PartBasicInfoRepository repository, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "{0}同一図番内「区分 / 参照元 / 部品コード / H下限 / H上限 / W下限 / W上限」全同的 2 行以上 " +
                "→ 按部品コード查 {1} 里的 xlsx 第 4 列 → 依次填入第 12 列「使い分け」",
                CmdAnalysisSteps.UsageKindStep.LogHeader, CmdUsageKindCollector.FolderName));

            List<string> notes = new List<string>();
            string directory = CmdUsageKindCollector.ResolveDirectory(outputDirectory, notes);

            if (string.IsNullOrEmpty(directory))
            {
                foreach (string note in notes)
                {
                    PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  " + note);
                }

                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "  找不到拾い出し表文件夹，跳过 " + CmdAnalysisSteps.UsageKindStep.Label);

                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Info, "  拾い出し表文件夹：" + directory);

            notes.Clear();
            CmdUsageKindIndex index = CmdUsageKindCollector.LoadIndex(directory, notes);

            foreach (string note in notes)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  " + note);
            }

            if (index.EntryCount == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                    "  {0} 里没有读到有效明细（第 4 列 = 部品ｺｰﾄﾞ/形材番号、第 12 列 = 使い分け），跳过 {1}",
                    directory, CmdAnalysisSteps.UsageKindStep.Label));

                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "  读到 xlsx {0} 个 / 明细 {1} 条", index.FileCount, index.EntryCount));

            foreach (PdfClassification item in targets)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Stem))
                {
                    continue;
                }

                string drawingNo = item.Stem.Trim();

                try
                {
                    AssignUsageKindsForDrawing(drawingNo, repository, index, summary, onLog);
                }
                catch (Exception ex)
                {
                    summary.FailedFileCount++;

                    PartAnalysisService.ReportLog(onLog, LogKind.Error,
                        "  使い分け设定失败（図番 " + drawingNo + "）：" + ex.Message);
                }
            }

            if (summary.UsageKindGroupCount == 0 && summary.UsageKindMissGroupCount == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info,
                    "  没有需要设置使い分け的多行（没有 7 个字段全同的 2 行以上）");
            }
        }

        /// <summary>
        /// 一个図番的第 6 步：读该図番的行 → 按 7 个字段分组 → 组内按顺序填「使い分け」→ 只更新这一列。
        /// </summary>
        private static void AssignUsageKindsForDrawing(string drawingNo, PartBasicInfoRepository repository,
            CmdUsageKindIndex index, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            List<PartBasicInfo> parts = new List<PartBasicInfo>(repository.Query(drawingNo));

            if (parts.Count == 0)
            {
                return;
            }

            List<UsageKindGroup> groups = BuildUsageKindGroups(parts);

            if (groups.Count == 0)
            {
                return;
            }

            List<PartBasicInfo> updates = new List<PartBasicInfo>();
            int matchedGroups = 0;

            foreach (UsageKindGroup group in groups)
            {
                List<string> values = index.FindUsageKinds(group.PartCode);

                if (values.Count == 0)
                {
                    summary.UsageKindMissGroupCount++;

                    PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                        "  {0}：{1} 行（部品コード {2}）在拾い出し表里找不到，跳过",
                        drawingNo, group.Parts.Count, group.PartCode));

                    continue;
                }

                List<string> assigned = new List<string>();

                for (int position = 0; position < group.Parts.Count && position < values.Count; position++)
                {
                    PartBasicInfo part = group.Parts[position];

                    if (!string.IsNullOrWhiteSpace(part.UsageKind))
                    {
                        // 已经有值（手工填过 / 上次已填）：不覆盖
                        continue;
                    }

                    part.UsageKind = values[position];
                    updates.Add(part);
                    assigned.Add(part.PartName + "→" + values[position]);
                }

                matchedGroups++;

                PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                    "  {0}：{1} 行（部品コード {2}）← 使い分け {3}",
                    drawingNo, group.Parts.Count, group.PartCode,
                    assigned.Count == 0 ? "（都已有值，不改）" : string.Join(" / ", assigned.ToArray())));
            }

            if (matchedGroups == 0)
            {
                return;
            }

            summary.UsageKindGroupCount += matchedGroups;
            summary.UsageKindDrawingCount++;

            int written = repository.UpdateUsageKinds(drawingNo, updates);

            summary.UsageKindRowCount += written;

            PartAnalysisService.ReportLog(onLog, LogKind.Success, string.Format(
                "  使い分け登录：{0} 行（図番 {1}，命中 {2} 组）", written, drawingNo, matchedGroups));
        }

        /// <summary>
        /// 按「区分 / 参照元 / 部品コード / H下限 / H上限 / W下限 / W上限」分组，
        /// 只返回 2 行以上的组（顺序 = 首次出现的顺序 = 页码 → 番号）。
        /// 空值与空值视为相同（区分 / H・W 上下限 经常是空）。
        /// </summary>
        private static List<UsageKindGroup> BuildUsageKindGroups(IList<PartBasicInfo> parts)
        {
            List<UsageKindGroup> ordered = new List<UsageKindGroup>();
            Dictionary<string, UsageKindGroup> map =
                new Dictionary<string, UsageKindGroup>(StringComparer.Ordinal);

            foreach (PartBasicInfo part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                string key = BuildUsageKindGroupKey(part);
                UsageKindGroup group;

                if (!map.TryGetValue(key, out group))
                {
                    group = new UsageKindGroup(part.PartCode);
                    map[key] = group;
                    ordered.Add(group);
                }

                group.Parts.Add(part);
            }

            List<UsageKindGroup> result = new List<UsageKindGroup>();

            foreach (UsageKindGroup group in ordered)
            {
                if (group.Parts.Count >= 2)
                {
                    result.Add(group);
                }
            }

            return result;
        }

        /// <summary>7 个字段拼成分组键（空 = 空，不去区分大小写，与画面显示一致）。</summary>
        private static string BuildUsageKindGroupKey(PartBasicInfo part)
        {
            return string.Join("\u0001", new string[]
            {
                NormalizeKey(part.FrameKind), NormalizeKey(part.SourceTag), NormalizeKey(part.PartCode),
                NormalizeKey(part.HeightMin), NormalizeKey(part.HeightMax),
                NormalizeKey(part.WidthMin), NormalizeKey(part.WidthMax)
            });
        }

        /// <summary>分组键用的规整：只去首尾空白，其余原样（全角 / 半角不同就算不同的组）。</summary>
        private static string NormalizeKey(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        /// <summary>
        /// 是否为第 4 步写入的行（「数据来源」= <see cref="PartSourceKind.CmdTz"/> = <c>CMD分析・tz.json</c>；
        /// 第 4 步现在读 <c>特注品.csv</c>，但这个<b>值沿用旧文字</b>以兼容已有数据，
        /// 所以这里不用跟着改）。
        /// 「参照元」现在写的是该行的図番，所以这里看「数据来源」；
        /// 老数据没有「数据来源」时退回看「参照元」（升级前的老行可能还是 tz.json）。
        /// </summary>
        private static bool IsTzRow(PartBasicInfo part)
        {
            if (part == null)
            {
                return false;
            }

            if (string.Equals(part.SourceKind, PartSourceKind.CmdTz, StringComparison.Ordinal))
            {
                return true;
            }

            return string.IsNullOrEmpty(part.SourceKind) &&
                string.Equals(part.SourceTag, PartSourceTag.CmdTz, StringComparison.Ordinal);
        }

        /// <summary>优先用清单里的分类依据；清单里没有该図番时退回一览的 Evidence。</summary>
        private static string FindEvidence(Dictionary<string, string> evidence, string drawingNo, string fallback)
        {
            string basis;

            if (evidence != null && drawingNo != null && evidence.TryGetValue(drawingNo, out basis) &&
                !string.IsNullOrWhiteSpace(basis))
            {
                return basis;
            }

            return fallback ?? string.Empty;
        }

        /// <summary>是否为追加步骤写入的行（来源表格是 02_要領図_部品機能情報.csv / 旧名 LT_STEP2.csv）。</summary>
        private static bool IsInstructionRow(PartBasicInfo part)
        {
            return part != null && CmdInstructionPartCollector.IsYoryoPartFile(part.SourceFile);
        }

        /// <summary>
        /// 【CMD 分析】写库前统一套用规则：<b>「特注」既不是 1 也不是「特注品」的行，
        /// 「数量」「切断寸法」一律清空</b>
        /// （<see cref="PartBasicInfo.ClearQuantityUnlessSpecial"/>）。
        /// 「特注品」= 図番 第 5 位是 Z 的 PDF 读到的行（<see cref="CmdSpecialProductRule"/>）。
        /// 返回被清空的行数。
        /// </summary>
        private static int ClearQuantityForNonSpecial(IList<PartBasicInfo> parts)
        {
            if (parts == null)
            {
                return 0;
            }

            int cleared = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part != null && part.ClearQuantityUnlessSpecial())
                {
                    cleared++;
                }
            }

            return cleared;
        }

        /// <summary>把 <see cref="ClearQuantityForNonSpecial"/> 清空的行数写进日志（0 行不写）。</summary>
        private static void ReportClearedQuantity(int cleared, Action<LogKind, string> onLog)
        {
            if (cleared <= 0)
            {
                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "  「特注」不是 1 / 特注品 的行：数量 / 切断寸法 已清空 {0} 行", cleared));
        }

        /// <summary>
        /// 把「特注品」标记的行数写进日志（0 行不写）。
        /// 只有図番 第 5 位是 Z 时才会标记（<see cref="CmdSpecialProductRule.MarkSpecialProducts"/>），
        /// 其余図番 的日志与以前完全一样。
        /// </summary>
        private static void ReportSpecialProducts(int marked, string drawingNo, Action<LogKind, string> onLog)
        {
            if (marked <= 0)
            {
                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "  図番 {0} 的第 {1} 位是 {2} → 记「{3}」{4} 行（数量 / 切断寸法 保留）",
                drawingNo, CmdSpecialProductRule.DrawingNoPosition, CmdSpecialProductRule.DrawingNoMark,
                CmdSpecialProductRule.SpecialProductMark, marked));
        }

        /// <summary>
        /// 第 3 步写库前：把「要領図」行与「アッセンブリ図」行里<b>部品名相同</b>的合掉 ——
        /// 用该装配图行的「区分」覆盖要領図行（<see cref="PartBasicInfo.OverrideFrameKind"/>），
        /// 然后把装配图行从整张表里删掉（只在本図番的明细里操作）。
        /// <para>
        /// 匹配规则：部品名按 <see cref="NormalizePartName"/> 规整后完全相同
        /// （忽略全部空白、全角 / 半角、英文大小写）。同一个要領図行对上多条装配图行时，
        /// <b>第一条</b>的区分用于覆盖，其余同名行也一并删除（同一部品只留要領図 这一条）。
        /// </para>
        /// </summary>
        /// <returns>删除的装配图行数（0 = 没有可合并的行）。</returns>
        private static int MergeInstructionWithAssembly(List<PartBasicInfo> merged, string drawingNo,
            Action<LogKind, string> onLog)
        {
            if (merged == null || merged.Count == 0)
            {
                return 0;
            }

            List<PartBasicInfo> instructionRows = merged.FindAll(IsInstructionRow);

            if (instructionRows.Count == 0)
            {
                return 0;
            }

            List<PartBasicInfo> assemblyRows = merged.FindAll(CmdCalloutPartSplitter.IsAssemblyRow);

            if (assemblyRows.Count == 0)
            {
                return 0;
            }

            HashSet<PartBasicInfo> removed = new HashSet<PartBasicInfo>();
            int removedCount = 0;

            foreach (PartBasicInfo instruction in instructionRows)
            {
                string name = NormalizePartName(instruction.PartName);

                if (name.Length == 0)
                {
                    continue;
                }

                PartBasicInfo source = null;

                foreach (PartBasicInfo assembly in assemblyRows)
                {
                    if (removed.Contains(assembly) ||
                        !string.Equals(NormalizePartName(assembly.PartName), name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (source == null)
                    {
                        source = assembly;
                        instruction.OverrideFrameKind(assembly.FrameKind);

                        PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                            "  同部品名合并：部品名「{0}」→ 区分 取装配图 番号 {1} 的「{2}」，要領図 番号 {3}（図番 {4}）",
                            instruction.PartName, assembly.Number, assembly.FrameKind, instruction.Number,
                            drawingNo));
                    }

                    removed.Add(assembly);
                    merged.Remove(assembly);
                    removedCount++;

                    PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                        "    删除装配图行：番号 {0} / 部品名「{1}」/ 区分「{2}」/ 部品コード「{3}」",
                        assembly.Number, assembly.PartName, assembly.FrameKind, assembly.PartCode));
                }
            }

            return removedCount;
        }

        /// <summary>位置前缀（第 2 / 3 步按位置拆条时加在部品名 / 部材名开头的字）。</summary>
        private static readonly char[] PositionPrefixChars = { '右', '左', '中', '上', '下' };

        /// <summary>
        /// 第 3 步合并之后的收尾：<b>「区分」还空着的行，用「同一个部品」的区分补上</b>
        /// （只补区分，不改部品名、也不删除任何行）。
        /// <para>
        /// 为什么需要：第 2 / 3 步按位置拆条时会<b>给部品名 / 部材名加位置前缀</b>（右 / 左、上 / 下），
        /// 而 <c>02_要領図_部品機能情報.csv</c> 没有「枠区分」列 —— 拆出来的行
        /// （例：本记录「右縦枠下部スペーサー」的原名是「縦枠下部スペーサー」）
        /// 在 <see cref="MergeInstructionWithAssembly"/> 里对不上装配图行时，区分 就一直空着。
        /// </para>
        /// <para>
        /// 查找顺序（只在本図番的行里找，先找到的优先）：
        /// <list type="number">
        ///   <item>部品名完全相同（<see cref="NormalizePartName"/> 规整后）；</item>
        ///   <item>去掉开头的位置前缀后部品名相同（<c>右縦枠下部スペーサー</c> → <c>縦枠下部スペーサー</c>）；</item>
        ///   <item>部材名完全相同（<c>右縦枠</c>）；</item>
        ///   <item>去掉开头的位置前缀后部材名相同（<c>右縦枠</c> → <c>縦枠</c>）。</item>
        /// </list>
        /// 同一个键上出现<b>两种以上</b>的区分时视为有歧义，跳过不补（记日志）。
        /// </para>
        /// </summary>
        /// <returns>补上「区分」的行数。</returns>
        private static int FillMissingFrameKind(List<PartBasicInfo> merged, string drawingNo,
            Action<LogKind, string> onLog)
        {
            if (merged == null || merged.Count == 0)
            {
                return 0;
            }

            Dictionary<string, PartBasicInfo> sources =
                new Dictionary<string, PartBasicInfo>(StringComparer.Ordinal);
            HashSet<string> ambiguous = new HashSet<string>(StringComparer.Ordinal);

            foreach (PartBasicInfo part in merged)
            {
                if (part == null || string.IsNullOrEmpty(part.FrameKind))
                {
                    continue;
                }

                foreach (string key in FrameKindLookupKeys(part))
                {
                    PartBasicInfo found;

                    if (!sources.TryGetValue(key, out found))
                    {
                        sources.Add(key, part);
                    }
                    else if (!string.Equals(found.FrameKind, part.FrameKind, StringComparison.Ordinal))
                    {
                        ambiguous.Add(key);
                    }
                }
            }

            if (sources.Count == 0)
            {
                return 0;
            }

            int filled = 0;

            foreach (PartBasicInfo part in merged)
            {
                if (part == null || !string.IsNullOrEmpty(part.FrameKind))
                {
                    continue;
                }

                PartBasicInfo source = FindFrameKindSource(sources, ambiguous, part);

                if (source == null)
                {
                    continue;
                }

                part.OverrideFrameKind(source.FrameKind);
                filled++;

                PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                    "  区分补全：番号 {0} 部品名「{1}」→ 区分「{2}」（取同部品 番号 {3} 的「{4}」，図番 {5}）",
                    part.Number, part.PartName, source.FrameKind, source.Number, source.PartName,
                    drawingNo));
            }

            return filled;
        }

        /// <summary>
        /// 按 <see cref="FillMissingFrameKind"/> 的查找顺序取「区分 的来源行」；
        /// 有歧义 / 一个都对不上时返回 <c>null</c>。
        /// </summary>
        private static PartBasicInfo FindFrameKindSource(IDictionary<string, PartBasicInfo> sources,
            ICollection<string> ambiguous, PartBasicInfo part)
        {
            foreach (string key in FrameKindLookupKeys(part))
            {
                // 同一个键上有两种以上的区分 → 判断不出该用哪个，跳过
                if (ambiguous != null && ambiguous.Contains(key))
                {
                    continue;
                }

                PartBasicInfo found;

                if (sources.TryGetValue(key, out found))
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 「区分」查找用的键（按优先级）：部品名 → 去位置前缀的部品名 →
        /// 部材名 → 去位置前缀的部材名。空值 / 重复的键不产生。
        /// </summary>
        private static List<string> FrameKindLookupKeys(PartBasicInfo part)
        {
            List<string> keys = new List<string>(4);

            if (part == null)
            {
                return keys;
            }

            AddFrameKindKeys(keys, part.PartName, "部品:");
            AddFrameKindKeys(keys, part.MemberName, "部材:");

            return keys;
        }

        /// <summary>
        /// 把「原名 / 去掉开头位置前缀的名字」两个键加进列表。
        /// 例：<c>右縦枠下部スペーサー</c> → <c>部品:右縦枠下部スペーサー</c> /
        /// <c>部品:縦枠下部スペーサー</c>。
        /// </summary>
        private static void AddFrameKindKeys(IList<string> keys, string value, string prefix)
        {
            string name = NormalizePartName(value);

            if (name.Length == 0)
            {
                return;
            }

            AddFrameKindKey(keys, prefix + name);

            if (name.Length > 1 && Array.IndexOf(PositionPrefixChars, name[0]) >= 0)
            {
                AddFrameKindKey(keys, prefix + name.Substring(1));
            }
        }

        /// <summary>列表里还没有这条键时才加。</summary>
        private static void AddFrameKindKey(IList<string> keys, string key)
        {
            if (string.IsNullOrEmpty(key) || keys.Contains(key))
            {
                return;
            }

            keys.Add(key);
        }

        /// <summary>把 <see cref="FillMissingFrameKind"/> 补上的行数写进日志（0 行不写）。</summary>
        private static void ReportFilledFrameKind(int filled, Action<LogKind, string> onLog)
        {
            if (filled <= 0)
            {
                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "  区分 为空的 {0} 行已用同一个部品的区分补全", filled));
        }

        /// <summary>
        /// 部品名比较用的规整：去掉全部空白（含全角空格）、全角 ASCII 转半角、英文转大写。
        /// 例：<c>部品セット（フラットレール）</c> 与 <c>部品セット(フラットレール)</c> 视为同名。
        /// </summary>
        private static string NormalizePartName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                char ch = c;

                // 全角 ASCII（！～～）转半角，保证（ ）／　等写法差异不影响比较
                if (ch >= '\uFF01' && ch <= '\uFF5E')
                {
                    ch = (char)(ch - 0xFEE0);
                }

                builder.Append(char.ToUpperInvariant(ch));
            }

            return builder.ToString();
        }

        /// <summary>取该図番第 1 页已有的最大「番号」+1（没有行时返回 1）。</summary>
        private static int NextNumber(IList<PartBasicInfo> parts)
        {
            int max = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part != null && part.Page <= 1 && part.Number > max)
                {
                    max = part.Number;
                }
            }

            return max + 1;
        }

        /// <summary>
        /// 第 5 步「部品コード正規化」：读 <c>部品マスタ\Tbl_部品マスタ.csv</c>，
        /// 把部品基本信息表的「部品コード」按 <see cref="CmdPartCodeNormalizer"/> 的规则改写。
        ///
        /// <list type="bullet">
        ///   <item><b>规则 1</b>：部品コード 能在部品マスタ 第 1 列找到时，用该行的「色」（第 4 列）
        ///         把部品コード 里的颜色段换成一个 <c>□</c>（<c>MEACC429</c> → <c>MEA□429</c>）。
        ///         「色」只出现在「材質」（第 3 列）里时<b>不换</b> —— 例如 <c>LAVS639</c> 的 <c>S</c>
        ///         是材質 <c>LAVS</c> 的一部分，换掉就成了别的部品。</item>
        ///   <item><b>规则 2</b>：部品コード 里连续出现 2 个以上符号（<c>□□</c> / <c>〇〇</c> / <c>△△</c> …）时，
        ///         压成一个 <c>□</c>（<c>MEA□□405</c> → <c>MEA□405</c>）。单个符号（<c>MZA□968</c>）保持原样。</item>
        /// </list>
        ///
        /// <para>
        /// 这一步<b>不建行、不删行</b>，只改「部品コード」一列（<see cref="PartBasicInfoRepository.UpdatePartCodes"/>），
        /// 所以第 1 / 2 / 3 / 4 步写入的内容不会被冲掉。
        /// 已经是 <c>MEA□429</c> 的行不会再变，重复执行结果不变。
        /// 换完以后和同図番 / 同页码 / 同番号 的另一行撞键时，那一行跳过（记警告），其余行照常写入。
        /// </para>
        /// </summary>
        private static void NormalizePartCodes(IList<PdfClassification> targets, string outputDirectory,
            PartBasicInfoRepository repository, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "{0}按 {1}\\{2} 的「色」把部品コード 里的颜色换成 □，" +
                "再把连续的符号（□□ / 〇〇 / △△ …）压成一个 □（只改部品コード 一列）",
                CmdAnalysisSteps.PartCodeStep.LogHeader, CmdPartCodeNormalizer.FolderName,
                CmdPartCodeNormalizer.FileName));

            List<string> notes = new List<string>();
            string masterFile = CmdPartCodeNormalizer.ResolveDataSource(outputDirectory, notes);

            if (string.IsNullOrEmpty(masterFile))
            {
                foreach (string note in notes)
                {
                    PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  " + note);
                }

                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "  找不到部品マスタ，跳过 " + CmdAnalysisSteps.PartCodeStep.Label);

                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Info, "  部品マスタ：" + masterFile);

            notes.Clear();
            CmdPartMasterIndex index = CmdPartCodeNormalizer.LoadIndex(masterFile, notes);

            foreach (string note in notes)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  " + note);
            }

            if (index.EntryCount == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                    "  部品マスタ 里没有读到有效明细（第 1 列 = 部品コード、第 3 列 = 材質、第 4 列 = 色），跳过 {0}",
                    CmdAnalysisSteps.PartCodeStep.Label));

                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format("  读到部品コード {0} 件",
                index.EntryCount));

            if (index.DuplicateCount > 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                    "  部品マスタ 里有 {0} 条重复的部品コード（只用第一次出现的行）", index.DuplicateCount));
            }

            foreach (PdfClassification item in targets)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Stem))
                {
                    continue;
                }

                string drawingNo = item.Stem.Trim();

                try
                {
                    NormalizePartCodesForDrawing(drawingNo, repository, index, summary, onLog);
                }
                catch (Exception ex)
                {
                    summary.FailedFileCount++;

                    PartAnalysisService.ReportLog(onLog, LogKind.Error,
                        "  部品コード 正規化失败（図番 " + drawingNo + "）：" + ex.Message);
                }
            }

            if (summary.PartCodeRowCount == 0 && summary.PartCodeSkippedRowCount == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info,
                    "  部品コード 没有需要改写的地方（已经正規化过，或部品マスタ 里没有对应的色 / 连续符号）");
            }
        }

        /// <summary>
        /// 一个図番的第 5 步：读该図番的行 → 逐行正規化部品コード → 只更新「部品コード」列。
        /// 换完以后会和同図番 / 同页码 / 同番号 的另一行撞键的行先剔除（避免被唯一索引拒绝）。
        /// </summary>
        private static void NormalizePartCodesForDrawing(string drawingNo, PartBasicInfoRepository repository,
            CmdPartMasterIndex index, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            List<PartBasicInfo> parts = new List<PartBasicInfo>(repository.Query(drawingNo));

            if (parts.Count == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "  数据库里没有该図番的部品行，跳过部品コード 正規化（図番 " + drawingNo + "）");

                return;
            }

            // 换完不能和同図番里已有的「页码 / 番号 / 部品コード」相撞（撞了会被唯一索引拒绝）
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);

            foreach (PartBasicInfo part in parts)
            {
                if (part != null)
                {
                    keys.Add(BuildPartCodeKey(part.Page, part.Number, part.PartCode));
                }
            }

            List<PartCodeUpdate> updates = new List<PartCodeUpdate>();
            int colorCount = 0;
            int symbolCount = 0;
            int conflictCount = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                PartCodeChangeKind kind;
                string normalized = CmdPartCodeNormalizer.Normalize(part.PartCode, index, out kind);

                if (kind == PartCodeChangeKind.None ||
                    string.Equals(normalized, part.PartCode, StringComparison.Ordinal))
                {
                    continue;
                }

                string oldKey = BuildPartCodeKey(part.Page, part.Number, part.PartCode);
                string newKey = BuildPartCodeKey(part.Page, part.Number, normalized);

                if (keys.Contains(newKey))
                {
                    conflictCount++;

                    PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                        "  页码 {0} / 番号 {1}：{2} → {3} 会和同一図番的另一行重复，跳过",
                        part.Page, part.Number, part.PartCode, normalized));

                    continue;
                }

                keys.Remove(oldKey);
                keys.Add(newKey);

                updates.Add(new PartCodeUpdate(part.Page, part.Number, part.PartCode, normalized));

                if (kind == PartCodeChangeKind.Color || kind == PartCodeChangeKind.ColorAndSymbol)
                {
                    colorCount++;
                }

                if (kind == PartCodeChangeKind.Symbol || kind == PartCodeChangeKind.ColorAndSymbol)
                {
                    symbolCount++;
                }
            }

            if (updates.Count == 0)
            {
                summary.PartCodeSkippedRowCount += conflictCount;

                return;
            }

            int failedRowCount;
            int written = repository.UpdatePartCodes(drawingNo, updates, out failedRowCount);

            summary.PartCodeRowCount += written;
            summary.PartCodeColorRowCount += colorCount;
            summary.PartCodeSymbolRowCount += symbolCount;
            summary.PartCodeSkippedRowCount += conflictCount + failedRowCount;
            summary.PartCodeDrawingCount++;

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "  部品コード 正規化：{0} 行（色 {1} / 连续符号 {2}）← 図番 {3}",
                written, colorCount, symbolCount, drawingNo));

            if (failedRowCount > 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                    "  有 {0} 行因为和别的行重复没能写入（部品コード 保持原样），请手工确认", failedRowCount));
            }
        }

        /// <summary>第 5 步的定位键：页码 / 番号 / 部品コード（与表的唯一索引一致）。</summary>
        private static string BuildPartCodeKey(int page, int number, string partCode)
        {
            return string.Format("{0}\u0001{1}\u0001{2}", page, number, partCode ?? string.Empty);
        }

        /// <summary>
        /// 第 6 步的分组：同一図番内「区分 / 参照元 / 部品コード / H・W 上下限」全部相同的 2 行以上。
        /// </summary>
        private sealed class UsageKindGroup
        {
            public UsageKindGroup(string partCode)
            {
                PartCode = partCode ?? string.Empty;
                Parts = new List<PartBasicInfo>();
            }

            /// <summary>部品コード（组内相同，用来查拾い出し表）。</summary>
            public string PartCode { get; private set; }

            /// <summary>组内的行（顺序 = <see cref="PartBasicInfoRepository.Query"/> 的顺序 = 页码 → 番号）。</summary>
            public List<PartBasicInfo> Parts { get; private set; }
        }

        /// <summary>生成一行中文汇总（界面状态栏与对话框共用）。</summary>
        public static string BuildSummaryText(PartAnalysisSummary summary)
        {
            if (summary == null)
            {
                return string.Empty;
            }

            return string.Format(
                "対象 {0} 个：登录成功 {1}，无部品数据 {2}，缺少 {3} {4}，失败 {5}；共登录 {6} 行（坐标一致 {7} / 不符 {8}）；" +
                "要領図部品 {9} 行（02 文件 {10} 个 / 缺 {11}）；" +
                "tz.json 部品 {12} 行（tz 明细 {13} 条 / {14} 个図番）；" +
                "按圈号分割 {15} 行（{16} 个部品 / {17} 个図番）；" +
                "部品コード 正規化 {18} 行（色 {19} / 连续符号 {20} / 跳过 {21}）；" +
                "使い分け {22} 行（{23} 组 / {24} 个図番 / 未命中 {25} 组）",
                summary.TargetCount, summary.SavedFileCount, summary.NoDataFileCount,
                CmdPartAnalyzer.LtStep1FileName, summary.MissingTableFileCount, summary.FailedFileCount,
                summary.SavedRowCount, summary.VerifiedPassedCount, summary.VerifiedFailedCount,
                summary.InstructionRowCount, summary.InstructionFileCount, summary.InstructionMissingFileCount,
                summary.TzRowCount, summary.TzEntryCount, summary.TzDrawingCount,
                summary.SplitRowCount, summary.SplitGroupCount, summary.SplitDrawingCount,
                summary.PartCodeRowCount, summary.PartCodeColorRowCount, summary.PartCodeSymbolRowCount,
                summary.PartCodeSkippedRowCount,
                summary.UsageKindRowCount, summary.UsageKindGroupCount, summary.UsageKindDrawingCount,
                summary.UsageKindMissGroupCount);
        }
    }
}
