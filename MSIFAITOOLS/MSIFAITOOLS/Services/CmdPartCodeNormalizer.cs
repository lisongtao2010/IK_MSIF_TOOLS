using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 部品マスタ（<c>部品マスタ\Tbl_部品マスタ.csv</c>）的一条明细 —— 保留用到的四列：
    /// 部品コード / 部品名（画面上「部品コード 为空」时按部品名 反查用）/ 材質 / 色。
    /// </summary>
    internal sealed class CmdPartMasterEntry
    {
        /// <summary>
        /// 创建一条部品マスタ 明细。
        /// </summary>
        /// <param name="partCode">部品コード（第 1 列）。</param>
        /// <param name="partName">部品名（第 2 列；画面上「部品コード 为空 → 按部品名 推定」用）。</param>
        /// <param name="material">材質（第 3 列）。</param>
        /// <param name="color">色（第 4 列）。</param>
        /// <param name="fileName">来源 CSV 文件名。</param>
        /// <param name="rowNumber">来源 CSV 行号。</param>
        public CmdPartMasterEntry(string partCode, string partName, string material, string color,
            string fileName, int rowNumber)
        {
            PartCode = partCode ?? string.Empty;
            PartName = partName ?? string.Empty;
            Material = material ?? string.Empty;
            Color = color ?? string.Empty;
            FileName = fileName ?? string.Empty;
            RowNumber = rowNumber;
        }

        /// <summary>旧签名（不带部品名）：保持兼容，部品名为空。</summary>
        public CmdPartMasterEntry(string partCode, string material, string color, string fileName, int rowNumber)
            : this(partCode, string.Empty, material, color, fileName, rowNumber)
        {
        }

        /// <summary>部品コード（CSV 第 1 列）。</summary>
        public string PartCode { get; private set; }

        /// <summary>
        /// 部品名（CSV 第 2 列）。同一个部品名 可能对应多个部品コード（材質 / 色 不同），
        /// 画面上「部品コード 为空」时按这一列查找（<see cref="CmdPartMasterIndex.FindByPartName"/>）。
        /// </summary>
        public string PartName { get; private set; }

        /// <summary>材質（CSV 第 3 列）—— 用来定位「色」在部品コード里的位置（例：<c>MEA</c>）。</summary>
        public string Material { get; private set; }

        /// <summary>色（CSV 第 4 列）—— 换成一个 <c>□</c> 的字符（例：<c>CC</c> / <c>S</c>）。</summary>
        public string Color { get; private set; }

        /// <summary>来源 CSV 文件名。</summary>
        public string FileName { get; private set; }

        /// <summary>来源 CSV 行号（从 1 开始，含表头行）。</summary>
        public int RowNumber { get; private set; }
    }

    /// <summary>
    /// 部品マスタ的索引：<c>部品コード → 材質 / 色</c>（同一部品コード 重复时取第一次出现的行）。
    /// </summary>
    internal sealed class CmdPartMasterIndex
    {
        private readonly Dictionary<string, CmdPartMasterEntry> _entries =
            new Dictionary<string, CmdPartMasterEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 部品名（归一化后）→ 该部品名 对应的全部明细（1:N）。
        /// 一个部品名 可能对应多个部品コード（材質 / 色 不同，例：<c>ﾄﾞｱﾁﾖｳﾊﾞﾝｾﾂﾄL(CC)</c> →
        /// <c>BBD0001LMATM</c> / <c>BBD0001LMHGT</c>），所以这里存列表，不是单个值。
        /// 归一是 <see cref="MemberPartCatalogLoader.NormalizeKey"/>（半角カナ / 全角英数 / 空白 / 大小写），
        /// 与「部材部品」的比对用同一套规则。
        /// </summary>
        private readonly Dictionary<string, List<CmdPartMasterEntry>> _entriesByPartName =
            new Dictionary<string, List<CmdPartMasterEntry>>(StringComparer.Ordinal);

        /// <summary>读到的明细条数（部品コード为空的行不计）。</summary>
        public int EntryCount
        {
            get { return _entries.Count; }
        }

        /// <summary>重复的部品コード 条数（保留第一行，其余只计数）。</summary>
        public int DuplicateCount { get; set; }

        /// <summary>加入一条明细；部品コード 为空 / 已存在时忽略。</summary>
        public void Add(CmdPartMasterEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.PartCode))
            {
                return;
            }

            if (_entries.ContainsKey(entry.PartCode))
            {
                DuplicateCount++;
                return;
            }

            _entries[entry.PartCode.Trim()] = entry;

            // 部品名 → 部品コード（画面上「部品コード 为空 → 按部品名 推定」用）
            string partNameKey = MemberPartCatalogLoader.NormalizeKey(entry.PartName);

            if (partNameKey.Length == 0)
            {
                return;
            }

            List<CmdPartMasterEntry> byName;

            if (!_entriesByPartName.TryGetValue(partNameKey, out byName))
            {
                byName = new List<CmdPartMasterEntry>();
                _entriesByPartName.Add(partNameKey, byName);
            }

            byName.Add(entry);
        }

        /// <summary>按部品コード 取明细（找不到返回 <c>false</c>）。</summary>
        public bool TryGet(string partCode, out CmdPartMasterEntry entry)
        {
            entry = null;

            if (string.IsNullOrWhiteSpace(partCode))
            {
                return false;
            }

            return _entries.TryGetValue(partCode.Trim(), out entry);
        }

        /// <summary>
        /// 按部品名 查明细（归一化比较：半角カナ / 全角英数 / 空白 / 大小写）。
        /// 同一个部品名 对应多个部品コード 时<b>全部返回</b>（调用方自己决定要不要采用）；
        /// 找不到 / 部品名为空时返回空列表。
        /// </summary>
        /// <param name="partName">画面上的「部品名」。</param>
        public IList<CmdPartMasterEntry> FindByPartName(string partName)
        {
            List<CmdPartMasterEntry> found = new List<CmdPartMasterEntry>();
            string partNameKey = MemberPartCatalogLoader.NormalizeKey(partName);

            if (partNameKey.Length == 0)
            {
                return found;
            }

            List<CmdPartMasterEntry> byName;

            if (_entriesByPartName.TryGetValue(partNameKey, out byName))
            {
                found.AddRange(byName);
            }

            return found;
        }
    }

    /// <summary>部品コード 的改写方式（用于统计 / 日志）。</summary>
    internal enum PartCodeChangeKind
    {
        /// <summary>没变。</summary>
        None,

        /// <summary>只把「色」换成了 <c>□</c>。</summary>
        Color,

        /// <summary>只把连续的符号（<c>□□</c> / <c>〇〇</c> / <c>△△</c> …）压成了一个 <c>□</c>。</summary>
        Symbol,

        /// <summary>既换了「色」、又压了连续符号。</summary>
        ColorAndSymbol
    }

    /// <summary>
    /// 【CMD 分析】第 5 步「部品コード正規化」的数据源与改写规则：
    /// <c>部品マスタ\Tbl_部品マスタ.csv</c>。
    ///
    /// <para>CSV 结构（第 1 行是表头，逗号分隔，UTF-8）：</para>
    /// <list type="bullet">
    ///   <item>第 1 列 = <c>部品コード</c>（与部品基本信息表的「部品コード」比对）；</item>
    ///   <item>第 2 列 = <c>部品名</c>（部品基本信息画面「部品コード 为空 → 按部品名 推定」用，
    ///     <see cref="CmdPartMasterIndex.FindByPartName"/>）；</item>
    ///   <item>第 3 列 = <c>材質</c>（定位「色」在部品コード里的位置，例：<c>MEA</c>）；</item>
    ///   <item>第 4 列 = <c>色</c>（例：<c>CC</c> / <c>S</c>）。</item>
    /// </list>
    ///
    /// <para>
    /// <b>改写规则 1（按部品マスタ 的「色」）</b>：部品コード 能在部品マスタ 里找到、且「色」出现在
    /// 「材質」之后时，把这一段换成 <c>□</c>（<c>MEACC429</c> → <c>MEA□429</c>）。
    /// 「色」只出现在「材質」里面时<b>不换</b> —— 例如 <c>LAVS639</c>（材質 <c>LAVS</c> / 色 <c>S</c>）里的 <c>S</c>
    /// 是材質的一部分，换掉就成了别的部品。
    /// </para>
    ///
    /// <para>
    /// <b>改写规则 2（连续符号压缩）</b>：部品コード 里连着出现 2 个以上的符号
    /// （<c>□ ■ △ ▲ ○ ● ◇ ◆ 〇</c>）时，压成一个 <c>□</c>（<c>MEA□□405</c> → <c>MEA□405</c>、
    /// <c>MZB○○963</c> → <c>MZB□963</c>）。单个符号（<c>MZA□968</c>）保持原样。
    /// </para>
    ///
    /// <para>
    /// <b>文件位置</b>（<see cref="ResolveDataSource"/>），找不到才报错：
    /// ① App.config 的 <c>PartMasterDataPath</c> →
    /// ② 环境变量 <c>MSIF_PARTS_MASTER</c> →
    /// ③ 从分解输出目录（<c>&lt;PDF 文件夹&gt;\AiAppData</c>）逐级向上找 <c>部品マスタ</c> 文件夹 →
    /// ④ 从程序目录逐级向上找（开发时就在项目目录里）。
    /// 文件夹里优先用 <c>Tbl_部品マスタ.csv</c>，没有时用最新的 <c>Tbl_部品マスタ*.csv</c>（带日期的导出文件）。
    /// </para>
    /// </summary>
    internal static class CmdPartCodeNormalizer
    {
        /// <summary>部品マスタ 所在的条件夹名。</summary>
        public const string FolderName = "部品マスタ";

        /// <summary>部品マスタ 文件名。</summary>
        public const string FileName = "Tbl_部品マスタ.csv";

        /// <summary>带日期的导出文件名（<c>Tbl_部品マスタ_20260619.csv</c> 这种），精确名找不到时用最新的一个。</summary>
        public const string FileSearchPattern = "Tbl_部品マスタ*.csv";

        /// <summary>App.config 里可配置部品マスタ 位置（文件夹或文件）的键。</summary>
        public const string SettingKey = "PartMasterDataPath";

        /// <summary>可配置部品マスタ 位置的环境变量。</summary>
        public const string EnvironmentVariable = "MSIF_PARTS_MASTER";

        /// <summary>「部品コード」所在列（第 1 列，从 0 数起 = 0）。</summary>
        public const int PartCodeColumnIndex = 0;

        /// <summary>「部品名」所在列（第 2 列，从 0 数起 = 1）—— 画面上「部品コード 为空 → 按部品名 推定」用。</summary>
        public const int PartNameColumnIndex = 1;

        /// <summary>「材質」所在列（第 3 列，从 0 数起 = 2）。</summary>
        public const int MaterialColumnIndex = 2;

        /// <summary>「色」所在列（第 4 列，从 0 数起 = 3）。</summary>
        public const int ColorColumnIndex = 3;

        /// <summary>表头文字（用它识别表头行）。</summary>
        public const string PartCodeHeaderText = "部品コード";

        /// <summary>替换用的符号（全角方框）。</summary>
        public const char PlaceholderChar = '\u25A1';

        /// <summary>认作「符号」的字符（与第 6 步部品コード 比对用的通配符同一套，外加 <c>〇</c>）。</summary>
        public const string SymbolChars = "□■△▲○●◇◆〇";

        /// <summary>同上级目录搜索的最大层数（一般 2 层就命中）。</summary>
        private const int MaxSearchDepth = 16;

        /// <summary>
        /// 解析 <c>Tbl_部品マスタ.csv</c> 的完整路径；找不到返回空字符串，并在 <paramref name="notes"/> 里写明原因。
        /// </summary>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="notes">提示信息（可为 <c>null</c>）。</param>
        public static string ResolveDataSource(string outputDirectory, IList<string> notes)
        {
            string configured = ReadConfiguredPath();

            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            string fromOutput = FindUpwards(outputDirectory);

            if (!string.IsNullOrEmpty(fromOutput))
            {
                return fromOutput;
            }

            string fromProgram = FindUpwards(AppDomain.CurrentDomain.BaseDirectory);

            if (!string.IsNullOrEmpty(fromProgram))
            {
                return fromProgram;
            }

            AddNote(notes, string.Format(
                "找不到 {0}\\{1}（已试：App.config 的 {2}、环境变量 {3}、从分解输出目录和程序目录向上查找 {4} 文件夹）",
                FolderName, FileName, SettingKey, EnvironmentVariable, FolderName));

            return string.Empty;
        }

        /// <summary>读 App.config / 环境变量里的部品マスタ 位置（可写文件夹，也可直接写文件）。</summary>
        private static string ReadConfiguredPath()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            string resolved = ResolveCandidate(fromEnvironment);

            if (!string.IsNullOrEmpty(resolved))
            {
                return resolved;
            }

            string fromConfiguration;

            try
            {
                fromConfiguration = ConfigurationManager.AppSettings[SettingKey];
            }
            catch (ConfigurationErrorsException)
            {
                fromConfiguration = null;
            }

            return ResolveCandidate(fromConfiguration);
        }

        /// <summary>
        /// 把配置值换成实际文件：路径 / 相对路径（相对程序目录）都可以；
        /// 给的是文件夹时，在里面找 <see cref="FileName"/>，没有就用最新的 <see cref="FileSearchPattern"/>。
        /// </summary>
        private static string ResolveCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            string path = candidate.Trim();

            try
            {
                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                }

                path = Path.GetFullPath(path);

                if (File.Exists(path))
                {
                    return path;
                }

                return Directory.Exists(path) ? FindFileIn(path) : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>在文件夹里找部品マスタ CSV：先精确名，再取最新的带日期文件。</summary>
        private static string FindFileIn(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return string.Empty;
            }

            string exact = Path.Combine(directory, FileName);

            if (File.Exists(exact))
            {
                return exact;
            }

            try
            {
                string[] files = Directory.GetFiles(directory, FileSearchPattern, SearchOption.TopDirectoryOnly);

                if (files.Length == 0)
                {
                    return string.Empty;
                }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                return files[files.Length - 1];
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 从 <paramref name="startDirectory"/> 起逐级向上找 <c>部品マスタ</c> 文件夹
        /// （也认 <c>MSIF_InputData\部品マスタ</c>，以及直接把 CSV 放在根目录的情况）。
        /// </summary>
        private static string FindUpwards(string startDirectory)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                return string.Empty;
            }

            string current;

            try
            {
                current = Path.GetFullPath(startDirectory.Trim());
            }
            catch (Exception)
            {
                return string.Empty;
            }

            for (int depth = 0; depth <= MaxSearchDepth && !string.IsNullOrEmpty(current); depth++)
            {
                string direct = FindFileIn(Path.Combine(current, FolderName));

                if (!string.IsNullOrEmpty(direct))
                {
                    return direct;
                }

                string nested = FindFileIn(Path.Combine(
                    Path.Combine(current, CmdUsageKindCollector.InputDataFolderName), FolderName));

                if (!string.IsNullOrEmpty(nested))
                {
                    return nested;
                }

                // 有的环境把 CSV 直接放在输入根目录下（D:\MSIF_InputData\Tbl_部品マスタ_20260619.csv）
                string plain = FindFileIn(current);

                if (!string.IsNullOrEmpty(plain))
                {
                    return plain;
                }

                try
                {
                    DirectoryInfo parent = Directory.GetParent(current);
                    current = parent == null ? string.Empty : parent.FullName;
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 读 <paramref name="filePath"/>（部品マスタ CSV），建立「部品コード → 材質 / 色」的索引。
        /// 读不了只记提示，返回空索引（调用方据此跳过第 5 步）。
        /// </summary>
        /// <param name="filePath">Tbl_部品マスタ.csv 的完整路径。</param>
        /// <param name="notes">提示信息（可为 <c>null</c>）。</param>
        public static CmdPartMasterIndex LoadIndex(string filePath, IList<string> notes)
        {
            CmdPartMasterIndex index = new CmdPartMasterIndex();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                AddNote(notes, "部品マスタ CSV 不存在：" + filePath);
                return index;
            }

            string[] lines;

            try
            {
                lines = File.ReadAllLines(filePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AddNote(notes, "读取部品マスタ CSV 失败（" + filePath + "）：" + ex.Message);
                return index;
            }

            string fileName = Path.GetFileName(filePath);

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] cells = SplitCsvLine(line);
                string partCode = CellText(cells, PartCodeColumnIndex);

                // 表头行（第 1 列是「部品コード」）
                if (string.Equals(partCode, PartCodeHeaderText, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(partCode))
                {
                    continue;
                }

                index.Add(new CmdPartMasterEntry(partCode, CellText(cells, PartNameColumnIndex),
                    CellText(cells, MaterialColumnIndex), CellText(cells, ColorColumnIndex),
                    fileName, lineIndex + 1));
            }

            return index;
        }

        /// <summary>
        /// 第 5 步的改写：先按部品マスタ 的「色」把颜色段换成 <c>□</c>，再把连续的符号压成一个 <c>□</c>。
        /// </summary>
        /// <param name="partCode">部品基本信息表里的部品コード。</param>
        /// <param name="index">部品マスタ 索引（可为 <c>null</c>，此时只做符号压缩）。</param>
        /// <param name="kind">改写方式（没变 / 只换色 / 只压符号 / 两者都有）。</param>
        /// <returns>改写后的部品コード（没有可改的地方时原样返回）。</returns>
        public static string Normalize(string partCode, CmdPartMasterIndex index, out PartCodeChangeKind kind)
        {
            kind = PartCodeChangeKind.None;

            if (string.IsNullOrWhiteSpace(partCode))
            {
                return partCode ?? string.Empty;
            }

            string result = partCode.Trim();

            if (index != null)
            {
                result = ReplaceColor(result, index, ref kind);
            }

            string collapsed = CollapseSymbols(result);

            if (!string.Equals(collapsed, result, StringComparison.Ordinal))
            {
                result = collapsed;

                kind = kind == PartCodeChangeKind.Color
                    ? PartCodeChangeKind.ColorAndSymbol
                    : PartCodeChangeKind.Symbol;
            }

            return result;
        }

        /// <summary>
        /// 按部品マスタ 的「色」把颜色段换成 <c>□</c>。
        /// 部品コード 以「材質」开头时从材質 之后开始找（避免把材質 里的同名字符换掉，例如 <c>LAVS639</c>）；
        /// 不以材質 开头时从第 1 个字符开始找。只换第一处。
        /// </summary>
        private static string ReplaceColor(string code, CmdPartMasterIndex index, ref PartCodeChangeKind kind)
        {
            CmdPartMasterEntry entry;

            if (!index.TryGet(code, out entry) || string.IsNullOrEmpty(entry.Color))
            {
                return code;
            }

            int start = 0;

            if (!string.IsNullOrEmpty(entry.Material) &&
                code.StartsWith(entry.Material, StringComparison.OrdinalIgnoreCase))
            {
                start = entry.Material.Length;
            }

            if (start > code.Length)
            {
                return code;
            }

            int position = code.IndexOf(entry.Color, start, StringComparison.OrdinalIgnoreCase);

            if (position < 0)
            {
                return code;
            }

            kind = PartCodeChangeKind.Color;

            return code.Substring(0, position) + PlaceholderChar +
                code.Substring(position + entry.Color.Length);
        }

        /// <summary>
        /// 把连续 2 个以上的符号压成一个 <c>□</c>；单个符号原样保留。
        /// 例：<c>MEA□□405</c> → <c>MEA□405</c>、<c>MZB○○963</c> → <c>MZB□963</c>、<c>MZA□968</c> 不变。
        /// </summary>
        public static string CollapseSymbols(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            bool inSymbolRun = false;

            foreach (char ch in value)
            {
                if (IsSymbol(ch))
                {
                    // 连续符号只在第一处放一个 □，后面的都丢掉
                    if (!inSymbolRun)
                    {
                        builder.Append(PlaceholderChar);
                        inSymbolRun = true;
                    }

                    continue;
                }

                builder.Append(ch);
                inSymbolRun = false;
            }

            return builder.ToString();
        }

        private static bool IsSymbol(char ch)
        {
            return SymbolChars.IndexOf(ch) >= 0;
        }

        /// <summary>取一行里第 <paramref name="index"/> 列的文本（缺列返回空字符串）。</summary>
        private static string CellText(string[] cells, int index)
        {
            if (cells == null || index < 0 || index >= cells.Length || cells[index] == null)
            {
                return string.Empty;
            }

            return cells[index].Trim();
        }

        /// <summary>逗号分隔的解析（支持用双引号包住含逗号的字段，<c>""</c> 表示一个引号）。</summary>
        private static string[] SplitCsvLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return new string[0];
            }

            List<string> cells = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool quoted = false;

            for (int position = 0; position < line.Length; position++)
            {
                char ch = line[position];

                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (position + 1 < line.Length && line[position + 1] == '"')
                        {
                            cell.Append('"');
                            position++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        cell.Append(ch);
                    }

                    continue;
                }

                if (ch == '"')
                {
                    quoted = true;
                }
                else if (ch == ',')
                {
                    cells.Add(cell.ToString());
                    cell.Length = 0;
                }
                else
                {
                    cell.Append(ch);
                }
            }

            cells.Add(cell.ToString());

            return cells.ToArray();
        }

        private static void AddNote(IList<string> notes, string message)
        {
            if (notes != null && !string.IsNullOrWhiteSpace(message))
            {
                notes.Add(message);
            }
        }
    }
}
