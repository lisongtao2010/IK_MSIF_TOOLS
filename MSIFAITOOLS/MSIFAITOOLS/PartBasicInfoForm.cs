using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MSIFAITOOLS.Services;

namespace MSIFAITOOLS
{
    /// <summary>
    /// 部品基本信息画面：显示指定 PDF（図番）已登录到 SQLite 的部品明细，
    /// 并支持在画面上直接编辑、追加行、删除行，保存后回写数据库。
    /// 由【分类结果】选中 PDF 行后点击【部品信息】/右键菜单/Enter 打开；
    /// 窗口可复用（<see cref="ShowDrawing"/> 切换显示内容）。
    /// </summary>
    internal sealed partial class PartBasicInfoForm : Form
    {
        /// <summary>本画面打开的 PDF 预览窗口（关闭本画面时一并关闭）。</summary>
        private PdfPreviewForm _previewForm;

        /// <summary>当前显示的図番（PDF 文件名去扩展名）。</summary>
        private string _drawingNo = string.Empty;

        /// <summary>当前显示的 PDF 完整路径。</summary>
        private string _pdfPath = string.Empty;

        /// <summary>指定的数据库路径（为空时使用 <see cref="PartBasicInfoRepository.ResolveDatabasePath"/>）。</summary>
        private readonly string _databasePath;

        /// <summary>填表期间抑制「单元格变更」引起的脏标记。</summary>
        private bool _suppressGridEvents;

        /// <summary>画面上是否存在尚未保存到数据库的修改。</summary>
        private bool _isDirty;

        /// <summary>窗口尚未显示，需要在 <see cref="OnShown"/> 里打开 PDF 预览窗口。</summary>
        private bool _openPreviewOnShown;

        /// <summary>部品基本信息窗口是否已按屏幕摆放（只摆一次，之后保留用户拖动的位置）。</summary>
        private bool _editorPlaced;

        /// <summary>
        /// 「部品コード」为空时该单元格的背景色（粉红色）—— 表示这个值不是图纸 / CSV 给的，
        /// 而是按「部品名」从部品マスタ（<c>部品マスタ\Tbl_部品マスタ.csv</c>）推定的，需要人工确认。
        /// </summary>
        private static readonly Color PartCodeGuessBackColor = Color.FromArgb(255, 192, 203);

        /// <summary>部品マスタ 索引（部品名 → 部品コード）；第一次用到时读一次 CSV。</summary>
        private CmdPartMasterIndex _partMasterIndex;

        /// <summary>是否已经尝试读过部品マスタ（读不到也只试一次，不反复读盘）。</summary>
        private bool _partMasterLoaded;

        /// <summary>
        /// 整行背景色（淡绿色）：这一行来自 <b>アッセンブリ図</b> 自身的部品表
        /// （「数据来源」= <see cref="PartSourceKind.CmdAssembly"/>，【CMD 分析】阶段 A）。
        /// </summary>
        private static readonly Color SourceAssemblyBackColor = Color.FromArgb(226, 240, 217);

        /// <summary>
        /// 整行背景色（淡蓝色）：这一行来自 <b>要領図</b> 的部品
        /// （「数据来源」= <see cref="PartSourceKind.CmdYoryo"/>，【CMD 分析】阶段 B）。
        /// </summary>
        private static readonly Color SourceYoryoBackColor = Color.FromArgb(222, 235, 247);

        /// <summary>
        /// 整行背景色（淡粉色）：这一行是<b>特注</b>
        /// （「特注」= <see cref="PartBasicInfo.SpecialNoteMark"/>，即 tz.json 的追加対応特注寸法）。
        /// 比推定值的粉红色（<see cref="PartCodeGuessBackColor"/>）浅，两者能区分开。
        /// </summary>
        private static readonly Color SpecialNoteBackColor = Color.FromArgb(252, 228, 236);

        /// <summary>
        /// 整行背景色（淡紫色）：这一行是<b>被分割</b>过的
        /// （备注里有 <see cref="CmdCalloutPartSplitter.RemarkPositionPrefix"/>＝<c>位置：</c>，
        /// 第 2 步按圈号把 1 行拆成「右… / 左…」多条时，拆出来的每一条都会带上这个标记）。
        /// </summary>
        private static readonly Color SplitRowBackColor = Color.FromArgb(230, 224, 245);

        /// <summary>整行背景色（白色）：不属于上面任何一类（tz.json / AI 分析 / 手工追加 / 数据来源为空）。</summary>
        private static readonly Color PlainRowBackColor = Color.White;

        /// <summary>「校验状态」= 坐标不符 时的单元格背景色（红底 + 白字，最需要注意）。</summary>
        private static readonly Color FailedStatusBackColor = Color.FromArgb(198, 40, 40);

        /// <summary>「校验状态」= 坐标一致 时的单元格文字颜色（深绿色）。</summary>
        private static readonly Color PassedStatusForeColor = Color.FromArgb(0, 100, 0);

        /// <summary>
        /// 「保存 Excel」导出的列（画面列名，顺序 = Excel 列顺序）：
        /// 部材名 / 部品名 / 区分 / 数量 / 切断寸法 / 部品ｺｰﾄﾞ / 参照元 / H下限 / H上限 / W下限 / W上限 / 使い分け / 特注。
        /// </summary>
        private static readonly string[] ExportColumnNames =
        {
            "colMemberName", "colPartName", "colFrameKind", "colQuantity", "colCutSize",
            "colPartCode", "colSourceTag", "colHeightMin", "colHeightMax", "colWidthMin",
            "colWidthMax", "colUsageKind", "colSpecialNote"
        };

        /// <summary>
        /// 导出表头（与 <see cref="ExportColumnNames"/> 一一对应）。
        /// 「部品ｺｰﾄﾞ」按要求用半角片假名（与拾い出し表 / マスタ的写法一致），其余与画面列名相同。
        /// </summary>
        private static readonly string[] ExportColumnHeaders =
        {
            "部材名", "部品名", "区分", "数量", "切断寸法", "部品ｺｰﾄﾞ", "参照元",
            "H下限", "H上限", "W下限", "W上限", "使い分け", "特注"
        };

        /// <summary>导出列宽（Excel 字符数，与 <see cref="ExportColumnHeaders"/> 一一对应）。</summary>
        private static readonly double[] ExportColumnWidths =
        {
            22, 34, 8, 8, 18, 16, 16, 10, 10, 10, 10, 16, 20
        };

        /// <summary>
        /// 导出时要写成 <b>数字类型</b>单元格的列（表头名，取自 <see cref="ExportColumnHeaders"/>）：
        /// 数量 / 切断寸法 / H下限 / H上限 / W下限 / W上限 —— Excel 里可直接排序、求和。
        /// 这几列里不是数字的值（如 <c>3-100</c>、<c>2枚</c>）仍按文本写，内容不丢。
        /// </summary>
        private static readonly string[] ExportNumericColumnHeaders =
        {
            "数量", "切断寸法", "H下限", "H上限", "W下限", "W上限"
        };

        /// <summary>
        /// <see cref="ExportNumericColumnHeaders"/> 在 <see cref="ExportColumnHeaders"/> 里的下标
        /// （按列名自动定位，表头顺序 / 名字改了也不会错位）。
        /// </summary>
        private static readonly int[] ExportNumericColumnIndexes =
            ResolveExportNumericColumnIndexes();

        /// <summary>导出目录下的一级子文件夹名（&lt;AiAppData&gt;\output\AssyDwg）。</summary>
        private const string ExportOutputFolderName = "output";

