namespace MSIFAITOOLS
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblVersion = new System.Windows.Forms.Label();
            this.lblAppSub = new System.Windows.Forms.Label();
            this.lblAppTitle = new System.Windows.Forms.Label();
            this.pnlHeaderAccent = new System.Windows.Forms.Panel();
            this.pnlContent = new System.Windows.Forms.Panel();
            this.grpLog = new System.Windows.Forms.GroupBox();
            this.tabMain = new System.Windows.Forms.TabControl();
            this.tabPageLog = new System.Windows.Forms.TabPage();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.tabPageClassify = new System.Windows.Forms.TabPage();
            this.dgvClassify = new System.Windows.Forms.DataGridView();
            this.cmsClassify = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuShowPartInfo = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuPreviewPdf = new System.Windows.Forms.ToolStripMenuItem();
            this.cmsLog = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuLogCopy = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuLogCopyAll = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuLogSelectAll = new System.Windows.Forms.ToolStripMenuItem();
            this.pnlClassifyBar = new System.Windows.Forms.Panel();
            this.lblClassifySummary = new System.Windows.Forms.Label();
            this.chkAutoPreview = new System.Windows.Forms.CheckBox();
            this.btnPartInfo = new System.Windows.Forms.Button();
            this.btnOpenClassifyDir = new System.Windows.Forms.Button();
            this.btnPreviewPdf = new System.Windows.Forms.Button();
            this.grpAction = new System.Windows.Forms.GroupBox();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblProgress = new System.Windows.Forms.Label();
            this.btnOpenOutput = new System.Windows.Forms.Button();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnAiAnalyze = new System.Windows.Forms.Button();
            this.btnCmdAnalyze = new System.Windows.Forms.Button();
            this.btnCmdStep1 = new System.Windows.Forms.Button();
            this.btnCmdStep2 = new System.Windows.Forms.Button();
            this.btnCmdStep3 = new System.Windows.Forms.Button();
            this.btnCmdStep4 = new System.Windows.Forms.Button();
            this.btnCmdStep5 = new System.Windows.Forms.Button();
            this.btnCmdStep7 = new System.Windows.Forms.Button();
            this.btnCmdStep8 = new System.Windows.Forms.Button();
            this.tipCmdStep = new System.Windows.Forms.ToolTip(this.components);
            this.btnAiSettings = new System.Windows.Forms.Button();
            this.btnMemberNameMap = new System.Windows.Forms.Button();
            this.grpInput = new System.Windows.Forms.GroupBox();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.txtFolder = new System.Windows.Forms.TextBox();
            this.lblFolder = new System.Windows.Forms.Label();
            this.chkCadSplit = new System.Windows.Forms.CheckBox();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblSpring = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblPdfInfo = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblEngine = new System.Windows.Forms.ToolStripStatusLabel();
            this.pnlHeader.SuspendLayout();
            this.pnlContent.SuspendLayout();
            this.grpLog.SuspendLayout();
            this.tabMain.SuspendLayout();
            this.tabPageLog.SuspendLayout();
            this.tabPageClassify.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvClassify)).BeginInit();
            this.cmsClassify.SuspendLayout();
            this.cmsLog.SuspendLayout();
            this.pnlClassifyBar.SuspendLayout();
            this.grpAction.SuspendLayout();
            this.grpInput.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlHeader
            // 
            this.pnlHeader.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(27)))), ((int)(((byte)(58)))), ((int)(((byte)(95)))));
            this.pnlHeader.Controls.Add(this.lblVersion);
            this.pnlHeader.Controls.Add(this.lblAppSub);
            this.pnlHeader.Controls.Add(this.lblAppTitle);
            this.pnlHeader.Controls.Add(this.pnlHeaderAccent);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(900, 76);
            this.pnlHeader.TabIndex = 0;
            // 
            // lblVersion
            // 
            this.lblVersion.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblVersion.AutoSize = true;
            this.lblVersion.BackColor = System.Drawing.Color.Transparent;
            this.lblVersion.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblVersion.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(140)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblVersion.Location = new System.Drawing.Point(824, 20);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(41, 17);
            this.lblVersion.TabIndex = 2;
            this.lblVersion.Text = "v1.0.0";
            // 
            // lblAppSub
            // 
            this.lblAppSub.AutoSize = true;
            this.lblAppSub.BackColor = System.Drawing.Color.Transparent;
            this.lblAppSub.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblAppSub.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(169)))), ((int)(((byte)(196)))), ((int)(((byte)(224)))));
            this.lblAppSub.Location = new System.Drawing.Point(24, 46);
            this.lblAppSub.Name = "lblAppSub";
            this.lblAppSub.Size = new System.Drawing.Size(306, 17);
            this.lblAppSub.TabIndex = 1;
            this.lblAppSub.Text = "PDF 分解工具 · 坐标 / 文本 / plu（pdfplumber 风格）";
            // 
            // lblAppTitle
            // 
            this.lblAppTitle.AutoSize = true;
            this.lblAppTitle.BackColor = System.Drawing.Color.Transparent;
            this.lblAppTitle.Font = new System.Drawing.Font("Microsoft YaHei", 18F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblAppTitle.ForeColor = System.Drawing.Color.White;
            this.lblAppTitle.Location = new System.Drawing.Point(22, 12);
            this.lblAppTitle.Name = "lblAppTitle";
            this.lblAppTitle.Size = new System.Drawing.Size(196, 31);
            this.lblAppTitle.TabIndex = 0;
            this.lblAppTitle.Text = "MSIF AI TOOLS";
            // 
            // pnlHeaderAccent
            // 
            this.pnlHeaderAccent.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(230)))), ((int)(((byte)(126)))), ((int)(((byte)(34)))));
            this.pnlHeaderAccent.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlHeaderAccent.Location = new System.Drawing.Point(0, 73);
            this.pnlHeaderAccent.Name = "pnlHeaderAccent";
            this.pnlHeaderAccent.Size = new System.Drawing.Size(900, 3);
            this.pnlHeaderAccent.TabIndex = 3;
            // 
            // pnlContent
            // 
            this.pnlContent.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.pnlContent.Controls.Add(this.grpLog);
            this.pnlContent.Controls.Add(this.grpAction);
            this.pnlContent.Controls.Add(this.grpInput);
            this.pnlContent.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlContent.Location = new System.Drawing.Point(0, 76);
            this.pnlContent.Name = "pnlContent";
            this.pnlContent.Size = new System.Drawing.Size(900, 602);
            this.pnlContent.TabIndex = 1;
            // 
            // grpLog
            // 
            this.grpLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpLog.BackColor = System.Drawing.Color.White;
            this.grpLog.Controls.Add(this.tabMain);
            this.grpLog.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.grpLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.grpLog.Location = new System.Drawing.Point(20, 262);
            this.grpLog.Name = "grpLog";
            this.grpLog.Size = new System.Drawing.Size(860, 320);
            this.grpLog.TabIndex = 2;
            this.grpLog.TabStop = false;
            this.grpLog.Text = "③  运行日志 / 分类结果";
            // 
            // tabMain
            // 
            this.tabMain.Controls.Add(this.tabPageLog);
            this.tabMain.Controls.Add(this.tabPageClassify);
            this.tabMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabMain.Location = new System.Drawing.Point(3, 19);
            this.tabMain.Name = "tabMain";
            this.tabMain.SelectedIndex = 0;
            this.tabMain.Size = new System.Drawing.Size(854, 278);
            this.tabMain.TabIndex = 0;
            // 
            // tabPageLog
            // 
            this.tabPageLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.tabPageLog.Controls.Add(this.rtbLog);
            this.tabPageLog.Location = new System.Drawing.Point(4, 26);
            this.tabPageLog.Name = "tabPageLog";
            this.tabPageLog.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageLog.Size = new System.Drawing.Size(846, 248);
            this.tabPageLog.TabIndex = 0;
            this.tabPageLog.Text = "运行日志";
            // 
            // rtbLog
            // 
            this.rtbLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.rtbLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.rtbLog.ContextMenuStrip = this.cmsLog;
            this.rtbLog.DetectUrls = false;
            this.rtbLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbLog.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.rtbLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(212)))), ((int)(((byte)(212)))), ((int)(((byte)(212)))));
            this.rtbLog.HideSelection = false;
            this.rtbLog.Location = new System.Drawing.Point(3, 3);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.Size = new System.Drawing.Size(840, 242);
            this.rtbLog.TabIndex = 0;
            this.rtbLog.Text = "";
            this.rtbLog.WordWrap = false;
            this.rtbLog.MouseDown += new System.Windows.Forms.MouseEventHandler(this.RtbLog_MouseDown);
            this.rtbLog.KeyDown += new System.Windows.Forms.KeyEventHandler(this.RtbLog_KeyDown);
            // 
            // tabPageClassify
            // 
            this.tabPageClassify.BackColor = System.Drawing.Color.White;
            this.tabPageClassify.Controls.Add(this.dgvClassify);
            this.tabPageClassify.Controls.Add(this.pnlClassifyBar);
            this.tabPageClassify.Location = new System.Drawing.Point(4, 26);
            this.tabPageClassify.Name = "tabPageClassify";
            this.tabPageClassify.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageClassify.Size = new System.Drawing.Size(846, 248);
            this.tabPageClassify.TabIndex = 1;
            this.tabPageClassify.Text = "分类结果";
            // 
            // dgvClassify
            // 
            this.dgvClassify.AllowUserToAddRows = false;
            this.dgvClassify.AllowUserToDeleteRows = false;
            this.dgvClassify.AllowUserToResizeRows = false;
            this.dgvClassify.BackgroundColor = System.Drawing.Color.White;
            this.dgvClassify.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.dgvClassify.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dgvClassify.ContextMenuStrip = this.cmsClassify;
            this.dgvClassify.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvClassify.Location = new System.Drawing.Point(3, 3);
            this.dgvClassify.MultiSelect = false;
            this.dgvClassify.Name = "dgvClassify";
            this.dgvClassify.RowHeadersVisible = false;
            this.dgvClassify.RowTemplate.Height = 24;
            this.dgvClassify.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvClassify.Size = new System.Drawing.Size(840, 198);
            this.dgvClassify.TabIndex = 0;
            this.dgvClassify.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DgvClassify_CellDoubleClick);
            this.dgvClassify.CurrentCellChanged += new System.EventHandler(this.DgvClassify_CurrentCellChanged);
            this.dgvClassify.DataError += new System.Windows.Forms.DataGridViewDataErrorEventHandler(this.DgvClassify_DataError);
            this.dgvClassify.KeyDown += new System.Windows.Forms.KeyEventHandler(this.DgvClassify_KeyDown);
            // 
            // cmsClassify
            // 
            this.cmsClassify.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuShowPartInfo,
            this.mnuPreviewPdf});
            this.cmsClassify.Name = "cmsClassify";
            this.cmsClassify.Size = new System.Drawing.Size(205, 48);
            this.cmsClassify.Opening += new System.ComponentModel.CancelEventHandler(this.CmsClassify_Opening);
            // 
            // mnuShowPartInfo
            // 
            this.mnuShowPartInfo.Name = "mnuShowPartInfo";
            this.mnuShowPartInfo.ShortcutKeyDisplayString = "Enter";
            this.mnuShowPartInfo.Size = new System.Drawing.Size(204, 22);
            this.mnuShowPartInfo.Text = "查看部品基本信息";
            this.mnuShowPartInfo.Click += new System.EventHandler(this.MnuShowPartInfo_Click);
            // 
            // mnuPreviewPdf
            // 
            this.mnuPreviewPdf.Name = "mnuPreviewPdf";
            this.mnuPreviewPdf.Size = new System.Drawing.Size(204, 22);
            this.mnuPreviewPdf.Text = "预览 PDF";
            this.mnuPreviewPdf.Click += new System.EventHandler(this.MnuPreviewPdf_Click);
            // 
            // cmsLog
            // 
            this.cmsLog.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuLogCopy,
            this.mnuLogCopyAll,
            this.mnuLogSelectAll});
            this.cmsLog.Name = "cmsLog";
            this.cmsLog.Size = new System.Drawing.Size(205, 70);
            this.cmsLog.Opening += new System.ComponentModel.CancelEventHandler(this.CmsLog_Opening);
            // 
            // mnuLogCopy
            // 
            this.mnuLogCopy.Name = "mnuLogCopy";
            this.mnuLogCopy.ShortcutKeyDisplayString = "Ctrl+C";
            this.mnuLogCopy.Size = new System.Drawing.Size(204, 22);
            this.mnuLogCopy.Text = "复制选中内容";
            this.mnuLogCopy.Click += new System.EventHandler(this.MnuLogCopy_Click);
            // 
            // mnuLogCopyAll
            // 
            this.mnuLogCopyAll.Name = "mnuLogCopyAll";
            this.mnuLogCopyAll.Size = new System.Drawing.Size(204, 22);
            this.mnuLogCopyAll.Text = "复制全部日志";
            this.mnuLogCopyAll.Click += new System.EventHandler(this.MnuLogCopyAll_Click);
            // 
            // mnuLogSelectAll
            // 
            this.mnuLogSelectAll.Name = "mnuLogSelectAll";
            this.mnuLogSelectAll.ShortcutKeyDisplayString = "Ctrl+A";
            this.mnuLogSelectAll.Size = new System.Drawing.Size(204, 22);
            this.mnuLogSelectAll.Text = "全选";
            this.mnuLogSelectAll.Click += new System.EventHandler(this.MnuLogSelectAll_Click);
            // 
            // pnlClassifyBar
            // 
            this.pnlClassifyBar.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.pnlClassifyBar.Controls.Add(this.lblClassifySummary);
            this.pnlClassifyBar.Controls.Add(this.chkAutoPreview);
            this.pnlClassifyBar.Controls.Add(this.btnPartInfo);
            this.pnlClassifyBar.Controls.Add(this.btnOpenClassifyDir);
            this.pnlClassifyBar.Controls.Add(this.btnPreviewPdf);
            this.pnlClassifyBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlClassifyBar.Location = new System.Drawing.Point(3, 201);
            this.pnlClassifyBar.Name = "pnlClassifyBar";
            this.pnlClassifyBar.Size = new System.Drawing.Size(840, 44);
            this.pnlClassifyBar.TabIndex = 1;
            // 
            // lblClassifySummary
            // 
            this.lblClassifySummary.AutoEllipsis = true;
            this.lblClassifySummary.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblClassifySummary.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblClassifySummary.Location = new System.Drawing.Point(6, 5);
            this.lblClassifySummary.Name = "lblClassifySummary";
            this.lblClassifySummary.Size = new System.Drawing.Size(294, 34);
            this.lblClassifySummary.TabIndex = 0;
            this.lblClassifySummary.Text = "尚未执行分类";
            this.lblClassifySummary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // chkAutoPreview
            // 
            this.chkAutoPreview.AutoSize = true;
            this.chkAutoPreview.Checked = true;
            this.chkAutoPreview.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoPreview.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.chkAutoPreview.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.chkAutoPreview.Location = new System.Drawing.Point(306, 13);
            this.chkAutoPreview.Name = "chkAutoPreview";
            this.chkAutoPreview.Size = new System.Drawing.Size(87, 21);
            this.chkAutoPreview.TabIndex = 0;
            this.chkAutoPreview.Text = "选中即预览";
            this.chkAutoPreview.UseVisualStyleBackColor = true;
            this.chkAutoPreview.CheckedChanged += new System.EventHandler(this.ChkAutoPreview_CheckedChanged);
            // 
            // btnPartInfo
            // 
            this.btnPartInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPartInfo.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(219)))), ((int)(((byte)(233)))), ((int)(((byte)(248)))));
            this.btnPartInfo.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPartInfo.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(152)))), ((int)(((byte)(184)))), ((int)(((byte)(214)))));
            this.btnPartInfo.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(203)))), ((int)(((byte)(222)))), ((int)(((byte)(242)))));
            this.btnPartInfo.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPartInfo.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnPartInfo.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(31)))), ((int)(((byte)(78)))), ((int)(((byte)(121)))));
            this.btnPartInfo.Location = new System.Drawing.Point(408, 8);
            this.btnPartInfo.Name = "btnPartInfo";
            this.btnPartInfo.Size = new System.Drawing.Size(90, 28);
            this.btnPartInfo.TabIndex = 1;
            this.btnPartInfo.Text = "部品信息";
            this.btnPartInfo.UseVisualStyleBackColor = false;
            this.btnPartInfo.Click += new System.EventHandler(this.BtnPartInfo_Click);
            // 
            // btnOpenClassifyDir
            // 
            this.btnOpenClassifyDir.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenClassifyDir.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnOpenClassifyDir.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenClassifyDir.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnOpenClassifyDir.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnOpenClassifyDir.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenClassifyDir.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnOpenClassifyDir.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnOpenClassifyDir.Location = new System.Drawing.Point(600, 8);
            this.btnOpenClassifyDir.Name = "btnOpenClassifyDir";
            this.btnOpenClassifyDir.Size = new System.Drawing.Size(110, 28);
            this.btnOpenClassifyDir.TabIndex = 3;
            this.btnOpenClassifyDir.Text = "打开分类目录";
            this.btnOpenClassifyDir.UseVisualStyleBackColor = false;
            this.btnOpenClassifyDir.Click += new System.EventHandler(this.BtnOpenClassifyDir_Click);
            // 
            // btnPreviewPdf
            // 
            this.btnPreviewPdf.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPreviewPdf.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnPreviewPdf.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPreviewPdf.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnPreviewPdf.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnPreviewPdf.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPreviewPdf.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnPreviewPdf.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnPreviewPdf.Location = new System.Drawing.Point(504, 8);
            this.btnPreviewPdf.Name = "btnPreviewPdf";
            this.btnPreviewPdf.Size = new System.Drawing.Size(90, 28);
            this.btnPreviewPdf.TabIndex = 1;
            this.btnPreviewPdf.Text = "预览 PDF";
            this.btnPreviewPdf.UseVisualStyleBackColor = false;
            this.btnPreviewPdf.Click += new System.EventHandler(this.BtnPreviewPdf_Click);
            // 
            // grpAction
            // 
            this.grpAction.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpAction.BackColor = System.Drawing.Color.White;
            this.grpAction.Controls.Add(this.progressBar);
            this.grpAction.Controls.Add(this.lblProgress);
            this.grpAction.Controls.Add(this.btnOpenOutput);
            this.grpAction.Controls.Add(this.btnStart);
            this.grpAction.Controls.Add(this.btnAiAnalyze);
            this.grpAction.Controls.Add(this.btnCmdAnalyze);
            this.grpAction.Controls.Add(this.btnCmdStep1);
            this.grpAction.Controls.Add(this.btnCmdStep9);
            this.grpAction.Controls.Add(this.btnCmdStep2);
            this.grpAction.Controls.Add(this.btnCmdStep3);
            this.grpAction.Controls.Add(this.btnCmdStep4);
            this.grpAction.Controls.Add(this.btnCmdStep5);
            this.grpAction.Controls.Add(this.btnCmdStep7);
            this.grpAction.Controls.Add(this.btnCmdStep8);
            this.grpAction.Controls.Add(this.btnAiSettings);
            this.grpAction.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.grpAction.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.grpAction.Location = new System.Drawing.Point(20, 126);
            this.grpAction.Name = "grpAction";
            this.grpAction.Size = new System.Drawing.Size(860, 130);
            this.grpAction.TabIndex = 1;
            this.grpAction.TabStop = false;
            this.grpAction.Text = "②  执行";
            // 
            // progressBar
            // 
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(692, 56);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(150, 18);
            this.progressBar.TabIndex = 6;
            // 
            // lblProgress
            // 
            this.lblProgress.AutoSize = true;
            this.lblProgress.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblProgress.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(108)))), ((int)(((byte)(117)))), ((int)(((byte)(125)))));
            this.lblProgress.Location = new System.Drawing.Point(692, 34);
            this.lblProgress.Name = "lblProgress";
            this.lblProgress.Size = new System.Drawing.Size(56, 17);
            this.lblProgress.TabIndex = 5;
            this.lblProgress.Text = "等待开始";
            // 
            // btnOpenOutput
            // 
            this.btnOpenOutput.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(242)))), ((int)(((byte)(245)))));
            this.btnOpenOutput.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenOutput.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnOpenOutput.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(232)))), ((int)(((byte)(240)))));
            this.btnOpenOutput.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenOutput.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnOpenOutput.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnOpenOutput.Location = new System.Drawing.Point(456, 34);
            this.btnOpenOutput.Name = "btnOpenOutput";
            this.btnOpenOutput.Size = new System.Drawing.Size(128, 46);
            this.btnOpenOutput.TabIndex = 3;
            this.btnOpenOutput.Text = "打开输出目录";
            this.btnOpenOutput.UseVisualStyleBackColor = false;
            this.btnOpenOutput.Click += new System.EventHandler(this.BtnOpenOutput_Click);
            // 
            // btnStart
            // 
            this.btnStart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(21)))), ((int)(((byte)(101)))), ((int)(((byte)(192)))));
            this.btnStart.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnStart.FlatAppearance.BorderSize = 0;
            this.btnStart.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(13)))), ((int)(((byte)(71)))), ((int)(((byte)(161)))));
            this.btnStart.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(136)))), ((int)(((byte)(229)))));
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.Font = new System.Drawing.Font("Microsoft YaHei", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.Location = new System.Drawing.Point(18, 34);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(190, 46);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "开始分解";
            this.btnStart.UseVisualStyleBackColor = false;
            this.btnStart.Click += new System.EventHandler(this.BtnStart_Click);
            // 
            // btnAiAnalyze
            // 
            this.btnAiAnalyze.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(46)))), ((int)(((byte)(125)))), ((int)(((byte)(50)))));
            this.btnAiAnalyze.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAiAnalyze.FlatAppearance.BorderSize = 0;
            this.btnAiAnalyze.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(27)))), ((int)(((byte)(94)))), ((int)(((byte)(32)))));
            this.btnAiAnalyze.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(76)))), ((int)(((byte)(175)))), ((int)(((byte)(80)))));
            this.btnAiAnalyze.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAiAnalyze.Font = new System.Drawing.Font("Microsoft YaHei", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnAiAnalyze.ForeColor = System.Drawing.Color.White;
            this.btnAiAnalyze.Location = new System.Drawing.Point(338, 34);
            this.btnAiAnalyze.Name = "btnAiAnalyze";
            this.btnAiAnalyze.Size = new System.Drawing.Size(112, 46);
            this.btnAiAnalyze.TabIndex = 2;
            this.btnAiAnalyze.Text = "AI 分析";
            this.btnAiAnalyze.UseVisualStyleBackColor = false;
            this.btnAiAnalyze.Click += new System.EventHandler(this.BtnAiAnalyze_Click);
            // 
            // btnCmdAnalyze
            // 
            this.btnCmdAnalyze.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(121)))), ((int)(((byte)(107)))));
            this.btnCmdAnalyze.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCmdAnalyze.FlatAppearance.BorderSize = 0;
            this.btnCmdAnalyze.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(77)))), ((int)(((byte)(64)))));
            this.btnCmdAnalyze.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(150)))), ((int)(((byte)(136)))));
            this.btnCmdAnalyze.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCmdAnalyze.Font = new System.Drawing.Font("Microsoft YaHei", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCmdAnalyze.ForeColor = System.Drawing.Color.White;
            this.btnCmdAnalyze.Location = new System.Drawing.Point(214, 34);
            this.btnCmdAnalyze.Name = "btnCmdAnalyze";
            this.btnCmdAnalyze.Size = new System.Drawing.Size(118, 46);
            this.btnCmdAnalyze.TabIndex = 1;
            this.btnCmdAnalyze.Text = "CMD 分析";
            this.btnCmdAnalyze.UseVisualStyleBackColor = false;
            this.btnCmdAnalyze.Click += new System.EventHandler(this.BtnCmdAnalyze_Click);
            // 
            // btnCmdStep9（按钮 1 · 第 1 步 特注品.json 作成：不写库，扫描 xlsx 生成 特注品.csv / 特注品.json）
            // 
            this.btnCmdStep9.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(244)))), ((int)(((byte)(249)))));
            this.btnCmdStep9.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCmdStep9.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnCmdStep9.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(226)))), ((int)(((byte)(240)))));
            this.btnCmdStep9.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCmdStep9.Font = new System.Drawing.Font("Microsoft YaHei", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCmdStep9.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnCmdStep9.Location = new System.Drawing.Point(114, 88);
            this.btnCmdStep9.Name = "btnCmdStep9";
            this.btnCmdStep9.Size = new System.Drawing.Size(98, 22);
            this.btnCmdStep9.TabIndex = 16;
            this.btnCmdStep9.Text = "1 特注品JSON";
            this.btnCmdStep9.UseVisualStyleBackColor = false;
            this.btnCmdStep9.Click += new System.EventHandler(this.BtnCmdStep9_Click);
            // 
            // btnCmdStep1
            // 
            this.btnCmdStep1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(244)))), ((int)(((byte)(249)))));
            this.btnCmdStep1.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCmdStep1.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnCmdStep1.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(226)))), ((int)(((byte)(240)))));
            this.btnCmdStep1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCmdStep1.Font = new System.Drawing.Font("Microsoft YaHei", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCmdStep1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnCmdStep1.Location = new System.Drawing.Point(12, 88);
            this.btnCmdStep1.Name = "btnCmdStep1";
            this.btnCmdStep1.Size = new System.Drawing.Size(98, 22);
            this.btnCmdStep1.TabIndex = 7;
            this.btnCmdStep1.Text = "0 前置校验";
            this.btnCmdStep1.UseVisualStyleBackColor = false;
            this.btnCmdStep1.Click += new System.EventHandler(this.BtnCmdStep1_Click);
            // 
            // btnCmdStep2
            // 
            this.btnCmdStep2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(244)))), ((int)(((byte)(249)))));
            this.btnCmdStep2.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCmdStep2.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnCmdStep2.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(226)))), ((int)(((byte)(240)))));
            this.btnCmdStep2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCmdStep2.Font = new System.Drawing.Font("Microsoft YaHei", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCmdStep2.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnCmdStep2.Location = new System.Drawing.Point(216, 88);
            this.btnCmdStep2.Name = "btnCmdStep2";
            this.btnCmdStep2.Size = new System.Drawing.Size(98, 22);
            this.btnCmdStep2.TabIndex = 8;
            this.btnCmdStep2.Text = "2 装配图入库";
            this.btnCmdStep2.UseVisualStyleBackColor = false;
            this.btnCmdStep2.Click += new System.EventHandler(this.BtnCmdStep2_Click);
            // 
            // btnCmdStep3
            // 
            this.btnCmdStep3.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(244)))), ((int)(((byte)(249)))));
            this.btnCmdStep3.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCmdStep3.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnCmdStep3.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(226)))), ((int)(((byte)(240)))));
            this.btnCmdStep3.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCmdStep3.Font = new System.Drawing.Font("Microsoft YaHei", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCmdStep3.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnCmdStep3.Location = new System.Drawing.Point(318, 88);
            this.btnCmdStep3.Name = "btnCmdStep3";
            this.btnCmdStep3.Size = new System.Drawing.Size(98, 22);
            this.btnCmdStep3.TabIndex = 9;
            this.btnCmdStep3.Text = "3 圈号拆分";
            this.btnCmdStep3.UseVisualStyleBackColor = false;
            this.btnCmdStep3.Click += new System.EventHandler(this.BtnCmdStep3_Click);
            // 
            // btnCmdStep4
            // 
            this.btnCmdStep4.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(244)))), ((int)(((byte)(249)))));
            this.btnCmdStep4.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCmdStep4.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnCmdStep4.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(226)))), ((int)(((byte)(240)))));
            this.btnCmdStep4.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCmdStep4.Font = new System.Drawing.Font("Microsoft YaHei", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCmdStep4.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnCmdStep4.Location = new System.Drawing.Point(420, 88);
            this.btnCmdStep4.Name = "btnCmdStep4";
            this.btnCmdStep4.Size = new System.Drawing.Size(98, 22);
            this.btnCmdStep4.TabIndex = 10;
            this.btnCmdStep4.Text = "4 要領図追加";
            this.btnCmdStep4.UseVisualStyleBackColor = false;
            this.btnCmdStep4.Click += new System.EventHandler(this.BtnCmdStep4_Click);
            // 
            // btnCmdStep5
            // 
            this.btnCmdStep5.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(244)))), ((int)(((byte)(249)))));
            this.btnCmdStep5.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCmdStep5.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnCmdStep5.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(226)))), ((int)(((byte)(240)))));
            this.btnCmdStep5.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCmdStep5.Font = new System.Drawing.Font("Microsoft YaHei", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCmdStep5.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnCmdStep5.Location = new System.Drawing.Point(522, 88);
            this.btnCmdStep5.Name = "btnCmdStep5";
            this.btnCmdStep5.Size = new System.Drawing.Size(98, 22);
            this.btnCmdStep5.TabIndex = 11;
            this.btnCmdStep5.Text = "5 特注登录";
            this.btnCmdStep5.UseVisualStyleBackColor = false;
            this.btnCmdStep5.Click += new System.EventHandler(this.BtnCmdStep5_Click);
            // 
            // btnCmdStep7（按钮 6 · 第 6 步 部品コード正規化）
            // 原 btnCmdStep6 编号已不存在：现在的 6 号按钮由 btnCmdStep7 显示（控件名保持历史命名）
            // 
            this.btnCmdStep7.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(244)))), ((int)(((byte)(249)))));
            this.btnCmdStep7.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCmdStep7.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnCmdStep7.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(226)))), ((int)(((byte)(240)))));
            this.btnCmdStep7.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCmdStep7.Font = new System.Drawing.Font("Microsoft YaHei", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCmdStep7.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnCmdStep7.Location = new System.Drawing.Point(624, 88);
            this.btnCmdStep7.Name = "btnCmdStep7";
            this.btnCmdStep7.Size = new System.Drawing.Size(98, 22);
            this.btnCmdStep7.TabIndex = 13;
            this.btnCmdStep7.Text = "6 部品コード";
            this.btnCmdStep7.UseVisualStyleBackColor = false;
            this.btnCmdStep7.Click += new System.EventHandler(this.BtnCmdStep7_Click);
            // 
            // btnCmdStep8
            // 
            this.btnCmdStep8.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(244)))), ((int)(((byte)(249)))));
            this.btnCmdStep8.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCmdStep8.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnCmdStep8.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(226)))), ((int)(((byte)(240)))));
            this.btnCmdStep8.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCmdStep8.Font = new System.Drawing.Font("Microsoft YaHei", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCmdStep8.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnCmdStep8.Location = new System.Drawing.Point(726, 88);
            this.btnCmdStep8.Name = "btnCmdStep8";
            this.btnCmdStep8.Size = new System.Drawing.Size(98, 22);
            this.btnCmdStep8.TabIndex = 14;
            this.btnCmdStep8.Text = "7 使い分け";
            this.btnCmdStep8.UseVisualStyleBackColor = false;
            this.btnCmdStep8.Click += new System.EventHandler(this.BtnCmdStep8_Click);
            // 
            // tipCmdStep
            // 
            this.tipCmdStep.AutoPopDelay = 30000;
            this.tipCmdStep.InitialDelay = 260;
            this.tipCmdStep.ReshowDelay = 120;
            this.tipCmdStep.ShowAlways = true;
            // 
            // btnAiSettings
            // 
            this.btnAiSettings.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(242)))), ((int)(((byte)(245)))));
            this.btnAiSettings.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAiSettings.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnAiSettings.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(232)))), ((int)(((byte)(240)))));
            this.btnAiSettings.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAiSettings.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnAiSettings.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnAiSettings.Location = new System.Drawing.Point(590, 34);
            this.btnAiSettings.Name = "btnAiSettings";
            this.btnAiSettings.Size = new System.Drawing.Size(96, 46);
            this.btnAiSettings.TabIndex = 4;
            this.btnAiSettings.Text = "AI 设置";
            this.btnAiSettings.UseVisualStyleBackColor = false;
            this.btnAiSettings.Click += new System.EventHandler(this.BtnAiSettings_Click);
            // 
            // btnMemberNameMap
            // 
            this.btnMemberNameMap.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(242)))), ((int)(((byte)(245)))));
            this.btnMemberNameMap.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnMemberNameMap.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnMemberNameMap.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(232)))), ((int)(((byte)(240)))));
            this.btnMemberNameMap.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMemberNameMap.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnMemberNameMap.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnMemberNameMap.Location = new System.Drawing.Point(520, 64);
            this.btnMemberNameMap.Name = "btnMemberNameMap";
            this.btnMemberNameMap.Size = new System.Drawing.Size(322, 26);
            this.btnMemberNameMap.TabIndex = 15;
            this.btnMemberNameMap.Text = "部材名置换表（例：鴨居 → 上枠）…";
            this.btnMemberNameMap.UseVisualStyleBackColor = false;
            this.btnMemberNameMap.Click += new System.EventHandler(this.BtnMemberNameMap_Click);
            // 
            // grpInput
            // 
            this.grpInput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpInput.BackColor = System.Drawing.Color.White;
            this.grpInput.Controls.Add(this.btnBrowse);
            this.grpInput.Controls.Add(this.txtFolder);
            this.grpInput.Controls.Add(this.lblFolder);
            this.grpInput.Controls.Add(this.chkCadSplit);
            this.grpInput.Controls.Add(this.btnMemberNameMap);
            this.grpInput.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.grpInput.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.grpInput.Location = new System.Drawing.Point(20, 16);
            this.grpInput.Name = "grpInput";
            this.grpInput.Size = new System.Drawing.Size(860, 104);
            this.grpInput.TabIndex = 0;
            this.grpInput.TabStop = false;
            this.grpInput.Text = "①  输入设置";
            // 
            // btnBrowse
            // 
            this.btnBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowse.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnBrowse.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnBrowse.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnBrowse.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnBrowse.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowse.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnBrowse.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnBrowse.Location = new System.Drawing.Point(732, 36);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(110, 32);
            this.btnBrowse.TabIndex = 2;
            this.btnBrowse.Text = "浏览...";
            this.btnBrowse.UseVisualStyleBackColor = false;
            this.btnBrowse.Click += new System.EventHandler(this.BtnBrowseFolder_Click);
            // 
            // txtFolder
            // 
            this.txtFolder.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtFolder.BackColor = System.Drawing.Color.White;
            this.txtFolder.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtFolder.Font = new System.Drawing.Font("Microsoft YaHei", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtFolder.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(33)))), ((int)(((byte)(37)))), ((int)(((byte)(41)))));
            this.txtFolder.Location = new System.Drawing.Point(112, 38);
            this.txtFolder.Name = "txtFolder";
            this.txtFolder.Size = new System.Drawing.Size(608, 25);
            this.txtFolder.TabIndex = 1;
            // 
            // lblFolder
            // 
            this.lblFolder.AutoSize = true;
            this.lblFolder.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblFolder.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblFolder.Location = new System.Drawing.Point(18, 44);
            this.lblFolder.Name = "lblFolder";
            this.lblFolder.Size = new System.Drawing.Size(82, 17);
            this.lblFolder.TabIndex = 0;
            this.lblFolder.Text = "PDF 文件夹：";
            // 
            // chkCadSplit
            // 
            this.chkCadSplit.AutoSize = true;
            this.chkCadSplit.Checked = true;
            this.chkCadSplit.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkCadSplit.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.chkCadSplit.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.chkCadSplit.Location = new System.Drawing.Point(112, 68);
            this.chkCadSplit.Name = "chkCadSplit";
            this.chkCadSplit.Size = new System.Drawing.Size(392, 21);
            this.chkCadSplit.TabIndex = 3;
            this.chkCadSplit.Text = "开始分解前先调用 CadPartSplitter 做零件分割（Part_NN.png 等）";
            this.chkCadSplit.UseVisualStyleBackColor = true;
            // 
            // statusStrip
            // 
            this.statusStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.statusStrip.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblStatus,
            this.lblSpring,
            this.lblPdfInfo,
            this.lblEngine});
            this.statusStrip.Location = new System.Drawing.Point(0, 678);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(900, 22);
            this.statusStrip.SizingGrip = false;
            this.statusStrip.TabIndex = 2;
            // 
            // lblStatus
            // 
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(32, 17);
            this.lblStatus.Text = "就绪";
            // 
            // lblSpring
            // 
            this.lblSpring.Name = "lblSpring";
            this.lblSpring.Size = new System.Drawing.Size(699, 17);
            this.lblSpring.Spring = true;
            // 
            // lblPdfInfo
            // 
            this.lblPdfInfo.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblPdfInfo.Name = "lblPdfInfo";
            this.lblPdfInfo.Size = new System.Drawing.Size(65, 17);
            this.lblPdfInfo.Text = "PDF：0 个";
            // 
            // lblEngine
            // 
            this.lblEngine.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(108)))), ((int)(((byte)(117)))), ((int)(((byte)(125)))));
            this.lblEngine.Name = "lblEngine";
            this.lblEngine.Size = new System.Drawing.Size(89, 17);
            this.lblEngine.Text = "引擎：加载中...";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.ClientSize = new System.Drawing.Size(900, 700);
            this.Controls.Add(this.pnlContent);
            this.Controls.Add(this.pnlHeader);
            this.Controls.Add(this.statusStrip);
            this.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MinimumSize = new System.Drawing.Size(900, 700);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "MSIF AI TOOLS  -  PDF 分解工具（坐标 + 文本 + 分类）";
            this.Shown += new System.EventHandler(this.Form1_Shown);
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.pnlContent.ResumeLayout(false);
            this.grpLog.ResumeLayout(false);
            this.tabMain.ResumeLayout(false);
            this.tabPageLog.ResumeLayout(false);
            this.tabPageClassify.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvClassify)).EndInit();
            this.cmsClassify.ResumeLayout(false);
            this.cmsLog.ResumeLayout(false);
            this.pnlClassifyBar.ResumeLayout(false);
            this.pnlClassifyBar.PerformLayout();
            this.grpAction.ResumeLayout(false);
            this.grpAction.PerformLayout();
            this.grpInput.ResumeLayout(false);
            this.grpInput.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblAppTitle;
        private System.Windows.Forms.Label lblAppSub;
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.Panel pnlHeaderAccent;
        private System.Windows.Forms.Panel pnlContent;
        private System.Windows.Forms.GroupBox grpInput;
        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.TextBox txtFolder;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.CheckBox chkCadSplit;
        private System.Windows.Forms.GroupBox grpAction;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnAiAnalyze;
        private System.Windows.Forms.Button btnCmdAnalyze;
        private System.Windows.Forms.Button btnCmdStep1;
        private System.Windows.Forms.Button btnCmdStep2;
        private System.Windows.Forms.Button btnCmdStep3;
        private System.Windows.Forms.Button btnCmdStep4;
        private System.Windows.Forms.Button btnCmdStep5;
        private System.Windows.Forms.Button btnCmdStep7;
        private System.Windows.Forms.Button btnCmdStep8;
        private System.Windows.Forms.Button btnCmdStep9;
        private System.Windows.Forms.ToolTip tipCmdStep;
        private System.Windows.Forms.Button btnAiSettings;
        private System.Windows.Forms.Button btnMemberNameMap;
        private System.Windows.Forms.Button btnOpenOutput;
        private System.Windows.Forms.Label lblProgress;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.GroupBox grpLog;
        private System.Windows.Forms.TabControl tabMain;
        private System.Windows.Forms.TabPage tabPageLog;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.TabPage tabPageClassify;
        private System.Windows.Forms.DataGridView dgvClassify;
        private System.Windows.Forms.Panel pnlClassifyBar;
        private System.Windows.Forms.Label lblClassifySummary;
        private System.Windows.Forms.Button btnOpenClassifyDir;
        private System.Windows.Forms.Button btnPreviewPdf;
        private System.Windows.Forms.Button btnPartInfo;
        private System.Windows.Forms.ContextMenuStrip cmsClassify;
        private System.Windows.Forms.ToolStripMenuItem mnuShowPartInfo;
        private System.Windows.Forms.ToolStripMenuItem mnuPreviewPdf;
        private System.Windows.Forms.ContextMenuStrip cmsLog;
        private System.Windows.Forms.ToolStripMenuItem mnuLogCopy;
        private System.Windows.Forms.ToolStripMenuItem mnuLogCopyAll;
        private System.Windows.Forms.ToolStripMenuItem mnuLogSelectAll;
        private System.Windows.Forms.CheckBox chkAutoPreview;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblSpring;
        private System.Windows.Forms.ToolStripStatusLabel lblPdfInfo;
        private System.Windows.Forms.ToolStripStatusLabel lblEngine;
    }
}
