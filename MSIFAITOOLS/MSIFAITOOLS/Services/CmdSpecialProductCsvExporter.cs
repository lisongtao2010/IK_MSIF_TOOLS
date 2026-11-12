using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 一次「特注品 CSV 生成」的结果：统计 + 写出的文件 + 提示。
    /// 由 <see cref="CmdSpecialProductCsvExporter.Export"/> 生成，供【CMD 分析】写日志。
    /// </summary>
    internal sealed class CmdSpecialProductCsvResult
    {
        public CmdSpecialProductCsvResult()
        {
            Notes = new List<string>();
            WrittenFiles = new List<string>();
        }

        /// <summary>扫描过的 xlsx 个数（含不是特注品的）。</summary>
        public int ScannedFileCount { get; set; }

        /// <summary>文件名第 5 位是 <c>Z</c>（特注品）的 xlsx 个数。</summary>
        public int SpecialProductFileCount { get; set; }

        /// <summary>特注品里读不到部品表（没有表头 / 打不开）而跳过的个数。</summary>
        public int NoPartTableFileCount { get; set; }

        /// <summary>明细里写着「追加不要」而没写进 CSV 的条数。</summary>
        public int SkippedNoNeedCount { get; set; }

        /// <summary>调用 AI 的次数（每个特注品文件 2 次：先补缺行/缺字段，再补特注寸法）。</summary>
        public int AiCallCount { get; set; }

        /// <summary>AI 补到内容的特注品文件个数。</summary>
        public int AiAppliedFileCount { get; set; }

        /// <summary>AI 补上的空白字段个数（Excel 已有值不算）。</summary>
        public int AiFieldCount { get; set; }

        /// <summary>AI 补上的新行条数（Excel 部品表里没有的部品）。</summary>
        public int AiNewRowCount { get; set; }

        /// <summary>AI 补上的「追加対応特注寸法」条数。</summary>
        public int AiSpecialSizeCount { get; set; }

        /// <summary>AI 补足失败的文件个数（失败时保留 Excel 读到的内容）。</summary>
        public int AiFailedFileCount { get; set; }

        /// <summary>写进 CSV 的明细条数（所有文件合计，不含表头行）。</summary>
        public int RowCount { get; set; }

        /// <summary>写出的 CSV 完整路径（顺序 = 文件名排序）。</summary>
        public List<string> WrittenFiles { get; private set; }

        /// <summary>处理过程中的提示（由调用方写进日志）。</summary>
        public List<string> Notes { get; private set; }
    }

    /// <summary>
    /// 【CMD 分析】最前面的一步：把「特注品」Excel 部品表里的部品，按文件导出成
    /// <c>&lt;PDF 文件夹&gt;\AiAppData\&lt;文件名&gt;\特注品.csv</c>。
    ///
    /// <para>
    /// <b>谁算特注品</b>：文件名（去扩展名）第 5 个字符是 <c>Z</c> 的 <c>.xlsx</c>
    /// （<see cref="CmdSpecialProductRule.IsSpecialProductDrawingNo"/>：半角 <c>Z</c> / 小写 <c>z</c> /
    /// 全角 <c>Ｚ</c> 都认，长度不足 5 位不算）。判定口径与第 1 / 2 / 3 步写「特注品」完全一致。
    /// </para>
    ///
    /// <para>
    /// <b>部品从哪里来</b>：直接读该 xlsx 的部品表（工作表 <c>カメラ</c>，与 <c>classify\tz.json</c>
    /// 的来源表一致；没有这张表时退回第一张工作表）。表头按列名找 ——
    /// <c>品種 / 部品名 / 枠区分 / 部品コード / 員数 / 追加対応特注寸法</c>（<see cref="CsvColumns"/>），
    /// 全角 / 半角、空格差异不影响比对（<see cref="NormalizeHeader"/>）。
    /// </para>
    ///
    /// <para>
    /// <b>为什么也写「追加対応特注寸法」</b>：第 4 步（<see cref="CmdTzPartCollector"/>）改成
    /// <b>直接读这里生成的 CSV</b>、不再解析 Excel，而 H / W 上下限与备注「特注寸法：…」
    /// 全靠这一列，所以必须一并写出（前 5 列的顺序不变）。
    /// </para>
    ///
    /// <para>
    /// <b>取不到值的列就空着</b>：例如部品表没有「員数」列，该列整列留空；某一行某个单元格为空同样留空，
    /// 不猜、不补 0。明细里写着「追加不要」（<see cref="CmdTzPartCollector.ContainsNoNeedMark"/>：
    /// <c>追加不要 / 追加しない / 追加なし / 追加無し</c>）的行与第 4 步 tz.json 一样<b>不写进 CSV</b>，
    /// 条数记在提示里。
    /// </para>
    ///
    /// <para>
    /// <b>AI 补足（可选）</b>：<see cref="Export(string, string, AiApiOptions)"/> 传入已配置的
    /// <see cref="AiApiOptions"/> 时，用 <see cref="CmdSpecialProductAiAdvisor"/> 读该図番的
    /// <c>plu_table.txt</c>，<b>先补空字段 / 缺行、再补「追加対応特注寸法」</b>（Excel 已有的值不动），
    /// 最后把结果同步进 <c>classify\tz.json</c>（<see cref="MergeIntoTzJson"/>）。
    /// 不传（<c>null</c>）或没配密钥时完全不联网，行为与以前一样。
    /// </para>
    ///
    /// <para>
    /// <b>输出</b>：<c>&lt;PDF 文件夹&gt;\AiAppData\&lt;文件名（去扩展名）&gt;\特注品.csv</c>，
    /// UTF-8 with BOM + CRLF（Excel 直接双击打开不乱码），第一行固定是
    /// <c>品種,部品名,枠区分,部品コード,員数,追加対応特注寸法</c>；即使一条明细都没有也写出只有表头的文件，
    /// 方便下游判断「这个特注品确实没有部品」。
    /// </para>
    /// </summary>
    internal static class CmdSpecialProductCsvExporter
    {
        /// <summary>生成的文件名（放在 <c>AiAppData\&lt;文件名&gt;\</c> 下）。</summary>
        public const string CsvFileName = "特注品.csv";

        /// <summary>Excel 部品表的工作表名（<c>tz.json</c> 的来源表也是这张）。</summary>
        public const string PartSheetName = "カメラ";

        /// <summary>CSV 的列（顺序固定 = 表头行顺序）。</summary>
        public static readonly string[] CsvColumns = new string[]
        {
            "品種", "部品名", "枠区分", "部品コード", "員数", "追加対応特注寸法"
        };

        /// <summary>只处理这种扩展名（用户口径：Excel 部品表 = <c>.xlsx</c>）。</summary>
        private const string XlsxExtension = ".xlsx";

        /// <summary>Excel 打开文件时留下的临时文件前缀。</summary>
        private const string TempFilePrefix = "~$";

        /// <summary>找表头时最多往下看这么多行（部品表的表头一般在头几行）。</summary>
        private const int HeaderSearchDepth = 30;

        /// <summary>「品種」列的别名（比较时按 <see cref="NormalizeHeader"/> 归一化）。</summary>
        private static readonly string[] VarietyHeaders = new string[] { "品種", "品种" };

        /// <summary>「部品名」列的别名。</summary>
        private static readonly string[] PartNameHeaders = new string[] { "部品名", "部品名称" };

        /// <summary>「枠区分」列的别名。</summary>
        private static readonly string[] FrameKindHeaders = new string[] { "枠区分", "框区分" };

        /// <summary>「部品コード」列的别名（<c>部品ｺｰﾄﾞ</c> 归一化后与 <c>部品コード</c> 相同）。</summary>
        private static readonly string[] PartCodeHeaders = new string[] { "部品コード", "品番", "型番" };

        /// <summary>「員数」列的别名（部品表没有该列时整列留空）。</summary>
        private static readonly string[] QuantityHeaders = new string[] { "員数", "员数", "数量", "入数" };

        /// <summary>
        /// 「追加対応特注寸法」列：写进 CSV 第 6 列（第 4 步据此算 H / W 上下限与备注），
        /// 同时用来判定「追加不要」（与第 4 步 tz.json 的判定口径一致）。
        /// </summary>
        private static readonly string[] SpecialSizeHeaders = new string[] { "追加対応特注寸法" };

        /// <summary>
        /// 扫描 <paramref name="pdfDirectory"/> 下的 <c>.xlsx</c>，把第 5 位是 <c>Z</c> 的文件的部品表
        /// 导出成 <c>&lt;outputDirectory&gt;\&lt;文件名&gt;\特注品.csv</c>。
        /// </summary>
        /// <param name="pdfDirectory">选择的 PDF 文件夹（&lt;outputDirectory&gt; 的上一级）。</param>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <returns>统计与提示（见 <see cref="CmdSpecialProductCsvResult"/>）。</returns>
        public static CmdSpecialProductCsvResult Export(string pdfDirectory, string outputDirectory)
        {
            return Export(pdfDirectory, outputDirectory, null);
        }

        /// <summary>
        /// 同上，并追加「AI 补足」：<paramref name="aiOptions"/> 已配置密钥时，对每个特注品文件
        /// 用 AI 读该図番的 PDF 图面文字（<c>AiAppData\&lt;図番&gt;\plu_table.txt</c>），
        /// <b>只补 Excel 读不到的内容</b>（空字段 / 缺的部品 / 追加対応特注寸法），
        /// 然后把最终内容同步进 <c>classify\tz.json</c>。
        /// <para>
        /// <paramref name="aiOptions"/> 为 <c>null</c> 或未配置密钥时与原来完全一样：
        /// 只写 Excel 读到的内容，不联网（结果仍会同步进 <c>classify\tz.json</c>）。
        /// </para>
        /// </summary>
        /// <param name="pdfDirectory">选择的 PDF 文件夹（&lt;outputDirectory&gt; 的上一级）。</param>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="aiOptions">AI 接口配置；<c>null</c> = 不做 AI 补足。</param>
        public static CmdSpecialProductCsvResult Export(string pdfDirectory, string outputDirectory,
            AiApiOptions aiOptions)
        {
            CmdSpecialProductCsvResult result = new CmdSpecialProductCsvResult();
            bool useAi = aiOptions != null && aiOptions.IsConfigured;

            if (string.IsNullOrWhiteSpace(pdfDirectory) || !Directory.Exists(pdfDirectory))
            {
                AddNote(result.Notes, "找不到 PDF 文件夹：" + pdfDirectory);
                return result;
            }

            string[] files;

            try
            {
                files = Directory.GetFiles(pdfDirectory, "*" + XlsxExtension, SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                AddNote(result.Notes, "读取文件夹失败（" + pdfDirectory + "）：" + ex.Message);
                return result;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);

                // Excel 打开文件时留下的临时文件（~$xxx.xlsx）
                if (fileName.StartsWith(TempFilePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(Path.GetExtension(file), XlsxExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.ScannedFileCount++;

                string drawingNo = Path.GetFileNameWithoutExtension(file);

                // 文件名第 5 位不是 Z：不是特注品，跳过
                if (!CmdSpecialProductRule.IsSpecialProductDrawingNo(drawingNo))
                {
                    continue;
                }

                result.SpecialProductFileCount++;

                List<string[]> rows = ReadPartTable(file, result.Notes);
                int headerRow = FindHeaderRow(rows);

                if (headerRow < 0)
                {
                    result.NoPartTableFileCount++;
                    AddNote(result.Notes, string.Format("{0}：读不到部品表（找不到「{1} / {2}」表头），跳过",
                        fileName, CsvColumns[1], CsvColumns[3]));

                    continue;
                }

                List<string[]> output = BuildCsvRows(rows, headerRow, fileName, result);

                // AI 补足（Excel 已有的值一律不动）：只补空字段 / 缺的部品 / 追加対応特注寸法
                if (useAi)
                {
                    ApplyAiEnrichment(drawingNo, file, outputDirectory, output, result, aiOptions);
                }

                string directory = Path.Combine(outputDirectory, drawingNo);
                string path = Path.Combine(directory, CsvFileName);

                try
                {
                    Directory.CreateDirectory(directory);
                    WriteCsv(path, output);
                }
                catch (Exception ex)
                {
                    AddNote(result.Notes, string.Format("{0}：写入 {1} 失败（{2}）", fileName, path, ex.Message));

                    continue;
                }

                result.RowCount += output.Count - 1;
                result.WrittenFiles.Add(path);

                // 特注品.csv 做出来了：把最终内容同步进 classify\tz.json（补足 tz.json 的明细 / 字段）
                MergeIntoTzJson(outputDirectory, fileName, output, result);

                if (output.Count <= 1)
                {
                    AddNote(result.Notes, string.Format("{0}：部品表里没有可写出的明细（只写了表头）", fileName));
                }
            }

            // 第 4 步是「读出所有 特注品.csv 就用」，残留的旧 CSV 会把已经不存在的部品挂进库：这里清一次
            RemoveStaleCsvFiles(outputDirectory, result.WrittenFiles, result.Notes);

            return result;
        }

        /// <summary>
        /// 清掉上一次生成、这次已经不该存在的 <c>特注品.csv</c>
        /// （对应的 xlsx 被改名 / 删掉 / 不再是特注品，或者整个文件夹已经不在了）。
        /// 只删本工具生成的那个文件名（<see cref="CsvFileName"/>），其它文件一概不动。
        /// </summary>
        /// <returns>删掉的个数（0 = 没有残留）。</returns>
        private static int RemoveStaleCsvFiles(string outputDirectory, IList<string> writtenFiles,
            IList<string> notes)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                return 0;
            }

            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (writtenFiles != null)
            {
                foreach (string path in writtenFiles)
                {
                    string directory = Path.GetDirectoryName(path);

                    if (!string.IsNullOrEmpty(directory))
                    {
                        keep.Add(directory);
                    }
                }
            }

            string[] files;

            try
            {
                files = Directory.GetFiles(outputDirectory, CsvFileName, SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                return 0;
            }

            int removed = 0;

            foreach (string file in files)
            {
                if (keep.Contains(Path.GetDirectoryName(file) ?? string.Empty))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex)
                {
                    AddNote(notes, string.Format("{0}：删除残留的 {1} 失败（{2}）", file, CsvFileName,
                        ex.Message));
                }
            }

            if (removed > 0)
            {
                AddNote(notes, string.Format("清掉上次生成、这次没有对应特注品 xlsx 的 {0} {1} 个",
                    CsvFileName, removed));
            }

            return removed;
        }

        /// <summary>
        /// 对一个特注品文件做 AI 补足：找该図番的分解输出目录 → 读 <c>plu_table*.txt</c> → 让 AI
        /// <b>先补空字段 / 缺行、再补「追加対応特注寸法」</b>（Excel 已有的值一律不动）。
        /// 失败只记提示，Excel 读到的内容照常写 CSV。
        /// </summary>
        private static void ApplyAiEnrichment(string drawingNo, string xlsxPath, string outputDirectory,
            IList<string[]> rows, CmdSpecialProductCsvResult result, AiApiOptions aiOptions)
        {
            string fileName = Path.GetFileName(xlsxPath);
            string directory = FindDrawingOutputDirectory(outputDirectory, drawingNo);
            SpecialProductAiResult ai;

            try
            {
                CmdSpecialProductAiAdvisor advisor =
                    new CmdSpecialProductAiAdvisor(aiOptions, SpecialProductPromptStore.Load());
                ai = advisor.Enrich(drawingNo, directory, rows);
            }
            catch (Exception ex)
            {
                result.AiFailedFileCount++;
                AddNote(result.Notes, string.Format("{0}：AI 补足失败（{1}），保留 Excel 读到的内容",
                    fileName, ex.Message));

                return;
            }

            foreach (string note in ai.Notes)
            {
                AddNote(result.Notes, fileName + "：" + note);
            }

            result.AiCallCount += ai.AiCallCount;
            result.AiFieldCount += ai.FilledFieldCount;
            result.AiNewRowCount += ai.AddedRowCount;
            result.AiSpecialSizeCount += ai.FilledSpecialSizeCount;

            if (ai.FilledFieldCount + ai.AddedRowCount + ai.FilledSpecialSizeCount > 0)
            {
                result.AiAppliedFileCount++;
                AddNote(result.Notes, string.Format(
                    "{0}：AI 补足 字段 {1} 个 / 新行 {2} 条 / 特注寸法 {3} 条",
                    fileName, ai.FilledFieldCount, ai.AddedRowCount, ai.FilledSpecialSizeCount));
            }
        }

        /// <summary>
        /// 该図番的分解输出目录：<c>&lt;outputDirectory&gt;\&lt;図番&gt;</c>；
        /// 多页 PDF 拆出来的是 <c>&lt;図番&gt;_P2</c> 这种时，退回第一个匹配的目录。
        /// </summary>
        private static string FindDrawingOutputDirectory(string outputDirectory, string drawingNo)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(drawingNo))
            {
                return string.Empty;
            }

            string direct = Path.Combine(outputDirectory, drawingNo);

            if (Directory.Exists(direct))
            {
                return direct;
            }

            try
            {
                string[] matches = Directory.GetDirectories(outputDirectory, drawingNo + "*",
                    SearchOption.TopDirectoryOnly);

                if (matches.Length > 0)
                {
                    Array.Sort(matches, StringComparer.OrdinalIgnoreCase);

                    return matches[0];
                }
            }
            catch (Exception)
            {
                // 目录枚举失败时按「没有分解输出」处理（AI 补足会被跳过）
            }

            return direct;
        }

        /// <summary>tz.json 顶层存放明细的键（与 <see cref="CmdTzPartCollector"/> 一致）。</summary>
        private const string TzItemKey = "items";

        /// <summary>tz.json 顶层的明细条数键（有就同步更新）。</summary>
        private const string TzItemCountKey = "item_count";

        /// <summary>tz.json 一条明细里的字段名（顺序与 <see cref="TzCsvColumns"/> 对应；没有「員数」）。</summary>
        private static readonly string[] TzFieldKeys = new string[]
        {
            "品種", "部品名", "枠区分", "部品コード", "追加対応特注寸法"
        };

        /// <summary><see cref="TzFieldKeys"/> 各自对应 CSV 的第几列（0 起）。</summary>
        private static readonly int[] TzCsvColumns = new int[] { 0, 1, 2, 3, 5 };

        /// <summary>
        /// 把刚生成的 特注品.csv 同步进 <c>classify\tz.json</c>（= 补足 tz.json）：
        /// 同一个 xlsx（file）的明细按 品種 + 部品名 + 部品コード 对上 → 只补空白字段；
        /// 对不上的 CSV 行追加成新明细（sheet = カメラ，row = 0）。
        /// <para>
        /// 只改这一部分：其它文件（甚至非特注品 xlsx）的明细、其它顶层键都原样保留。
        /// tz.json 不存在时什么也不做（不新建，避免和 CadPartSplitter 抢着写）。
        /// </para>
        /// </summary>
        private static void MergeIntoTzJson(string outputDirectory, string xlsxFileName,
            IList<string[]> csvRows, CmdSpecialProductCsvResult result)
        {
            string path = CmdTzPartCollector.GetFilePath(outputDirectory);

            if (!File.Exists(path))
            {
                return;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;

            Dictionary<string, object> root;
            List<object> items;

            try
            {
                root = serializer.DeserializeObject(File.ReadAllText(path, Encoding.UTF8))
                    as Dictionary<string, object>;
                items = ReadTzItems(root);
            }
            catch (Exception ex)
            {
                AddNote(result.Notes, string.Format("{0}：读取失败（{1}），没有同步 {2}",
                    CmdTzPartCollector.FileName, ex.Message, CsvFileName));

                return;
            }

            if (root == null || items == null)
            {
                AddNote(result.Notes, string.Format("{0}：结构不认识（没有「{1}」数组），没有同步 {2}",
                    CmdTzPartCollector.FileName, TzItemKey, CsvFileName));

                return;
            }

            int filled = 0;
            int added = 0;

            for (int rowIndex = 1; rowIndex < csvRows.Count; rowIndex++)
            {
                string[] csvCells = csvRows[rowIndex];
                Dictionary<string, object> item = FindTzItem(items, xlsxFileName, csvCells);

                if (item == null)
                {
                    items.Add(CreateTzItem(xlsxFileName, csvCells));
                    added++;

                    continue;
                }

                for (int index = 0; index < TzFieldKeys.Length; index++)
                {
                    if (GetTzText(item, TzFieldKeys[index]).Length > 0)
                    {
                        continue;
                    }

                    string value = CellText(csvCells, TzCsvColumns[index]);

                    if (value.Length == 0)
                    {
                        continue;
                    }

                    item[TzFieldKeys[index]] = value;
                    filled++;
                }
            }

            if (filled == 0 && added == 0)
            {
                return;
            }

            root[TzItemKey] = items.ToArray();

            if (root.ContainsKey(TzItemCountKey))
            {
                root[TzItemCountKey] = items.Count;
            }

            try
            {
                WriteJsonFile(path, root, serializer);
            }
            catch (Exception ex)
            {
                AddNote(result.Notes, string.Format("{0}：写入失败（{1}），没有同步 {2}",
                    CmdTzPartCollector.FileName, ex.Message, CsvFileName));

                return;
            }

            AddNote(result.Notes, string.Format("{0}：同步 {1}（补字段 {2} 个 / 追加明细 {3} 条）",
                CmdTzPartCollector.FileName, xlsxFileName, filled, added));
        }

        /// <summary>取 tz.json 的 items 数组（没有 / 不是数组时返回 null）。</summary>
        private static List<object> ReadTzItems(Dictionary<string, object> root)
        {
            if (root == null)
            {
                return null;
            }

            object value;

            if (!root.TryGetValue(TzItemKey, out value))
            {
                return null;
            }

            object[] array = value as object[];

            return array == null ? null : new List<object>(array);
        }

        /// <summary>
        /// 在 tz.json 的明细里找与这一行 CSV 对应的明细：同一个 xlsx（file）
        /// + 品種 / 部品名 相同 + 部品コード（两边都有时比较）；两边都没有部品コード 时比较 枠区分。
        /// </summary>
        private static Dictionary<string, object> FindTzItem(List<object> items, string xlsxFileName,
            string[] csvCells)
        {
            string variety = NormalizeHeader(CellText(csvCells, 0));
            string partName = NormalizeHeader(CellText(csvCells, 1));
            string frameKind = NormalizeHeader(CellText(csvCells, 2));
            string partCode = NormalizeHeader(CellText(csvCells, 3));

            foreach (object item in items)
            {
                Dictionary<string, object> map = item as Dictionary<string, object>;

                if (map == null || !IsSameExcelFile(GetTzText(map, "file"), xlsxFileName))
                {
                    continue;
                }

                if (!string.Equals(NormalizeHeader(GetTzText(map, TzFieldKeys[0])), variety,
                        StringComparison.Ordinal) ||
                    !string.Equals(NormalizeHeader(GetTzText(map, TzFieldKeys[1])), partName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string itemCode = NormalizeHeader(GetTzText(map, TzFieldKeys[3]));

                if (itemCode.Length > 0 || partCode.Length > 0)
                {
                    if (!string.Equals(itemCode, partCode, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
                else if (!string.Equals(NormalizeHeader(GetTzText(map, TzFieldKeys[2])), frameKind,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                return map;
            }

            return null;
        }

        /// <summary>tz.json 的 file 与这个 xlsx 是不是同一个文件（忽略扩展名与路径）。</summary>
        private static bool IsSameExcelFile(string tzFile, string xlsxFileName)
        {
            if (string.IsNullOrWhiteSpace(tzFile) || string.IsNullOrWhiteSpace(xlsxFileName))
            {
                return false;
            }

            string left = Path.GetFileNameWithoutExtension(tzFile.Trim());
            string right = Path.GetFileNameWithoutExtension(xlsxFileName.Trim());

            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>按 CSV 行造一条新的 tz.json 明细（row = 0：不是从 Excel 行号来的）。</summary>
        private static Dictionary<string, object> CreateTzItem(string xlsxFileName, string[] csvCells)
        {
            Dictionary<string, object> item = new Dictionary<string, object>(StringComparer.Ordinal);
            item["file"] = xlsxFileName;
            item["sheet"] = PartSheetName;
            item["row"] = 0;

            for (int index = 0; index < TzFieldKeys.Length; index++)
            {
                item[TzFieldKeys[index]] = CellText(csvCells, TzCsvColumns[index]);
            }

            return item;
        }

        /// <summary>取 tz.json 明细里的文本（空 / 对象 / 数组按空串处理）。</summary>
        private static string GetTzText(Dictionary<string, object> map, string key)
        {
            object value;

            if (map == null || string.IsNullOrEmpty(key) || !map.TryGetValue(key, out value) ||
                value == null)
            {
                return string.Empty;
            }

            if (value is Dictionary<string, object> || value is object[])
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        /// <summary>把 JSON 写回文件：UTF-8 无 BOM、CRLF、2 空格缩进（保持 tz.json 可读）。</summary>
        private static void WriteJsonFile(string path, object value, JavaScriptSerializer serializer)
        {
            StringBuilder builder = new StringBuilder();
            AppendJsonValue(builder, value, 0, serializer);
            builder.Append("\r\n");

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        /// <summary>缩进输出 JSON：对象 / 数组逐层换行，标量交给 <see cref="JavaScriptSerializer"/> 转义。</summary>
        private static void AppendJsonValue(StringBuilder builder, object value, int indent,
            JavaScriptSerializer serializer)
        {
            Dictionary<string, object> map = value as Dictionary<string, object>;

            if (map != null)
            {
                if (map.Count == 0)
                {
                    builder.Append("{}");

                    return;
                }

                builder.Append("{");
                bool first = true;

                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (!first)
                    {
                        builder.Append(",");
                    }

                    first = false;
                    builder.AppendLine();
                    AppendJsonIndent(builder, indent + 1);
                    builder.Append(serializer.Serialize(pair.Key)).Append(": ");
                    AppendJsonValue(builder, pair.Value, indent + 1, serializer);
                }

                builder.AppendLine();
                AppendJsonIndent(builder, indent);
                builder.Append("}");

                return;
            }

            object[] array = value as object[];

            if (array != null)
            {
                if (array.Length == 0)
                {
                    builder.Append("[]");

                    return;
                }

                builder.Append("[");

                for (int index = 0; index < array.Length; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(",");
                    }

                    builder.AppendLine();
                    AppendJsonIndent(builder, indent + 1);
                    AppendJsonValue(builder, array[index], indent + 1, serializer);
                }

                builder.AppendLine();
                AppendJsonIndent(builder, indent);
                builder.Append("]");

                return;
            }

            builder.Append(serializer.Serialize(value));
        }

        /// <summary>JSON 缩进（每层 2 个空格）。</summary>
        private static void AppendJsonIndent(StringBuilder builder, int indent)
        {
            builder.Append(' ', indent * 2);
        }

        /// <summary>
        /// 从表头行开始把部品表转成 CSV 行（第一行固定是 <see cref="CsvColumns"/>）。
        /// 表头里没有的列按「取不到值就空着」处理（<see cref="IndexOfHeader"/> 返回 -1 → 整列空）。
        /// </summary>
        private static List<string[]> BuildCsvRows(List<string[]> rows, int headerRow, string fileName,
            CmdSpecialProductCsvResult result)
        {
            string[] header = rows[headerRow];
            int varietyCol = IndexOfHeader(header, VarietyHeaders);
            int partNameCol = IndexOfHeader(header, PartNameHeaders);
            int frameKindCol = IndexOfHeader(header, FrameKindHeaders);
            int partCodeCol = IndexOfHeader(header, PartCodeHeaders);
            int quantityCol = IndexOfHeader(header, QuantityHeaders);

            // 「追加対応特注寸法」写进 CSV 第 6 列（第 4 步据此算 H/W 上下限），也用来判定「追加不要」
            int specialSizeCol = IndexOfHeader(header, SpecialSizeHeaders);

            List<string[]> output = new List<string[]>();
            output.Add(CsvColumns);

            int noNeed = 0;

            for (int rowIndex = headerRow + 1; rowIndex < rows.Count; rowIndex++)
            {
                string[] cells = rows[rowIndex];

                if (IsEmptyRow(cells))
                {
                    continue;
                }

                // 分区重复的表头行（有的部品表每块都印一遍表头）不写成数据
                if (IsRepeatedHeader(cells, header))
                {
                    continue;
                }

                string[] values = new string[CsvColumns.Length];
                values[0] = CellText(cells, varietyCol);
                values[1] = CellText(cells, partNameCol);
                values[2] = CellText(cells, frameKindCol);
                values[3] = CellText(cells, partCodeCol);
                values[4] = FormatQuantity(CellText(cells, quantityCol));
                // 第 6 列：特注寸法原文（第 4 步读 CSV 时据此算 H / W 上下限）
                values[5] = CellText(cells, specialSizeCol);

                // 前 5 列全空 = 没有部品信息（只写了特注寸法也不算部品）
                if (IsBlankPartRow(values))
                {
                    continue;
                }

                // Excel 明写「追加不要」的行与第 4 步 tz.json 一样不采用
                if (ContainsNoNeedMark(values) ||
                    CmdTzPartCollector.ContainsNoNeedMark(CellText(cells, specialSizeCol)))
                {
                    noNeed++;

                    continue;
                }

                output.Add(values);
            }

            if (noNeed > 0)
            {
                result.SkippedNoNeedCount += noNeed;

                AddNote(result.Notes, string.Format("{0}：「追加不要」{1} 条，没有写进 {2}",
                    fileName, noNeed, CsvFileName));
            }

            return output;
        }

        /// <summary>
        /// 读部品表：先读 <c>カメラ</c> 工作表；没有这张表（或读不到）时退回第一张工作表，
        /// 并在提示里写明用了哪张。两张都读不到时把失败原因全部写进 <paramref name="notes"/>。
        /// </summary>
        private static List<string[]> ReadPartTable(string filePath, IList<string> notes)
        {
            List<string> sheetNotes = new List<string>();
            List<string[]> rows = XlsxSheetReader.ReadSheet(filePath, PartSheetName, sheetNotes);

            if (rows.Count > 0)
            {
                return rows;
            }

            List<string> fallbackNotes = new List<string>();
            rows = XlsxSheetReader.ReadFirstSheet(filePath, fallbackNotes);

            if (rows.Count > 0)
            {
                AddNote(notes, string.Format("{0}：没有读到「{1}」表，改用第一张工作表",
                    Path.GetFileName(filePath), PartSheetName));

                return rows;
            }

            foreach (string note in sheetNotes)
            {
                AddNote(notes, note);
            }

            foreach (string note in fallbackNotes)
            {
                AddNote(notes, note);
            }

            return rows;
        }

        /// <summary>
        /// 找表头行：前 <see cref="HeaderSearchDepth"/> 行里第一行含「部品名」或「部品コード」的。
        /// 找不到返回 -1（调用方跳过该文件并记提示）。
        /// </summary>
        private static int FindHeaderRow(IList<string[]> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return -1;
            }

            int depth = Math.Min(rows.Count, HeaderSearchDepth);

            for (int rowIndex = 0; rowIndex < depth; rowIndex++)
            {
                string[] cells = rows[rowIndex];

                if (IndexOfHeader(cells, PartCodeHeaders) >= 0 || IndexOfHeader(cells, PartNameHeaders) >= 0)
                {
                    return rowIndex;
                }
            }

            return -1;
        }

        /// <summary>
        /// 在一行表头里找别名命中的列号（从 0 开始）；没有时返回 -1。
        /// 比较时两边都先 <see cref="NormalizeHeader"/>（全角转半角、去空白、转大写），
        /// 所以 <c>部品ｺｰﾄﾞ</c> 与 <c>部品コード</c>、<c>品 種</c> 与 <c>品種</c> 都能对上。
        /// </summary>
        private static int IndexOfHeader(string[] cells, string[] aliases)
        {
            if (cells == null || aliases == null)
            {
                return -1;
            }

            for (int index = 0; index < cells.Length; index++)
            {
                string text = NormalizeHeader(cells[index]);

                if (text.Length == 0)
                {
                    continue;
                }

                foreach (string alias in aliases)
                {
                    if (string.Equals(text, NormalizeHeader(alias), StringComparison.Ordinal))
                    {
                        return index;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// 表头 / 单元格的归一化：NFKC（全角 → 半角、半角片假名 → 全角片假名）→ 去空白 → 转大写。
        /// 归一化后的文本只用于<b>比较</b>，写进 CSV 的仍是原文（<see cref="CellText"/>）。
        /// </summary>
        private static string NormalizeHeader(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string text = value.Normalize(NormalizationForm.FormKC);
            StringBuilder builder = new StringBuilder(text.Length);

            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                builder.Append(char.ToUpperInvariant(ch));
            }

            return builder.ToString();
        }

        /// <summary>取一行里第 <paramref name="index"/> 列的文本（列号 &lt; 0 / 缺列 / null 都返回空字符串）。</summary>
        private static string CellText(string[] cells, int index)
        {
            if (cells == null || index < 0 || index >= cells.Length || cells[index] == null)
            {
                return string.Empty;
            }

            return cells[index].Trim();
        }

        /// <summary>整行是否全空（null / 空白都算空）。</summary>
        private static bool IsEmptyRow(string[] cells)
        {
            if (cells == null || cells.Length == 0)
            {
                return true;
            }

            foreach (string cell in cells)
            {
                if (!string.IsNullOrWhiteSpace(cell))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 前 5 列（品種 / 部品名 / 枠区分 / 部品コード / 員数）是否全为空 ——
        /// 只写了「追加対応特注寸法」的行不算部品，不写进 CSV。
        /// </summary>
        private static bool IsBlankPartRow(string[] values)
        {
            if (values == null)
            {
                return true;
            }

            int count = Math.Min(values.Length, CsvColumns.Length - 1);

            for (int index = 0; index < count; index++)
            {
                if (!string.IsNullOrWhiteSpace(values[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 这一行是不是「重复的表头行」（同一列位置上的文字与表头相同，忽略全角 / 半角 / 空格）。
        /// 部品表分区打印时每块都会重复一次表头，这种行不能当成部品写进 CSV。
        /// </summary>
        private static bool IsRepeatedHeader(string[] cells, string[] header)
        {
            if (cells == null || header == null)
            {
                return false;
            }

            int length = Math.Min(cells.Length, header.Length);

            for (int index = 0; index < length; index++)
            {
                string text = NormalizeHeader(cells[index]);

                if (text.Length == 0)
                {
                    continue;
                }

                if (string.Equals(text, NormalizeHeader(header[index]), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>任一处出现「追加不要」这类字样即返回 true（判定本身见 <see cref="CmdTzPartCollector"/>）。</summary>
        private static bool ContainsNoNeedMark(string[] values)
        {
            if (values == null)
            {
                return false;
            }

            foreach (string value in values)
            {
                if (CmdTzPartCollector.ContainsNoNeedMark(value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 写 CSV：UTF-8 with BOM（Excel 双击打开不乱码）、CRLF 换行、字段按 RFC 4180 转义
        /// （含 <c>,</c> / <c>"</c> / 换行时用 <c>"</c> 包起来，内部 <c>"</c> 变 <c>""</c>）。
        /// </summary>
        private static void WriteCsv(string path, IList<string[]> rows)
        {
            StringBuilder builder = new StringBuilder();

            foreach (string[] cells in rows)
            {
                for (int index = 0; index < cells.Length; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(EscapeCsv(cells[index]));
                }

                builder.Append("\r\n");
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// CSV 字段转义（没有特殊字符时原样返回）。
        /// <para>
        /// 字段内的换行（Excel 的 Alt+Enter）先压成空格：下游 <see cref="CmdTzPartCollector"/> 读 CSV
        /// 是<b>按行</b>拆的（<c>File.ReadAllLines</c> + 逐行解析），字段里带真换行会把后面的列挤到下一行。
        /// </para>
        /// </summary>
        private static string EscapeCsv(string value)
        {
            string text = (value ?? string.Empty).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

            if (text.IndexOf(',') < 0 && text.IndexOf('"') < 0)
            {
                return text;
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// 「員数」的写法：Excel 里的数字会读成 <c>2</c> / <c>2.0</c> 两种，整数值统一成
        /// <c>2</c>（避免 CSV 里出现 <c>2.0</c>）；不是数字的写法（<c>2枚</c> 等）原样保留，取不到就留空。
        /// </summary>
        private static string FormatQuantity(string text)
        {
            string value = (text ?? string.Empty).Trim();

            if (value.Length == 0)
            {
                return string.Empty;
            }

            double number;

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                Math.Abs(number - Math.Round(number)) < 0.0000001)
            {
                return Math.Round(number).ToString("0", CultureInfo.InvariantCulture);
            }

            return value;
        }

        /// <summary>往提示列表里加一条（空白不加）。</summary>
        private static void AddNote(IList<string> notes, string message)
        {
            if (notes != null && !string.IsNullOrWhiteSpace(message))
            {
                notes.Add(message);
            }
        }
    }
}
