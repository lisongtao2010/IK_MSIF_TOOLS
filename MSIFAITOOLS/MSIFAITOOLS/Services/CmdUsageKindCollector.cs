using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 拾い出し表（<c>hiroidashiData\*.xlsx</c>）的一条明细 —— 只保留第 6 步用到的两列：
    /// 第 4 列「部品ｺｰﾄﾞ/形材番号」与第 12 列「使い分け」。
    /// </summary>
    internal sealed class CmdUsageKindEntry
    {
        public CmdUsageKindEntry(string partCode, string usageKind, string fileName, int rowNumber)
        {
            PartCode = partCode ?? string.Empty;
            UsageKind = usageKind ?? string.Empty;
            FileName = fileName ?? string.Empty;
            RowNumber = rowNumber;
        }

        /// <summary>部品ｺｰﾄﾞ/形材番号（xlsx 第 4 列）。</summary>
        public string PartCode { get; private set; }

        /// <summary>使い分け（xlsx 第 12 列）。</summary>
        public string UsageKind { get; private set; }

        /// <summary>来源 xlsx 文件名。</summary>
        public string FileName { get; private set; }

        /// <summary>来源 xlsx 行号（从 1 开始，含表头行）。</summary>
        public int RowNumber { get; private set; }
    }

    /// <summary>
    /// 拾い出し表的索引：<c>部品ｺｰﾄﾞ → 使い分け（按表里的出现顺序）</c>。
    /// 同一部品コード在多个品種 / 多个文件里出现时，值全部按「文件名 → 行号」顺序收集，
    /// 第 6 步按这个顺序依次填到部品基本信息表的多行上。
    /// </summary>
    internal sealed class CmdUsageKindIndex
    {
        private readonly List<CmdUsageKindEntry> _entries = new List<CmdUsageKindEntry>();

        /// <summary>全部明细（顺序 = 文件按名字排序 → 行号顺序）。</summary>
        public IList<CmdUsageKindEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>读到的 xlsx 文件数。</summary>
        public int FileCount { get; set; }

        /// <summary>明细条数（部品コード为空的行不计）。</summary>
        public int EntryCount
        {
            get { return _entries.Count; }
        }

        public void Add(CmdUsageKindEntry entry)
        {
            if (entry != null && !string.IsNullOrWhiteSpace(entry.PartCode))
            {
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// 取该部品コード对应的「使い分け」值（按出现顺序，跳过空值）。
        /// 搜索时把 <c>□ / ■ / △ / ▲ / ○ / ●</c> 等符号当通配符（任一侧含符号即可，
        /// 因为数据库里的部品コード是具体值、xlsx 里是 <c>MEA□429</c> 这种带符号的写法）。
        /// </summary>
        /// <param name="partCode">部品基本信息表的「部品コード」。</param>
        public List<string> FindUsageKinds(string partCode)
        {
            List<string> values = new List<string>();

            if (string.IsNullOrWhiteSpace(partCode))
            {
                return values;
            }

            string key = partCode.Trim();

            foreach (CmdUsageKindEntry entry in _entries)
            {
                if (!CmdUsageKindCollector.PartCodeMatches(key, entry.PartCode))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.UsageKind))
                {
                    continue;
                }

                values.Add(entry.UsageKind);
            }

            return values;
        }
    }

    /// <summary>
    /// 【CMD 分析】第 6 步「使い分け自動設定」的数据源：<c>hiroidashiData</c> 文件夹里的
    /// 拾い出し表（<c>*.xlsx</c>）。
    ///
    /// <para>表结构（第 1 行是表头）：</para>
    /// <list type="bullet">
    ///   <item>第 4 列 = <c>部品ｺｰﾄﾞ/形材番号</c>（与部品基本信息表的「部品コード」比对）；</item>
    ///   <item>第 12 列 = <c>使い分け</c>（要填到部品基本信息表的值）；</item>
    ///   <item><c>データ区分 = X</c> 的行是「代表ｺｰﾄﾞ」标题行（第 4 列为空），自然会被跳过。</item>
    /// </list>
    ///
    /// <para>
    /// <b>部品コード 的比对</b>：拾い出し表里同一个部品会写成 <c>MEA□429</c>（□ = 可变的颜色 / 种别字符），
    /// 而部品基本信息表里是具体值 <c>MEACC429</c>，所以两边都要按「通配符」比对
    /// （<see cref="PartCodeMatches"/>），否则一条都对不上。
    /// </para>
    ///
    /// <para>
    /// <b>文件夹位置</b>（<see cref="ResolveDirectory"/>）：依次看
    /// ① App.config 的 <c>HiroidashiDataPath</c> →
    /// ② 环境变量 <c>MSIF_HIROIDASHI_DATA</c> →
    /// ③ 从分解输出目录（<c>&lt;PDF 文件夹&gt;\AiAppData</c>）逐级向上找 <c>hiroidashiData</c> /
    ///    <c>MSIF_InputData\hiroidashiData</c> →
    /// ④ 从程序目录逐级向上同样找。
    /// 找到后日志里会打印实际使用的目录，方便排查。
    /// </para>
    /// </summary>
    internal static class CmdUsageKindCollector
    {
        /// <summary>拾い出し表所在的文件夹名。</summary>
        public const string FolderName = "hiroidashiData";

        /// <summary>上游数据根目录名（<c>MSIF_InputData\hiroidashiData</c> 这种布局）。</summary>
        public const string InputDataFolderName = "MSIF_InputData";

        /// <summary>App.config 里可覆盖文件夹位置的键。</summary>
        public const string SettingKey = "HiroidashiDataPath";

        /// <summary>可覆盖文件夹位置的环境变量。</summary>
        public const string EnvironmentVariable = "MSIF_HIROIDASHI_DATA";

        /// <summary>部品ｺｰﾄﾞ/形材番号 所在的列（第 4 列，从 0 开始 = 3）。</summary>
        public const int PartCodeColumnIndex = 3;

        /// <summary>使い分け 所在的列（第 12 列，从 0 开始 = 11）。</summary>
        public const int UsageKindColumnIndex = 11;

        /// <summary>表头文字（用来跳过表头行）。</summary>
        public const string PartCodeHeaderText = "部品ｺｰﾄﾞ/形材番号";

        /// <summary>表头文字（用来跳过表头行）。</summary>
        public const string UsageKindHeaderText = "使い分け";

        /// <summary>向上查找目录时的最大层数（避免异常路径下一直往上找；16 层足够到盘符根）。</summary>
        private const int MaxSearchDepth = 16;

        /// <summary>当成通配符处理的符号（拾い出し表里的可变字符）。</summary>
        private static readonly char[] WildcardChars = new char[] { '□', '■', '△', '▲', '○', '●', '◇', '◆' };

        /// <summary>部品コード 比对用的正则缓存（pattern → Regex）。</summary>
        private static readonly Dictionary<string, Regex> PatternCache = new Dictionary<string, Regex>();

        /// <summary>
        /// 解析 <c>hiroidashiData</c> 文件夹的位置；找不到时返回空字符串，
        /// 尝试过的位置写入 <paramref name="notes"/>（日志里提示用）。
        /// </summary>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="notes">提示信息（可为 <c>null</c>）。</param>
        public static string ResolveDirectory(string outputDirectory, IList<string> notes)
        {
            string configured = ReadConfiguredDirectory();

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
                "找不到 {0} 文件夹（已看：App.config 的 {1}、环境变量 {2}、分解输出目录与程序目录的上级）。",
                FolderName, SettingKey, EnvironmentVariable));

            return string.Empty;
        }

        /// <summary>读 App.config / 环境变量里的文件夹位置（相对路径按程序目录解析）。</summary>
        private static string ReadConfiguredDirectory()
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

        /// <summary>把候选路径（绝对 / 相对程序目录）变成存在的绝对路径；不存在返回空。</summary>
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

                return Directory.Exists(path) ? path : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>从 <paramref name="startDirectory"/> 起逐级向上找 <c>hiroidashiData</c>。</summary>
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
                string direct = ResolveCandidate(Path.Combine(current, FolderName));

                if (!string.IsNullOrEmpty(direct))
                {
                    return direct;
                }

                string nested = ResolveCandidate(Path.Combine(current, InputDataFolderName, FolderName));

                if (!string.IsNullOrEmpty(nested))
                {
                    return nested;
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

        private static void AddNote(IList<string> notes, string message)
        {
            if (notes != null && !string.IsNullOrWhiteSpace(message))
            {
                notes.Add(message);
            }
        }

        /// <summary>
        /// 读 <paramref name="directory"/> 下的全部 <c>*.xlsx</c>（文件名排序），建立索引。
        /// 读不了的文件只记提示，不影响其它文件。
        /// </summary>
        /// <param name="directory">拾い出し表所在文件夹。</param>
        /// <param name="notes">提示信息（可为 <c>null</c>）。</param>
        public static CmdUsageKindIndex LoadIndex(string directory, IList<string> notes)
        {
            CmdUsageKindIndex index = new CmdUsageKindIndex();

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                AddNote(notes, "拾い出し表文件夹不存在：" + directory);
                return index;
            }

            string[] files;

            try
            {
                files = Directory.GetFiles(directory, "*.xlsx", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                AddNote(notes, "读取文件夹失败（" + directory + "）：" + ex.Message);
                return index;
            }

            // 文件名排序，保证每次执行「按顺序设置」的结果一样
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);

                // Excel 打开时生成的临时文件
                if (fileName.StartsWith("~$", StringComparison.Ordinal))
                {
                    continue;
                }

                List<string> fileNotes = new List<string>();
                List<string[]> rows = XlsxSheetReader.ReadFirstSheet(file, fileNotes);

                foreach (string note in fileNotes)
                {
                    AddNote(notes, note);
                }

                index.FileCount++;

                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    string[] cells = rows[rowIndex];
                    string partCode = CellText(cells, PartCodeColumnIndex);

                    if (IsHeaderRow(cells) || string.IsNullOrWhiteSpace(partCode))
                    {
                        continue;
                    }

                    index.Add(new CmdUsageKindEntry(partCode, CellText(cells, UsageKindColumnIndex),
                        fileName, rowIndex + 1));
                }
            }

            return index;
        }

        /// <summary>表头行判定（第 4 列 / 第 12 列出现表头文字）。</summary>
        private static bool IsHeaderRow(string[] cells)
        {
            return string.Equals(CellText(cells, PartCodeColumnIndex), PartCodeHeaderText,
                       StringComparison.Ordinal) ||
                   string.Equals(CellText(cells, UsageKindColumnIndex), UsageKindHeaderText,
                       StringComparison.Ordinal);
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

        /// <summary>
        /// 两个部品コード是否指同一个部品：完全相同就算命中；否则只要任一侧带通配符
        /// （<c>□ / △ / ○ …</c>）就按通配符比对（<c>MEA□429</c> 与 <c>MEACC429</c> 视为相同）。
        /// </summary>
        /// <param name="left">一侧的部品コード（通常是部品基本信息表里的具体值）。</param>
        /// <param name="right">另一侧的部品コード（通常是拾い出し表里的写法）。</param>
        public static bool PartCodeMatches(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            string first = left.Trim();
            string second = right.Trim();

            if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return MatchWildcard(first, second) || MatchWildcard(second, first);
        }

        /// <summary>把 <paramref name="pattern"/> 里的通配符换成 <c>.*</c> 后匹配 <paramref name="value"/>。</summary>
        private static bool MatchWildcard(string pattern, string value)
        {
            if (!ContainsWildcard(pattern))
            {
                return false;
            }

            // 通配符以外的字符太少时（例如只有「□」）不管内容都会命中，不算匹配
            if (CountLiteralChars(pattern) < 2)
            {
                return false;
            }

            Regex regex;

            if (!PatternCache.TryGetValue(pattern, out regex))
            {
                StringBuilder builder = new StringBuilder("^");

                foreach (char ch in pattern)
                {
                    if (IsWildcard(ch))
                    {
                        builder.Append(".*");
                    }
                    else
                    {
                        builder.Append(Regex.Escape(ch.ToString()));
                    }
                }

                builder.Append('$');

                regex = new Regex(builder.ToString(),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                PatternCache[pattern] = regex;
            }

            return regex.IsMatch(value);
        }

        private static bool ContainsWildcard(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (char ch in value)
            {
                if (IsWildcard(ch))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountLiteralChars(string value)
        {
            int count = 0;

            if (string.IsNullOrEmpty(value))
            {
                return count;
            }

            foreach (char ch in value)
            {
                if (!IsWildcard(ch))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsWildcard(char ch)
        {
            foreach (char wildcard in WildcardChars)
            {
                if (ch == wildcard)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
