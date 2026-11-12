using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 部材部品マスタ（<c>部材部品\部材部品.csv</c>）的一条明细：<b>部材名 → 部品名</b>。
    /// </summary>
    internal sealed class MemberPartEntry
    {
        public MemberPartEntry(string memberName, string partName, string fileName, int rowNumber)
        {
            MemberName = memberName ?? string.Empty;
            PartName = partName ?? string.Empty;
            FileName = fileName ?? string.Empty;
            RowNumber = rowNumber;
        }

        /// <summary>部材名（CSV 第 1 列，例：<c>上ﾚｰﾙ</c> / <c>右縦枠</c> / <c>その他</c>）。</summary>
        public string MemberName { get; private set; }

        /// <summary>部品名（CSV 第 2 列，例：<c>ｼﾞｮｲﾝﾄ金具</c> / <c>取説</c>）。</summary>
        public string PartName { get; private set; }

        /// <summary>来源文件名：从 CSV 导入时是 CSV 文件名，从数据库读时是数据库文件名。</summary>
        public string FileName { get; private set; }

        /// <summary>来源行号：CSV 时是从 1 开始的行号（含表头行），数据库时是「部材部品」表的 <c>id</c>。</summary>
        public int RowNumber { get; private set; }
    }

    /// <summary>
    /// 部材部品マスタ的索引（数据来自「部材部品」表 / 首次导入的 CSV）：
    /// <list type="bullet">
    ///   <item><b>部材名 一览</b>（第 1 列，去重、保持出现顺序）—— AI 可以回答的部材名；</item>
    ///   <item><b>部品名 → 部材名</b>（第 2 列）—— 图上只写部品名时，用它换成该部品所属的部材。</item>
    ///   <item><b>部品名 → 全部の部材名</b>（1:N，<see cref="GetMemberNamesForPart"/>）——
    ///     同じ部品名が 2 行以上ある場合（<c>鬼目ﾅｯﾄ(ﾌﾞﾚｰｷ取付用)</c> → <c>上ﾚｰﾙ</c> / <c>上枠</c>）、
    ///     AI に見せる候補一覧。プログラムの兜底はこのうち<b>最初の 1 件</b>を使う。</item>
    /// </list>
    /// 比较都走 <see cref="MemberPartCatalogLoader.NormalizeKey"/>（半角カナ / 全角英数 / 空白 / 大小写全部归一化），
    /// 所以 CSV 里写成 <c>上ﾚｰﾙ</c>、图上写 <c>上レール</c> 也能对上。
    /// </summary>
    internal sealed class MemberPartCatalog
    {
        private readonly List<MemberPartEntry> _entries = new List<MemberPartEntry>();

        private readonly List<string> _memberNames = new List<string>();

        private readonly Dictionary<string, string> _memberByKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _memberByPartKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>部品名 → 所属する<b>全部</b>の部材名（1:N。CSV で同じ部品名が 2 行以上ある場合）。</summary>
        private readonly Dictionary<string, List<string>> _memberNamesByPartKey =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<string>> _partNamesByMember =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>
        /// 读到的数据库路径（数据库打不开、本次退回 CSV 时是 CSV 路径；都没读到时为空串）。
        /// </summary>
        public string Path { get; set; }

        /// <summary>部材名 的件数（去重后）。</summary>
        public int MemberNameCount
        {
            get { return _memberNames.Count; }
        }

        /// <summary>部品名 的件数（去重后）。</summary>
        public int PartNameCount
        {
            get { return _memberByPartKey.Count; }
        }

        /// <summary>部品名 在 CSV 里重复出现的次数（保留第一次出现的行）。</summary>
        public int DuplicatePartNameCount { get; private set; }

        /// <summary>是否什么都没读到（文件不存在 / 全是空行）。此时 AI 只能从图上文字里选。</summary>
        public bool IsEmpty
        {
            get { return _memberNames.Count == 0; }
        }

        /// <summary>CSV 明细（按文件顺序）。</summary>
        public IList<MemberPartEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>部材名 一览（CSV 第 1 列，去重、出现顺序；含「その他」）。</summary>
        public IList<string> MemberNames
        {
            get { return _memberNames; }
        }
        /// <summary>加入一条明细：部材名为空时忽略；部材名 去重（第一次出现的写法为准）；部品名 重复时保留第一条。</summary>
        public void Add(MemberPartEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.MemberName))
            {
                return;
            }

            _entries.Add(entry);

            string memberKey = MemberPartCatalogLoader.NormalizeKey(entry.MemberName);

            if (memberKey.Length == 0)
            {
                return;
            }

            if (!_memberByKey.ContainsKey(memberKey))
            {
                string memberName = entry.MemberName.Trim();

                _memberByKey.Add(memberKey, memberName);
                _memberNames.Add(memberName);
                _partNamesByMember.Add(memberKey, new List<string>());
            }

            if (string.IsNullOrWhiteSpace(entry.PartName))
            {
                return;
            }

            string partKey = MemberPartCatalogLoader.NormalizeKey(entry.PartName);

            if (partKey.Length == 0)
            {
                return;
            }

            List<string> partNames = _partNamesByMember[memberKey];
            string partName = entry.PartName.Trim();

            // 表里每个部材都列出自己的部品（ｼﾞｮｲﾝﾄ金具 同时属于上枠和下枠 → 两边都列），
            // 这样 AI 能按坐标位置自己判断该算哪个部材
            if (!ContainsWord(partNames, partKey))
            {
                partNames.Add(partName);
            }

            if (_memberByPartKey.ContainsKey(partKey))
            {
                // 同一个部品名 出现多次（CSV 里「部品ｾｯﾄ」有很多行 / 一个部品挂在两个部材下）：
                // 程序兜底时用第一次出现的部材名，其余只计数
                DuplicatePartNameCount++;

                // ただし「この部品はどの部材に属するか」の一覧には<b>全部</b>入れる
                // （鬼目ﾅｯﾄ(ﾌﾞﾚｰｷ取付用) → 上ﾚｰﾙ と 上枠）。AI に候補として見せるため
                AddMemberNameForPart(partKey, _memberByKey[memberKey]);

                return;
            }

            _memberByPartKey.Add(partKey, _memberByKey[memberKey]);
            AddMemberNameForPart(partKey, _memberByKey[memberKey]);
        }

        /// <summary>部品名 → 部材名（1:N）の索引に 1 件足す（同じ部材名は重複させない）。</summary>
        private void AddMemberNameForPart(string partKey, string memberName)
        {
            List<string> memberNames;

            if (!_memberNamesByPartKey.TryGetValue(partKey, out memberNames))
            {
                memberNames = new List<string>();
                _memberNamesByPartKey.Add(partKey, memberNames);
            }

            if (!ContainsWord(memberNames, MemberPartCatalogLoader.NormalizeKey(memberName)))
            {
                memberNames.Add(memberName);
            }
        }
        /// <summary>归一化后是否等于 CSV 第 1 列的某个部材名（含「その他」）。</summary>
        public bool IsMemberName(string value)
        {
            string memberName;

            return TryGetMemberName(value, out memberName);
        }

        /// <summary>按部材名 取 CSV 里的写法（归一化比较；找不到返回 <c>false</c>）。</summary>
        public bool TryGetMemberName(string value, out string memberName)
        {
            memberName = string.Empty;

            string key = MemberPartCatalogLoader.NormalizeKey(value);

            if (key.Length == 0)
            {
                return false;
            }

            return _memberByKey.TryGetValue(key, out memberName);
        }

        /// <summary>按部品名 取它所属的部材名（归一化比较；找不到返回 <c>false</c>）。</summary>
        public bool TryResolvePartName(string partName, out string memberName)
        {
            memberName = string.Empty;

            string key = MemberPartCatalogLoader.NormalizeKey(partName);

            if (key.Length == 0)
            {
                return false;
            }

            return _memberByPartKey.TryGetValue(key, out memberName);
        }

        /// <summary>
        /// 按部品名 取它所属的<b>全部</b>部材名（CSV 顺序；1:N の部品は複数返る）。
        /// 例：<c>鬼目ﾅｯﾄ(ﾌﾞﾚｰｷ取付用)</c> → <c>上ﾚｰﾙ</c> / <c>上枠</c>。
        /// マスタに無い部品名 / 空文字のときは空リスト。
        /// </summary>
        public IList<string> GetMemberNamesForPart(string partName)
        {
            string key = MemberPartCatalogLoader.NormalizeKey(partName);
            List<string> memberNames;

            if (key.Length == 0 || !_memberNamesByPartKey.TryGetValue(key, out memberNames))
            {
                return new List<string>();
            }

            // 呼び出し側が編集しても索引が壊れないよう複製して返す
            return new List<string>(memberNames);
        }

        /// <summary>
        /// 图上的文字 → 部材名：文字本身是部材名就返回它；是部品名就返回该部品所属的部材名；
        /// 两者都不是（マスタ 里没有）时返回空串。
        /// </summary>
        public string ResolveMemberNameForText(string text)
        {
            string memberName;

            if (TryGetMemberName(text, out memberName))
            {
                return memberName;
            }

            if (TryResolvePartName(text, out memberName))
            {
                return memberName;
            }

            return string.Empty;
        }

        /// <summary>某个部材名 下面登记的全部部品名（按 CSV 顺序；没有时返回空列表）。</summary>
        public IList<string> GetPartNames(string memberName)
        {
            string key = MemberPartCatalogLoader.NormalizeKey(memberName);
            List<string> partNames;

            if (key.Length == 0 || !_partNamesByMember.TryGetValue(key, out partNames))
            {
                return new List<string>();
            }

            return partNames;
        }

        /// <summary>部材名 一览的显示文字（<c>上レール / 上枠 / … / その他</c>）。</summary>
        public string DescribeMemberNames()
        {
            return string.Join(" / ", _memberNames.ToArray());
        }

        /// <summary>列表里是否已有归一化后等于 <paramref name="key"/> 的文字。</summary>
        private static bool ContainsWord(IList<string> words, string key)
        {
            if (words == null || words.Count == 0)
            {
                return false;
            }

            foreach (string word in words)
            {
                if (string.Equals(MemberPartCatalogLoader.NormalizeKey(word), key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }


    }

    /// <summary>
    /// 部材部品マスタ 的加载器 + 文字归一化。
    ///
    /// <para>
    /// <b>数据从哪来</b>：SQLite 数据库（<see cref="MemberPartRepository"/>，默认
    /// <c>D:\MSIFDB\MSIF_MemberParts.db</c> 的「部材部品」表：第 1 列 = <c>部材名</c>、
    /// 第 2 列 = <c>部品名</c>）。<b>数据库里还没有数据时</b>，才读一次原来的
    /// <c>部材部品\部材部品.csv</c>（第 1 行是表头，逗号分隔，Shift-JIS / UTF-8 都能读）
    /// 并导入数据库；之后只读数据库。
    /// </para>
    /// <list type="bullet">
    ///   <item>部材名 例：<c>上ﾚｰﾙ</c> / <c>上枠</c> / <c>右縦枠</c> / <c>その他</c>；</item>
    ///   <item>部品名 例：<c>ｼﾞｮｲﾝﾄ金具</c> / <c>取説</c>。</item>
    /// </list>
    ///
    /// <para>
    /// <b>用途</b>：【AI 分析】（<see cref="MemberNameAiAdvisor"/>）判断部材名时用 ——
    /// 图纸上不一定写着部材名（可能只写部品名、或者什么都写），
    /// 这张表给 AI 一份「可以回答的部材名」，以及「部品名 → 部材名」的对照。
    /// </para>
    ///
    /// <para>
    /// <b>数据库位置</b>（<see cref="MemberPartRepository.ResolveDatabasePath"/>）：
    /// ① 环境变量 <c>MSIF_MEMBER_PARTS_DB</c> → ② App.config 的 <c>MemberPartDatabasePath</c> →
    /// ③ 默认 <c>D:\MSIFDB\MSIF_MemberParts.db</c>（D 盘不可用时退回 <c>&lt;程序目录&gt;\Data</c>）。
    /// </para>
    ///
    /// <para>
    /// <b>CSV 的位置</b>（只在首次导入时找，<see cref="ResolveDataSource"/>，找不到只是少一份参考、不报错）：
    /// ① 环境变量 <c>MSIF_MEMBER_PARTS</c> → ② App.config 的 <c>MemberPartDataPath</c> →
    /// ③ 从分解输出目录（<c>&lt;PDF 文件夹&gt;\AiAppData</c>）逐级向上找 <c>部材部品</c> 文件夹 →
    /// ④ 从程序目录逐级向上找（开发时就在项目目录里）。
    /// 文件夹里优先用 <c>部材部品.csv</c>，没有时用最新的 <c>部材部品*.csv</c>（带日期的导出文件）。
    /// </para>
    /// </summary>
    internal static class MemberPartCatalogLoader
    {
        /// <summary>部材部品マスタ 所在的文件夹名。</summary>
        public const string FolderName = "部材部品";

        /// <summary>部材部品マスタ 文件名。</summary>
        public const string FileName = "部材部品.csv";

        /// <summary>带日期的导出文件名（<c>部材部品_20260619.csv</c> 这种），精确名找不到时用最新的一个。</summary>
        public const string FileSearchPattern = "部材部品*.csv";

        /// <summary>App.config 里可配置部材部品マスタ 位置（文件夹或文件）的键。</summary>
        public const string SettingKey = "MemberPartDataPath";

        /// <summary>可配置部材部品マスタ 位置的环境变量。</summary>
        public const string EnvironmentVariable = "MSIF_MEMBER_PARTS";

        /// <summary>「部材名」所在列（第 1 列，从 0 数起 = 0）。</summary>
        public const int MemberNameColumnIndex = 0;

        /// <summary>「部品名」所在列（第 2 列，从 0 数起 = 1）。</summary>
        public const int PartNameColumnIndex = 1;

        /// <summary>「部材名」表头文字（用它识别表头行）。</summary>
        public const string MemberNameHeaderText = "部材名";

        /// <summary>「部品名」表头文字（用它识别表头行）。</summary>
        public const string PartNameHeaderText = "部品名";

        /// <summary>同上级目录搜索的最大层数（一般 2 层就命中）。</summary>
        private const int MaxSearchDepth = 16;

        /// <summary>Shift-JIS 的代码页（现有 部材部品.csv 是这个编码）。</summary>
        private const int ShiftJisCodePage = 932;

        /// <summary>
        /// 解析 <c>部材部品.csv</c> 的完整路径（<b>只在数据库还没有数据、需要首次导入时用</b>）；
        /// 找不到返回空字符串，并在 <paramref name="notes"/> 里写明原因。
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
                "部材部品 数据库里还没有数据，也找不到 {0}\\{1}（已试：环境变量 {2}、App.config 的 {3}、从分解输出目录和程序目录向上查找 {0} 文件夹）→ 本次只用图纸上的文字判定部材名（数据库：{4}）",
                FolderName, FileName, EnvironmentVariable, SettingKey,
                MemberPartRepository.ResolveDatabasePath()));

            return string.Empty;
        }

        /// <summary>
        /// 读入部材部品マスタ：<b>先读 SQLite 数据库</b>；数据库里还没有数据时，
        /// 才找 <c>部材部品\部材部品.csv</c> 导入数据库（之后本次直接用导入的内容）。
        /// 什么都不行时返回空索引（调用方只用图纸上的文字），不抛异常。
        /// </summary>
        /// <param name="outputDirectory">分解输出根目录（可为 <c>null</c>）。</param>
        /// <param name="notes">提示信息（可为 <c>null</c>）。</param>
        public static MemberPartCatalog LoadFor(string outputDirectory, IList<string> notes)
        {
            MemberPartCatalog catalog = LoadFromDatabase(notes);

            if (catalog != null && !catalog.IsEmpty)
            {
                return catalog;
            }

            // 数据库里还没有数据（或数据库打不开）→ 从 CSV 导入一次
            string filePath = ResolveDataSource(outputDirectory, notes);

            if (string.IsNullOrEmpty(filePath))
            {
                return catalog ?? new MemberPartCatalog();
            }

            MemberPartCatalog fromCsv = Load(filePath, notes);

            if (fromCsv.IsEmpty)
            {
                return catalog ?? fromCsv;
            }

            ImportToDatabase(fromCsv, filePath, notes);

            return fromCsv;
        }

        /// <summary>
        /// 读 SQLite 数据库里的部材部品（表不存在时建空表）。打不开时记提示并返回 <c>null</c>
        /// （调用方据此再试 CSV）。
        /// </summary>
        private static MemberPartCatalog LoadFromDatabase(IList<string> notes)
        {
            string databasePath;

            try
            {
                databasePath = MemberPartRepository.ResolveDatabasePath();
            }
            catch (Exception ex)
            {
                AddNote(notes, "部材部品 数据库路径解析失败：" + ex.Message);

                return null;
            }

            try
            {
                using (MemberPartRepository repository = new MemberPartRepository(databasePath))
                {
                    MemberPartCatalog catalog = new MemberPartCatalog();
                    catalog.Path = repository.DatabasePath;

                    foreach (MemberPartEntry entry in repository.LoadEntries())
                    {
                        catalog.Add(entry);
                    }

                    return catalog;
                }
            }
            catch (Exception ex)
            {
                AddNote(notes, "读取部材部品 数据库失败（" + databasePath + "）：" + ex.Message);

                return null;
            }
        }

        /// <summary>把 CSV 读到的明细整表写进数据库（失败只记提示，本次仍用 CSV 的内容）。</summary>
        private static void ImportToDatabase(MemberPartCatalog catalog, string csvPath, IList<string> notes)
        {
            string databasePath = string.Empty;

            try
            {
                databasePath = MemberPartRepository.ResolveDatabasePath();

                using (MemberPartRepository repository = new MemberPartRepository(databasePath))
                {
                    int saved = repository.ReplaceAll(catalog.Entries);

                    catalog.Path = repository.DatabasePath;

                    AddNote(notes, string.Format(
                        "部材部品 CSV 已导入数据库：{0}（{1} 行，来源 {2}）",
                        repository.DatabasePath, saved, csvPath));
                }
            }
            catch (Exception ex)
            {
                AddNote(notes, "部材部品 CSV 导入数据库失败（" + databasePath + "）：" + ex.Message +
                    " → 本次直接用 CSV");
            }
        }

        /// <summary>
        /// 把部品基本信息画面【保存】时确定下来的「部材名 + 部品名」当成新的对应规则登录进
        /// <c>部材部品</c> 表（<see cref="MemberPartRepository.MergeRules"/>：没有的组合追加，
        /// 已有的组合只刷新「登录时间」）。由 <c>PartBasicInfoForm.SaveParts</c> 在写库成功后调用；
        /// <b>不抛异常</b>（规则登录失败不影响部品基本信息本身的保存）。
        ///
        /// <para>
        /// 跳过这些行：部材名 / 部品名 为空、部材名 = 「その他」（<see cref="PartBasicInfo.MemberNameFallback"/>，
        /// 意思是「不属于任何部材」，不是对应规则）、部材名里含 <c>|</c>（<c>鴨居|上レール</c> 这种
        /// 多个候选、还没确定）。用户在画面上把部材名改成真正的部材名后保存，那一条就会被登录进去。
        /// </para>
        /// </summary>
        /// <param name="parts">部品基本信息的行（可为 <c>null</c>）。</param>
        /// <returns>给人看的提示；没有可登录的规则时返回空串。</returns>
        public static string RegisterRules(IEnumerable<PartBasicInfo> parts)
        {
            if (parts == null)
            {
                return string.Empty;
            }

            List<MemberPartEntry> entries = new List<MemberPartEntry>();
            string fileName = MemberPartRepository.DefaultDatabaseFileName;

            foreach (PartBasicInfo part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                string memberName = (part.MemberName ?? string.Empty).Trim();
                string partName = (part.PartName ?? string.Empty).Trim();

                if (partName.Length == 0 || !IsLearnableMemberName(memberName))
                {
                    continue;
                }

                entries.Add(new MemberPartEntry(memberName, partName, fileName, 0));
            }

            if (entries.Count == 0)
            {
                return string.Empty;
            }

            int added;

            try
            {
                added = MergeRulesSafely(entries);
            }
            catch (Exception ex)
            {
                return "部材部品 规则登录失败（" + MemberPartRepository.ResolveDatabasePath() + "）：" + ex.Message;
            }

            return added > 0
                ? string.Format("部材部品 规则：新增 {0} 条（本次确认 {1} 条）", added, entries.Count)
                : string.Format("部材部品 规则：{0} 条都已登录过（没有新增）", entries.Count);
        }

        /// <summary>
        /// 把规则合并进 部材部品 表。<b>表里还是空的</b>时（程序启动时的初始化没成功、或数据库刚被删掉），
        /// 先按 <see cref="LoadFor"/> 把 <c>部材部品\部材部品.csv</c> 导入一次，
        /// 免得这次保存的几条规则把「首次导入」挡在外面。
        /// </summary>
        private static int MergeRulesSafely(IList<MemberPartEntry> entries)
        {
            int count;

            using (MemberPartRepository repository = new MemberPartRepository(string.Empty))
            {
                count = repository.Count();
            }

            if (count == 0)
            {
                LoadFor(null, null);
            }

            using (MemberPartRepository repository = new MemberPartRepository(string.Empty))
            {
                return repository.MergeRules(entries);
            }
        }

        /// <summary>
        /// 这一行的部材名 能不能当成「部材名 → 部品名」的规则登录：
        /// 空 / 「その他」/ 含 <c>|</c>（多个候选、未确定）都不算。
        /// </summary>
        private static bool IsLearnableMemberName(string memberName)
        {
            if (string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            if (memberName.IndexOf('|') >= 0 || memberName.IndexOf('｜') >= 0)
            {
                return false;
            }

            string key = NormalizeKey(memberName);

            return key.Length > 0
                && !string.Equals(key, NormalizeKey(PartBasicInfo.MemberNameFallback), StringComparison.Ordinal);
        }

        /// <summary>
        /// 读 <paramref name="filePath"/>（部材部品 CSV），建立「部材名 一览 / 部品名 → 部材名」的索引。
        /// <b>只在数据库里还没有数据时由 <see cref="LoadFor"/> 调用（首次导入）</b>。
        /// 读不了只记提示并返回空索引（调用方据此只用图纸上的文字）。
        /// </summary>
        /// <param name="filePath">部材部品.csv 的完整路径。</param>
        /// <param name="notes">提示信息（可为 <c>null</c>）。</param>
        public static MemberPartCatalog Load(string filePath, IList<string> notes)
        {
            MemberPartCatalog catalog = new MemberPartCatalog();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                AddNote(notes, "部材部品 CSV 不存在：" + filePath);

                return catalog;
            }

            string[] lines;

            try
            {
                lines = SplitLines(ReadAllText(filePath));
            }
            catch (Exception ex)
            {
                AddNote(notes, "读取部材部品 CSV 失败（" + filePath + "）：" + ex.Message);

                return catalog;
            }

            catalog.Path = filePath;

            string fileName = System.IO.Path.GetFileName(filePath);

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] cells = SplitCsvLine(line);
                string memberName = CellText(cells, MemberNameColumnIndex);
                string partName = CellText(cells, PartNameColumnIndex);

                // 表头行（第 1 列是「部材名」或第 2 列是「部品名」）
                if (string.Equals(memberName, MemberNameHeaderText, StringComparison.Ordinal)
                    || string.Equals(partName, PartNameHeaderText, StringComparison.Ordinal))
                {
                    continue;
                }

                if (memberName.Length == 0)
                {
                    continue;
                }

                catalog.Add(new MemberPartEntry(memberName, partName, fileName, lineIndex + 1));
            }

            return catalog;
        }

        /// <summary>
        /// 比较用的归一化：NFKC（半角カナ → 全角カナ、全角英数 → 半角）+ 去掉全部空白（含全角空格）+ 统一大写。
        /// 例：<c>上ﾚｰﾙ</c> / <c>上レール</c> / <c>” 上レール ”</c> → <c>上レール</c>。
        /// </summary>
        public static string NormalizeKey(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string normalized;

            try
            {
                normalized = value.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                // 孤立代理项等非法序列：退回原文，至少还能比空白 / 大小写
                normalized = value;
            }

            StringBuilder builder = new StringBuilder(normalized.Length);

            foreach (char ch in normalized)
            {
                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                builder.Append(char.ToUpperInvariant(ch));
            }

            return builder.ToString();
        }


        /// <summary>读环境变量 / App.config 里的部材部品マスタ 位置（可写文件夹，也可直接写文件）。</summary>
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

        /// <summary>在文件夹里找部材部品 CSV：先精确名，再取最新的带日期文件。</summary>
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
        /// 从 <paramref name="startDirectory"/> 起逐级向上找 <c>部材部品</c> 文件夹
        /// （也认 <c>MSIF_InputData\部材部品</c>，以及直接把 CSV 放在根目录的情况）。
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

                // 有的环境把 CSV 直接放在输入根目录下
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
        /// 读文本：先按 BOM 判断，其次按严格 UTF-8，失败时按 Shift-JIS（现有 部材部品.csv 是 Shift-JIS）。
        /// </summary>
        private static string ReadAllText(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // 不是 UTF-8 → 按 Shift-JIS 读（读不了再退回系统默认编码）
            }

            try
            {
                return Encoding.GetEncoding(ShiftJisCodePage).GetString(bytes);
            }
            catch (ArgumentException)
            {
                return Encoding.Default.GetString(bytes);
            }
            catch (NotSupportedException)
            {
                return Encoding.Default.GetString(bytes);
            }
        }

        /// <summary>按 CRLF / LF / CR 切行（保留空行位置，行号 = CSV 里的行号）。</summary>
        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new string[0];
            }

            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        /// <summary>取第 <paramref name="index"/> 列的单元格文字（去掉首尾空白；没有该列时返回空串）。</summary>
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
                    if (ch == '\"')
                    {
                        if (position + 1 < line.Length && line[position + 1] == '\"')
                        {
                            cell.Append('\"');
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

                if (ch == '\"')
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

        /// <summary>加一条提示信息（<paramref name="notes"/> 为 <c>null</c> 时忽略）。</summary>
        private static void AddNote(IList<string> notes, string message)
        {
            if (notes == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            notes.Add(message);
        }
    }


}
