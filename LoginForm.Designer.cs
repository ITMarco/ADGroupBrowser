namespace ADGroupBrowser;

partial class LoginForm
{
    private System.ComponentModel.IContainer components = null;

    private TableLayoutPanel root;
    private PictureBox picLogo;
    private Label lblTitle;
    private Label lblServerLabel;
    private Label lblServerValue;
    private Label lblDomainLabel;
    private Label lblDomainValue;
    private Label lblUsernameLabel;
    private TextBox txtUsername;
    private Label lblPasswordLabel;
    private TableLayoutPanel pwRow;   // textbox + eye toggle
    private TextBox txtPassword;
    private Label lblEye;
    private Label lblError;
    private CheckBox chkIntegrated;
    private TableLayoutPanel btnRow;
    private Button btnConfig;
    private Button btnExit;
    private Button btnConnect;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        var brandBlue = Color.FromArgb(31, 73, 125);
        var lightBtn  = Color.FromArgb(240, 242, 245);

        // Font-based auto-scaling — TableLayoutPanel reflows so cells never overlap.
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;

        Text            = "AD Group Browser — Connect";
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        Font            = new Font("Segoe UI", 9.5f);
        BackColor       = Color.White;
        ClientSize      = new Size(470, 484);

        // ── root table ────────────────────────────────────────────────────────
        root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            Padding     = new Padding(24, 18, 24, 14),
            BackColor   = Color.White,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        // ── title row (logo + heading) ───────────────────────────────────────
        var titleRow = new FlowLayoutPanel
        {
            AutoSize      = true,
            WrapContents  = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin        = new Padding(0, 0, 0, 14),
        };

        picLogo = new PictureBox
        {
            Width    = 36,
            Height   = 36,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin   = new Padding(0, 0, 10, 0),
            Image    = AppIcon.LoadLogo(64),
        };

        lblTitle = new Label
        {
            Text      = "Active Directory Group Browser",
            Font      = new Font("Segoe UI", 13.5f),
            ForeColor = brandBlue,
            AutoSize  = true,
            Margin    = new Padding(0, 6, 0, 0),
        };

        titleRow.Controls.Add(picLogo);
        titleRow.Controls.Add(lblTitle);

