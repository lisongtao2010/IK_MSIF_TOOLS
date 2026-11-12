using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 部材部品マスタ 的 SQLite 仓储（原 <c>部材部品\部材部品.csv</c>：第 1 列 = 部材名、第 2 列 = 部品名）。
    ///
    /// <para>
    /// <b>数据库位置</b>（<see cref="ResolveDatabasePath"/>）：
    /// ① 环境变量 <c>MSIF_MEMBER_PARTS_DB</c> → ② App.config 的 <c>MemberPartDatabasePath</c> →
    /// ③ 默认 <c>D:\MSIFDB\MSIF_MemberParts.db</c>；D 盘不可用时退回
    /// <c>&lt;程序目录&gt;\Data\MSIF_MemberParts.db</c>。
    /// 配置值写文件夹也可以（自动补 <see cref="DefaultDatabaseFileName"/>），相对路径按程序目录解析。
    /// </para>
    ///
    /// <para>
    /// <b>表结构</b>：<c>部材部品</c>（<c>id</c> 自增、<c>部材名</c>、<c>部品名</c>、<c>登录时间</c>），
    /// 按 <c>id</c> 顺序读出（= 原来 CSV 的行顺序）。同一个部材名 / 部品名可以重复出现
    /// （原 CSV 里「部品ｾｯﾄ」就有很多行），所以不建唯一索引。
    /// </para>
    ///
    /// <para>
    /// <b>什么时候用到 CSV</b>：第一次运行（表里没有数据）时 <see cref="MemberPartCatalogLoader.LoadFor"/>
    /// 会读一次 <c>部材部品\部材部品.csv</c> 导入这张表，之后只读数据库。
    /// 要重新导入，清空 <c>部材部品</c> 表或删掉数据库文件即可（下次运行自动重建 + 导入）。
    /// </para>
    /// </summary>
    internal sealed class MemberPartRepository : IDisposable
    {
        /// <summary>默认数据库文件夹（<c>D:\MSIFDB</c>）。</summary>
        public const string DefaultDatabaseFolder = @"D:\MSIFDB";

        /// <summary>默认数据库文件名。</summary>
        public const string DefaultDatabaseFileName = "MSIF_MemberParts.db";

        /// <summary>D 盘不可用时的备选子文件夹名（相对程序目录）。</summary>
        public const string FallbackDatabaseFolderName = "Data";

        /// <summary>App.config 中指定数据库位置的键（可写文件夹，也可写文件）。</summary>
        public const string DatabasePathSettingKey = "MemberPartDatabasePath";

        /// <summary>可指定数据库位置的环境变量。</summary>
        public const string DatabasePathEnvironmentVariable = "MSIF_MEMBER_PARTS_DB";

        /// <summary>部材部品表名。</summary>
        public const string TableName = "部材部品";

        /// <summary>「部材名」列名（= 原 CSV 第 1 列）。</summary>
        public const string MemberNameColumnName = "部材名";

        /// <summary>「部品名」列名（= 原 CSV 第 2 列）。</summary>
        public const string PartNameColumnName = "部品名";

        /// <summary>「登录时间」列名（导入 / 手工维护的时间，便于排查是哪一次写进来的）。</summary>
        public const string RegisteredAtColumnName = "登录时间";

        /// <summary>部材名 的索引名（按部材名 取部品清单时用）。</summary>
        private const string MemberNameIndexName = "ix_部材部品_部材名";

        /// <summary>部品名 的索引名（部品名 → 部材名 时用）。</summary>
        private const string PartNameIndexName = "ix_部材部品_部品名";

        private readonly string _databasePath;
        private readonly SQLiteConnection _connection;

        /// <summary>
        /// 打开（不存在时创建）数据库并确保表结构存在。
        /// </summary>
        /// <param name="databasePath">数据库文件完整路径；为空时使用 <see cref="ResolveDatabasePath"/>。</param>
        public MemberPartRepository(string databasePath)
        {
            _databasePath = string.IsNullOrWhiteSpace(databasePath)
                ? ResolveDatabasePath()
                : databasePath.Trim();

            string folder = Path.GetDirectoryName(_databasePath);

            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder();
            builder.DataSource = _databasePath;
            builder.FailIfMissing = false;
            builder.ForeignKeys = true;

            _connection = new SQLiteConnection(builder.ToString());
            _connection.Open();

            EnsureSchema();
        }

        /// <summary>数据库文件的完整路径。</summary>
        public string DatabasePath
        {
            get { return _databasePath; }
        }

        /// <summary>
        /// 解析数据库路径：环境变量 <c>MSIF_MEMBER_PARTS_DB</c> → App.config 的
        /// <c>MemberPartDatabasePath</c> → 默认 <c>D:\MSIFDB\MSIF_MemberParts.db</c> →
        /// （D 盘不可用时）<c>&lt;程序目录&gt;\Data\MSIF_MemberParts.db</c>。
        /// </summary>
        public static string ResolveDatabasePath()
        {
            string configured = ResolveConfiguredPath();

            if (!string.IsNullOrEmpty(configured) && IsRootAvailable(configured))
            {
                return configured;
            }

            string preferred = Path.Combine(DefaultDatabaseFolder, DefaultDatabaseFileName);

            return IsRootAvailable(preferred)
                ? preferred
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FallbackDatabaseFolderName,
                    DefaultDatabaseFileName);
        }

        /// <summary>表里的明细条数（0 = 还没有导入过）。</summary>
        public int Count()
        {
            using (SQLiteCommand command = new SQLiteCommand(
                "SELECT COUNT(*) FROM [" + TableName + "];", _connection))
            {
                object value = command.ExecuteScalar();

                return value == null || value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 读出全部明细（按 <c>id</c> 顺序 = 原来 CSV 的顺序）。
        /// <see cref="MemberPartEntry.FileName"/> = 数据库文件名，<see cref="MemberPartEntry.RowNumber"/> = 表里的 <c>id</c>。
        /// </summary>
        public IList<MemberPartEntry> LoadEntries()
        {
            List<MemberPartEntry> entries = new List<MemberPartEntry>();
            string fileName = Path.GetFileName(_databasePath);

            string sql = "SELECT [id], [" + MemberNameColumnName + "], [" + PartNameColumnName + "] FROM [" +
                TableName + "] ORDER BY [id];";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long id = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                        string memberName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        string partName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

                        entries.Add(new MemberPartEntry(memberName, partName, fileName,
                            (int)Math.Min(id, int.MaxValue)));
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// 用 <paramref name="entries"/> 整表替换（先 DELETE 再 INSERT，一个事务），
        /// 只在「数据库还没有数据 → 从 CSV 导入」时调用。
        /// </summary>
        /// <param name="entries">要写入的明细（部材名为空的行跳过）。</param>
        /// <returns>实际写入的行数。</returns>
        public int ReplaceAll(IEnumerable<MemberPartEntry> entries)
        {
            if (entries == null)
            {
                return 0;
            }

            string insertSql =
                "INSERT INTO [" + TableName + "] ([" + MemberNameColumnName + "], [" +
                PartNameColumnName + "], [" + RegisteredAtColumnName + "]) " +
                "VALUES (@memberName, @partName, @registeredAt);";

            string registeredAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            int saved = 0;

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                Execute("DELETE FROM [" + TableName + "];", transaction);

                foreach (MemberPartEntry entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.MemberName))
                    {
                        continue;
                    }

                    using (SQLiteCommand command = new SQLiteCommand(insertSql, _connection, transaction))
                    {
                        command.Parameters.AddWithValue("@memberName", entry.MemberName.Trim());
                        command.Parameters.AddWithValue("@partName", (entry.PartName ?? string.Empty).Trim());
                        command.Parameters.AddWithValue("@registeredAt", registeredAt);
                        command.ExecuteNonQuery();
                    }

                    saved++;
                }

                transaction.Commit();
            }

            return saved;
        }

        /// <summary>
        /// 登录 / 更新「部材名 → 部品名」的对应规则（部品基本信息画面【保存】后调用）：
        /// 数据库里<b>还没有的组合</b>插入新行，<b>已有</b>的组合只刷新「登录时间」（不新增行、不改 <c>id</c>、
        /// 保持原来 CSV 的行顺序）。
        ///
        /// <para>
        /// 比对走 <see cref="MemberPartCatalogLoader.NormalizeKey"/>（半角カナ → 全角カナ / 全角英数 → 半角 /
        /// 去空白 / 统一大写），所以表里已有 <c>上ﾚｰﾙ</c> 时，画面上写 <c>上レール</c> 也不会再登录一行；
        /// 同一次保存里重复出现的组合也只插一行。
        /// </para>
        /// <para>
        /// 同一个部品名可以挂在多个部材下（1:N，<c>鬼目ﾅｯﾄ(ﾌﾞﾚｰｷ取付用)</c> → <c>上ﾚｰﾙ</c> / <c>上枠</c>），
        /// 所以这里<b>只追加、不删除旧组合</b>。
        /// </para>
        /// </summary>
        /// <param name="entries">要登录的规则（部材名 或 部品名 为空的行跳过）。</param>
        /// <returns>新增的行数（已存在的组合不计）。</returns>
        public int MergeRules(IEnumerable<MemberPartEntry> entries)
        {
            if (entries == null)
            {
                return 0;
            }

            string insertSql =
                "INSERT INTO [" + TableName + "] ([" + MemberNameColumnName + "], [" +
                PartNameColumnName + "], [" + RegisteredAtColumnName + "]) " +
                "VALUES (@memberName, @partName, @registeredAt);";

            string touchSql = "UPDATE [" + TableName + "] SET [" + RegisteredAtColumnName +
                "] = @registeredAt WHERE [id] = @id;";

            string registeredAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            // 数据库里已有的组合（归一化 key → id）。同一个 key 出现多行时只记第一行的 id
            Dictionary<string, long> existing = new Dictionary<string, long>(StringComparer.Ordinal);

            string selectSql = "SELECT [id], [" + MemberNameColumnName + "], [" + PartNameColumnName +
                "] FROM [" + TableName + "];";

            using (SQLiteCommand command = new SQLiteCommand(selectSql, _connection))
            {
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long id = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                        string memberName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        string partName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        string key = BuildRuleKey(memberName, partName);

                        if (key.Length > 0 && !existing.ContainsKey(key))
                        {
                            existing.Add(key, id);
                        }
                    }
                }
            }

            int added = 0;

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                foreach (MemberPartEntry entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    string memberName = (entry.MemberName ?? string.Empty).Trim();
                    string partName = (entry.PartName ?? string.Empty).Trim();

                    if (memberName.Length == 0 || partName.Length == 0)
                    {
                        continue;
                    }

                    string key = BuildRuleKey(memberName, partName);

                    if (key.Length == 0)
                    {
                        continue;
                    }

                    long existingId;

                    if (existing.TryGetValue(key, out existingId))
                    {
                        // 已有这条规则：只刷新「登录时间」（不新增行，避免重复）
                        using (SQLiteCommand command = new SQLiteCommand(touchSql, _connection, transaction))
                        {
                            command.Parameters.AddWithValue("@registeredAt", registeredAt);
                            command.Parameters.AddWithValue("@id", existingId);
                            command.ExecuteNonQuery();
                        }

                        continue;
                    }

                    using (SQLiteCommand command = new SQLiteCommand(insertSql, _connection, transaction))
                    {
                        command.Parameters.AddWithValue("@memberName", memberName);
                        command.Parameters.AddWithValue("@partName", partName);
                        command.Parameters.AddWithValue("@registeredAt", registeredAt);
                        command.ExecuteNonQuery();
                    }

                    // 同一次保存里重复出现的组合也只插一行
                    existing.Add(key, -1);
                    added++;
                }

                transaction.Commit();
            }

            return added;
        }

        /// <summary>「部材名 + 部品名」的比较键（各自归一化后用 <c>\\u0001</c> 连接；任一侧为空时返回空串）。</summary>
        private static string BuildRuleKey(string memberName, string partName)
        {
            string memberKey = MemberPartCatalogLoader.NormalizeKey(memberName);
            string partKey = MemberPartCatalogLoader.NormalizeKey(partName);

            if (memberKey.Length == 0 || partKey.Length == 0)
            {
                return string.Empty;
            }

            return memberKey + "\u0001" + partKey;
        }

        /// <summary>建表（已存在则跳过）并创建「部材名」「部品名」索引。</summary>
        private void EnsureSchema()
        {
            string createTable =
                "CREATE TABLE IF NOT EXISTS [" + TableName + "] (" +
                "[id] INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "[" + MemberNameColumnName + "] TEXT NOT NULL, " +
                "[" + PartNameColumnName + "] TEXT, " +
                "[" + RegisteredAtColumnName + "] TEXT NOT NULL" +
                ");";

            string createMemberIndex = "CREATE INDEX IF NOT EXISTS [" + MemberNameIndexName + "] ON [" +
                TableName + "] ([" + MemberNameColumnName + "]);";

            string createPartIndex = "CREATE INDEX IF NOT EXISTS [" + PartNameIndexName + "] ON [" +
                TableName + "] ([" + PartNameColumnName + "]);";

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                Execute(createTable, transaction);
                Execute(createMemberIndex, transaction);
                Execute(createPartIndex, transaction);
                transaction.Commit();
            }
        }

        /// <summary>执行一条不返回结果的 SQL。</summary>
        private void Execute(string sql, SQLiteTransaction transaction)
        {
            using (SQLiteCommand command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>关闭数据库连接。</summary>
        public void Dispose()
        {
            if (_connection != null)
            {
                _connection.Dispose();
            }
        }

        /// <summary>读环境变量 / App.config 里的数据库位置（相对路径按程序目录）。</summary>
        private static string ResolveConfiguredPath()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(DatabasePathEnvironmentVariable);
            string resolved = ResolveCandidate(fromEnvironment);

            if (!string.IsNullOrEmpty(resolved))
            {
                return resolved;
            }

            string fromConfiguration;

            try
            {
                fromConfiguration = ConfigurationManager.AppSettings[DatabasePathSettingKey];
            }
            catch (ConfigurationErrorsException)
            {
                fromConfiguration = null;
            }

            return ResolveCandidate(fromConfiguration);
        }

        /// <summary>
        /// 把配置值换成数据库文件路径：给的是文件夹（已存在或没有扩展名）时补上
        /// <see cref="DefaultDatabaseFileName"/>；解析不了返回空串。
        /// </summary>
        private static string ResolveCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            try
            {
                string path = candidate.Trim();

                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                }

                path = Path.GetFullPath(path);

                if (Directory.Exists(path) || Path.GetExtension(path).Length == 0)
                {
                    path = Path.Combine(path, DefaultDatabaseFileName);
                }

                return path;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>路径所在盘的根目录是否存在（D 盘不可用时退回程序目录）。</summary>
        private static bool IsRootAvailable(string path)
        {
            try
            {
                string root = Path.GetPathRoot(path);

                return !string.IsNullOrEmpty(root) && Directory.Exists(root);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
