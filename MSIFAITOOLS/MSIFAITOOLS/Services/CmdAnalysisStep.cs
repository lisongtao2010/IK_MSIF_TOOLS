using System.Collections.Generic;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 【CMD 分析】内部的一步：编号 + 代号 + 名字 + 一句话说明。
    /// 只是「命名」用的只读数据，<b>不参与任何分析逻辑</b>。
    /// </summary>
    internal sealed class CmdAnalysisStep
    {
        /// <summary>显示名（空 = 用「第 N 步 · 名字」）。</summary>
        private readonly string _label;

        public CmdAnalysisStep(int number, string code, string name, string purpose)
            : this(number, code, name, purpose, null)
        {
        }

        /// <summary>
        /// 同上，但显示名可以另给（<paramref name="label"/> 非空时 <see cref="Label"/> 直接用它）。
        /// 用于「不占第 N 步编号、但要排在所有步骤最前面」的步骤（最前面的「特注品 CSV 生成」）。
        /// </summary>
        public CmdAnalysisStep(int number, string code, string name, string purpose, string label)
        {
            Number = number;
            Code = code == null ? string.Empty : code.Trim();
            Name = name == null ? string.Empty : name.Trim();
            Purpose = purpose == null ? string.Empty : purpose.Trim();
            _label = label == null ? string.Empty : label.Trim();
        }

        /// <summary>
        /// 步骤号：<c>-1</c> = 最前面（特注品 CSV 生成，显示名单独给、不占编号）；
        /// <c>0</c> = 按钮执行之前的准备；<c>1 / 2 / 3 / 4 / 5 / 6</c> = 点一次【CMD 分析】后依次执行的步骤。
        /// </summary>
        public int Number { get; private set; }

        /// <summary>代号（<c>S0C</c> / <c>S0</c> / <c>S1</c> / … / <c>S6</c>），口头简称用。</summary>
        public string Code { get; private set; }

        /// <summary>步骤名（日志与文档统一用这一个名字）。</summary>
        public string Name { get; private set; }

        /// <summary>一句话说明这一步做什么。</summary>
        public string Purpose { get; private set; }

        /// <summary>步骤标签：<c>第 1 步 · 装配图部品入库</c>（<see cref="CmdAnalysisSteps.SpecialProductCsvStep"/> 用自定义显示名）。</summary>
        public string Label
        {
            get
            {
                return _label.Length > 0 ? _label : string.Format("第 {0} 步 · {1}", Number, Name);
            }
        }

        /// <summary>日志前缀：<c>【第 1 步 · 装配图部品入库】</c>。</summary>
        public string LogHeader
        {
            get { return "【" + Label + "】"; }
        }

        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 【CMD 分析】按钮的步骤划分 —— <b>步骤名的唯一定义处</b>。
    /// <see cref="CmdPartAnalysisService"/> 的日志、界面提示与
    /// <c>docs\CMD分析_步骤命名.md</c> 都按这里的名字写，避免各处叫法不一致。
    ///
    /// 点一次【CMD 分析】依次执行<b>最前面 1 步 + 第 1~6 步</b>（按钮名字不变，仍是「一次性跑完」）：
    /// <list type="number">
    ///   <item><b>最前面 · 特注品 CSV 生成</b>（<see cref="CmdSpecialProductCsvExporter"/>，代号 <c>S0C</c>，
    ///         不占「第 N 步」编号）：遍历 PDF 文件夹下的 <c>*.xlsx</c>，文件名第 5 位是 <c>Z</c>
    ///         （= 特注品）的读部品表，生成 <c>AiAppData\&lt;文件名&gt;\特注品.csv</c>
    ///         （品種 / 部品名 / 枠区分 / 部品コード / 員数 / 追加対応特注寸法，取不到值的列留空）。
    ///         <b>不依赖数据库</b>，所以排在所有步骤之前；</item>
    ///   <item><b>第 1 步 装配图部品入库</b>（旧称「阶段 A」，<see cref="CmdPartAnalyzer"/>）：
    ///         读 <c>01_アッセンブリ図_部品機能情報.csv</c> → 建行 → <c>xy.txt</c> 坐标校验 → 入库；</item>
    ///   <item><b>第 2 步 圈号拆分</b>（旧称「阶段 A2」，<see cref="CmdCalloutPartSplitter"/>）：
    ///         按 <c>parts_info.json</c> 的圈号（<c>callouts</c>）把装配图部品按位置拆成多条；</item>
    ///   <item><b>第 3 步 要領図部品追加</b>（旧称「阶段 B」，<see cref="CmdInstructionPartCollector"/>）：
    ///         顺着 <c>01_アッセンブリ図_要領図.csv</c> 读各要領図的
    ///         <c>02_要領図_部品機能情報.csv</c>，把部品挂到装配图名下。</item>
    ///   <item><b>第 4 步 tz.json 部品追加</b>（<see cref="CmdTzPartCollector"/>，步骤名沿用旧名）：
    ///         读 <c>classify\PdfInfoList.txt</c> 里「アッセンブリ図」的分类依据，
    ///         把最前面那步生成的 <c>AiAppData\&lt;文件名&gt;\特注品.csv</c>（Excel 部品表，
    ///         <b>不再解析 Excel</b>）中「品種」命中该依据的明细挂到同一装配图名下
    ///         （一个 CSV 都没有时才退回读 <c>classify\tz.json</c>）。</item>
    ///   <item><b>第 5 步 部品コード正規化</b>（<see cref="CmdPartCodeNormalizer"/>）：
    ///         用 <c>部品マスタ\Tbl_部品マスタ.csv</c> 的「色」把部品コード 里的颜色换成 <c>□</c>
    ///         （<c>MEACC429</c> → <c>MEA□429</c>），再把连续的符号（<c>□□</c> / <c>〇〇</c> / <c>△△</c> …）
    ///         压成一个 <c>□</c>（<c>MEA□□405</c> → <c>MEA□405</c>）。只改「部品コード」一列；
    ///         换完会和同図番 / 同页码 / 同番号 的另一行撞键时，那一行跳过并记警告（= 重複チェック）。</item>
    ///   <item><b>第 6 步 使い分け自動設定</b>（<see cref="CmdUsageKindCollector"/>）：
    ///         同一図番内「区分 / 参照元 / 部品コード / H下限 / H上限 / W下限 / W上限」全相同的多行，
    ///         按「部品コード」查 <c>hiroidashiData</c> 里的 <c>*.xlsx</c> 第 4 列（部品ｺｰﾄﾞ/形材番号）相同的行，
    ///         把第 12 列（使い分け）的值按顺序填到这些行上。
    ///         <b>放在第 5 步之后</b>：这时部品コード 已经是正規化后的写法（<c>MEA□429</c>），
    ///         和拾い出し表里带 <c>□</c> 的写法一致，查表最准。</item>
    /// </list>
    /// 这 7 步之前还有一步不属于本按钮：<b>第 0 步 分解与分类</b>
    /// （【开始分解】= CadPartSplitter 分割 + 本工具分解 + <c>classify\PdfInfoList.txt</c>）。
    /// </summary>
    internal static class CmdAnalysisSteps
    {
        /// <summary>
        /// 「最前面」步骤（特注品 CSV 生成）的编号：<c>-1</c> 表示它<b>不占第 1~6 步的编号</b>、
        /// 排在所有步骤之前（枚举里只要和 0 / 1~6 不重复即可；显示名用
        /// <see cref="SpecialProductCsvLabel"/>，日志里不会出现「第 -1 步」）。
        /// </summary>
        public const int SpecialProductCsvNumber = -1;

        /// <summary>「最前面」步骤的代号。</summary>
        public const string SpecialProductCsvCode = "S0C";

        /// <summary>「最前面」步骤的名字。</summary>
        public const string SpecialProductCsvName = "特注品 CSV 生成";

        /// <summary>「最前面」步骤的显示名（不带「第 N 步」，因为它排在 1~6 之前）。</summary>
        public const string SpecialProductCsvLabel = "最前面 · 特注品 CSV 生成";

        /// <summary>第 0 步（按钮外）的编号。</summary>
        public const int PrepareNumber = 0;

        /// <summary>第 0 步（按钮外）的代号。</summary>
        public const string PrepareCode = "S0";

        /// <summary>第 0 步（按钮外）的名字。</summary>
        public const string PrepareName = "分解与分类";

        /// <summary>第 1 步的编号。</summary>
        public const int AssemblyNumber = 1;

        /// <summary>第 1 步的代号。</summary>
        public const string AssemblyCode = "S1";

        /// <summary>第 1 步的名字（旧称「阶段 A」）。</summary>
        public const string AssemblyName = "装配图部品入库";

        /// <summary>第 2 步的编号。</summary>
        public const int CalloutSplitNumber = 2;

        /// <summary>第 2 步的代号。</summary>
        public const string CalloutSplitCode = "S2";

        /// <summary>第 2 步的名字（旧称「阶段 A2」）。</summary>
        public const string CalloutSplitName = "圈号拆分";

        /// <summary>第 3 步的编号。</summary>
        public const int YoryoAppendNumber = 3;

        /// <summary>第 3 步的代号。</summary>
        public const string YoryoAppendCode = "S3";

        /// <summary>第 3 步的名字（旧称「阶段 B」）。</summary>
        public const string YoryoAppendName = "要領図部品追加";

        /// <summary>第 4 步的编号。</summary>
        public const int TzAppendNumber = 4;

        /// <summary>第 4 步的代号。</summary>
        public const string TzAppendCode = "S4";

        /// <summary>第 4 步的名字。</summary>
        public const string TzAppendName = "tz.json 部品追加";

        /// <summary>第 5 步的编号（旧「第 6 步」）。</summary>
        public const int PartCodeNumber = 5;

        /// <summary>第 5 步的代号。</summary>
        public const string PartCodeCode = "S5";

        /// <summary>第 5 步的名字（按部品マスタ 把部品コード 正規化，含重複チェック）。</summary>
        public const string PartCodeName = "部品コード正規化";

        /// <summary>第 6 步的编号（旧「第 5 步」）。</summary>
        public const int UsageKindNumber = 6;

        /// <summary>第 6 步的代号。</summary>
        public const string UsageKindCode = "S6";

        /// <summary>第 6 步的名字（按拾い出し表给「使い分け」列赋值）。</summary>
        public const string UsageKindName = "使い分け自動設定";

        /// <summary>
        /// 最前面：特注品 CSV 生成（文件名第 5 位是 <c>Z</c> 的 xlsx → 读部品表 →
        /// <c>AiAppData\&lt;文件名&gt;\特注品.csv</c>）。排在第 1 步之前，只有这一个步骤不依赖数据库。
        /// </summary>
        public static readonly CmdAnalysisStep SpecialProductCsvStep = new CmdAnalysisStep(
            SpecialProductCsvNumber, SpecialProductCsvCode, SpecialProductCsvName,
            "遍历 PDF 文件夹下的 xlsx：文件名第 5 位是 Z 的 → 读部品表 → 写 AiAppData\\<文件名>\\特注品.csv" +
            "（品種 / 部品名 / 枠区分 / 部品コード / 員数 / 追加対応特注寸法，取不到值的列留空；第 4 步直接读这个 CSV）",
            SpecialProductCsvLabel);

        /// <summary>第 0 步：分解与分类（按钮外，由【开始分解】完成）。</summary>
        public static readonly CmdAnalysisStep PrepareStep = new CmdAnalysisStep(PrepareNumber, PrepareCode,
            PrepareName, "【开始分解】调用 CadPartSplitter 分割并生成分类清单 PdfInfoList.txt");

        /// <summary>第 1 步：装配图部品入库。</summary>
        public static readonly CmdAnalysisStep AssemblyStep = new CmdAnalysisStep(AssemblyNumber, AssemblyCode,
            AssemblyName, "读 01_アッセンブリ図_部品機能情報.csv → 建行 → xy.txt 校验 → 入库");

        /// <summary>第 2 步：圈号拆分。</summary>
        public static readonly CmdAnalysisStep CalloutSplitStep = new CmdAnalysisStep(CalloutSplitNumber,
            CalloutSplitCode, CalloutSplitName, "按 parts_info.json 的圈号把装配图部品按位置拆成多条");

        /// <summary>第 3 步：要領図部品追加。</summary>
        public static readonly CmdAnalysisStep YoryoAppendStep = new CmdAnalysisStep(YoryoAppendNumber,
            YoryoAppendCode, YoryoAppendName, "读 01_アッセンブリ図_要領図.csv → 各 02_要領図_部品機能情報.csv → 追加建行");

        /// <summary>第 4 步：tz.json 部品追加（明细优先读 特注品.csv，见 <see cref="CmdTzPartCollector"/>）。</summary>
        public static readonly CmdAnalysisStep TzAppendStep = new CmdAnalysisStep(TzAppendNumber,
            TzAppendCode, TzAppendName,
            "读最前面生成的 AiAppData\\<文件名>\\特注品.csv（Excel 部品表，不再解析 Excel）→ 品種命中分类依据的明细 → 追加建行" +
            "（没有 CSV 时退回 classify\\tz.json）");

        /// <summary>第 5 步：部品コード正規化（含同図番内の重複チェック）。</summary>
        public static readonly CmdAnalysisStep PartCodeStep = new CmdAnalysisStep(PartCodeNumber,
            PartCodeCode, PartCodeName,
            "按 部品マスタ\\Tbl_部品マスタ.csv 的「色」把部品コード 里的颜色换成 □，再把连续的符号（□□ / 〇〇 / △△ …）压成一个 □（撞键的行跳过 = 重複チェック）");

        /// <summary>第 6 步：使い分け自動設定（正規化後の部品コード で拾い出し表を引く）。</summary>
        public static readonly CmdAnalysisStep UsageKindStep = new CmdAnalysisStep(UsageKindNumber,
            UsageKindCode, UsageKindName,
            "同一図番内「区分 / 参照元 / 部品コード / H・W 上下限」全同的多行 → 按部品コード查 hiroidashiData 的 xlsx（第 4 列）→ 依次填入第 12 列「使い分け」");

        /// <summary>全部步骤（第 0 步在前），供文档 / 界面提示列出。</summary>
        public static IList<CmdAnalysisStep> All
        {
            get
            {
                return new CmdAnalysisStep[]
                {
                    SpecialProductCsvStep, PrepareStep, AssemblyStep, CalloutSplitStep, YoryoAppendStep,
                    TzAppendStep, PartCodeStep, UsageKindStep
                };
            }
        }

        /// <summary>
        /// 日志里的「执行步骤」计划行：
        /// <c>执行步骤：共 7 步 —— 最前面 · 特注品 CSV 生成 → 第 1 步 · 装配图部品入库 → …
        /// → 第 6 步 · 使い分け自動設定</c>。
        /// 「步数」= 最前面 1 步 + 第 1~6 步（第 0 步 分解与分类不属于本按钮，不计入）。
        /// </summary>
        public static string PlanText
        {
            get
            {
                return string.Format("执行步骤：共 {0} 步 —— {1} → {2} → {3} → {4} → {5} → {6} → {7}",
                    UsageKindNumber + 1, SpecialProductCsvStep.Label, AssemblyStep.Label,
                    CalloutSplitStep.Label, YoryoAppendStep.Label, TzAppendStep.Label, PartCodeStep.Label,
                    UsageKindStep.Label);
            }
        }

        /// <summary>
        /// 按 <see cref="CmdAnalysisStepKind"/> 取步骤对象；<see cref="CmdAnalysisStepKind.All"/> 返回 <c>null</c>
        /// （表示「全部步骤」，不对应单一步骤）。
        /// </summary>
        public static CmdAnalysisStep GetStep(CmdAnalysisStepKind kind)
        {
            switch (kind)
            {
                case CmdAnalysisStepKind.SpecialProductCsv:
                    return SpecialProductCsvStep;

                case CmdAnalysisStepKind.Assembly:
                    return AssemblyStep;

                case CmdAnalysisStepKind.CalloutSplit:
                    return CalloutSplitStep;

                case CmdAnalysisStepKind.YoryoAppend:
                    return YoryoAppendStep;

                case CmdAnalysisStepKind.TzAppend:
                    return TzAppendStep;

                case CmdAnalysisStepKind.UsageKind:
                    return UsageKindStep;

                case CmdAnalysisStepKind.PartCode:
                    return PartCodeStep;

                default:
                    return null;
            }
        }

        /// <summary>
        /// 日志里的「本次执行哪几步」计划行：
        /// 全跑时是 <see cref="PlanText"/>，单步执行时是
        /// <c>执行步骤：仅 第 2 步 · 圈号拆分（分步执行）</c>。
        /// </summary>
        public static string DescribeRun(CmdAnalysisStepKind kind)
        {
            CmdAnalysisStep step = GetStep(kind);

            return step == null
                ? PlanText
                : string.Format("执行步骤：仅 {0}（分步执行）", step.Label);
        }

        /// <summary>
        /// 分步执行的按钮文字（主按钮传 <see cref="CmdAnalysisStepKind.All"/> 时返回 <c>CMD 分析</c>）。
        /// 名字本身已带编号，界面直接用这个名字，保证按钮 / 日志 / 文档叫法一致。
        /// </summary>
        public static string ButtonText(CmdAnalysisStepKind kind)
        {
            CmdAnalysisStep step = GetStep(kind);

            return step == null ? "CMD 分析" : step.Label;
        }
    }

    /// <summary>
    /// 【CMD 分析】要执行哪一步：主按钮走 <see cref="All"/>，界面上的六个子按钮各走一步。
    /// 值取自 <see cref="CmdAnalysisSteps"/> 的步骤编号，保证「第 N 步」只有一处定义。
    /// </summary>
    internal enum CmdAnalysisStepKind
    {
        /// <summary>一次跑完 6 步（= 主按钮【CMD 分析】，旧行为）。</summary>
        All = 0,

        /// <summary>最前面（不属于第 1~6 步）：特注品 CSV 生成。</summary>
        SpecialProductCsv = CmdAnalysisSteps.SpecialProductCsvNumber,

        /// <summary>第 1 步 装配图部品入库。</summary>
        Assembly = CmdAnalysisSteps.AssemblyNumber,

        /// <summary>第 2 步 圈号拆分。</summary>
        CalloutSplit = CmdAnalysisSteps.CalloutSplitNumber,

        /// <summary>第 3 步 要領図部品追加。</summary>
        YoryoAppend = CmdAnalysisSteps.YoryoAppendNumber,

        /// <summary>第 4 步 tz.json 部品追加。</summary>
        TzAppend = CmdAnalysisSteps.TzAppendNumber,

        /// <summary>第 5 步 部品コード正規化（含同図番内の重複チェック）。</summary>
        PartCode = CmdAnalysisSteps.PartCodeNumber,

        /// <summary>第 6 步 使い分け自動設定（正規化後の部品コード で拾い出し表を引く）。</summary>
        UsageKind = CmdAnalysisSteps.UsageKindNumber
    }
}