        /// <summary>导出目录下的二级子文件夹名（&lt;AiAppData&gt;\output\AssyDwg）。</summary>
        private const string ExportDrawingFolderName = "AssyDwg";

        /// <summary>导出文件名前缀（<c>a_&lt;図番&gt;.xlsx</c>）。</summary>
        private const string ExportFileNamePrefix = "a_";

        /// <summary>
        /// 导出 Excel 的工作表名：固定写「アセンブリ図」（不再用図番 —— 客户按图纸种类认这张表，
        /// 図番 里有 <c>_</c> 之类字符时 sheet 名也不好看）。文件里只有这一张表。
        /// </summary>
        private const string ExportSheetName = "アセンブリ図";

        public PartBasicInfoForm()
            : this(null)
        {
        }

        /// <summary>
        /// 创建部品基本信息画面。
        /// </summary>
        /// <param name="databasePath">数据库文件路径；为空时按 App.config / 程序目录解析。</param>
        public PartBasicInfoForm(string databasePath)
        {
            InitializeComponent();
            InitializePartsGrid();
            _databasePath = string.IsNullOrWhiteSpace(databasePath) ? null : databasePath.Trim();
        }

        /// <summary>当前显示的図番。</summary>
        public string DrawingNo
        {
            get { return _drawingNo; }
        }

        /// <summary>当前显示的 PDF 完整路径。</summary>
        public string PdfPath
        {
            get { return _pdfPath; }
        }

        /// <summary>
        /// 切换到指定 PDF 对应的部品基本信息（重新从数据库读取），
        /// 并让 PDF 预览窗口同时显示该 PDF（两个画面成对显示）。
        /// 若有未保存的修改，先询问是否保存。
        /// </summary>
        /// <param name="pdfFilePath">PDF 完整路径（図番 = 文件名去扩展名）。</param>
        public void ShowDrawing(string pdfFilePath)
        {
            if (!ConfirmUnsavedChanges("切换图番"))
            {
                return;
            }

            string newPath = pdfFilePath == null ? string.Empty : pdfFilePath.Trim();
            bool drawingChanged = !string.Equals(_pdfPath, newPath, StringComparison.OrdinalIgnoreCase);

            _pdfPath = newPath;
            _drawingNo = PartBasicInfoRepository.ResolveDrawingNo(_pdfPath);

            lblDrawingNo.Text = "図番：" + (_drawingNo.Length == 0 ? "-" : _drawingNo);
            lblPdfPath.Text = "PDF：" + (_pdfPath.Length == 0 ? "-" : _pdfPath);
            UpdateTitle();

            LoadParts();

            if (drawingChanged)
            {
                // 与部品基本信息画面同时打开 PDF 预览（窗口未显示时延后到 OnShown）
                ShowPreviewWindow(false);
            }
            else
            {
                // 同一图番刷新：预览已打开时只同步文件，不抢焦点
                SyncPreviewDocument();
            }
        }

        /// <summary>
        /// 从 SQLite 读取当前図番的部品基本信息并填充表格（异常不会抛出，只显示在画面上）。
        /// </summary>
        private void LoadParts()
        {
            IList<PartBasicInfo> parts = new List<PartBasicInfo>();
            string databasePath = _databasePath ?? PartBasicInfoRepository.ResolveDatabasePath();
            string error = null;

            if (_drawingNo.Length == 0)
            {
                error = "无法从 PDF 文件名解析出図番，请确认文件路径。";
            }
            else if (!File.Exists(databasePath))
            {
                error = "数据库尚未创建（请先执行【AI 分析】）：\n" + databasePath;
            }
            else
            {
                try
                {
                    using (PartBasicInfoRepository repository = new PartBasicInfoRepository(databasePath))
                    {
                        parts = repository.Query(_drawingNo);
                    }
                }
                catch (Exception ex)
                {
                    error = "读取数据库失败：" + ex.Message;
                }
            }

            lblDatabase.Text = "数据库：" + databasePath;

            SetDirty(false);
            PopulateParts(parts);

            if (error != null)
            {
                lblSummary.Text = "读取失败";
                lblSummary.ForeColor = Color.FromArgb(198, 40, 40);
                lblEmpty.Text = error;
            }
            else
            {
                lblSummary.ForeColor = Color.FromArgb(52, 73, 94);
                lblEmpty.Text = "该図番尚未登录部品基本信息。\n可点击【追加行】手工录入，或先对该 PDF 执行【AI 分析】。";
            }

            lblEmpty.Visible = parts.Count == 0;
            dgvParts.Visible = parts.Count > 0;
        }

