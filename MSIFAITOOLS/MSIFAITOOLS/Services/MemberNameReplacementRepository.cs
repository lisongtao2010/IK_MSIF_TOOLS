using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 部材名置换表的 1 行：把「置换前」（图纸 / <c>parts_info.json</c> 上的部材名，例：<c>鴨居</c>）
    /// 换成「置换后」（希望登录进部品基本信息表的部材名，例：<c>上枠</c>）。
    /// </summary>
    internal sealed class MemberNameReplacement
    {
        public MemberNameReplacement(string source, string target)
        {
            Source = (source ?? string.Empty).Trim();
            Target = (target ?? string.Empty).Trim();
        }

        /// <summary>置换前（表里的主键；空值的行不会被采用）。</summary>
        public string Source { get; private set; }

        /// <summary>置换后（空值的行不会被采用）。</summary>
        public string Target { get; private set; }
    }

    /// <summary>
    /// 部材名置换表的 SQLite 仓储（维护画面 <see cref="MemberNameReplacementForm"/> 用）。
    ///
    /// <para>
    /// <b>数据库位置</b>：与部材部品マスタ<b>同一个库</b>（<c>D:\MSIFDB\MSIF_MemberParts.db</c>），
    /// 只多一张表 <see cref="TableName"/>。解析顺序与 <see cref="MemberPartRepository"/> 完全一致
    /// （<see cref="ResolveDatabasePath(string)"/>）：① 传入值 → ② 环境变量
    /// <c>MSIF_MEMBER_PARTS_DB</c> → ③ App.config 的 <c>MemberPartDatabasePath</c> →
    /// ④ 默认 <c>D:\MSIFDB\MSIF_MemberParts.db</c>（D 盘不可用时
    /// <c>&lt;程序目录&gt;\Data\MSIF_MemberParts.db</c>）。
    /// </para>
    ///
    /// <para>
    /// <b>表结构</b>：<c>部材名置换</c>（<c>置换前</c> TEXT 主键、<c>置换后</c> TEXT、<c>登录时间</c> TEXT）。
    /// 打开（不存在时创建）数据库并 <c>CREATE TABLE IF NOT EXISTS</c>，所以第一次打开会自动建表，无需手工准备。
    /// </para>
    /// </summary>
    internal sealed class MemberNameReplacementRepository : IDisposable
    {
        /// <summary>表名。</summary>
        public const string TableName = "部材名置换";

        /// <summary>「置换前」列名（主键 = 图纸上的部材名）。</summary>
        public const string SourceColumnName = "置换前";

        /// <summary>「置换后」列名（登录进部品基本信息表的部材名）。</summary>
        public const string TargetColumnName = "置换后";

        /// <summary>「登录时间」列名（保存时自动写入，便于排查是哪一次改的）。</summary>
        public const string RegisteredAtColumnName = "登录时间";

        /// <summary>默认数据库文件夹（与 <see cref="MemberPartRepository"/> 相同：<c>D:\MSIFDB</c>）。</summary>
        public const string DefaultDatabaseFolder = MemberPartRepository.DefaultDatabaseFolder;

        /// <summary>默认数据库文件名（与 <see cref="MemberPartRepository"/> 同一个库，只多一张表）。</summary>
        public const string DefaultDatabaseFileName = MemberPartRepository.DefaultDatabaseFileName;

        /// <summary>D 盘不可用时的备选子文件夹名（相对程序目录）。</summary>
        public const string FallbackDatabaseFolderName = MemberPartRepository.FallbackDatabaseFolderName;

        /// <summary>App.config 中指定数据库位置的键（与 部材部品 共用，保证一定是同一个库）。</summary>
        public const string DatabasePathSettingKey = MemberPartRepository.DatabasePathSettingKey;

        /// <summary>可指定数据库位置的环境变量（与 部材部品 共用）。</summary>
        public const string DatabasePathEnvironmentVariable = MemberPartRepository.DatabasePathEnvironmentVariable;

        /// <summary>「登录时间」的格式。</summary>
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

        private readonly string _databasePath;
        private readonly SQLiteConnection _connection;

        /// <summary>
        /// 打开（不存在时创建）数据库并确保表结构存在。
        /// </summary>
        /// <param name="databasePath">数据库文件完整路径；为空时按 <see cref="ResolveDatabasePath()"/> 解析。</param>
        public MemberNameReplacementRepository(string databasePath)
        {
            _databasePath = ResolveDatabasePath(databasePath);

            string folder = Path.GetDirectoryName(_databasePath);

            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder();
            builder.DataSource = _databasePath;
            builder.FailIfMissing = false;
            builder.Version = 3;

            _connection = new SQLiteConnection(builder.ConnectionString);
            _connection.Open();
            EnsureSchema();
        }

        /// <summary>实际使用的数据库文件路径（维护画面上显示给用户看）。</summary>
        public string DatabasePath
        {
            get { return _databasePath; }
        }

        /// <summary>按「置换前」排序读出全部行。</summary>
        public IList<MemberNameReplacement> Query()
        {
            List<MemberNameReplacement> rows = new List<MemberNameReplacement>();

            string sql = "SELECT [" + SourceColumnName + "], [" + TargetColumnName + "] FROM [" +
                TableName + "] ORDER BY [" + SourceColumnName + "];";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add(new MemberNameReplacement(
                        reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                        reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
                }
            }

            return rows;
        }

        /// <summary>
        /// 整表覆盖写：先删除全部行，再按顺序插入（维护画面点【保存】时调用）。
        /// 空主键 / 空目标值的行直接跳过（不会写进库）。
        /// </summary>
        public void Replace(IList<MemberNameReplacement> rows)
        {
            string timestamp = DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                using (SQLiteCommand delete = new SQLiteCommand("DELETE FROM [" + TableName + "];", _connection, transaction))
                {
                    delete.ExecuteNonQuery();
                }

                string insertSql = "INSERT INTO [" + TableName + "] ([" + SourceColumnName + "], [" +
                    TargetColumnName + "], [" + RegisteredAtColumnName +
                    "]) VALUES (@source, @target, @registeredAt);";

                using (SQLiteCommand insert = new SQLiteCommand(insertSql, _connection, transaction))
                {
                    SQLiteParameter sourceParameter = insert.Parameters.Add("@source", System.Data.DbType.String);
                    SQLiteParameter targetParameter = insert.Parameters.Add("@target", System.Data.DbType.String);
                    SQLiteParameter registeredAtParameter =
                        insert.Parameters.Add("@registeredAt", System.Data.DbType.String);

                    if (rows != null)
                    {
                        foreach (MemberNameReplacement row in rows)
                        {
                            if (row == null || row.Source.Length == 0 || row.Target.Length == 0)
                            {
                                continue;
                            }

                            sourceParameter.Value = row.Source;
                            targetParameter.Value = row.Target;
                            registeredAtParameter.Value = timestamp;
                            insert.ExecuteNonQuery();
                        }
                    }
                }

                transaction.Commit();
            }
        }

        /// <summary>表里的行数（日志 / 画面提示用）。</summary>
        public int Count()
        {
            using (SQLiteCommand command = new SQLiteCommand("SELECT COUNT(*) FROM [" + TableName + "];", _connection))
            {
                object value = command.ExecuteScalar();

                return value == null || value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>建表（已存在时不做事）。</summary>
        private void EnsureSchema()
        {
            string createTable =
                "CREATE TABLE IF NOT EXISTS [" + TableName + "] (" +
                "[" + SourceColumnName + "] TEXT NOT NULL PRIMARY KEY, " +
                "[" + TargetColumnName + "] TEXT NOT NULL, " +
                "[" + RegisteredAtColumnName + "] TEXT);";

            using (SQLiteCommand command = new SQLiteCommand(createTable, _connection))
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

        /// <summary>
        /// 解析数据库文件路径（默认 = 部材部品マスタ 的库）。
        /// </summary>
        public static string ResolveDatabasePath()
        {
            return ResolveDatabasePath(null);
        }

        /// <summary>
        /// 解析数据库文件路径：① 传入值 → ② 环境变量 <see cref="DatabasePathEnvironmentVariable"/> →
        /// ③ App.config 的 <see cref="DatabasePathSettingKey"/> → ④ 默认
        /// <c>D:\MSIFDB\MSIF_MemberParts.db</c>（D 盘不可用时
        /// <c>&lt;程序目录&gt;\Data\MSIF_MemberParts.db</c>）。
        /// <para>配置值写文件夹也可以（自动补 <see cref="DefaultDatabaseFileName"/>），相对路径按程序目录解析。</para>
        /// </summary>
        /// <param name="databasePath">显式指定的路径；为空时按环境变量 / App.config / 默认值解析。</param>
        public static string ResolveDatabasePath(string databasePath)
        {
            if (!string.IsNullOrWhiteSpace(databasePath))
            {
                return databasePath.Trim();
            }

            string fromEnvironment =
                ResolveCandidate(Environment.GetEnvironmentVariable(DatabasePathEnvironmentVariable));

            if (fromEnvironment.Length > 0)
            {
                return fromEnvironment;
            }

            string fromConfiguration = string.Empty;

            try
            {
                fromConfiguration = ResolveCandidate(ConfigurationManager.AppSettings[DatabasePathSettingKey]);
            }
            catch (ConfigurationErrorsException)
            {
                // App.config 读不出来时退回默认值（不终止）
                fromConfiguration = string.Empty;
            }

            if (fromConfiguration.Length > 0)
            {
                return fromConfiguration;
            }

            string defaultPath = Path.Combine(DefaultDatabaseFolder, DefaultDatabaseFileName);

            if (IsRootAvailable(defaultPath))
            {
                return defaultPath;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                FallbackDatabaseFolderName, DefaultDatabaseFileName);
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
