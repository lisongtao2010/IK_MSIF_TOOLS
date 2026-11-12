using System;
using System.Collections.Generic;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 部品表「下表参照 / 左上表参照」展开：读 <c>tables\table_NN.csv</c>，把主表的这类行按参照表的明细拆开，
    /// 输出下游 AI 环节约定的 <c>01_アッセンブリ図_部品機能情報.csv</c>
    /// （旧文件名 <c>LT_STEP1.csv</c>；开关仍是 <see cref="DetectorOptions.BuildLtStep1Table"/> / <c>--no-lt-step1</c>）。
    ///
    /// 规则（与图纸上的写法一一对应）：
    ///   · 主表 —— 表头（第一行）同时含「番号」「部品名」，它的明细就是输出内容；
    ///     「部品コード」可有可无：有的部品表根本没有这一列，那些表明细照样收，该格留空；
    ///   · 明细行里「部品コード」含「下表参照」→ 往后找第一张「表头含部品コード且有明细」的表（图纸上的「下表」）；
    ///   · 含「左上表参照」→ 往前找同样条件的表（图纸上位于左上方，CSV 编号更小 —— 就是往前翻 CSV 文件）；
    ///   · 参照表有几条明细，这一行就拆成几条：只把「部品コード」换成参照表对应的值，
    ///     其余列（番号 / 部品名 / 枠区分 / 員数 / 寸法）沿用原行 —— 同一个部品的多个变体共用番号；
    ///   · 参照表找不到 / 明细全空 → 该行原样保留并写 WARN，不做猜测；
    ///   · 输出表头里一定有「部品コード」列（主表没有这一列时在末尾补一列、值留空），
    ///     下游按固定列取数时不会因为缺列而错位。
    ///
    /// 编码与其它清单一致：CSV 用带 BOM 的 UTF-8 + CRLF + RFC 4180 转义（Excel 双击即可打开）。
    /// 只读 <c>tables\*.csv</c>；除顺手删掉自己改名前的老文件 <c>LT_STEP1.csv</c> 外，不改动任何已有产物。
    ///
    /// **只有アッセンブリ図（組立図）才写这份清单**：图面分类读自分类清单 <c>classify\PdfInfoList.txt</c>
    /// （<see cref="PdfInfoListBuilder"/> 在流水线里先跑一步生成；清单缺失 / 没有这张图时退回按
    /// <c>tables\*.csv</c> 现判，同一套规则）。基本図 / 加工図 / 要領図 / 未识别 一律跳过，
    /// 并删掉上一次运行可能留下的同名产物。
    /// </summary>
    public static class LtStep1Builder
    {
        /// <summary>
        /// 下游约定的文件名（写在输出目录根，与 tables_info.json 同级）。
        /// 旧名是 <c>LT_STEP1.csv</c>，现按「图纸类别编号 + 日文名」统一成
        /// <c>01_アッセンブリ図_部品機能情報.csv</c>（アッセンブリ図 = 組立図，部品機能情報 = 部品表）。
        /// </summary>
        public const string OutputFileName = "01_アッセンブリ図_部品機能情報.csv";

        /// <summary>
        /// 改名前的老文件名：同一输出目录里若还留着上一次运行写下的它，本次顺手清掉，
        /// 免得下游按老名字取到过期内容。
        /// </summary>
        private const string LegacyOutputFileName = "LT_STEP1.csv";

        /// <summary>
        /// 主表表头必须同时出现的列名。
        ///
        /// 「部品コード」不在其中：有的图纸的部品表只写 番号 / 部品名 / 枠区分 / 員数，没有品番列，
        /// 这种表照样要收（下游要「一个部品一条记录」），只是输出的「部品コード」格留空。
        /// </summary>
        private static readonly string[] PartsHeaders = { "番号", "部品名" };

        /// <summary>「部品コード」列名（也是参照表必须有的列）。</summary>
        private const string PartCodeHeader = "部品コード";

        /// <summary>「部品コード」列出现这个字样时，表示要用「下表」的明细展开。</summary>
        private const string NextTableKeyword = "下表参照";

        /// <summary>
        /// 「部品コード」列出现这个字样时，表示要用「左上表」的明细展开：
        /// 参照表在图纸上位于左上方，CSV 编号比主表小 —— 于是「左上表参照」就是**往前翻** CSV 文件。
        /// </summary>
        private const string PreviousTableKeyword = "左上表参照";

        /// <summary>
        /// 扫描 <paramref name="outputDirectory"/> 下的 <c>tables\*.csv</c> 并合成
        /// <c>01_アッセンブリ図_部品機能情報.csv</c>。
        ///
        /// **只有アッセンブリ図（組立図）才写这份清单**：图面分类取自分类清单
        /// <c>classify\PdfInfoList.txt</c>（<see cref="PdfInfoListBuilder"/> 在这之前刚生成），
        /// 清单缺失时退回按 <c>tables\*.csv</c> 现判；分类不是アッセンブリ図就直接返回 0，
        /// 并把上一次可能留下的同名产物清掉。
        /// </summary>
        /// <returns>写出的明细行数（不含表头）；0 表示没有生成文件。</returns>
        public static int Build(string outputDirectory, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            if (!o.BuildLtStep1Table)
            {
                o.Log("[INFO] 01_アッセンブリ図_部品機能情報 合成已关闭（BuildLtStep1Table = false）。");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
                return 0;

            // 只有アッセンブリ図（組立図）才需要这份「部品機能情報」清单：
            // 先读分类清单 classify\PdfInfoList.txt（流水线刚写好的图面分类），清单缺失 / 没有这张图时
            // 退回按 tables\*.csv 现判（与清单同一套规则）。判成别的图类就什么都不写。
            string drawingNumber = PdfInfoListBuilder.DrawingNumberFromOutputDirectory(outputDirectory);
            string categorySource;
            string category = PdfInfoListBuilder.ResolveCategory(outputDirectory, drawingNumber, o, out categorySource);

            if (!PdfInfoListBuilder.IsAssemblyCategory(category))
            {
                o.Log("[INFO] 01_アッセンブリ図_部品機能情報：图面分类「"
                      + (string.IsNullOrEmpty(category) ? "未识别" : category) + "」（依据：" + categorySource
                      + "）不是アッセンブリ図，跳过（并清掉旧的同名产物）。");

                DeleteStaleOutput(outputDirectory, o);
                return 0;
            }

            o.Log("[INFO] 01_アッセンブリ図_部品機能情報：图面分类「アッセンブリ図」（依据：" + categorySource + "），开始合成。");

            string csvDir = Path.Combine(outputDirectory, "tables");

            if (!Directory.Exists(csvDir))
            {
                o.Log("[INFO] 01_アッセンブリ図_部品機能情報：没有 tables 目录（本次未生成表格 CSV），跳过。");
                return 0;
            }

            string[] files = Directory.GetFiles(csvDir, "*.csv");

            // 文件名补零（table_01 … table_10），按名字排序即图纸上从上到下的顺序
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            List<Sheet> sheets = LoadSheets(files);

            if (sheets.Count == 0)
            {
                o.Log("[INFO] 01_アッセンブリ図_部品機能情報：" + csvDir + " 下没有可读的表格 CSV，跳过。");
                return 0;
            }

            var output = new List<string[]>();
            string[] outHeader = null;
            int mainSheets = 0;
            int expandedRows = 0;

            for (int i = 0; i < sheets.Count; i++)
            {
                Sheet sheet = sheets[i];

                if (!IsPartsTable(sheet.Header))
                    continue;

                if (outHeader == null)
                    outHeader = EnsurePartCodeColumn(sheet.Header, sheet.Name, o);

                mainSheets++;

                int codeCol = sheet.CodeColumn;

                if (codeCol < 0)
                    o.Log("[INFO] 01_アッセンブリ図_部品機能情報：" + sheet.Name + " 没有「" + PartCodeHeader
                          + "」列，明细照收，该列留空。");

                int outCodeCol = CsvFile.IndexOfHeader(outHeader, PartCodeHeader);

                for (int r = 1; r < sheet.Rows.Count; r++)
                {
                    string[] row = sheet.Rows[r];

                    if (CsvFile.IsEmptyRow(row))
                        continue;

                    string code = codeCol >= 0 && codeCol < row.Length ? row[codeCol] : string.Empty;

                    if (!IsReferenceKeyword(code))
                    {
                        output.Add(MapToHeader(row, sheet.Header, outHeader));
                        continue;
                    }

                    // 「下表参照」→ 往后翻（+1）；「左上表参照」→ 往前翻（-1，图纸上位于左上方、CSV 编号更小）。
                    // 都取第一张「表头含部品コード且有明细」的表当参照表；两个词同时出现时按「左上表」处理。
                    string flat = CsvFile.Normalize(code);
                    int step = flat.IndexOf(PreviousTableKeyword, StringComparison.Ordinal) >= 0 ? -1 : 1;
                    string refName;

                    List<string> refCodes = FindReferenceCodes(sheets, i, step, out refName);

                    if (refCodes == null)
                    {
                        o.Log("[WARN] 01_アッセンブリ図_部品機能情報：" + sheet.Name + " 第 " + (r + 1) + " 行「" + code
                              + "」按" + (step < 0 ? "前（左上）" : "后（下）")
                              + "找不到可用的参照表，该行原样保留。");
                        output.Add(MapToHeader(row, sheet.Header, outHeader));
                        continue;
                    }

                    foreach (string refCode in refCodes)
                    {
                        string[] clone = MapToHeader(row, sheet.Header, outHeader);

                        if (outCodeCol >= 0)
                            clone[outCodeCol] = refCode;

                        output.Add(clone);
                        expandedRows++;
                    }

                    o.Log("[INFO] 01_アッセンブリ図_部品機能情報：" + sheet.Name + " 第 " + (r + 1) + " 行（" + code + "）→ "
                          + refName + " 的 " + refCodes.Count + " 条部品コード："
                          + string.Join(" / ", refCodes.ToArray()));
                }
            }

            if (outHeader == null)
            {
                o.Log("[INFO] 01_アッセンブリ図_部品機能情報：未找到表头含「番号 / 部品名」的表格，不生成 "
                      + OutputFileName + "。");
                return 0;
            }

            string path = Path.Combine(outputDirectory, OutputFileName);

            DeleteLegacyOutput(outputDirectory, o);

            CsvFile.Write(path, outHeader, output);

            o.Log("[INFO] 01_アッセンブリ図_部品機能情報：" + mainSheets + " 张主表 → 展开 " + expandedRows + " 行 → 共 "
                  + output.Count + " 条明细，已保存：" + path);

            return output.Count;
        }

        /// <summary>
        /// 清掉上一次运行留下的旧文件名（改名前的 <c>LT_STEP1.csv</c>）。
        /// 删不掉（被 Excel 打开 / 无权限）只记 WARN，不影响本次输出。
        /// </summary>
        private static void DeleteLegacyOutput(string outputDirectory, DetectorOptions o)
        {
            RemoveIfExists(Path.Combine(outputDirectory, LegacyOutputFileName), "旧文件名残留 ", o);
        }

        /// <summary>
        /// 这张图不是アッセンブリ図时，把上一次运行留下的同名产物清掉
        /// （上次可能判成アッセンブリ図，留着会让下游按固定名字取到过期内容）。
        /// </summary>
        private static void DeleteStaleOutput(string outputDirectory, DetectorOptions o)
        {
            RemoveIfExists(Path.Combine(outputDirectory, OutputFileName), string.Empty, o);
            RemoveIfExists(Path.Combine(outputDirectory, LegacyOutputFileName), "旧文件名残留 ", o);
        }

        /// <summary>
        /// 删一个文件；不存在就什么也不做。删不掉（被 Excel 占用 / 无权限）只记 WARN。
        /// </summary>
        /// <param name="path">要删的文件。</param>
        /// <param name="note">日志里的说明（例如「旧文件名残留 」），可为空。</param>
        /// <param name="o">参数 / 日志。</param>
        private static void RemoveIfExists(string path, string note, DetectorOptions o)
        {
            if (!File.Exists(path))
                return;

            try
            {
                File.Delete(path);
                o.Log("[INFO] 01_アッセンブリ図_部品機能情報：已删除" + note + path);
            }
            catch (IOException)
            {
                o.Log("[WARN] 01_アッセンブリ図_部品機能情報：删不掉" + note + path + "（可能正被 Excel 打开）。");
            }
            catch (UnauthorizedAccessException)
            {
                o.Log("[WARN] 01_アッセンブリ図_部品機能情報：删不掉" + note + path + "（无权限）。");
            }
        }

        // ── CSV 读取 ─────────────────────────────────────────────────────────

        /// <summary>一张表（一个 CSV 文件）的解析结果。</summary>
        private sealed class Sheet
        {
            /// <summary>文件名（含扩展名），日志里用。</summary>
            public string Name;

            /// <summary>按行拆好的单元格；<c>[0]</c> 是表头。</summary>
            public List<string[]> Rows;

            /// <summary>表头里「部品コード」所在列，-1 表示没有这一列。</summary>
            public int CodeColumn;

            /// <summary>表头行（等于 <c>Rows[0]</c>）。</summary>
            public string[] Header
            {
                get { return Rows[0]; }
            }
        }

        private static List<Sheet> LoadSheets(string[] files)
        {
            var sheets = new List<Sheet>();

            foreach (string file in files)
            {
                List<string[]> rows;

                try
                {
                    rows = CsvFile.Read(file);
                }
                catch (IOException)
                {
                    continue;   // 被 Excel 占用等情况：跳过这一张，不影响其它表
                }

                if (rows.Count == 0)
                    continue;

                sheets.Add(new Sheet
                {
                    Name = Path.GetFileName(file),
                    Rows = rows,
                    CodeColumn = CsvFile.IndexOfHeader(rows[0], PartCodeHeader)
                });
            }

            return sheets;
        }

        // ── 判定 / 映射 ──────────────────────────────────────────────────────

        /// <summary>
        /// 是否部品表（主表）：表头同时含「番号」「部品名」。
        /// 「部品コード」可有可无 —— 没有这一列的部品表也收，输出时该列留空（见 <see cref="EnsurePartCodeColumn"/>）。
        /// </summary>
        private static bool IsPartsTable(string[] header)
        {
            if (header == null)
                return false;

            foreach (string keyword in PartsHeaders)
            {
                if (CsvFile.IndexOfHeader(header, keyword) < 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 取参照表的部品コード清单（按明细行顺序、去空、去重复表头、去链式参照行）。
        /// 没有部品コード列或没有有效值 → 返回 null（表示这张表不能当参照表）。
        /// </summary>
        private static List<string> CollectReferenceCodes(Sheet sheet)
        {
            if (sheet.CodeColumn < 0)
                return null;

            var codes = new List<string>();

            for (int r = 1; r < sheet.Rows.Count; r++)
            {
                string[] row = sheet.Rows[r];

                if (row.Length <= sheet.CodeColumn)
                    continue;

                string value = CsvFile.Normalize(row[sheet.CodeColumn]);

                if (value.Length == 0)
                    continue;

                if (IsReferenceKeyword(value))
                    continue;   // 参照表自己也在「下表参照 / 左上表参照」：不接第二层，避免越扯越乱

                if (value.IndexOf(PartCodeHeader, StringComparison.Ordinal) >= 0)
                    continue;   // 重复的表头行

                codes.Add(value);
            }

            return codes.Count > 0 ? codes : null;
        }

        /// <summary>
        /// 这一格是不是「…表参照」这种指向别处的写法（「下表参照」/「左上表参照」）。
        /// 判定前先归一化：PDF 里这两个词常被拆成两行（「左上表」+「参照」）、中间还夹空格。
        /// </summary>
        private static bool IsReferenceKeyword(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            string flat = CsvFile.Normalize(value);

            return flat.IndexOf(NextTableKeyword, StringComparison.Ordinal) >= 0
                || flat.IndexOf(PreviousTableKeyword, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 按方向找参照表：<paramref name="step"/> = +1 往后翻（「下表参照」）、-1 往前翻（「左上表参照」），
        /// 返回第一张「表头含部品コード且有明细」的表（图纸上位于下方 / 左上方的那个小表）。
        /// </summary>
        /// <param name="sheets">全部表格（按文件名排序，与图纸上从上到下的顺序一致）。</param>
        /// <param name="fromIndex">主表在 <paramref name="sheets"/> 里的下标。</param>
        /// <param name="step">翻的方向：+1 往后、-1 往前。</param>
        /// <param name="sheetName">命中的参照表文件名；没找到时写 null。</param>
        /// <returns>参照表的部品コード清单；找不到返回 null。</returns>
        private static List<string> FindReferenceCodes(List<Sheet> sheets, int fromIndex, int step, out string sheetName)
        {
            sheetName = null;

            for (int k = fromIndex + step; k >= 0 && k < sheets.Count; k += step)
            {
                List<string> found = CollectReferenceCodes(sheets[k]);

                if (found == null)
                    continue;

                sheetName = sheets[k].Name;

                return found;
            }

            return null;
        }

        /// <summary>把源行按「列名」映射到输出表头（列顺序 / 列数不同也能对齐，缺列留空）。</summary>
        private static string[] MapToHeader(string[] row, string[] srcHeader, string[] dstHeader)
        {
            var result = new string[dstHeader.Length];

            for (int c = 0; c < dstHeader.Length; c++)
            {
                string name = CsvFile.Normalize(dstHeader[c]);
                int src = CsvFile.IndexOfHeader(srcHeader, name);

                if (src < 0)
                {
                    // 归一化之后可能变成「被包含」的反向关系，退回同名精确比较
                    for (int s = 0; s < srcHeader.Length; s++)
                    {
                        if (CsvFile.Normalize(srcHeader[s]) == name)
                        {
                            src = s;
                            break;
                        }
                    }
                }

                result[c] = (src >= 0 && src < row.Length) ? row[src] : string.Empty;
            }

            return result;
        }

        /// <summary>
        /// 输出表头里必须有「部品コード」列：主表自己没这一列时，在末尾补一列（明细值留空）。
        ///
        /// 这样「有的部品表写了品番、有的没写」时，输出仍是同一套列 —— 下游按固定列取数不会错位，
        /// 缺的那一列读出来就是空串。
        /// </summary>
        /// <param name="header">主表表头（只读，不改动原数组）。</param>
        /// <param name="sheetName">主表文件名（日志里用）。</param>
        /// <param name="o">参数 / 日志。</param>
        /// <returns>含「部品コード」列的表头（原表头已经有这一列时原样返回）。</returns>
        private static string[] EnsurePartCodeColumn(string[] header, string sheetName, DetectorOptions o)
        {
            if (header == null)
                return new[] { PartCodeHeader };

            if (CsvFile.IndexOfHeader(header, PartCodeHeader) >= 0)
                return header;

            var extended = new string[header.Length + 1];

            Array.Copy(header, extended, header.Length);
            extended[header.Length] = PartCodeHeader;

            o.Log("[INFO] 01_アッセンブリ図_部品機能情報：" + sheetName + " 没有「" + PartCodeHeader
                  + "」列，输出表头末尾补一列，明细该格留空。");

            return extended;
        }
    }
}