        // ── server / domain info ──────────────────────────────────────────────
        lblServerLabel = new Label { Text = "Server:", AutoSize = true, ForeColor = Color.Gray,    Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 10, 3) };
        lblServerValue = new Label { Text = "",        AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 3) };
        lblDomainLabel = new Label { Text = "Domain:", AutoSize = true, ForeColor = Color.Gray,    Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 10, 10) };
        lblDomainValue = new Label { Text = "",        AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 10), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };

        // ── SSO checkbox ──────────────────────────────────────────────────────
        chkIntegrated = new CheckBox
        {
            Text      = "Use my current Windows sign-in  (Single Sign-On)",
            AutoSize  = true,
            Checked   = true,
            Font      = new Font("Segoe UI", 9.5f),
            ForeColor = brandBlue,
            Margin    = new Padding(0, 10, 0, 6),
            Cursor    = Cursors.Hand,
        };
        chkIntegrated.CheckedChanged += chkIntegrated_CheckedChanged;

        // ── username ──────────────────────────────────────────────────────────
        lblUsernameLabel = new Label { Text = "Username", AutoSize = true, Margin = new Padding(0, 4, 0, 2) };
        txtUsername = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        txtUsername.KeyDown += txtUsername_KeyDown;

        // ── password (textbox + eye toggle in a 2-col sub-table) ──────────────
        lblPasswordLabel = new Label { Text = "Password", AutoSize = true, Margin = new Padding(0, 4, 0, 2) };

        pwRow = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1,
            AutoSize    = true,
            Margin      = new Padding(0, 0, 0, 12),
        };
        pwRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pwRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        txtPassword = new TextBox
        {
            Dock         = DockStyle.Fill,
            PasswordChar = '●',
            Margin       = new Padding(0, 0, 4, 0),
        };
        txtPassword.KeyDown += txtPassword_KeyDown;

        lblEye = new Label
        {
            Text      = "👁",
            AutoSize  = false,
            Width     = 34,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor    = Cursors.Hand,
            Font      = new Font("Segoe UI", 11f),
            BackColor = Color.FromArgb(245, 247, 250),
            BorderStyle = BorderStyle.FixedSingle,
        };
        lblEye.Click += lblEye_Click;

        pwRow.Controls.Add(txtPassword, 0, 0);
        pwRow.Controls.Add(lblEye, 1, 0);

        // ── error / status ────────────────────────────────────────────────────
        lblError = new Label
        {
            Text        = "",
            AutoSize    = true,
            MaximumSize = new Size(410, 0),   // wrap, grow downward
            ForeColor   = Color.Firebrick,
            Font        = new Font("Segoe UI", 8.5f),
            Margin      = new Padding(0, 2, 0, 8),
            MinimumSize = new Size(410, 34),
        };

        // ── buttons row ───────────────────────────────────────────────────────
        btnRow = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1,
            AutoSize    = true,
            Margin      = new Padding(0, 6, 0, 0),
        };
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        btnConfig = new Button
        {
            Text      = "⚙  Config",
            AutoSize  = true,
            Padding   = new Padding(8, 6, 8, 6),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.DimGray,
            BackColor = lightBtn,
            Margin    = new Padding(0),
            Visible   = false,   // shown only when config.json is writable
        };
        btnConfig.FlatAppearance.BorderColor = Color.FromArgb(190, 195, 205);
        btnConfig.Click += btnConfig_Click;

        btnExit = new Button
        {
            Text      = "Exit",
            AutoSize  = true,
            Padding   = new Padding(14, 6, 14, 6),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.DimGray,
            BackColor = lightBtn,
            Margin    = new Padding(0, 0, 8, 0),
        };
        btnExit.FlatAppearance.BorderColor = Color.FromArgb(190, 195, 205);
        btnExit.Click += btnExit_Click;

        btnConnect = new Button
        {
            Text      = "Connect",
            AutoSize  = true,
            Padding   = new Padding(22, 6, 22, 6),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = brandBlue,
            Font      = new Font("Segoe UI", 10f),
            Margin    = new Padding(0),
        };
        btnConnect.FlatAppearance.BorderSize = 0;
        btnConnect.Click += btnConnect_Click;
        AcceptButton = btnConnect;

        // Right-aligned action buttons in a non-wrapping FlowLayoutPanel so Connect
        // never gets pushed onto its own row (same fix as the config editor's Save /
        // Save As buttons).
        var rightBtns = new FlowLayoutPanel
        {
            AutoSize      = true,
            WrapContents  = false,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor        = AnchorStyles.Right,
            Margin        = new Padding(0),
        };
        rightBtns.Controls.Add(btnExit);
        rightBtns.Controls.Add(btnConnect);

        btnRow.Controls.Add(btnConfig, 0, 0);
        btnRow.Controls.Add(rightBtns, 1, 0);

        // ── assemble rows ─────────────────────────────────────────────────────
        root.Controls.Add(titleRow,          0, 0);  root.SetColumnSpan(titleRow,          2);
        root.Controls.Add(lblServerLabel,    0, 1);  root.Controls.Add(lblServerValue,    1, 1);
        root.Controls.Add(lblDomainLabel,    0, 2);  root.Controls.Add(lblDomainValue,    1, 2);
        root.Controls.Add(chkIntegrated,     0, 3);  root.SetColumnSpan(chkIntegrated,    2);
        root.Controls.Add(lblUsernameLabel,  0, 4);  root.SetColumnSpan(lblUsernameLabel, 2);
        root.Controls.Add(txtUsername,       0, 5);  root.SetColumnSpan(txtUsername,      2);
        root.Controls.Add(lblPasswordLabel,  0, 6);  root.SetColumnSpan(lblPasswordLabel, 2);
        root.Controls.Add(pwRow,             0, 7);  root.SetColumnSpan(pwRow,            2);
        root.Controls.Add(lblError,          0, 8);  root.SetColumnSpan(lblError,         2);
        root.Controls.Add(btnRow,            0, 9);  root.SetColumnSpan(btnRow,           2);

        Controls.Add(root);
    }
}
