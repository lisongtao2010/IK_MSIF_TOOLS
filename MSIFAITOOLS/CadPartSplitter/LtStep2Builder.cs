using System;
using System.Collections.Generic;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 品種表整理：读 <c>tables\table_NN.csv</c>，把两种表摊平成一行一个部品的
    /// <c>02_要領図_部品機能情報.csv</c>：①「横向重复 N 组 (部品コード, 員数)」的多列重复品種表；
    /// ②图纸上普通的「単純部品表」（<c>部品名 / 枠区分</c>；<c>部品コード</c> 可缺）。
    ///
    /// 认得的样子（图纸上常见，表头被拆成两行 + 合并单元格）：
    /// <code>
    /// 品種,部品名 ： 縦枠上部スペーサー,,部品名 ： 小縦枠上部スペーサー,, …
    /// ,部品コード,員数,部品コード,員数, …
    /// ,,,,,,,
    /// 片引戸,MEACC428,1,MEACC428,2, …
    /// </code>
    /// 整理成（一行一个部品，品種 / 品名往下填满；品種表里没有「枠区分」列，该格留空）：
    /// <code>
    /// 品種,部品名,枠区分,部品コード,員数
    /// 片引戸,縦枠上部スペーサー,,MEACC428,1
    /// 片引戸,小縦枠上部スペーサー,,MEACC428,2
    /// </code>
    ///
    /// 收录两种表：
    /// <para>
    /// ①「多列重复的品種表」（上面那种）—— 两条同时满足才认：
    ///   · 表头区里有含「品種」的单元格 → 品種列；
    ///   · 表头区里存在「同一个列名出现 ≥ 2 次」的行（多列重复）→ 每次出现的列就是一组部品ブロック的起点。
    /// 只有一块、没有重复列的表不是这种表。
    /// </para>
    /// <para>
    /// ②「単純部品表」—— 图纸上普通的部品表（<c>H310U10034.pdf</c> / <c>H310U10027.pdf</c> 那种，
    /// 只有一个部品、没有品種列）：
    /// </para>
    /// <code>
    /// 番号,部品名,枠区分,員数,基本寸法,切断寸法,部品コード
    /// 1,ロットＮｏ．ラベル,三方枠,1,-,-,LAVS639
    /// </code>
    /// 表头（前 6 行内）同含「部品名」「枠区分」即认这种表：一行一条明细，
    /// <c>品種</c> 留空（这种表本来就没有品種列）、<c>枠区分</c> 按列名搬 → <c>,ロットＮｏ．ラベル,三方枠,LAVS639,1</c>。
    /// <c>部品コード</c> 可有可无 —— 有些部品表只写「番号 / 部品名 / 枠区分 / 員数 / 基本寸法 / 切断寸法」
    /// （<c>H453U10003.pdf</c> / <c>H453U10004.pdf</c> 就是这种），缺这一列时输出里的 <c>部品コード</c> 恒留空，
    /// 不会因为缺列就把整张表跳过（与 <see cref="LtStep1Builder"/> 的「部品コード 可缺」一致）。
    /// </para>
    /// <para>
    /// 两种表共有的一条「品番なし」规则：<c>部品コード</c> 那一格是 <c>－</c> 这类占位符
    /// （整格只由破折号类字符组成 —— <c>－ - ー — – ― −</c> …，半角 / 全角空格不计）时，
    /// 这条明细不收：图纸上写「－」就是「这个部没有品番」。与「缺列」区分开：
    /// 整张表都没有 <c>部品コード</c> 列时照旧出明细（该格留空），只有列在、格子里写着「－」才丢这条。
    /// 丢掉的条数会打进日志。
    /// </para>
    /// <para>两种都不是的表原样跳过（不改动）。</para>
    ///
    /// 输出：<c>&lt;输出目录&gt;\02_要領図_部品機能情報.csv</c>（旧名 <c>LT_STEP2.csv</c>；
    /// 要領図 = 要領書，部品機能情報 = 部品表。UTF-8 带 BOM + CRLF + RFC 4180），
    /// 列固定为 品種 / 部品名 / 枠区分 / 部品コード / 員数 —— 下游按固定列取数
    /// （品種表没有枠区分列 → 该格留空；単純部品表没有品種列 → 该格留空；有的部品表没有部品コード列 → 该格留空）。
    /// 多张表按文件名顺序追加到同一个文件。只读 <c>tables\*.csv</c>，不改动任何已有产物。
    ///
    /// **只有要領図（要領書）才写这份清单**：图面分类读自分类清单 <c>classify\PdfInfoList.txt</c>
    /// （<see cref="PdfInfoListBuilder"/> 在流水线里先跑一步生成；清单缺失 / 没有这张图时退回按
    /// <c>tables\*.csv</c> 现判，同一套规则）。基本図 / 加工図 / アッセンブリ図 / 未识别 一律跳过，
    /// 并删掉上一次运行可能留下的同名产物。
    /// </summary>
    public static class LtStep2Builder
    {
        /// <summary>
        /// 下游约定的文件名（写在输出目录根，与 01_アッセンブリ図_部品機能情報.csv 同级）。
        /// 旧名是 <c>LT_STEP2.csv</c>，现按「图纸类别编号 + 日文名」统一成
        /// <c>02_要領図_部品機能情報.csv</c>（要領図 = 要領書，部品機能情報 = 部品表）。
        /// </summary>
        public const string OutputFileName = "02_要領図_部品機能情報.csv";

        /// <summary>
        /// 改名前的老文件名：同一输出目录里若还留着上一次运行写下的它，本次顺手清掉，
        /// 免得下游按老名字取到过期内容。
        /// </summary>
        private const string LegacyOutputFileName = "LT_STEP2.csv";

        /// <summary>品種表的标志文字（表头里出现它就认这一列是品種）。</summary>
        private const string VarietyKeyword = "品種";

        /// <summary>块标题的前缀（图纸上写成「部品名 ： 縦枠上部スペーサー」）。</summary>
        private const string PartNameKeyword = "部品名";

        /// <summary>块里的品番列名。</summary>
        private const string PartCodeKeyword = "部品コード";

        /// <summary>块里的数量列名。</summary>
        private const string QuantityKeyword = "員数";

        /// <summary>
        /// 「単純部品表」（<c>番号 / 部品名 / 枠区分 / 員数 / 基本寸法 / 切断寸法 [/ 部品コード]</c>）的标志列名
        /// —— 和 <see cref="PartNameKeyword"/> 同时出现才认这种表（<c>部品コード</c> 可缺）。
        /// </summary>
        private const string FrameKeyword = "枠区分";

        /// <summary>
        /// 「品番なし」を表す占位符：<c>－</c>（全角）/ <c>-</c>（半角）/ <c>ー</c>（長音）/ <c>—</c> / <c>–</c> …。
        /// 一整格只由这些符号组成（1 个或几个）时不算品番 —— 明细不收；
        /// 真品番里夹着 <c>-</c>（<c>TJ-3086</c>）不受影响：判定要求「整格都是这些符号」。
        /// </summary>
        private static readonly char[] PlaceholderCodeChars =
        {
            (char)0x002D,   // - 半角ハイフンマイナス
            (char)0x2010,   // ハイフン
            (char)0x2011,   // 非分割ハイフン
            (char)0x2012,   // 図形ダッシュ
            (char)0x2013,   // en dash
            (char)0x2014,   // em dash
            (char)0x2015,   // 水平バー
            (char)0x2043,   // ハイフンブレット
            (char)0x2212,   // 数学用マイナス
            (char)0xFE63,   // 小ハイフンマイナス
            (char)0xFF0D,   // 全角ハイフンマイナス（图纸上最常见）
            (char)0x30FC,   // 長音符（表格里也常拿来当「－」）
            (char)0xFF70    // 半角長音符
        };

        /// <summary>找表头（品種列 / 重复列名行 / 部品名+枠区分 行）时最多看前几行。</summary>
        private const int MaxHeaderScanRows = 6;

        /// <summary>输出表头：固定 5 列，下游按列取数。</summary>
        private static readonly string[] OutHeader =
        {
            VarietyKeyword, PartNameKeyword, FrameKeyword, PartCodeKeyword, QuantityKeyword
        };

        /// <summary>
        /// 扫描 <paramref name="outputDirectory"/> 下的 <c>tables\*.csv</c> 并写出
        /// <c>02_要領図_部品機能情報.csv</c>（图面分类不是「要領図」时不写）。
        /// </summary>
        /// <returns>写出的明细行数（不含表头）；0 表示没有生成文件。</returns>
        public static int Build(string outputDirectory, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            if (!o.BuildLtStep2Table)
            {
                o.Log("[INFO] 02_要領図_部品機能情報 摊平已关闭（BuildLtStep2Table = false）。");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
                return 0;

            // 只有要領図（要領書）才需要这份清单：先读分类清单 classify\PdfInfoList.txt（流水线刚写好的
            // 图面分类），清单缺失 / 没有这张图时退回按 tables\*.csv 现判（与清单同一套规则）。
            // 判成别的图类就什么都不写，并清掉上一次留下的同名产物。
            string drawingNumber = PdfInfoListBuilder.DrawingNumberFromOutputDirectory(outputDirectory);
            string categorySource;
            string category = PdfInfoListBuilder.ResolveCategory(outputDirectory, drawingNumber, o, out categorySource);

            if (!PdfInfoListBuilder.IsRequirementCategory(category))
            {
                o.Log("[INFO] 02_要領図_部品機能情報：图面分类「"
                      + (string.IsNullOrEmpty(category) ? "未识别" : category) + "」（依据：" + categorySource
                      + "）不是要領図，跳过（并清掉旧的同名产物）。");

                DeleteStaleOutput(outputDirectory, o);
                return 0;
            }

            o.Log("[INFO] 02_要領図_部品機能情報：图面分类「要領図」（依据：" + categorySource + "），开始摊平。");

            string csvDir = Path.Combine(outputDirectory, "tables");

            if (!Directory.Exists(csvDir))
            {
                o.Log("[INFO] 02_要領図_部品機能情報：没有 tables 目录（本次未生成表格 CSV），跳过。");
                return 0;
            }

            string[] files = Directory.GetFiles(csvDir, "*.csv");

            // 文件名补零（table_01 … table_10），按名字排序即图纸上从上到下的顺序
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var output = new List<string[]>();
            int tables = 0;
            int placeholders = 0;   // 因 部品コード 是「－」这类占位符而没收的明细总数

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

                // 两种表都收：①多列重复的品種表（横向排开 N 组部品）
                //             ②単純部品表（番号 / 部品名 / 枠区分 / 員数 / … [/ 部品コード]，品種留空）
                // 部品コード 是「－」这类占位符（品番なし）的明细两种表都不收，只计数、在日志里报一句。
                int skipped;
                List<string[]> detail = Expand(rows, out skipped);
                string form = "多列重复的品種表";

                if (detail == null)
                {
                    detail = ExpandPartTable(rows, out skipped);
                    form = "単純部品表（部品名 / 枠区分）";
                }

                if (skipped > 0)
                {
                    placeholders += skipped;

                    o.Log("[INFO] 02_要領図_部品機能情報：" + Path.GetFileName(file) + " 有 " + skipped
                          + " 条明细的 部品コード 是「－」这类占位符（品番なし）→ 不收。");
                }

                if (detail == null || detail.Count == 0)
                    continue;   // 两种形式都不是（或表头下面一条明细都没有 / 全是占位符）→ 不动这张表

                tables++;
                output.AddRange(detail);

                o.Log("[INFO] 02_要領図_部品機能情報：" + Path.GetFileName(file) + " 是" + form + " → 摊平 "
                      + detail.Count + " 条明细。");
            }

            if (tables == 0)
            {
                o.Log("[INFO] 02_要領図_部品機能情報：未找到品種表（品種 + 列名重复）或単純部品表"
                      + "（部品名 / 枠区分，部品コード 可缺），不生成 " + OutputFileName + "。");
                return 0;
            }

            string path = Path.Combine(outputDirectory, OutputFileName);

            DeleteLegacyOutput(outputDirectory, o);
            CsvFile.Write(path, OutHeader, output);

            o.Log("[INFO] 02_要領図_部品機能情報：" + tables + " 张表 → 共 " + output.Count + " 条明细，已保存：" + path);

            if (placeholders > 0)
            {
                o.Log("[INFO] 02_要領図_部品機能情報：另有 " + placeholders
                      + " 条明细因 部品コード 是「－」这类占位符（品番なし）未收。");
            }

            return output.Count;
        }

        /// <summary>
        /// 清掉上一次运行留下的旧文件名（改名前的 <c>LT_STEP2.csv</c>）。
        /// 删不掉（被 Excel 打开 / 无权限）只记 WARN，不影响本次输出。
        /// </summary>
        private static void DeleteLegacyOutput(string outputDirectory, DetectorOptions o)
        {
            RemoveIfExists(Path.Combine(outputDirectory, LegacyOutputFileName), "旧文件名残留 ", o);
        }

        /// <summary>
        /// 这张图不是要領図时，把上一次运行留下的同名产物清掉
        /// （上次可能判成要領図，留着会让下游按固定名字取到过期内容）。
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
                o.Log("[INFO] 02_要領図_部品機能情報：已删除" + note + path);
            }
            catch (IOException)
            {
                o.Log("[WARN] 02_要領図_部品機能情報：删不掉" + note + path + "（可能正被 Excel 打开）。");
            }
            catch (UnauthorizedAccessException)
            {
                o.Log("[WARN] 02_要領図_部品機能情報：删不掉" + note + path + "（无权限）。");
            }
        }

        // ── 品番占位符 ───────────────────────────────────────────────────────

        /// <summary>
        /// 这一格的 `部品コード` 是不是「品番なし」的占位符（<c>－</c> / <c>-</c> / <c>ー</c> / <c>—</c> …，
        /// 整格只由破折号类字符组成，半角 / 全角空格不计）。<c>TJ-3086</c> 这种真品番返回 false。
        /// </summary>
        private static bool IsPlaceholderCode(string code)
        {
            string s = CsvFile.Normalize(code);     // 去掉半角 / 全角空格与换行

            if (s.Length == 0)
                return false;                       // 空格子不算占位符（由调用方按「没有品番」处理）

            for (int i = 0; i < s.Length; i++)
            {
                if (Array.IndexOf(PlaceholderCodeChars, s[i]) < 0)
                    return false;
            }

            return true;
        }

        // ── 摊平 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 摊平一张表：找「列名重复」的表头行 → 按块拆出 (品種, 部品名, 枠区分, 部品コード, 員数)。
        /// 这种表没有「枠区分」列，该格固定留空。
        /// 品番格是空 / 只是 <c>－</c> 这类占位符的行不收（占位符的行数由 <paramref name="skipped"/> 带出，供日志用）。
        /// 不是「品種 + 多列重复」表 → 返回 null（调用方跳过，不动这张表）。
        /// </summary>
        /// <param name="rows">一张 <c>tables\table_NN.csv</c> 的全部行。</param>
        /// <param name="skipped">因 <c>部品コード</c> 是占位符（<c>－</c> 等）而没收的格数。</param>
        private static List<string[]> Expand(List<string[]> rows, out int skipped)
        {
            skipped = 0;

            int maxColumns = 0;

            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Length > maxColumns)
                    maxColumns = rows[r].Length;
            }

            if (maxColumns < 3)
                return null;

            // 1) 表头区里找「列名重复」的行 + 含「品種」的列
            int blockRow = -1;
            int varietyColumn = -1;
            string keyName = null;

            int scan = Math.Min(MaxHeaderScanRows, rows.Count);

            for (int r = 0; r < scan; r++)
            {
                if (!HasDuplicateName(rows[r]))
                    continue;

                int varietyColumnFound = FindVarietyColumn(rows, r);

                if (varietyColumnFound < 0)
                    continue;   // 列名是重复的，但没有品種列 → 不是这种表（例如型枠表的 A/B/C 員数列）

                blockRow = r;
                varietyColumn = varietyColumnFound;
                keyName = MostRepeatedName(rows[r]);
                break;
            }

            if (blockRow < 0 || keyName == null)
                return null;

            // 2) 重复列名出现的每一列 = 一组部品ブロック的起点（左 → 右）
            var starts = new List<int>();

            for (int c = 0; c < rows[blockRow].Length; c++)
            {
                if (CsvFile.Normalize(rows[blockRow][c]) == keyName)
                    starts.Add(c);
            }

            if (starts.Count < 2)
                return null;    // 只有一块 → 不是「多列重复」

            // 3) 每块：品名（块标题）+ 品番列 + 員数列
            var names = new List<string>();
            var codeColumns = new List<int>();
            var quantityColumns = new List<int>();

            for (int b = 0; b < starts.Count; b++)
            {
                int start = starts[b];
                int end = (b + 1 < starts.Count) ? starts[b + 1] - 1 : maxColumns - 1;

                names.Add(BlockName(rows, blockRow, start));

                int codeOffset = -1;
                int quantityOffset = -1;

                for (int c = start; c <= end && c < rows[blockRow].Length; c++)
                {
                    string child = CsvFile.Normalize(rows[blockRow][c]);

                    if (codeOffset < 0 && child.IndexOf(PartCodeKeyword, StringComparison.Ordinal) >= 0)
                        codeOffset = c - start;

                    if (quantityOffset < 0 && child.IndexOf(QuantityKeyword, StringComparison.Ordinal) >= 0)
                        quantityOffset = c - start;
                }

                // 块里没有「部品コード」列名时，退回块首列（很多表把品番直接写在标题行那一列下面）
                codeColumns.Add(start + (codeOffset >= 0 ? codeOffset : 0));
                quantityColumns.Add(quantityOffset >= 0 ? start + quantityOffset : -1);
            }

            // 4) 明细行：品種往下填满，每块有品番就出一条
            var detail = new List<string[]>();
            string variety = string.Empty;

            for (int r = blockRow + 1; r < rows.Count; r++)
            {
                string[] row = rows[r];

                if (CsvFile.IsEmptyRow(row))
                    continue;   // 合并单元格 / 表格线造成的空行

                string value = Cell(row, varietyColumn).Trim();
                string normalized = CsvFile.Normalize(value);

                // 合并单元格：品種只写在第一行，下面留空 → 沿用上一个（表头行本身不算）
                if (normalized.Length > 0
                    && normalized.IndexOf(VarietyKeyword, StringComparison.Ordinal) < 0)
                    variety = value;

                for (int b = 0; b < starts.Count; b++)
                {
                    string code = Cell(row, codeColumns[b]).Trim();
                    string codeNormalized = CsvFile.Normalize(code);

                    if (codeNormalized.Length == 0)
                        continue;   // 这块这一行没有品番

                    if (IsPlaceholderCode(code))
                    {
                        skipped++;  // 部品コード 是「－」这类占位符 → 这条明细不收
                        continue;
                    }

                    if (codeNormalized.IndexOf(PartCodeKeyword, StringComparison.Ordinal) >= 0)
                        continue;   // 表中间重复出现的表头行

                    string quantity = quantityColumns[b] >= 0
                        ? Cell(row, quantityColumns[b]).Trim()
                        : string.Empty;

                    // 品種表里没有「枠区分」列 → 该格固定留空（输出列数与単純部品表一致）
                    detail.Add(new string[] { variety, names[b], string.Empty, code, quantity });
                }
            }

            return detail;
        }

        // ── 単純部品表 ───────────────────────────────────────────────────────

        /// <summary>
        /// 摊平第二种形式：「単純部品表」—— 图纸上普通的部品表
        /// （表头 <c>番号 / 部品名 / 枠区分 / 員数 / 基本寸法 / 切断寸法 / 部品コード</c>，
        /// 没有品種列、也没有重复列名）。一行一条明细：<c>品種</c> 留空，
        /// <c>部品名 / 枠区分 / 部品コード / 員数</c> 都按列名搬。
        /// <c>部品コード</c> 列可缺（例如 <c>H453U10003.pdf</c> 只有
        /// <c>番号 / 部品名 / 枠区分 / 員数 / 基本寸法 / 切断寸法</c>）—— 缺这一列时明细里的
        /// <c>部品コード</c> 留空，仍然出明细（不再把整张表当「不是部品表」跳过）。
        /// 有 <c>部品コード</c> 列时，那一格是 <c>－</c> 这类占位符的行不收
        /// （图纸上写「－」= 这个部没有品番，这条明细不要）。
        /// 不是这种表 → 返回 null（调用方跳过，不动这张表）。
        /// </summary>
        /// <param name="rows">一张 <c>tables\table_NN.csv</c> 的全部行。</param>
        /// <param name="skipped">因 <c>部品コード</c> 是占位符（<c>－</c> 等）而没收的行数。</param>
        private static List<string[]> ExpandPartTable(List<string[]> rows, out int skipped)
        {
            skipped = 0;

            if (rows == null || rows.Count == 0)
                return null;

            int headerRow = -1;     // 表头起始行（含「部品名」「枠区分」那一行）
            int detailStart = -1;   // 明细从哪一行开始（表头可能占两行）
            int nameColumn = -1;
            int frameColumn = -1;
            int codeColumn = -1;
            int quantityColumn = -1;

            // 1) 前 6 行里找表头：必须有「部品名」+「枠区分」；「部品コード」可缺（缺了该格留空）。
            //    表头被拆成两行时（`番号 / 部品名 / 枠区分` 在上面一行、`部品コード / 員数` 落在紧跟的下一行）
            //    也认，这时明细从表头第二行之后开始。
            //    表題欄 / 変更履歴表 / 枠タイプ表不会同时出现「部品名」+「枠区分」，`A部` 那种空框也不会。
            int scan = Math.Min(MaxHeaderScanRows, rows.Count);

            for (int r = 0; r < scan; r++)
            {
                int name = FindColumn(rows[r], PartNameKeyword);
                int frame = FindColumn(rows[r], FrameKeyword);

                if (name < 0 || frame < 0)
                    continue;

                int code = FindColumn(rows[r], PartCodeKeyword);
                int quantity = FindColumn(rows[r], QuantityKeyword);
                int start = r + 1;

                // 表头拆成两行：只在下一行确实还写着这些列名时才算第二行表头
                // （不把明细行拿来当表头）
                if ((code < 0 || quantity < 0) && start < rows.Count)
                {
                    int nextCode = code < 0 ? FindColumn(rows[start], PartCodeKeyword) : -1;
                    int nextQuantity = quantity < 0 ? FindColumn(rows[start], QuantityKeyword) : -1;

                    if (nextCode >= 0 || nextQuantity >= 0)
                    {
                        if (nextCode >= 0)
                            code = nextCode;

                        if (nextQuantity >= 0)
                            quantity = nextQuantity;

                        start++;    // 第二行也算表头，明细从它的下一行开始
                    }
                }

                headerRow = r;
                detailStart = start;
                nameColumn = name;
                frameColumn = frame;
                codeColumn = code;          // -1 = 这张表没有「部品コード」列 → 该格留空
                quantityColumn = quantity;  // -1 = 没有「員数」列 → 该格留空

                break;
            }

            if (headerRow < 0)
                return null;

            // 2) 明细：一行一条；有「部品コード」列时品番为空 / 只有「－」这类占位符都跳过，
            //    没有这一列时用「部品名」判断
            var detail = new List<string[]>();

            for (int r = detailStart; r < rows.Count; r++)
            {
                string[] row = rows[r];

                if (CsvFile.IsEmptyRow(row))
                    continue;   // 合并单元格 / 表格线造成的空行

                string code = codeColumn >= 0 ? Cell(row, codeColumn).Trim() : string.Empty;
                string name = Cell(row, nameColumn).Trim();

                if (codeColumn >= 0)
                {
                    if (code.Length == 0)
                        continue;   // 这一行没有品番

                    if (IsPlaceholderCode(code))
                    {
                        skipped++;  // 部品コード 是「－」这类占位符 → 这条明细不收
                        continue;
                    }

                    if (CsvFile.Normalize(code).IndexOf(PartCodeKeyword, StringComparison.Ordinal) >= 0)
                        continue;   // 表中间重复出现的表头行
                }
                else
                {
                    // 这张表没有「部品コード」列 → 只用「部品名」判断这一行是不是明细
                    if (name.Length == 0)
                        continue;   // 这一行没有部品名

                    if (CsvFile.Normalize(name).IndexOf(PartNameKeyword, StringComparison.Ordinal) >= 0)
                        continue;   // 表中间重复出现的表头行（部品名 那一格）
                }

                string frameValue = frameColumn >= 0
                    ? Cell(row, frameColumn).Trim()
                    : string.Empty;
                string quantity = quantityColumn >= 0
                    ? Cell(row, quantityColumn).Trim()
                    : string.Empty;

                // 这种表没有品種列 → 品種留空；枠区分 按列名搬；没有部品コード 列时该格留空
                detail.Add(new string[] { string.Empty, name, frameValue, code, quantity });
            }

            return detail;
        }

        /// <summary>
        /// 在一行里找含 <paramref name="keyword"/> 的列（左 → 右），-1 表示这一行没有这一列。
        /// </summary>
        private static int FindColumn(string[] row, string keyword)
        {
            if (row == null)
                return -1;

            for (int c = 0; c < row.Length; c++)
            {
                if (CsvFile.Normalize(row[c]).IndexOf(keyword, StringComparison.Ordinal) >= 0)
                    return c;
            }

            return -1;
        }

        // ── 表头判定 ─────────────────────────────────────────────────────────

        /// <summary>这一行的列名里是否有重复（同一列名出现 ≥ 2 次）—— 多列重复表的标志。</summary>
        private static bool HasDuplicateName(string[] row)
        {
            if (row == null)
                return false;

            for (int c = 0; c < row.Length; c++)
            {
                string name = CsvFile.Normalize(row[c]);

                if (name.Length == 0)
                    continue;

                for (int d = c + 1; d < row.Length; d++)
                {
                    if (CsvFile.Normalize(row[d]) == name)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 在 <paramref name="blockRow"/> 及它上方这两三行里找含「品種」的列（表头常被拆成两行），
        /// -1 表示这张表没有品種列 → 不是这种表。
        /// </summary>
        private static int FindVarietyColumn(List<string[]> rows, int blockRow)
        {
            int last = Math.Min(blockRow, rows.Count - 1);

            for (int r = 0; r <= last; r++)
            {
                string[] row = rows[r];

                for (int c = 0; c < row.Length; c++)
                {
                    if (CsvFile.Normalize(row[c]).IndexOf(VarietyKeyword, StringComparison.Ordinal) >= 0)
                        return c;
                }
            }

            return -1;
        }

        /// <summary>
        /// 出现次数最多的列名（≥ 2 次）；次数相同时取最左边那个 —— 图纸上块的首列是品番
        /// （「部品コード」），右邻是「員数」，两者都重复 N 次，靠左优先正好选中品番列。
        /// </summary>
        private static string MostRepeatedName(string[] row)
        {
            string best = null;
            int bestCount = 0;

            for (int c = 0; c < row.Length; c++)
            {
                string name = CsvFile.Normalize(row[c]);

                if (name.Length == 0)
                    continue;

                int count = 0;

                for (int d = 0; d < row.Length; d++)
                {
                    if (CsvFile.Normalize(row[d]) == name)
                        count++;
                }

                if (count > bestCount)
                {
                    best = name;
                    bestCount = count;
                }
            }

            return bestCount >= 2 ? best : null;
        }

        // ── 块标题 / 单元格 ──────────────────────────────────────────────────

        /// <summary>
        /// 块的品名：从「列名重复行」往上找该块起始列最后一个非空单元格，去掉「部品名 ：」前缀。
        /// </summary>
        private static string BlockName(List<string[]> rows, int blockRow, int column)
        {
            for (int r = blockRow - 1; r >= 0; r--)
            {
                string text = Cell(rows[r], column).Trim();

                if (text.Length > 0)
                    return StripPrefix(text);
            }

            return string.Empty;
        }

        /// <summary>去掉「部品名 ：」「部品名：」这类前缀（全角 / 半角冒号都认）。</summary>
        private static string StripPrefix(string text)
        {
            string s = text.Trim();

            int colon = s.LastIndexOf('：');

            if (colon < 0)
                colon = s.LastIndexOf(':');

            if (colon >= 0)
            {
                string tail = s.Substring(colon + 1).Trim();

                if (tail.Length > 0)
                    return tail;
            }

            if (s.StartsWith(PartNameKeyword, StringComparison.Ordinal))
                s = s.Substring(PartNameKeyword.Length).Trim();

            return s;
        }

        /// <summary>取单元格（越界返回空串）。</summary>
        private static string Cell(string[] row, int column)
        {
            if (row == null || column < 0 || column >= row.Length || row[column] == null)
                return string.Empty;

            return row[column];
        }
    }
}
