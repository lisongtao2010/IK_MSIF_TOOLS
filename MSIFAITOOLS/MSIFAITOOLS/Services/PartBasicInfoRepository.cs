using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 【CMD 分析】第 5 步 部品コード正規化 要更新的一行：
    /// 用「页码 + 番号 + 原部品コード」定位，把「部品コード」换成 <see cref="NewPartCode"/>。
    /// 原部品コード 必须一起带着 —— 它是定位键的一部分，换掉以后就找不回来了。
    /// </summary>
    internal sealed class PartCodeUpdate
    {
        public PartCodeUpdate(int page, int number, string oldPartCode, string newPartCode)
        {
            Page = page;
            Number = number;
            OldPartCode = oldPartCode ?? string.Empty;
            NewPartCode = newPartCode ?? string.Empty;
        }

        /// <summary>页码（定位键）。</summary>
        public int Page { get; private set; }

        /// <summary>番号（定位键）。</summary>
        public int Number { get; private set; }

        /// <summary>原来的部品コード（定位键，也是 WHERE 条件）。</summary>
        public string OldPartCode { get; private set; }

        /// <summary>正規化后的部品コード（写进去的值）。</summary>
        public string NewPartCode { get; private set; }
    }

    /// <summary>
    /// 部品基本信息表的 SQLite 仓储。
    /// 数据库文件默认位于 &lt;程序目录&gt;\Data\MSIF_Parts.db，
    /// 可用 App.config 的 PartDatabasePath 覆盖（相对路径按程序目录解析）。
    /// </summary>
    internal sealed class PartBasicInfoRepository : IDisposable
    {
        /// <summary>数据库默认文件名。</summary>
        public const string DefaultDatabaseFileName = "MSIF_Parts.db";

        /// <summary>数据库默认子文件夹名。</summary>
        public const string DefaultDatabaseFolderName = "Data";

        /// <summary>部品基本信息表名。</summary>
        public const string TableName = "部品基本信息";

        /// <summary>App.config 中指定数据库路径的键名。</summary>
        public const string DatabasePathSettingKey = "PartDatabasePath";

        /// <summary>唯一索引名：図番 + 页码 + 番号 + 部品コード（同一番号でも部品コードが違えば別行）。</summary>
        public const string UniqueIndexName = "ux_部品基本信息_図番_页码_番号_部品コード";

        /// <summary>旧版唯一索引名（図番 + 页码 + 番号）。打开数据库时删除并升级成 <see cref="UniqueIndexName"/>。</summary>
        public const string LegacyUniqueIndexName = "ux_部品基本信息_図番_页码_番号";

        /// <summary>
        /// 表的全部列（顺序 = 新建数据库时的列顺序 = 插入语句的参数顺序）。
        /// 前 14 列是业务必需列：番号 / 部材名 / 部品名 / 区分 / 数量 / 切断寸法 / 部品コード /
        /// 参照元 / H下限 / H上限 / W下限 / W上限 / 使い分け / 特注（画面也按这个顺序优先显示），
        /// 后面是辅助列。
        /// </summary>
        private static readonly string[] ColumnNames = new string[]
        {
            "番号", "部材名", "部材名AI推荐", "部品名", "区分", "数量", "切断寸法", "部品コード", "参照元",
            "H下限", "H上限", "W下限", "W上限", "使い分け", "特注",
            "図番", "页码", "部品図番", "基本寸法", "校验状态", "备注", "数据来源",
            "来源表格文件", "来源PDF", "登录时间"
        };

        /// <summary>「数据来源」列名（后加的可选列，打开旧数据库时自动补上）。</summary>
        public const string SourceKindColumnName = "数据来源";

        /// <summary>「参照元」列名（旧名「来源标识」：老数据库打开时改成这个名字，数据保留）。</summary>
        public const string SourceTagColumnName = "参照元";

        /// <summary>旧版「来源标识」列名（只在升级老数据库识别旧列时用）。</summary>
        public const string LegacySourceTagColumnName = "来源标识";

        /// <summary>「部材名」列名（后加的可选列，打开旧数据库时自动补上；空值由 <see cref="NormalizeReferenceColumns"/> 补成「その他」）。</summary>
        public const string MemberNameColumnName = "部材名";

        /// <summary>
        /// 「部材名AI推荐」列名（后加的可选列，打开旧数据库时自动补上）。
        /// 【AI 分析】对「部材名」里带 <c>|</c> 的行算出的推荐值写在这里，
        /// 只推荐、不覆盖「部材名」（见 <see cref="PartBasicInfo.MemberNameAiSuggestion"/>）。
        /// </summary>
        public const string MemberNameAiColumnName = "部材名AI推荐";

        /// <summary>「区分」列名（旧名「枠区分」：老数据库打开时改成这个名字，数据保留）。</summary>
        public const string FrameKindColumnName = "区分";

        /// <summary>旧版「枠区分」列名（只在升级老数据库识别旧列时用）。</summary>
        public const string LegacyFrameKindColumnName = "枠区分";

        /// <summary>「数量」列名（旧名「員数」：老数据库打开时改成这个名字，数据保留）。</summary>
        public const string QuantityColumnName = "数量";

        /// <summary>旧版「員数」列名（只在升级老数据库识别旧列时用）。</summary>
        public const string LegacyQuantityColumnName = "員数";

        /// <summary>「H下限」列名（后加的可选列）。</summary>
        public const string HeightMinColumnName = "H下限";

        /// <summary>「H上限」列名（后加的可选列）。</summary>
        public const string HeightMaxColumnName = "H上限";

        /// <summary>「W下限」列名（后加的可选列）。</summary>
        public const string WidthMinColumnName = "W下限";

        /// <summary>「W上限」列名（后加的可选列）。</summary>
        public const string WidthMaxColumnName = "W上限";

        /// <summary>「部品コード」列名（第 6 步用它在拾い出し表里找同一个部品）。</summary>
        public const string PartCodeColumnName = "部品コード";

        /// <summary>「使い分け」列名（后加的可选列；第 6 步按拾い出し表自动填）。</summary>
        public const string UsageKindColumnName = "使い分け";

        /// <summary>「特注」列名（后加的可选列；第 4 步把 tz.json 的「追加対応特注寸法」写在这里）。</summary>
        public const string SpecialNoteColumnName = "特注";

        /// <summary>插入语句（列名与参数名共用 <see cref="ColumnNames"/>，避免两处不一致）。</summary>
        private static readonly string InsertSql = BuildInsertSql();


        private readonly string _databasePath;
        private readonly SQLiteConnection _connection;

        /// <summary>
        /// 打开（不存在时创建）数据库并确保表结构存在。
        /// </summary>
        /// <param name="databasePath">数据库文件完整路径；为空时使用 <see cref="ResolveDatabasePath"/>。</param>
        public PartBasicInfoRepository(string databasePath)
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
        /// 解析数据库路径：App.config 的 PartDatabasePath（相对路径按程序目录）→ 默认 &lt;程序目录&gt;\Data\MSIF_Parts.db。
        /// </summary>
        public static string ResolveDatabasePath()
        {
            string configured = null;

            try
            {
                configured = ConfigurationManager.AppSettings[DatabasePathSettingKey];
            }
            catch (ConfigurationErrorsException)
            {
                configured = null;
            }

            if (!string.IsNullOrWhiteSpace(configured))
            {
                string value = configured.Trim();

                return Path.IsPathRooted(value)
                    ? value
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultDatabaseFolderName,
                DefaultDatabaseFileName);
        }

        /// <summary>
        /// 建表（已存在则跳过）并创建索引。
        ///
        /// 唯一键 = 図番 + 页码 + 番号 + 部品コード：
        /// 部品表里「下表参照」的行会被展开成多条同一番号、不同部品コード的明细
        /// （见 CadPartSplitter 的 01_アッセンブリ図_部品機能情報.csv，旧名 LT_STEP1.csv），
        /// 只用「図番 + 页码 + 番号」做唯一键会把它们互相顶掉。
        /// 旧版本留下的索引 <see cref="LegacyUniqueIndexName"/> 在这里删除并换成 <see cref="UniqueIndexName"/>，
        /// 已有数据库打开时自动迁移（旧索引保证旧数据在新索引下同样唯一）。
        /// 后加的列用 <see cref="EnsureColumn"/> 补（老数据库自动升级）；
        /// 改过名的列（枠区分→区分、員数→数量、来源标识→参照元）用
        /// <see cref="MigrateLegacyColumn"/> 改名，数据保留。
        /// </summary>
        private void EnsureSchema()
        {
            string createTable =
                "CREATE TABLE IF NOT EXISTS [" + TableName + "] (" +
                "[id] INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "[番号] INTEGER NOT NULL, " +
                "[" + MemberNameColumnName + "] TEXT, " +
                "[" + MemberNameAiColumnName + "] TEXT, " +
                "[部品名] TEXT, " +
                "[" + FrameKindColumnName + "] TEXT, " +
                "[" + QuantityColumnName + "] TEXT, " +
                "[切断寸法] TEXT, " +
                "[部品コード] TEXT, " +
                "[" + SourceTagColumnName + "] TEXT, " +
                "[" + HeightMinColumnName + "] TEXT, " +
                "[" + HeightMaxColumnName + "] TEXT, " +
                "[" + WidthMinColumnName + "] TEXT, " +
                "[" + WidthMaxColumnName + "] TEXT, " +
                "[" + UsageKindColumnName + "] TEXT, " +
                "[" + SpecialNoteColumnName + "] TEXT, " +
                "[図番] TEXT NOT NULL, " +
                "[页码] INTEGER NOT NULL DEFAULT 0, " +
                "[部品図番] TEXT, " +
                "[基本寸法] TEXT, " +
                "[校验状态] TEXT, " +
                "[备注] TEXT, " +
                "[" + SourceKindColumnName + "] TEXT, " +
                "[来源表格文件] TEXT, " +
                "[来源PDF] TEXT, " +
                "[登录时间] TEXT NOT NULL" +
                ");";

            string dropLegacyIndex = "DROP INDEX IF EXISTS [" + LegacyUniqueIndexName + "];";

            string createIndex =
                "CREATE UNIQUE INDEX IF NOT EXISTS [" + UniqueIndexName + "] ON [" + TableName +
                "] ([図番], [页码], [番号], [部品コード]);";

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                Execute(createTable, transaction);

                // 老库的列改名（只改名、数据保留）：枠区分 → 区分、員数 → 数量、来源标识 → 参照元
                MigrateLegacyColumn(transaction, LegacyFrameKindColumnName, FrameKindColumnName);
                MigrateLegacyColumn(transaction, LegacyQuantityColumnName, QuantityColumnName);
                MigrateLegacyColumn(transaction, LegacySourceTagColumnName, SourceTagColumnName);

                // 连这 3 列都没有的极老库：直接补上（改名那步不会做任何事）
                EnsureColumn(transaction, FrameKindColumnName, "TEXT");
                EnsureColumn(transaction, QuantityColumnName, "TEXT");

                bool sourceKindAdded = EnsureColumn(transaction, SourceKindColumnName, "TEXT");
                bool sourceTagAdded = EnsureColumn(transaction, SourceTagColumnName, "TEXT");

                // 后加的列：老库补上即可（历史上没有这项数据，不回填）
                EnsureColumn(transaction, MemberNameColumnName, "TEXT");
                EnsureColumn(transaction, MemberNameAiColumnName, "TEXT");
                EnsureColumn(transaction, HeightMinColumnName, "TEXT");
                EnsureColumn(transaction, HeightMaxColumnName, "TEXT");
                EnsureColumn(transaction, WidthMinColumnName, "TEXT");
                EnsureColumn(transaction, WidthMaxColumnName, "TEXT");
                EnsureColumn(transaction, UsageKindColumnName, "TEXT");
                EnsureColumn(transaction, SpecialNoteColumnName, "TEXT");

                if (sourceKindAdded)
                {
                    // 刚补的列：按「来源表格文件」把老数据的来源回填一次
                    BackfillSourceKind(transaction);
                }

                if (sourceTagAdded)
                {
                    // 刚补的列：先按「数据来源」换算，再按「来源表格文件」兜底
                    BackfillSourceTag(transaction);
                }

                // 统一「参照元 = 図番」「部材名 空 → その他」（老数据也一起对齐，可重复执行）
                NormalizeReferenceColumns(transaction);

                Execute(dropLegacyIndex, transaction);
                Execute(createIndex, transaction);
                transaction.Commit();
            }
        }

        /// <summary>
        /// 老数据库的列改名（只改名、数据保留）：旧列存在、且新列还不存在时执行一次。
        /// <c>ALTER TABLE ... RENAME COLUMN</c> 需要 SQLite 3.25+（本项目的 System.Data.SQLite 1.0.119 已支持）；
        /// 万一执行不了，退回「加新列 + 复制数据」，旧列留着不影响使用（唯一索引不含这些列）。
        /// </summary>
        /// <param name="legacyName">旧列名（枠区分 / 員数 / 来源标识）。</param>
        /// <param name="columnName">新列名（区分 / 数量 / 参照元）。</param>
        private void MigrateLegacyColumn(SQLiteTransaction transaction, string legacyName, string columnName)
        {
            if (string.IsNullOrEmpty(legacyName) || string.IsNullOrEmpty(columnName) ||
                string.Equals(legacyName, columnName, StringComparison.Ordinal))
            {
                return;
            }

            if (!ColumnExists(transaction, legacyName) || ColumnExists(transaction, columnName))
            {
                return;
            }

            try
            {
                Execute("ALTER TABLE [" + TableName + "] RENAME COLUMN [" + legacyName + "] TO [" +
                    columnName + "];", transaction);
            }
            catch (SQLiteException)
            {
                // 极老的 SQLite 不支持改名：加一列，再把旧列的数据复制过去
                EnsureColumn(transaction, columnName, "TEXT");
                Execute("UPDATE [" + TableName + "] SET [" + columnName + "] = [" + legacyName +
                    "] WHERE [" + columnName + "] IS NULL;", transaction);
            }
        }

        /// <summary>
        /// 给已存在的表补一个可空列（旧数据库升级用）：列已存在时什么都不做。
        /// 「数据来源」「参照元」「部材名」「H下限」等是后加的列，老数据库（本版本之前建的表）打开时在这里补上，
        /// 老数据该列保持 NULL，来源相关的列随后由 <see cref="BackfillSourceKind"/> /
        /// <see cref="BackfillSourceTag"/> 按来源文件（或已填好的「数据来源」）推断填值。
        /// </summary>
        /// <returns>true = 本次新加了列。</returns>
        private bool EnsureColumn(SQLiteTransaction transaction, string columnName, string columnType)
        {
            if (ColumnExists(transaction, columnName))
            {
                return false;
            }

            Execute("ALTER TABLE [" + TableName + "] ADD COLUMN [" + columnName + "] " + columnType + ";",
                transaction);

            return true;
        }

        /// <summary>判断表里是否已有指定列（用 PRAGMA table_info 取列清单）。</summary>
        private bool ColumnExists(SQLiteTransaction transaction, string columnName)
        {
            using (SQLiteCommand command = new SQLiteCommand(
                "PRAGMA table_info([" + TableName + "]);", _connection, transaction))
            {
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // PRAGMA table_info 的第 2 列（ordinal 1）是列名
                        if (!reader.IsDBNull(1) &&
                            string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 补上「数据来源」列后，按「来源表格文件」把老数据的来源回填一次（只填空值）。
        /// 规则与写入时的取值一致：<c>02_要領図_部品機能情報.csv</c>（旧名 <c>LT_STEP2.csv</c>）→ 要領図；
        /// <c>01_アッセンブリ図_部品機能情報.csv</c>（旧名 <c>LT_STEP1.csv</c>）→ アッセンブリ図；
        /// <c>plu_table.txt</c> → AI 分析。认不出来的（手工录入等）保持空。
        /// </summary>
        private void BackfillSourceKind(SQLiteTransaction transaction)
        {
            FillColumnBySourceFile(transaction, SourceKindColumnName, PartSourceKind.CmdYoryo,
                CmdInstructionPartCollector.YoryoPartFileName,
                CmdInstructionPartCollector.LegacyYoryoPartFileName);

            FillColumnBySourceFile(transaction, SourceKindColumnName, PartSourceKind.CmdAssembly,
                CmdPartAnalyzer.LtStep1FileName,
                CmdPartAnalyzer.LegacyLtStep1FileName);

            FillColumnBySourceFile(transaction, SourceKindColumnName, PartSourceKind.AiAnalysis,
                "plu_table.txt");
        }

        /// <summary>
        /// 补上「参照元」列后回填老数据（只填空值）：先按已有的「数据来源」换算（最准确），
        /// 仍是空的行再按「来源表格文件」推断（与 <see cref="BackfillSourceKind"/> 同一套规则）。
        /// </summary>
        private void BackfillSourceTag(SQLiteTransaction transaction)
        {
            FillColumnBySourceKind(transaction, SourceTagColumnName, PartSourceTag.CmdYoryo,
                PartSourceKind.CmdYoryo);

            FillColumnBySourceKind(transaction, SourceTagColumnName, PartSourceTag.CmdAssembly,
                PartSourceKind.CmdAssembly);

            FillColumnBySourceKind(transaction, SourceTagColumnName, PartSourceTag.AiAnalysis,
                PartSourceKind.AiAnalysis);

            FillColumnBySourceKind(transaction, SourceTagColumnName, PartSourceTag.Manual,
                PartSourceKind.Manual);

            FillColumnBySourceFile(transaction, SourceTagColumnName, PartSourceTag.CmdYoryo,
                CmdInstructionPartCollector.YoryoPartFileName,
                CmdInstructionPartCollector.LegacyYoryoPartFileName);

            FillColumnBySourceFile(transaction, SourceTagColumnName, PartSourceTag.CmdAssembly,
                CmdPartAnalyzer.LtStep1FileName,
                CmdPartAnalyzer.LegacyLtStep1FileName);

            FillColumnBySourceFile(transaction, SourceTagColumnName, PartSourceTag.AiAnalysis,
                "plu_table.txt");
        }

        /// <summary>
        /// 把「来源表格文件」路径里含任一文件名的行，其空的 <paramref name="columnName"/> 填成
        /// <paramref name="value"/>（只填空值，可重复执行）。
        /// </summary>
        private void FillColumnBySourceFile(SQLiteTransaction transaction, string columnName, string value,
            params string[] fileNames)
        {
            StringBuilder sql = new StringBuilder();

            sql.Append("UPDATE [").Append(TableName).Append("] SET [").Append(columnName)
               .Append("] = @value WHERE [").Append(columnName).Append("] IS NULL AND (");

            for (int index = 0; index < fileNames.Length; index++)
            {
                if (index > 0)
                {
                    sql.Append(" OR ");
                }

                sql.Append("[来源表格文件] LIKE @name").Append(index.ToString(CultureInfo.InvariantCulture));
            }

            sql.Append(");");

            using (SQLiteCommand command = new SQLiteCommand(sql.ToString(), _connection, transaction))
            {
                command.Parameters.Add(new SQLiteParameter("@value", value));

                for (int index = 0; index < fileNames.Length; index++)
                {
                    // 用参数（而不是拼接）传文件名，避免 LIKE 通配符被文件名影响
                    command.Parameters.Add(new SQLiteParameter(
                        "@name" + index.ToString(CultureInfo.InvariantCulture),
                        "%" + fileNames[index] + "%"));
                }

                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 把「数据来源」等于 <paramref name="sourceKind"/> 的行，其空的 <paramref name="columnName"/>
        /// 填成 <paramref name="value"/>（只填空值，可重复执行）。
        /// </summary>
        private void FillColumnBySourceKind(SQLiteTransaction transaction, string columnName, string value,
            string sourceKind)
        {
            string sql = "UPDATE [" + TableName + "] SET [" + columnName + "] = @value " +
                "WHERE [" + columnName + "] IS NULL AND [" + SourceKindColumnName + "] = @sourceKind;";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.Parameters.Add(new SQLiteParameter("@value", value));
                command.Parameters.Add(new SQLiteParameter("@sourceKind", sourceKind));
                command.ExecuteNonQuery();
            }
        }

        private static string BuildInsertSql()
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("INSERT OR REPLACE INTO [").Append(TableName).Append("] (");

            for (int index = 0; index < ColumnNames.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('[').Append(ColumnNames[index]).Append(']');
            }

            builder.Append(") VALUES (");

            for (int index = 0; index < ColumnNames.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('@').Append(ColumnNames[index]);
            }

            builder.Append(");");

            return builder.ToString();
        }

        /// <summary>
        /// 把一条部品基本信息绑到 <see cref="InsertSql"/> 的参数上。
        /// 参数名 = 列名（顺序见 <see cref="ColumnNames"/>），新增列时只改这里 + <see cref="ColumnNames"/>。
        /// </summary>
        private static void BindInsertParameters(SQLiteCommand command, PartBasicInfo part, string drawingNo,
            string loginTime)
        {
            command.Parameters["@図番"].Value = drawingNo;
            command.Parameters["@页码"].Value = part.Page;
            command.Parameters["@番号"].Value = part.Number;
            command.Parameters["@部材名"].Value = ResolveMemberName(part.MemberName);
            command.Parameters["@部材名AI推荐"].Value = part.MemberNameAiSuggestion ?? string.Empty;
            command.Parameters["@部品名"].Value = part.PartName;
            command.Parameters["@区分"].Value = part.FrameKind;
            command.Parameters["@数量"].Value = part.Quantity;
            command.Parameters["@切断寸法"].Value = part.CutSize;
            command.Parameters["@部品コード"].Value = part.PartCode;
            // 「参照元」= 该行的「図番」（不再写 要領図 / tz.json 之类的来源简称）。
            // 来源分类看「数据来源」列（PartBasicInfo.SourceKind）。
            command.Parameters["@参照元"].Value = string.IsNullOrWhiteSpace(drawingNo)
                ? part.SourceTag ?? string.Empty
                : drawingNo.Trim();
            command.Parameters["@H下限"].Value = part.HeightMin ?? string.Empty;
            command.Parameters["@H上限"].Value = part.HeightMax ?? string.Empty;
            command.Parameters["@W下限"].Value = part.WidthMin ?? string.Empty;
            command.Parameters["@W上限"].Value = part.WidthMax ?? string.Empty;
            command.Parameters["@使い分け"].Value = part.UsageKind ?? string.Empty;
            command.Parameters["@特注"].Value = part.SpecialNote ?? string.Empty;
            command.Parameters["@部品図番"].Value = part.PartDrawingNo;
            command.Parameters["@基本寸法"].Value = part.BasicSize;
            command.Parameters["@校验状态"].Value = part.VerifyStatus;
            command.Parameters["@备注"].Value = part.Remark;
            command.Parameters["@数据来源"].Value = part.SourceKind ?? string.Empty;
            command.Parameters["@来源表格文件"].Value = part.SourceFile;
            command.Parameters["@来源PDF"].Value = part.PdfFile;
            command.Parameters["@登录时间"].Value = loginTime;
        }

        /// <summary>
        /// 「部材名」为空（NULL / 空串 / 只有空白）时返回 <see cref="PartBasicInfo.MemberNameFallback"/>＝「その他」，
        /// 否则返回去掉首尾空白的原值。写库前统一走这里，保证画面上不会出现空的「部材名」。
        /// </summary>
        private static string ResolveMemberName(string memberName)
        {
            return string.IsNullOrWhiteSpace(memberName)
                ? PartBasicInfo.MemberNameFallback
                : memberName.Trim();
        }

        /// <summary>
        /// 把已有数据对齐到「参照元 = 図番」「部材名 空 → その他」（可重复执行，第二次不会再改任何行）：
        /// <list type="number">
        ///   <item>「参照元」不等于「図番」的行一律改成该行的「図番」——
        ///         以前这里写的是 要領図 / アッセンブリ図 / tz.json 之类的来源简称，
        ///         现在来源分类看「数据来源」列；</item>
        ///   <item>「部材名」为 NULL / 空串 / 只有空白的行补成 <see cref="PartBasicInfo.MemberNameFallback"/>。</item>
        /// </list>
        /// 发生在 <see cref="EnsureSchema"/> 里，所以老数据库一打开就对齐，画面上不会再看到旧值。
        /// </summary>
        private void NormalizeReferenceColumns(SQLiteTransaction transaction)
        {
            Execute("UPDATE [" + TableName + "] SET [" + SourceTagColumnName + "] = [図番] " +
                "WHERE IFNULL([" + SourceTagColumnName + "], '') <> [図番];", transaction);

            Execute("UPDATE [" + TableName + "] SET [" + MemberNameColumnName + "] = '" +
                PartBasicInfo.MemberNameFallback + "' WHERE IFNULL(TRIM(REPLACE([" +
                MemberNameColumnName + "], char(12288), ' ')), '') = '';", transaction);
        }

        private void Execute(string sql, SQLiteTransaction transaction)
        {
            using (SQLiteCommand command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 登录一批部品基本信息（同一 図番 + 页码 + 番号 覆盖旧值），返回实际写入条数。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        /// <param name="parts">要写入的部品基本信息。</param>
        public int Save(string drawingNo, IList<PartBasicInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(drawingNo))
            {
                throw new ArgumentException("図番不能为空。", nameof(drawingNo));
            }

            if (parts == null || parts.Count == 0)
            {
                return 0;
            }

            string loginTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string key = drawingNo.Trim();
            int written = 0;

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                // 同一図番先清空，保证本次结果完整覆盖上次结果（避免残留旧行）
                using (SQLiteCommand deleteCommand = new SQLiteCommand(
                    "DELETE FROM [" + TableName + "] WHERE [図番] = @図番;", _connection, transaction))
                {
                    deleteCommand.Parameters.Add(new SQLiteParameter("@図番", key));
                    deleteCommand.ExecuteNonQuery();
                }

                using (SQLiteCommand command = new SQLiteCommand(InsertSql, _connection, transaction))
                {
                    foreach (string column in ColumnNames)
                    {
                        command.Parameters.Add(new SQLiteParameter("@" + column));
                    }

                    foreach (PartBasicInfo part in parts)
                    {
                        BindInsertParameters(command, part, key, loginTime);

                        written += command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return written;
        }

        /// <summary>
        /// 用给定内容整体替换某図番的部品明细（供画面手工编辑后保存）。
        /// 与 <see cref="Save"/> 的区别：本次未修改的行保留原「登录时间」，新增行才写入当前时间；
        /// <paramref name="parts"/> 为空表示清空该図番的全部明细。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        /// <param name="parts">画面上当前的部品明细（全部行）。</param>
        /// <returns>实际写入的条数。</returns>
        public int ReplaceByDrawing(string drawingNo, IList<PartBasicInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(drawingNo))
            {
                throw new ArgumentException("図番不能为空。", nameof(drawingNo));
            }

            string key = drawingNo.Trim();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            int written = 0;

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                // 先清空该図番，保证画面上的删除操作真正生效（避免残留旧行）
                using (SQLiteCommand deleteCommand = new SQLiteCommand(
                    "DELETE FROM [" + TableName + "] WHERE [図番] = @図番;", _connection, transaction))
                {
                    deleteCommand.Parameters.Add(new SQLiteParameter("@図番", key));
                    deleteCommand.ExecuteNonQuery();
                }

                if (parts != null && parts.Count > 0)
                {
                    using (SQLiteCommand command = new SQLiteCommand(InsertSql, _connection, transaction))
                    {
                        foreach (string column in ColumnNames)
                        {
                            command.Parameters.Add(new SQLiteParameter("@" + column));
                        }

                        foreach (PartBasicInfo part in parts)
                        {
                            if (part == null)
                            {
                                continue;
                            }

                            string loginTime = string.IsNullOrWhiteSpace(part.LoginTime)
                                ? now
                                : part.LoginTime.Trim();

                            BindInsertParameters(command, part, key, loginTime);

                            written += command.ExecuteNonQuery();
                        }
                    }
                }

                transaction.Commit();
            }

            return written;
        }

        /// <summary>统计指定図番已经登录的条数。</summary>
        public int Count(string drawingNo)
        {
            string sql = "SELECT COUNT(1) FROM [" + TableName + "] WHERE [図番] = @図番;";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                command.Parameters.Add(new SQLiteParameter("@図番", (drawingNo ?? string.Empty).Trim()));

                object result = command.ExecuteScalar();

                return result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>统计表内全部条数。</summary>
        public int CountAll()
        {
            string sql = "SELECT COUNT(1) FROM [" + TableName + "];";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                object result = command.ExecuteScalar();

                return result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 只更新「使い分け」列（【CMD 分析】第 6 步 使い分け自動設定 用）。
        /// 定位键 = 図番 + 页码 + 番号 + 部品コード（与唯一索引一致），其它列一概不动，
        /// 避免整行重写把「登录时间」「校验状态」「部品名」等一起改掉。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        /// <param name="parts">使い分け 已设好的行（只读 <see cref="PartBasicInfo.UsageKind"/> 与定位键）。</param>
        /// <returns>实际更新的行数。</returns>
        public int UpdateUsageKinds(string drawingNo, IList<PartBasicInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(drawingNo) || parts == null || parts.Count == 0)
            {
                return 0;
            }

            string key = drawingNo.Trim();
            int updated = 0;

            string sql =
                "UPDATE [" + TableName + "] SET [" + UsageKindColumnName + "] = @値 " +
                "WHERE [図番] = @図番 AND [页码] = @页码 AND [番号] = @番号 AND [" +
                PartCodeColumnName + "] = @部品コード;";

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                using (SQLiteCommand command = new SQLiteCommand(sql, _connection, transaction))
                {
                    command.Parameters.Add(new SQLiteParameter("@値"));
                    command.Parameters.Add(new SQLiteParameter("@図番"));
                    command.Parameters.Add(new SQLiteParameter("@页码"));
                    command.Parameters.Add(new SQLiteParameter("@番号"));
                    command.Parameters.Add(new SQLiteParameter("@部品コード"));

                    foreach (PartBasicInfo part in parts)
                    {
                        if (part == null || string.IsNullOrWhiteSpace(part.UsageKind))
                        {
                            continue;
                        }

                        command.Parameters["@値"].Value = part.UsageKind.Trim();
                        command.Parameters["@図番"].Value = key;
                        command.Parameters["@页码"].Value = part.Page;
                        command.Parameters["@番号"].Value = part.Number;
                        command.Parameters["@部品コード"].Value = part.PartCode ?? string.Empty;

                        updated += command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return updated;
        }

        /// <summary>
        /// 只更新「部材名AI推荐」列（【AI 分析】用）。
        /// 定位键 = 図番 + 页码 + 番号 + 部品コード（与唯一索引一致），其它列一概不动，
        /// 所以「部材名」「登录时间」「校验状态」等不会被推荐值连带改掉。
        /// 推荐值为空的行跳过（不会被清空已有推荐）。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        /// <param name="parts">推荐值已设好的行（只读 <see cref="PartBasicInfo.MemberNameAiSuggestion"/> 与定位键）。</param>
        /// <returns>实际更新的行数。</returns>
        public int UpdateMemberNameSuggestions(string drawingNo, IList<PartBasicInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(drawingNo) || parts == null || parts.Count == 0)
            {
                return 0;
            }

            string key = drawingNo.Trim();
            int updated = 0;

            string sql =
                "UPDATE [" + TableName + "] SET [" + MemberNameAiColumnName + "] = @推荐値 " +
                "WHERE [図番] = @図番 AND [页码] = @页码 AND [番号] = @番号 AND [" +
                PartCodeColumnName + "] = @部品コード;";

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                using (SQLiteCommand command = new SQLiteCommand(sql, _connection, transaction))
                {
                    command.Parameters.Add(new SQLiteParameter("@推荐値"));
                    command.Parameters.Add(new SQLiteParameter("@図番"));
                    command.Parameters.Add(new SQLiteParameter("@页码"));
                    command.Parameters.Add(new SQLiteParameter("@番号"));
                    command.Parameters.Add(new SQLiteParameter("@部品コード"));

                    foreach (PartBasicInfo part in parts)
                    {
                        if (part == null || string.IsNullOrWhiteSpace(part.MemberNameAiSuggestion))
                        {
                            continue;
                        }

                        command.Parameters["@推荐値"].Value = part.MemberNameAiSuggestion.Trim();
                        command.Parameters["@図番"].Value = key;
                        command.Parameters["@页码"].Value = part.Page;
                        command.Parameters["@番号"].Value = part.Number;
                        command.Parameters["@部品コード"].Value = part.PartCode ?? string.Empty;

                        updated += command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return updated;
        }

        /// <summary>
        /// 只更新「部品コード」列（【CMD 分析】第 5 步 部品コード正規化 用）。
        /// 定位键 = 図番 + 页码 + 番号 + 原部品コード（与唯一索引一致），其它列一概不动。
        ///
        /// <para>
        /// 换掉部品コード 会改变定位键，所以调用方必须保证「换完以后不会和别的行撞键」——
        /// 撞键的那一行会被 SQLite 的唯一索引拒绝，这里逐行 try/catch 跳过并计入 <paramref name="failedRowCount"/>，
        /// 其余行照常写入（不会整批回滚）。
        /// </para>
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        /// <param name="updates">要更新的行（原 / 新部品コード 与定位键）。</param>
        /// <param name="failedRowCount">因唯一索引冲突等原因没写成功的行数。</param>
        /// <returns>实际更新的行数。</returns>
        public int UpdatePartCodes(string drawingNo, IList<PartCodeUpdate> updates, out int failedRowCount)
        {
            failedRowCount = 0;

            if (string.IsNullOrWhiteSpace(drawingNo) || updates == null || updates.Count == 0)
            {
                return 0;
            }

            string key = drawingNo.Trim();
            int updated = 0;

            string sql =
                "UPDATE [" + TableName + "] SET [" + PartCodeColumnName + "] = @新部品コード " +
                "WHERE [図番] = @図番 AND [页码] = @页码 AND [番号] = @番号 AND [" +
                PartCodeColumnName + "] = @旧部品コード;";

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                using (SQLiteCommand command = new SQLiteCommand(sql, _connection, transaction))
                {
                    command.Parameters.Add(new SQLiteParameter("@新部品コード"));
                    command.Parameters.Add(new SQLiteParameter("@図番"));
                    command.Parameters.Add(new SQLiteParameter("@页码"));
                    command.Parameters.Add(new SQLiteParameter("@番号"));
                    command.Parameters.Add(new SQLiteParameter("@旧部品コード"));

                    foreach (PartCodeUpdate update in updates)
                    {
                        if (update == null ||
                            string.Equals(update.OldPartCode, update.NewPartCode, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        command.Parameters["@新部品コード"].Value = update.NewPartCode;
                        command.Parameters["@図番"].Value = key;
                        command.Parameters["@页码"].Value = update.Page;
                        command.Parameters["@番号"].Value = update.Number;
                        command.Parameters["@旧部品コード"].Value = update.OldPartCode ?? string.Empty;

                        try
                        {
                            updated += command.ExecuteNonQuery();
                        }
                        catch (SQLiteException)
                        {
                            // 换完和同図番 / 同页码 / 同番号 的另一行撞键：跳过这一行，其它行继续
                            failedRowCount++;
                        }
                    }
                }

                transaction.Commit();
            }

            return updated;
        }

        /// <summary>
        /// 读取指定図番已经登录的全部部品基本信息（按页码、番号排序）。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        public IList<PartBasicInfo> Query(string drawingNo)
        {
            List<PartBasicInfo> parts = new List<PartBasicInfo>();

            string sql =
                "SELECT " + JoinColumnNames() + " FROM [" + TableName + "] WHERE [図番] = @図番 " +
                "ORDER BY [页码], [番号];";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                command.Parameters.Add(new SQLiteParameter("@図番", (drawingNo ?? string.Empty).Trim()));

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        PartBasicInfo part = new PartBasicInfo(
                            ReadText(reader, "図番"),
                            ReadInt32(reader, "页码"),
                            ReadInt32(reader, "番号"),
                            ReadText(reader, "部品名"),
                            ReadText(reader, "部品図番"),
                            ReadText(reader, "区分"),
                            ReadText(reader, "数量"),
                            ReadText(reader, "基本寸法"),
                            ReadText(reader, "切断寸法"),
                            ReadText(reader, "部品コード"));

                        part.MemberName = ReadText(reader, "部材名");
                        part.MemberNameAiSuggestion = ReadText(reader, MemberNameAiColumnName);
                        part.HeightMin = ReadText(reader, "H下限");
                        part.HeightMax = ReadText(reader, "H上限");
                        part.WidthMin = ReadText(reader, "W下限");
                        part.WidthMax = ReadText(reader, "W上限");
                        part.UsageKind = ReadText(reader, "使い分け");
                        part.SpecialNote = ReadText(reader, "特注");
                        part.VerifyStatus = ReadText(reader, "校验状态");
                        part.Remark = ReadText(reader, "备注");
                        part.SourceKind = ReadText(reader, "数据来源");
                        part.SourceTag = ReadText(reader, "参照元");
                        part.SourceFile = ReadText(reader, "来源表格文件");
                        part.PdfFile = ReadText(reader, "来源PDF");
                        part.LoginTime = ReadText(reader, "登录时间");

                        parts.Add(part);
                    }
                }
            }

            return parts;
        }

        /// <summary>从 PDF 路径解析図番（文件名去扩展名，与 AI 分析登录时一致）。</summary>
        public static string ResolveDrawingNo(string pdfFilePath)
        {
            return string.IsNullOrWhiteSpace(pdfFilePath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(pdfFilePath.Trim());
        }

        /// <summary>SELECT 的列清单（= <see cref="ColumnNames"/> 的全部列，顺序相同）。</summary>
        private static string JoinColumnNames()
        {
            StringBuilder builder = new StringBuilder();

            for (int index = 0; index < ColumnNames.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('[').Append(ColumnNames[index]).Append(']');
            }

            return builder.ToString();
        }

        /// <summary>按列名读取可空文本列（NULL 视为空字符串；数字 / 其它类型一律转成文本）。</summary>
        private static string ReadText(SQLiteDataReader reader, string columnName)
        {
            object value = reader.GetValue(reader.GetOrdinal(columnName));

            return value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>按列名读取整数列（NULL / 空串 / 非数字一律视为 0）。</summary>
        private static int ReadInt32(SQLiteDataReader reader, string columnName)
        {
            object value = reader.GetValue(reader.GetOrdinal(columnName));
            string text = value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture);
            int number;

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
        }

        public void Dispose()
        {
            if (_connection != null)
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                {
                    _connection.Close();
                }

                _connection.Dispose();
            }
        }
    }
}
