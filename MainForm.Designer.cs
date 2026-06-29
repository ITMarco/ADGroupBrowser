namespace ADGroupBrowser;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    // Controls
    private Panel pnlTop;
    private Label lblConnected;
    private Button btnRefresh;
    private Button btnConfig;
    private Button btnExit;
    private TextBox txtSearch;
    private CheckBox chkRecursive;

    private SplitContainer split;

    // Left pane
    private Label lblGroupHeader;
    private TreeView treeGroups;

    // Right pane
    private Label lblMembersHeader;
    private DataGridView dgvMembers;
    private DataGridViewTextBoxColumn colType;
    private DataGridViewTextBoxColumn colDisplayName;
    private DataGridViewTextBoxColumn colSam;
    private DataGridViewTextBoxColumn colMail;

    // Status
    private StatusStrip statusStrip;
    private ToolStripStatusLabel lblStatus;
    private ToolStripProgressBar progressBar;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        var brandBlue    = Color.FromArgb(31, 73, 125);
        var toolbarDark  = Color.FromArgb(45, 95, 155);

        // Font-based auto-scaling: scales docked panel heights, buttons, labels and
        // fonts together across 100/125/150/175% displays. Custom pixel properties
        // (grid widths, list item height, splitter) are handled in MainForm.OnLoad.
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;

        Font            = new Font("Segoe UI", 9.5f);
        Text            = "AD Group Browser";
        ClientSize      = new Size(1000, 680);
        StartPosition   = FormStartPosition.CenterScreen;
        MinimumSize     = new Size(700, 450);
        BackColor       = Color.White;

        // ── top bar ───────────────────────────────────────────────────────────
        pnlTop = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 50,
            BackColor = brandBlue,
        };

        lblConnected = new Label
        {
            Text      = "",
            ForeColor = Color.White,
            AutoSize  = true,
            Location  = new Point(12, 15),
            Font      = new Font("Segoe UI", 9f),
        };

        // Buttons: wrap in a FlowLayoutPanel docked Right so AutoSize works correctly
        // at any DPI (fixed Width on Dock=Right clips text at 125%/150%).
        btnExit = new Button
        {
            Text      = "Exit",
            AutoSize  = true,
            Padding   = new Padding(14, 0, 14, 0),
            Margin    = new Padding(0, 13, 0, 13),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(170, 50, 50),
            Font      = new Font("Segoe UI", 9.5f),
            Cursor    = Cursors.Hand,
        };
        btnExit.FlatAppearance.BorderSize = 0;
        btnExit.Click += btnExit_Click;

        btnConfig = new Button
        {
            Text      = "⚙  Config",
            AutoSize  = true,
            Padding   = new Padding(14, 0, 14, 0),
            Margin    = new Padding(0, 13, 0, 13),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = toolbarDark,
            Font      = new Font("Segoe UI", 9.5f),
            Cursor    = Cursors.Hand,
            Visible   = false,   // shown only when config.json is writable
        };
        btnConfig.FlatAppearance.BorderSize = 0;
        btnConfig.Click += btnConfig_Click;

        btnRefresh = new Button
        {
            Text      = "⟳  Refresh",
            AutoSize  = true,
            Padding   = new Padding(14, 0, 14, 0),
            Margin    = new Padding(0, 13, 0, 13),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = toolbarDark,
            Font      = new Font("Segoe UI", 9.5f),
            Cursor    = Cursors.Hand,
        };
        btnRefresh.FlatAppearance.BorderSize = 0;
        btnRefresh.Click += btnRefresh_Click;

        // FlowLayoutPanel lets each button auto-size its width to fit the text.
        var rightBtns = new FlowLayoutPanel
        {
            Dock          = DockStyle.Right,
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = brandBlue,
            Margin        = new Padding(0),
            Padding       = new Padding(0),
        };
        rightBtns.Controls.Add(btnRefresh);
        rightBtns.Controls.Add(btnConfig);
        rightBtns.Controls.Add(btnExit);

        pnlTop.Controls.Add(lblConnected);
        pnlTop.Controls.Add(rightBtns);

        // ── search bar ────────────────────────────────────────────────────────
        var pnlSearch = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 38,
            Padding   = new Padding(8, 6, 8, 2),
            BackColor = Color.FromArgb(245, 247, 250),
        };

        txtSearch = new TextBox
        {
            Dock            = DockStyle.Fill,
            PlaceholderText = "🔍  Filter groups by name or description…",
            Font            = new Font("Segoe UI", 10f),
            BorderStyle     = BorderStyle.FixedSingle,
        };
        txtSearch.TextChanged += txtSearch_TextChanged;

        chkRecursive = new CheckBox
        {
            Dock      = DockStyle.Right,
            Width     = 190,
            Text      = "Recursive (incl. nested)",
            Checked   = false,           // default: fast direct-members query
            TextAlign = ContentAlignment.MiddleLeft,
            Font      = new Font("Segoe UI", 9f),
            BackColor = Color.FromArgb(245, 247, 250),
            Cursor    = Cursors.Hand,
        };
        chkRecursive.CheckedChanged += chkRecursive_CheckedChanged;

        // Add Fill control first, Right control last → checkbox docks to the right edge,
        // textbox fills the remainder.
        pnlSearch.Controls.Add(txtSearch);
        pnlSearch.Controls.Add(chkRecursive);

        // ── status strip ──────────────────────────────────────────────────────
        statusStrip = new StatusStrip { SizingGrip = false };
        lblStatus   = new ToolStripStatusLabel
        {
            Text      = "Ready",
            Spring    = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        progressBar = new ToolStripProgressBar
        {
            Style                 = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 0,
            Visible               = false,
            Width                 = 130,
        };
        statusStrip.Items.Add(lblStatus);
        statusStrip.Items.Add(progressBar);

        // ── split container ───────────────────────────────────────────────────
        // NOTE: SplitterDistance / Panel*MinSize are NOT set here. At construction
        // the control is still at its tiny default width, so setting SplitterDistance
        // out of range throws. They're applied in MainForm.OnLoad once the form is
        // at full size. (This was the post-login crash.)
        split = new SplitContainer
        {
            Dock          = DockStyle.Fill,
            SplitterWidth = 5,
        };

        // Left: group list
        lblGroupHeader = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 28,
            Text      = "Groups",
            Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = brandBlue,
            BackColor = Color.FromArgb(235, 240, 248),
            Padding   = new Padding(8, 5, 0, 0),
        };

        treeGroups = new TreeView
        {
            Dock          = DockStyle.Fill,
            BorderStyle   = BorderStyle.None,
            Font          = new Font("Segoe UI", 9.5f),
            HideSelection = false,
            ShowLines     = true,
            ShowRootLines = true,
            ShowPlusMinus = true,
            // ItemHeight auto-tracks the font (DPI-safe) when left unset.
        };
        treeGroups.AfterSelect += treeGroups_AfterSelect;

        split.Panel1.Controls.Add(treeGroups);
        split.Panel1.Controls.Add(lblGroupHeader);

        // Right: member grid
        lblMembersHeader = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 28,
            Text      = "Select a group to view members",
            Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = brandBlue,
            BackColor = Color.FromArgb(235, 240, 248),
            Padding   = new Padding(8, 5, 0, 0),
        };

        // Content/fill-based widths scale automatically with font/DPI (no fixed pixels).
        colType        = new DataGridViewTextBoxColumn { Name = "colType",        HeaderText = "Type",         ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
        colDisplayName = new DataGridViewTextBoxColumn { Name = "colDisplayName", HeaderText = "Display Name", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 45 };
        colSam         = new DataGridViewTextBoxColumn { Name = "colSam",         HeaderText = "SAM Account",  ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
        colMail        = new DataGridViewTextBoxColumn { Name = "colMail",        HeaderText = "E-mail",       ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 55 };

        dgvMembers = new DataGridView
        {
            Dock              = DockStyle.Fill,
            BorderStyle       = BorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows    = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly          = true,
            SelectionMode     = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect       = true,
            AutoSizeRowsMode  = DataGridViewAutoSizeRowsMode.DisplayedCells,   // rows fit font/DPI
            BackgroundColor   = Color.White,
            GridColor         = Color.FromArgb(230, 230, 235),
            Font              = new Font("Segoe UI", 9.5f),
            ColumnHeadersDefaultCellStyle = { Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) },
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize, // header fits font/DPI
        };
        dgvMembers.Columns.AddRange(colType, colDisplayName, colSam, colMail);
        dgvMembers.RowsDefaultCellStyle.BackColor            = Color.White;
        dgvMembers.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 252);
        dgvMembers.CellDoubleClick += dgvMembers_CellDoubleClick;
        dgvMembers.KeyDown         += dgvMembers_KeyDown;

        split.Panel2.Controls.Add(dgvMembers);
        split.Panel2.Controls.Add(lblMembersHeader);

        // ── assemble ──────────────────────────────────────────────────────────
        Controls.Add(split);
        Controls.Add(pnlSearch);
        Controls.Add(pnlTop);
        Controls.Add(statusStrip);
    }
}