        /// <summary>把部品明细填入表格（填表期间不产生脏标记）。</summary>
        private void PopulateParts(IList<PartBasicInfo> parts)
        {
            _suppressGridEvents = true;

            try
            {
                dgvParts.Rows.Clear();

                for (int index = 0; index < parts.Count; index++)
                {
                    PartBasicInfo part = parts[index];
                    string status = IsKnownVerifyStatus(part.VerifyStatus)
                        ? part.VerifyStatus
                        : PartVerifyStatus.NotChecked;

                    int rowIndex = dgvParts.Rows.Add(
                        part.Number.ToString(),     // 番号
                        part.MemberName,            // 部材名
                        part.MemberNameAiSuggestion,// 部材名AI推荐
                        part.PartName,              // 部品名
                        part.FrameKind,             // 区分
                        part.Quantity,              // 数量
                        part.CutSize,               // 切断寸法
                        part.PartCode,              // 部品コード
                        part.SourceTag,             // 参照元
                        part.HeightMin,             // H下限
                        part.HeightMax,             // H上限
                        part.WidthMin,              // W下限
                        part.WidthMax,              // W上限
                        part.UsageKind,             // 使い分け
                        part.SpecialNote,           // 特注
                        part.Page.ToString(),       // 页码
                        part.PartDrawingNo,         // 部品図番
                        part.BasicSize,             // 基本寸法
                        status,                     // 校验状态
                        part.Remark,                // 备注
                        part.SourceKind,            // 数据来源
                        part.LoginTime,             // 登录时间
                        (index + 1).ToString());    // No

                    DataGridViewRow row = dgvParts.Rows[rowIndex];
                    row.Tag = part;
                    // 整行背景 = 数据来源 / 特注 / 是否被分割（淡绿 / 淡蓝 / 淡粉 / 淡紫）
                    ApplyRowStyle(row);

                    // 部品コード 为空的行：按部品名 从部品マスタ 推定，并标粉红色提醒确认
                    ApplyPartCodeGuess(row, true);
                }
            }
            finally
            {
                _suppressGridEvents = false;
            }

            RefreshGridSummary();
        }
        /// <summary>
        /// 初始化部品明细表格的列（与 <see cref="PopulateParts"/> 的填充顺序一致）。
        /// 前 15 列是业务必需列，按「番号 / 部材名 / 部材名AI推荐 / 部品名 / 区分 / 数量 / 切断寸法 / 部品コード /
        /// 参照元 / H下限 / H上限 / W下限 / W上限 / 使い分け / 特注」的顺序优先显示，其余为辅助列。
        /// 除「No」「参照元」「数据来源」「登录时间」以外均可编辑；「校验状态」为下拉选择。
        /// </summary>
        private void InitializePartsGrid()
        {
            dgvParts.AutoGenerateColumns = false;
            dgvParts.Columns.Clear();

            // ── 业务必需列（优先显示）─────────────────────────────────────
            AddColumn("colNumber", "番号", 46, DataGridViewContentAlignment.MiddleCenter, false);
            // 部材名：用「番号」在 parts_info.json 的 callouts 里找圈号后，取该圈号所在零件图的
            // texts（去掉数字、去掉「XX部」）用 | 连接；程序自动填，也允许在画面上修正
            AddColumn("colMemberName", "部材名", 120, DataGridViewContentAlignment.MiddleLeft, false);
            // 部材名AI推荐：【AI 分析】对「部材名」里含 | 的行用 AI 重新判定的推荐值
            // （数据来源 = CMD分析・アッセンブリ図 且 部材名含 | 的行；只推荐，不覆盖「部材名」）
            AddColumn("colMemberNameAi", "部材名AI推荐", 120, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colPartName", "部品名", 170, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colFrameKind", "区分", 66, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colQuantity", "数量", 56, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colCutSize", "切断寸法", 100, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colPartCode", "部品コード", 102, DataGridViewContentAlignment.MiddleLeft, false);
            // 参照元 = 该行的「図番」（写库时统一由 PartBasicInfoRepository.BindInsertParameters 填成図番，
            // 老数据库打开时也会把旧值改成図番），画面只读；来源分类看后面的「数据来源」列
            AddColumn("colSourceTag", "参照元", 92, DataGridViewContentAlignment.MiddleCenter);
            AddColumn("colHeightMin", "H下限", 64, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colHeightMax", "H上限", 64, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colWidthMin", "W下限", 64, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colWidthMax", "W上限", 64, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colUsageKind", "使い分け", 96, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colSpecialNote", "特注", 120, DataGridViewContentAlignment.MiddleLeft, false);

            // ── 辅助列 ───────────────────────────────────────────────────
            AddColumn("colPage", "页码", 46, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colPartDrawingNo", "部品図番", 118, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colBasicSize", "基本寸法", 100, DataGridViewContentAlignment.MiddleLeft, false);
            AddVerifyStatusColumn("colVerifyStatus", "校验状态", 88);
            AddColumn("colRemark", "备注", 170, DataGridViewContentAlignment.MiddleLeft, false);
            // 数据来源 = 这行是哪个功能写的（CMD分析・アッセンブリ図 / CMD分析・要領図 / CMD分析・tz.json / AI分析 / 手工追加），由程序写入，画面不可编辑
            AddColumn("colSourceKind", "数据来源", 140, DataGridViewContentAlignment.MiddleCenter);
            // 行背景色的图例（鼠标停在「数据来源」表头上显示）：颜色含义见 ApplyRowStyle
            dgvParts.Columns["colSourceKind"].HeaderCell.ToolTipText =
                "行背景色（同一行满足多个时按此顺序取第一个）：\n" +
                "  淡绿 = 数据来源「CMD分析・アッセンブリ図」\n" +
                "  淡蓝 = 数据来源「CMD分析・要領図」\n" +
                "  淡粉 = 特注（特注 = 1）\n" +
                "  淡紫 = 被分割的行（备注含「位置：」，第 2 步按圈号拆出来的）\n" +
                "  白色 = 其它（tz.json / AI 分析 / 手工追加）\n" +
                "「部品コード」单元格的粉红色 = 按部品名 从部品マスタ 推定的值（需确认）";
            AddColumn("colLoginTime", "登录时间", 130, DataGridViewContentAlignment.MiddleCenter);
            // No = 画面行号（追加 / 删除行后自动重排），放在最后不占必需列的位置
            AddColumn("colNo", "No", 40, DataGridViewContentAlignment.MiddleCenter);
        }

        /// <summary>添加一个文本列。</summary>
        /// <param name="readOnly">true = 只读（No、参照元、数据来源、登录时间等由程序维护的列）。</param>
        private void AddColumn(string name, string headerText, int width,
            DataGridViewContentAlignment alignment, bool readOnly = true)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = headerText;
            column.Width = width;
            column.ReadOnly = readOnly;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.DefaultCellStyle.Alignment = alignment;
            column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvParts.Columns.Add(column);
        }

        /// <summary>添加「校验状态」下拉列（候选项 = <see cref="PartVerifyStatus.All"/>）。</summary>
        private void AddVerifyStatusColumn(string name, string headerText, int width)
        {
            DataGridViewComboBoxColumn column = new DataGridViewComboBoxColumn();
            column.Name = name;
            column.HeaderText = headerText;
            column.Width = width;
            column.ReadOnly = false;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.FlatStyle = FlatStyle.Flat;
            column.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
            column.Items.AddRange(PartVerifyStatus.All);
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvParts.Columns.Add(column);
        }

        /// <summary>
        /// 「追加行」：在当前选中行的下方插入一行（未选中任何行时追加到表格末尾）。
        /// 页码沿用插入点上一行，番号取其下一个。
        /// </summary>
        private void BtnAddRow_Click(object sender, EventArgs e)
        {
            if (_drawingNo.Length == 0)
            {
                MessageBox.Show(this, "无法从 PDF 文件名解析出図番，不能追加部品信息。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            dgvParts.EndEdit();

            // 插入位置 = 选中行（多选时取最靠下的行）的下一行；没有选中行时插到末尾
            DataGridViewRow anchor = GetInsertAnchorRow();
            int insertIndex = anchor == null ? dgvParts.Rows.Count : anchor.Index + 1;
            int page = 1;
            int number = 1;

            if (anchor != null)
            {
                int anchorPage;
                int anchorNumber;

                if (int.TryParse(GetCellText(anchor, "colPage"), out anchorPage) && anchorPage > 0)
                {
                    page = anchorPage;
                }

                if (int.TryParse(GetCellText(anchor, "colNumber"), out anchorNumber) && anchorNumber > 0)
                {
                    number = anchorNumber + 1;
                }
            }

            _suppressGridEvents = true;
            DataGridViewRow row;

            try
            {
                dgvParts.Visible = true;
                lblEmpty.Visible = false;

                dgvParts.Rows.Insert(insertIndex,
                    number.ToString(),                  // 番号
                    string.Empty,                       // 部材名
                    string.Empty,                       // 部材名AI推荐
                    string.Empty,                       // 部品名
                    string.Empty,                       // 区分
                    string.Empty,                       // 数量
                    string.Empty,                       // 切断寸法
                    string.Empty,                       // 部品コード
                    string.Empty,                       // 参照元（保存时按「手工追加」写入）
                    string.Empty,                       // H下限
                    string.Empty,                       // H上限
                    string.Empty,                       // W下限
                    string.Empty,                       // W上限
                    string.Empty,                       // 使い分け
                    string.Empty,                       // 特注
                    page.ToString(),                    // 页码
                    string.Empty,                       // 部品図番
                    string.Empty,                       // 基本寸法
                    PartVerifyStatus.NotChecked,        // 校验状态
                    string.Empty,                       // 备注
                    string.Empty,                       // 数据来源
                    string.Empty,                       // 登录时间
                    (insertIndex + 1).ToString());      // No

                row = dgvParts.Rows[insertIndex];
                row.Tag = null;
                ApplyRowStyle(row);

                // 新行的「部品コード」是空的 → 先标粉红色（填了部品名 之后会自动推定）
                ApplyPartCodeGuess(row, false);
                dgvParts.CurrentCell = row.Cells["colPartName"];
            }
            finally
            {
                _suppressGridEvents = false;
            }

            SetDirty(true);
            RenumberRows();

            if (dgvParts.IsHandleCreated)
            {
                dgvParts.BeginEdit(true);
            }
        }

        /// <summary>
        /// 取「追加行」的参照行（插入点上一行）：优先取选中行中最靠下的一行，
        /// 没有选中行时取当前单元格所在行；表格为空时返回 null（表示追加到末尾）。
        /// </summary>
        private DataGridViewRow GetInsertAnchorRow()
        {
            DataGridViewRow anchor = null;

            foreach (DataGridViewRow row in dgvParts.SelectedRows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                if (anchor == null || row.Index > anchor.Index)
                {
                    anchor = row;
                }
            }

            if (anchor == null && dgvParts.CurrentCell != null)
            {
                DataGridViewRow currentRow = dgvParts.CurrentCell.OwningRow;

                if (currentRow != null && !currentRow.IsNewRow)
                {
                    anchor = currentRow;
                }
            }

            return anchor;
        }

        /// <summary>「删除行」：删除表格中选中的行（可按住 Ctrl / Shift 多选）。</summary>
        private void BtnDeleteRow_Click(object sender, EventArgs e)
        {
            DeleteSelectedRows();
        }

        /// <summary>删除当前选中的行，并刷新序号与统计。</summary>
        private void DeleteSelectedRows()
        {
            dgvParts.EndEdit();

            List<DataGridViewRow> targets = new List<DataGridViewRow>();

            foreach (DataGridViewRow row in dgvParts.SelectedRows)
            {
                if (!row.IsNewRow)
                {
                    targets.Add(row);
                }
            }

            if (targets.Count == 0 && dgvParts.CurrentCell != null)
            {
                DataGridViewRow currentRow = dgvParts.CurrentCell.OwningRow;

                if (currentRow != null && !currentRow.IsNewRow)
                {
                    targets.Add(currentRow);
                }
            }

            if (targets.Count == 0)
            {
                MessageBox.Show(this, "请先选中要删除的行。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    "确定删除选中的 " + targets.Count + " 行吗？\n（点击【保存】后才会真正从数据库删除）",
                    "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            _suppressGridEvents = true;

            try
            {
                foreach (DataGridViewRow row in targets)
                {
                    dgvParts.Rows.Remove(row);
                }
            }
            finally
            {
                _suppressGridEvents = false;
            }

            RenumberRows();
            SetDirty(true);
        }

        /// <summary>「刷新」：重新从数据库读取当前図番的部品明细（会丢弃未保存的修改）。</summary>
        private void BtnRefresh_Click(object sender, EventArgs e)
        {
            if (!ConfirmUnsavedChanges("重新载入"))
            {
                return;
            }

            LoadParts();
        }

        /// <summary>「保存」：把表格内容写回数据库（该図番整体替换）。</summary>
        private void BtnSave_Click(object sender, EventArgs e)
        {
            SaveParts();
        }

        /// <summary>
        /// 校验表格内容并整体保存到数据库，成功后重新载入画面。
        /// </summary>
        /// <returns>true = 已保存（或无修改）；false = 校验失败 / 保存失败 / 用户取消。</returns>
        private bool SaveParts()
        {
            if (_drawingNo.Length == 0)
            {
                MessageBox.Show(this, "无法从 PDF 文件名解析出図番，不能保存部品信息。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            dgvParts.EndEdit();

            List<PartBasicInfo> parts = new List<PartBasicInfo>();
            List<string> errors = new List<string>();
            Dictionary<string, int> usedKeys = new Dictionary<string, int>();

            foreach (DataGridViewRow row in dgvParts.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                int rowNo = row.Index + 1;
                int page;
                int number;

                if (!int.TryParse(GetCellText(row, "colPage"), out page) || page < 0)
                {
                    errors.Add("第 " + rowNo + " 行：「页码」必须是不小于 0 的整数。");
                    continue;
                }

                if (!int.TryParse(GetCellText(row, "colNumber"), out number) || number <= 0)
                {
                    errors.Add("第 " + rowNo + " 行：「番号」必须是大于 0 的整数。");
                    continue;
                }

                // 唯一键与数据库索引一致：页码 + 番号 + 部品コード
                // （部品表「下表参照」展开后，同一番号会有多条不同部品コード的明细）
                string partCode = GetCellText(row, "colPartCode");
                string key = page.ToString() + "/" + number.ToString() + "/" + partCode;
                int firstRowNo;

                if (usedKeys.TryGetValue(key, out firstRowNo))
                {
                    errors.Add("第 " + rowNo + " 行：「页码 " + page + " / 番号 " + number +
                        " / 部品コード " + (partCode.Length == 0 ? "（空）" : partCode) +
                        "」与第 " + firstRowNo + " 行重复。");
                    continue;
                }

                usedKeys[key] = rowNo;

                PartBasicInfo origin = row.Tag as PartBasicInfo;

                PartBasicInfo part = new PartBasicInfo(
                    _drawingNo,
                    page,
                    number,
                    GetCellText(row, "colPartName"),
                    GetCellText(row, "colPartDrawingNo"),
                    GetCellText(row, "colFrameKind"),
                    GetCellText(row, "colQuantity"),
                    GetCellText(row, "colBasicSize"),
                    GetCellText(row, "colCutSize"),
                    GetCellText(row, "colPartCode"));

                string status = GetCellText(row, "colVerifyStatus");
                part.VerifyStatus = IsKnownVerifyStatus(status) ? status : PartVerifyStatus.NotChecked;
                part.MemberName = GetCellText(row, "colMemberName");
                part.MemberNameAiSuggestion = GetCellText(row, "colMemberNameAi");
                part.HeightMin = GetCellText(row, "colHeightMin");
                part.HeightMax = GetCellText(row, "colHeightMax");
                part.WidthMin = GetCellText(row, "colWidthMin");
                part.WidthMax = GetCellText(row, "colWidthMax");
                part.UsageKind = GetCellText(row, "colUsageKind");
                part.SpecialNote = GetCellText(row, "colSpecialNote");
                part.Remark = GetCellText(row, "colRemark");

                if (origin != null)
                {
                    // 未修改的行沿用原来的来源与登录时间
                    part.SourceKind = origin.SourceKind ?? string.Empty;
                    part.SourceTag = origin.SourceTag ?? string.Empty;
                    part.SourceFile = origin.SourceFile ?? string.Empty;
                    part.PdfFile = string.IsNullOrEmpty(origin.PdfFile) ? _pdfPath : origin.PdfFile;
                    part.LoginTime = origin.LoginTime ?? string.Empty;
                }
                else
                {
                    // 新追加的行：来源 = 手工追加；登录时间留空，由仓储写入当前时间
                    part.SourceKind = PartSourceKind.Manual;
                    part.SourceTag = PartSourceTag.Manual;
                    part.SourceFile = string.Empty;
                    part.PdfFile = _pdfPath;
                    part.LoginTime = string.Empty;
                }

                parts.Add(part);
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(this, "保存前请先修正以下问题：\n\n" + string.Join("\n", errors.ToArray()),
                    "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            int blankCount = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part.PartName.Length == 0 && part.PartCode.Length == 0)
                {
                    blankCount++;
                }
            }

            if (blankCount > 0 &&
                MessageBox.Show(this, "有 " + blankCount + " 行的「部品名」与「部品コード」都为空，仍要保存吗？",
                    "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return false;
            }

            if (parts.Count == 0 &&
                MessageBox.Show(this, "表格中没有任何行，保存会清空该図番的全部部品明细。\n确定要继续吗？",
                    "确认清空", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return false;
            }

            string databasePath = _databasePath ?? PartBasicInfoRepository.ResolveDatabasePath();
            int written;

            try
            {
                using (PartBasicInfoRepository repository = new PartBasicInfoRepository(databasePath))
                {
                    written = repository.ReplaceByDrawing(_drawingNo, parts);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存到数据库失败：\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // 部品基本信息写库成功后，把这次确定的「部材名 + 部品名」当成新的对应规则登录进
            // 部材部品 数据库（D:\MSIFDB\MSIF_MemberParts.db：「その他」/ 含 | 的行不登录）。
            // 失败只提示，不影响上面的保存结果。
            string ruleMessage = MemberPartCatalogLoader.RegisterRules(parts);

            LoadParts();

            MessageBox.Show(this, "已保存 " + written + " 条部品基本信息。" +
                (ruleMessage.Length == 0 ? string.Empty : "\n" + ruleMessage), "完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            return true;
        }

        /// <summary>
        /// 按列名把 <see cref="ExportNumericColumnHeaders"/> 映射成 <see cref="ExportColumnHeaders"/>
        /// 的下标（找不到的列名直接跳过；顺序与声明顺序一致）。
        /// </summary>
        private static int[] ResolveExportNumericColumnIndexes()
        {
            List<int> indexes = new List<int>(ExportNumericColumnHeaders.Length);

            for (int index = 0; index < ExportNumericColumnHeaders.Length; index++)
            {
                int columnIndex = Array.IndexOf(ExportColumnHeaders, ExportNumericColumnHeaders[index]);

                if (columnIndex >= 0)
                {
                    indexes.Add(columnIndex);
                }
            }

            return indexes.ToArray();
        }

        /// <summary>「保存 Excel」：把画面当前内容导出成 <c>a_&lt;図番&gt;.xlsx</c>。</summary>
        private void BtnSaveExcel_Click(object sender, EventArgs e)
        {
            ExportToExcel();
        }

        /// <summary>
        /// 把画面当前显示的内容（含尚未保存到数据库的修改）导出成
        /// <c>&lt;PDF 文件夹&gt;\AiAppData\output\AssyDwg\a_&lt;図番&gt;.xlsx</c>。
        ///
        /// <para>
        /// 只导出必要列（见 <see cref="ExportColumnNames"/>），行顺序 = 画面顺序；
        /// 空值单元格留空。不改数据库、不改画面内容。
        /// </para>
        ///
        /// <para>
        /// 数量 / 切断寸法 / H下限 / H上限 / W下限 / W上限 这 6 列写成<b>数字类型</b>单元格
        /// （见 <see cref="ExportNumericColumnIndexes"/>），Excel 里可直接排序 / 求和；
        /// 这 6 列里不是数字的值（如 <c>3-100</c>、<c>2枚</c>）仍然按文本写，内容不丢。
        /// 其余列全部按文本写（<c>3-100</c>、<c>0012</c> 之类不会被 Excel 转成日期 / 数字）。
        /// </para>
        ///
        /// <para>
        /// 工作表名固定写「アセンブリ図」（<see cref="ExportSheetName"/>），不再用図番 ——
        /// 文件里只有这一张表，図番 已经体现在文件名（<c>a_&lt;図番&gt;.xlsx</c>）上。
        /// </para>
        /// </summary>
        /// <returns>true = 已导出；false = 未导出（无図番 / 无数据 / 用户取消 / 写入失败）。</returns>
        private bool ExportToExcel()
        {
            if (_drawingNo.Length == 0)
            {
                MessageBox.Show(this, "无法从 PDF 文件名解析出図番，不能导出 Excel。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string outputDirectory = ResolveExcelOutputDirectory();

            if (outputDirectory == null)
            {
                MessageBox.Show(this,
                    "找不到 PDF 所在文件夹，无法确定导出目录：\n" +
                    (_pdfPath.Length == 0 ? "(未指定 PDF)" : _pdfPath),
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            dgvParts.EndEdit();

            List<string[]> rows = new List<string[]>();

            foreach (DataGridViewRow row in dgvParts.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                string[] values = new string[ExportColumnNames.Length];

                for (int index = 0; index < ExportColumnNames.Length; index++)
                {
                    values[index] = GetCellText(row, ExportColumnNames[index]);
                }

                rows.Add(values);
            }

            if (rows.Count == 0)
            {
                MessageBox.Show(this, "当前図番没有任何部品明细，没有可导出的内容。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            string filePath = Path.Combine(outputDirectory, ExportFileNamePrefix + _drawingNo + ".xlsx");

            if (File.Exists(filePath) &&
                MessageBox.Show(this, "文件已存在，要覆盖吗？\n\n" + filePath, "确认覆盖",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return false;
            }

            if (_isDirty &&
                MessageBox.Show(this,
                    "画面上还有未保存到数据库的修改。\n" +
                    "导出的是画面当前内容（含未保存的修改）。\n\n继续导出吗？",
                    "确认导出", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return false;
            }

            try
            {
                XlsxSheetWriter.WriteSheet(filePath, ExportSheetName, ExportColumnHeaders, rows,
                    ExportColumnWidths, ExportNumericColumnIndexes);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this,
                    "无法写入 Excel 文件（可能正被 Excel 打开）：\n" + filePath + "\n\n" + ex.Message,
                    "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出 Excel 失败：\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (MessageBox.Show(this,
                    "已导出 " + rows.Count + " 条到：\n" + filePath + "\n\n是否打开所在文件夹？",
                    "导出完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                OpenFolder(outputDirectory);
            }

            return true;
        }

        /// <summary>
        /// 计算「保存 Excel」的导出目录（只计算路径，不创建目录）：
        /// <c>&lt;PDF 文件夹&gt;\AiAppData\output\AssyDwg</c>。
        /// 找不到 PDF 所在文件夹（未指定 PDF / 文件夹已删除）时返回 <c>null</c>。
        /// </summary>
        private string ResolveExcelOutputDirectory()
        {
            string pdfFolder = _pdfPath.Length == 0 ? null : Path.GetDirectoryName(_pdfPath);

            if (string.IsNullOrEmpty(pdfFolder) || !Directory.Exists(pdfFolder))
            {
                return null;
            }

            return Path.Combine(
                PdfSplitService.GetOutputRoot(pdfFolder),
                ExportOutputFolderName,
                ExportDrawingFolderName);
        }

        /// <summary>用资源管理器打开指定文件夹（失败只提示，不抛出）。</summary>
        /// <param name="folder">文件夹绝对路径。</param>
        private void OpenFolder(string folder)
        {
            try
            {
                Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开文件夹失败：\n" + ex.Message, "异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>「预览 PDF」：显示该図番对应的 PDF（窗口已打开时前置）。</summary>
        private void BtnPreviewPdf_Click(object sender, EventArgs e)
        {
            if (_pdfPath.Length == 0 || !File.Exists(_pdfPath))
            {
                MessageBox.Show(this,
                    "找不到对应的 PDF 文件：\n" + (_pdfPath.Length == 0 ? "(未指定)" : _pdfPath),
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ShowPreviewWindow(true);
        }

        /// <summary>
        /// 显示（必要时创建）PDF 预览窗口并让它显示当前图番的 PDF。
        /// 本画面尚未显示时延后到 <see cref="OnShown"/> 执行，避免窗口出现在错误的屏幕 / 位置。
        /// </summary>
        /// <param name="activatePreview">是否把预览窗口提到最前（用户点【预览 PDF】时为 true）。</param>
        private void ShowPreviewWindow(bool activatePreview)
        {
            if (_pdfPath.Length == 0 || !File.Exists(_pdfPath))
            {
                return;
            }

            if (!Visible)
            {
                _openPreviewOnShown = true;
                return;
            }

            bool created = _previewForm == null || _previewForm.IsDisposed;

            if (created)
            {
                _previewForm = new PdfPreviewForm();
                _previewForm.FormClosed += PreviewForm_FormClosed;
                // 不设 Owner（不用 Show(this)）：预览窗口是独立的顶层窗口，不会「始终浮在本画面之上」，
                // 本画面关闭时由 OnFormClosed 一并关闭预览窗口。
                _previewForm.Show();
            }
            else if (!_previewForm.Visible)
            {
                _previewForm.Show();
            }

            if (!string.Equals(_previewForm.CurrentPath, _pdfPath, StringComparison.OrdinalIgnoreCase))
            {
                // 换文档；activatePreview = false（随本画面自动打开）时不把预览窗口激活到前面
                _previewForm.ShowPdf(_pdfPath, activatePreview);
            }

            bool placeEditor = !_editorPlaced;

            if (created && placeEditor)
            {
                // 首次打开：PDF 预览 → 屏幕 1，部品基本信息 → 屏幕 2（单屏时左右并排）
                _editorPlaced = true;
                WindowLayoutHelper.ArrangePreviewAndEditor(_previewForm, this);
            }
            else if (created)
            {
                // 用户关闭预览后再次打开：只把预览窗口放回屏幕 1
                WindowLayoutHelper.ArrangePreview(_previewForm);
            }
            else if (placeEditor)
            {
                // 预览窗口由主画面打开并已摆好，只把编辑画面移到屏幕 2
                _editorPlaced = true;
                WindowLayoutHelper.ArrangeEditor(this);
            }

            if (activatePreview)
            {
                _previewForm.BringToFront();
                _previewForm.Activate();
            }
        }

        /// <summary>把已打开的 PDF 预览窗口同步到当前图番的 PDF（不抢焦点）。</summary>
        private void SyncPreviewDocument()
        {
            if (_previewForm == null || _previewForm.IsDisposed || _pdfPath.Length == 0)
            {
                return;
            }

            if (!string.Equals(_previewForm.CurrentPath, _pdfPath, StringComparison.OrdinalIgnoreCase))
            {
                // 只换文档，不激活窗口（预览画面不抢到编辑画面前面）
                _previewForm.ShowPdf(_pdfPath, false);
            }
        }

        /// <summary>
        /// 接管主画面已经打开的 PDF 预览窗口（避免同时存在两个预览窗口）。
        /// 接管后由本画面负责切换内容，并在本画面关闭时一并关闭。
        /// </summary>
        /// <param name="previewForm">主画面已打开的预览窗口。</param>
        internal void AttachPreviewForm(PdfPreviewForm previewForm)
        {
            if (previewForm == null || previewForm.IsDisposed)
            {
                return;
            }

            if (_previewForm != null && !_previewForm.IsDisposed
                && !ReferenceEquals(_previewForm, previewForm))
            {
                PdfPreviewForm oldPreview = _previewForm;
                _previewForm = null;
                oldPreview.Close();
                oldPreview.Dispose();
            }

            _previewForm = previewForm;
            _previewForm.FormClosed += PreviewForm_FormClosed;
        }

        /// <summary>显示 / 前置本画面的 PDF 预览窗口（主画面复用同一窗口时调用）。</summary>
        /// <param name="bringToFront">是否把预览窗口提到最前。</param>
        internal void ShowPreview(bool bringToFront)
        {
            ShowPreviewWindow(bringToFront);
        }

        /// <summary>预览窗口关闭后清空引用，便于下次重新创建。</summary>
        private void PreviewForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            PdfPreviewForm form = sender as PdfPreviewForm;

            if (form != null)
            {
                form.FormClosed -= PreviewForm_FormClosed;
            }

            _previewForm = null;
        }

        /// <summary>「关闭」。</summary>
        private void BtnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>更新标题（有未保存的修改时追加提示）。</summary>
        private void UpdateTitle()
        {
            string title = _drawingNo.Length == 0 ? "部品基本信息" : "部品基本信息 - " + _drawingNo;

            Text = _isDirty ? title + "　＊有未保存的修改" : title;
            btnSave.Text = _isDirty ? "保存 ＊" : "保存";
        }

        /// <summary>设置「是否有未保存的修改」并刷新标题、统计标签。</summary>
        private void SetDirty(bool dirty)
        {
            _isDirty = dirty;
            UpdateTitle();
            RefreshGridSummary();
        }

        /// <summary>按表格当前内容统计条数与校验状态，并提示是否有未保存的修改。</summary>
        private void RefreshGridSummary()
        {
            int total = 0;
            int passedCount = 0;
            int failedCount = 0;
            int notCheckedCount = 0;
            int aiSuggestionCount = 0;

            foreach (DataGridViewRow row in dgvParts.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                total++;

                string status = GetCellText(row, "colVerifyStatus");

                if (status == PartVerifyStatus.Passed)
                {
                    passedCount++;
                }
                else if (status == PartVerifyStatus.Failed)
                {
                    failedCount++;
                }
                else
                {
                    notCheckedCount++;
                }

                // 「部材名AI推荐」有值的行数（【AI 分析】写进来的推荐）
                if (!string.IsNullOrWhiteSpace(GetCellText(row, "colMemberNameAi")))
                {
                    aiSuggestionCount++;
                }
            }

            lblSummary.Text = string.Format("共 {0} 条（坐标一致 {1} / 不符 {2} / 未校验 {3}）{4}{5}",
                total, passedCount, failedCount, notCheckedCount,
                aiSuggestionCount > 0 ? string.Format("　AI 推荐 {0} 条", aiSuggestionCount) : string.Empty,
                _isDirty ? "　＊未保存" : string.Empty);
        }

        /// <summary>重新编号「No」列（追加/删除行后保持连续）。</summary>
        private void RenumberRows()
        {
            _suppressGridEvents = true;

            try
            {
                for (int index = 0; index < dgvParts.Rows.Count; index++)
                {
                    DataGridViewRow row = dgvParts.Rows[index];

                    if (row.IsNewRow)
                    {
                        continue;
                    }

                    row.Cells["colNo"].Value = (index + 1).ToString();
                }
            }
            finally
            {
                _suppressGridEvents = false;
            }
        }

        /// <summary>
        /// 按「数据来源 / 特注 / 是否被分割」给整行上色（一行同时满足多个条件时，按下面的顺序取第一条）。
        /// <list type="bullet">
        ///   <item><b>特注</b>（「特注」= <see cref="PartBasicInfo.SpecialNoteMark"/>）→ <b>淡粉色</b>；</item>
        ///   <item><b>被分割的行</b>（备注里有 <see cref="CmdCalloutPartSplitter.RemarkPositionPrefix"/>＝<c>位置：</c>，
        ///     由第 2 步按圈号把 1 行拆成多条产生，拆出来的每一条都算）→ <b>淡紫色</b>；</item>
        ///   <item><b>数据来源 = <see cref="PartSourceKind.CmdAssembly"/></b>（アッセンブリ図 自身的部品表）→ <b>淡绿色</b>；</item>
        ///   <item><b>数据来源 = <see cref="PartSourceKind.CmdYoryo"/></b>（要領図 的部品）→ <b>淡蓝色</b>；</item>
        ///   <item>其它（<see cref="PartSourceKind.CmdTz"/> / <see cref="PartSourceKind.AiAnalysis"/> /
        ///     <see cref="PartSourceKind.Manual"/> / 数据来源为空）→ 白色。</item>
        /// </list>
        /// <para>
        /// 「校验状态」不再用整行背景表示（会把上面的来源色盖掉），改成只给「校验状态」单元格上色
        /// （<see cref="ApplyVerifyStatusCellStyle"/>：坐标不符 = 红底白字、坐标一致 = 深绿色字）。
        /// 「部品コード」单元格的粉红色（推定值）由 <see cref="ApplyPartCodeGuess"/> 单独设置，
        /// 单元格样式优先于行样式，所以两种颜色可以同时看到。
        /// </para>
        /// </summary>
        /// <param name="row">要处理的行（<c>null</c> / 新增行时忽略）。</param>
        private static void ApplyRowStyle(DataGridViewRow row)
        {
            if (row == null || row.IsNewRow)
            {
                return;
            }

            row.DefaultCellStyle.BackColor = ResolveRowBackColor(row);
            ApplyVerifyStatusCellStyle(row);
            ApplySourceKindToolTip(row);
        }

        /// <summary>
        /// 按「特注 → 被分割 → アッセンブリ図 → 要領図」的顺序决定整行背景色（都不满足时为白色）。
        /// </summary>
        private static Color ResolveRowBackColor(DataGridViewRow row)
        {
            if (IsSpecialNoteRow(row))
            {
                return SpecialNoteBackColor;
            }

            if (IsSplitRow(row))
            {
                return SplitRowBackColor;
            }

            string sourceKind = GetCellText(row, "colSourceKind");

            if (string.Equals(sourceKind, PartSourceKind.CmdAssembly, StringComparison.Ordinal))
            {
                return SourceAssemblyBackColor;
            }

            if (string.Equals(sourceKind, PartSourceKind.CmdYoryo, StringComparison.Ordinal))
            {
                return SourceYoryoBackColor;
            }

            return PlainRowBackColor;
        }

        /// <summary>
        /// 该行是否为<b>特注</b>：「特注」= <see cref="PartBasicInfo.SpecialNoteMark"/>（<c>1</c>）。
        /// tz.json 的追加対応特注寸法一律是 1，画面上也能手工改成 1。
        /// </summary>
        private static bool IsSpecialNoteRow(DataGridViewRow row)
        {
            return string.Equals(GetCellText(row, "colSpecialNote"), PartBasicInfo.SpecialNoteMark,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// 该行是否<b>被按位置分割过</b>：备注里带 <see cref="CmdCalloutPartSplitter.RemarkPositionPrefix"/>（<c>位置：</c>）。
        /// 第 2 步按圈号把 1 行拆成「右… / 左…」多条时，拆出来的<b>每一条</b>备注都会追加这个标记
        /// （<c>CmdCalloutPartSplitter.BuildPositionRemark</c>）。
        /// </summary>
        private static bool IsSplitRow(DataGridViewRow row)
        {
            return GetCellText(row, "colRemark").IndexOf(CmdCalloutPartSplitter.RemarkPositionPrefix,
                StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 给「数据来源」单元格设置 ToolTip，说明这一行为什么用这个背景色
        /// （颜色含义见 <see cref="ApplyRowStyle"/>）。
        /// </summary>
        private static void ApplySourceKindToolTip(DataGridViewRow row)
        {
            DataGridViewCell cell = row.Cells["colSourceKind"];

            if (cell == null)
            {
                return;
            }

            if (IsSpecialNoteRow(row))
            {
                cell.ToolTipText = "行背景 淡粉 = 特注（「特注」= " + PartBasicInfo.SpecialNoteMark + "）";
                return;
            }

            if (IsSplitRow(row))
            {
                cell.ToolTipText = "行背景 淡紫 = 被分割的行（备注含「" +
                    CmdCalloutPartSplitter.RemarkPositionPrefix + "」，按圈号 / 位置 1 行拆成多条）";
                return;
            }

            string sourceKind = GetCellText(row, "colSourceKind");

            if (string.Equals(sourceKind, PartSourceKind.CmdAssembly, StringComparison.Ordinal))
            {
                cell.ToolTipText = "行背景 淡绿 = 数据来源「" + PartSourceKind.CmdAssembly + "」";
                return;
            }

            if (string.Equals(sourceKind, PartSourceKind.CmdYoryo, StringComparison.Ordinal))
            {
                cell.ToolTipText = "行背景 淡蓝 = 数据来源「" + PartSourceKind.CmdYoryo + "」";
                return;
            }

            cell.ToolTipText = sourceKind.Length == 0
                ? "数据来源为空（老数据 / 未写来源）→ 行背景 白色"
                : "行背景 白色 = 数据来源「" + sourceKind + "」（不属于上面几类）";
        }

        /// <summary>
        /// 只给「校验状态」单元格上色（不动整行背景，避免盖住「数据来源」的颜色）：
        /// 坐标不符 = 红底白字，坐标一致 = 深绿色字，未校验 = 默认。
        /// </summary>
        private static void ApplyVerifyStatusCellStyle(DataGridViewRow row)
        {
            DataGridViewCell cell = row.Cells["colVerifyStatus"];

            if (cell == null)
            {
                return;
            }

            string status = GetCellText(row, "colVerifyStatus");

            if (status == PartVerifyStatus.Failed)
            {
                cell.Style.BackColor = FailedStatusBackColor;
                cell.Style.ForeColor = Color.White;
            }
            else if (status == PartVerifyStatus.Passed)
            {
                cell.Style.BackColor = Color.Empty;
                cell.Style.ForeColor = PassedStatusForeColor;
            }
            else
            {
                cell.Style.BackColor = Color.Empty;
                cell.Style.ForeColor = Color.Empty;
            }
        }

        /// <summary>
        /// 「部品コード」为空的行：用「部品名」去部品マスタ（<c>部品マスタ\Tbl_部品マスタ.csv</c>）找部品コード。
        /// <list type="bullet">
        ///   <item>只命中 1 个 → 填进单元格（推定值），并把该单元格标成<b>粉红色</b>；</item>
        ///   <item>命中多个（同一个部品名 对应材質 / 色 不同的多个部品コード）→ <b>不填</b>，只标粉红色，
        ///     ToolTip 里列出全部候选，让用户按材質 / 色 自己选；</item>
        ///   <item>一个都没有 / 部品名为空 → 只标粉红色，ToolTip 说明原因。</item>
        /// </list>
        /// 已经有部品コード 的行清掉粉红色（= 不是推定值）。改了「部品名」之后会重新推定。
        /// </summary>
        /// <param name="row">要处理的行（<c>null</c> / 新增行时忽略）。</param>
        /// <param name="assignValue">
        /// true = 允许把推定值填进单元格（载入明细 / 用户改了「部品名」时）；
        /// false = 只更新粉红色标记和 ToolTip，<b>不动单元格里的值</b>
        /// （用户正在手工改「部品コード」时，不要把刚清空的值又自动填回去）。
        /// </param>
        private void ApplyPartCodeGuess(DataGridViewRow row, bool assignValue)
        {
            if (row == null || row.IsNewRow)
            {
                return;
            }

            DataGridViewCell cell = row.Cells["colPartCode"];

            if (cell == null)
            {
                return;
            }

            if (GetCellText(row, "colPartCode").Length > 0)
            {
                // 已经有部品コード（图纸 / CSV / 用户填写）→ 不是推定值，恢复普通背景
                cell.Style.BackColor = Color.Empty;
                cell.ToolTipText = string.Empty;

                return;
            }

            // 没有部品コード 的行一律标粉红色（推定值 / 缺值都需要人工确认）
            cell.Style.BackColor = PartCodeGuessBackColor;

            string partName = GetCellText(row, "colPartName");

            if (partName.Length == 0)
            {
                cell.ToolTipText = "部品コード 为空，部品名 也为空 → 无法从部品マスタ（"
                    + CmdPartCodeNormalizer.FileName + "）推定，请手工输入";

                return;
            }

            IList<CmdPartMasterEntry> candidates = GetPartMasterIndex().FindByPartName(partName);

            if (candidates.Count == 1)
            {
                CmdPartMasterEntry found = candidates[0];

                if (!assignValue)
                {
                    // 用户正在手工改部品コード → 只告诉他マスタ里是什么，不替他填
                    cell.ToolTipText = string.Format(
                        "部品コード 为空 → 部品名「{0}」在部品マスタ 里是 {1}（{2} 第 {3} 行）→ 需要时请手工输入",
                        partName, found.PartCode, found.FileName, found.RowNumber);

                    return;
                }

                // 自动填值不算「用户改动」（不要因为填值再触发一次本方法 / 脏标记）
                bool previous = _suppressGridEvents;

                _suppressGridEvents = true;

                try
                {
                    cell.Value = found.PartCode;
                }
                finally
                {
                    _suppressGridEvents = previous;
                }

                cell.ToolTipText = string.Format(
                    "部品コード 为空 → 按部品名「{0}」从部品マスタ 推定：{1}（{2} 第 {3} 行）。请确认。",
                    partName, found.PartCode, found.FileName, found.RowNumber);

                return;
            }

            if (candidates.Count > 1)
            {
                List<string> codes = new List<string>();

                foreach (CmdPartMasterEntry candidate in candidates)
                {
                    codes.Add(candidate.PartCode);
                }

                cell.ToolTipText = string.Format(
                    "部品マスタ 里「{0}」有 {1} 个部品コード：{2} → 请按材質 / 色 手工选择",
                    partName, candidates.Count, string.Join(" / ", codes.ToArray()));

                return;
            }

            cell.ToolTipText = string.Format(
                "部品マスタ（{0}）里没有「{1}」→ 请手工输入部品コード",
                CmdPartCodeNormalizer.FileName, partName);
        }

        /// <summary>
        /// 取部品マスタ 索引（第一次用到时读 <c>部品マスタ\Tbl_部品マスタ.csv</c>，整个画面生命周期只读一次）。
        /// 找不到 / 读不了时返回空索引 —— 只是不自动补部品コード，不影响画面其它功能，也不弹提示。
        /// </summary>
        private CmdPartMasterIndex GetPartMasterIndex()
        {
            if (_partMasterLoaded)
            {
                return _partMasterIndex;
            }

            _partMasterLoaded = true;

            CmdPartMasterIndex index = new CmdPartMasterIndex();

            try
            {
                string searchFrom = null;

                if (_pdfPath.Length > 0)
                {
                    searchFrom = Path.GetDirectoryName(_pdfPath);
                }

                string filePath = CmdPartCodeNormalizer.ResolveDataSource(searchFrom, null);

                if (!string.IsNullOrEmpty(filePath))
                {
                    index = CmdPartCodeNormalizer.LoadIndex(filePath, null);
                }
            }
            catch (Exception)
            {
                // 部品マスタ 读不到 / 格式异常：只是不自动补部品コード（不去打扰用户）
            }

            _partMasterIndex = index;

            return _partMasterIndex;
        }

        /// <summary>读取单元格文本（NULL 视为空字符串，并去掉首尾空白）。</summary>
        private static string GetCellText(DataGridViewRow row, string columnName)
        {
            if (row == null)
            {
                return string.Empty;
            }

            object value = row.Cells[columnName].Value;

            return value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value).Trim();
        }

        /// <summary>判断是否为 <see cref="PartVerifyStatus"/> 中定义的状态。</summary>
        private static bool IsKnownVerifyStatus(string status)
        {
            if (string.IsNullOrEmpty(status))
            {
                return false;
            }

            foreach (string item in PartVerifyStatus.All)
            {
                if (item == status)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 有未保存的修改时询问：「是」= 保存后继续，「否」= 放弃修改继续，「取消」= 中止操作。
        /// </summary>
        /// <param name="action">后续动作的描述（如「切换图番」「关闭窗口」）。</param>
        /// <returns>true = 可以继续；false = 中止当前操作。</returns>
        private bool ConfirmUnsavedChanges(string action)
        {
            if (!_isDirty)
            {
                return true;
            }

            DialogResult result = MessageBox.Show(this,
                "有未保存的修改。\n\n是否先保存，再" + action + "？\n\n" +
                "「是」：保存后" + action + "\n「否」：放弃修改并" + action + "\n「取消」：留在当前画面",
                "确认", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (result == DialogResult.Cancel)
            {
                return false;
            }

            if (result == DialogResult.Yes)
            {
                return SaveParts();
            }

            return true;
        }

        /// <summary>单元格内容变化时：刷新行颜色、统计与未保存标记。</summary>
        private void DgvParts_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressGridEvents || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewRow row = dgvParts.Rows[e.RowIndex];
            string columnName = dgvParts.Columns[e.ColumnIndex].Name;

            // 校验状态改了 → 只刷「校验状态」单元格的配色（红底白字 / 深绿色字）；
            // 特注改成 1 → 整行淡粉；备注里出现 / 去掉「位置：」→ 整行淡紫（被分割）→ 整行背景要跟着换
            if (columnName == "colVerifyStatus" || columnName == "colSpecialNote" ||
                columnName == "colRemark")
            {
                ApplyRowStyle(row);
            }

            // 部品名 改了 → 按部品名 从部品マスタ 重新推定部品コード（粉红色 = 推定值）；
            // 部品コード 改了 → 只刷新粉红色标记（手工清空时不要把值自动填回去）
            if (columnName == "colPartName")
            {
                ApplyPartCodeGuess(row, true);
            }
            else if (columnName == "colPartCode")
            {
                ApplyPartCodeGuess(row, false);
            }

            if (columnName != "colNo")
            {
                SetDirty(true);
            }
        }

        /// <summary>表格内按 Delete 键删除选中的行。</summary>
        private void DgvParts_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && !dgvParts.IsCurrentCellInEditMode)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                DeleteSelectedRows();
            }
        }

        /// <summary>画面内按 Ctrl+S 保存。</summary>
        private void PartBasicInfoForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SaveParts();
            }
        }

        /// <summary>关闭前确认未保存的修改（系统关机/任务管理器结束时不询问）。</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing)
            {
                base.OnFormClosing(e);
                return;
            }

            if (!ConfirmUnsavedChanges("关闭窗口"))
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        /// <summary>
        /// 窗口显示后与部品基本信息画面同时打开 PDF 预览窗口：
        /// PDF 预览 → 屏幕 1，部品基本信息 → 屏幕 2（只有 1 块屏幕时左右并排）。
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (!_openPreviewOnShown)
            {
                return;
            }

            _openPreviewOnShown = false;
            ShowPreviewWindow(false);

            // 自动打开的预览窗口不抢焦点，焦点留在编辑画面
            Activate();
        }

        /// <summary>关闭本画面时一并关闭 PDF 预览窗口，释放文件句柄。</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            PdfPreviewForm preview = _previewForm;
            _previewForm = null;

            // Close() 会触发 FormClosed，所以先把引用取到局部变量再关闭
            if (preview != null && !preview.IsDisposed)
            {
                preview.Close();
                preview.Dispose();
            }

            base.OnFormClosed(e);
        }
    }
}
