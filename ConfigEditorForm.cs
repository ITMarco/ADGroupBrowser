using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ADGroupBrowser;

/// <summary>
/// GUI editor for the JSON configuration file.
/// Caller is responsible for only showing this when the file is writable.
/// </summary>
public sealed class ConfigEditorForm : Form
{
    private readonly string _configPath;
    private string _savePath;

    // Connection tab
    private TextBox _txtDomain = null!;
    private ListBox _lstDCs = null!;
    private TextBox _txtDCEntry = null!;
    private Button _btnDCAdd = null!, _btnDCUpdate = null!, _btnDCRemove = null!, _btnDCTest = null!;

    // OUs tab
    private ListBox _lstOUs = null!;
    private TextBox _txtOUDn = null!;
    private CheckBox _chkSubtree = null!;
    private Button _btnOUAdd = null!, _btnOUUpdate = null!, _btnOURemove = null!;
    private readonly List<SearchOu> _ous = new();

    // Access tab
    private ListBox _lstGroups = null!;
    private TextBox _txtGroup = null!;
    private Button _btnGroupAdd = null!, _btnGroupUpdate = null!, _btnGroupRemove = null!;

    // Advanced tab
    private CheckBox _chkSsl = null!;
    private NumericUpDown _numPort = null!;
    private NumericUpDown _numTimeout = null!;
    private NumericUpDown _numConnectTimeout = null!;
    private TextBox _txtAuditPath = null!;
    private NumericUpDown _numRetainDays = null!;

    public ConfigEditorForm(AppConfig config, string configPath)
    {
        _configPath = configPath;
        _savePath   = configPath;
        BuildUI();
        LoadFromConfig(config);
    }

    // ── UI construction ────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var brandBlue = Color.FromArgb(31, 73, 125);

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text            = $"Configuration Editor — {Path.GetFileName(_configPath)}";
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize     = new Size(580, 520);
        ClientSize      = new Size(660, 580);
        Font            = new Font("Segoe UI", 9.5f);
        BackColor       = Color.White;

        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 2,
            Padding     = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // tabs fill
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));         // button bar

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9f) };
        tabs.TabPages.Add(BuildConnectionTab());
        tabs.TabPages.Add(BuildOUsTab());
        tabs.TabPages.Add(BuildAccessTab());
        tabs.TabPages.Add(BuildAdvancedTab());

        // ── bottom button bar ──
        var btnBar = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 3,
            RowCount    = 1,
            AutoSize    = true,
            Padding     = new Padding(12, 8, 12, 10),
            BackColor   = Color.FromArgb(245, 247, 250),
        };
        btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var leftBtns = new FlowLayoutPanel
        {
            AutoSize          = true,
            WrapContents      = false,
            FlowDirection     = FlowDirection.LeftToRight,
            Margin            = new Padding(0),
        };
        var btnSave = MakeButton("Save", brandBlue, Color.White);
        btnSave.Padding = new Padding(20, 6, 20, 6);
        btnSave.Click  += (_, _) => DoSave(_savePath);

        var btnSaveAs = MakeButton("Save As…", Color.FromArgb(240, 242, 245), Color.Black);
        btnSaveAs.Padding = new Padding(14, 6, 14, 6);
        btnSaveAs.Click  += (_, _) => DoSaveAs();

        leftBtns.Controls.Add(btnSave);
        leftBtns.Controls.Add(btnSaveAs);

        var btnCancel = MakeButton("Cancel", Color.FromArgb(240, 242, 245), Color.Black);
        btnCancel.Padding = new Padding(14, 6, 14, 6);
        btnCancel.Click  += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        btnBar.Controls.Add(leftBtns, 0, 0);
        btnBar.Controls.Add(btnCancel, 2, 0);

        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(btnBar, 0, 1);
        Controls.Add(root);

        CancelButton = btnCancel;
    }

    // ── Connection tab ─────────────────────────────────────────────────────────

    private TabPage BuildConnectionTab()
    {
        var page = new TabPage("Connection") { Padding = new Padding(10) };

        var tbl = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 5,
            Padding     = new Padding(4),
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // domain row
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // DC label
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // DC list
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // DC entry + buttons
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Test Connection

        // Domain row
        var domainRow = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            AutoSize    = true,
            ColumnCount = 2,
            RowCount    = 1,
            Margin      = new Padding(0, 0, 0, 10),
        };
        domainRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        domainRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        var lblDomain = new Label
        {
            Text   = "Domain:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(0, 4, 10, 0),
        };
        _txtDomain = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f) };
        domainRow.Controls.Add(lblDomain, 0, 0);
        domainRow.Controls.Add(_txtDomain, 1, 0);

        var lblDCs = new Label
        {
            Text     = "Domain Controllers  (hostname  or  hostname:port)",
            AutoSize = true,
            Margin   = new Padding(0, 0, 0, 4),
        };

        _lstDCs = new ListBox
        {
            Dock          = DockStyle.Fill,
            IntegralHeight = false,
            Font          = new Font("Segoe UI", 9.5f),
        };
        _lstDCs.SelectedIndexChanged += (_, _) =>
        {
            bool hasSel = _lstDCs.SelectedIndex >= 0;
            if (hasSel) _txtDCEntry.Text = _lstDCs.SelectedItem?.ToString() ?? "";
            _btnDCUpdate.Enabled = hasSel;
            _btnDCRemove.Enabled = hasSel;
            _btnDCTest.Enabled   = hasSel || !string.IsNullOrWhiteSpace(_txtDCEntry.Text);
        };

        // DC entry row: [TextBox entry] [Add] [Update] [Remove]
        var dcEntryRow = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            AutoSize    = true,
            ColumnCount = 4,
            RowCount    = 1,
            Margin      = new Padding(0, 4, 0, 2),
        };
        dcEntryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        dcEntryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dcEntryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dcEntryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _txtDCEntry = new TextBox
        {
            Dock            = DockStyle.Fill,
            Font            = new Font("Segoe UI", 9.5f),
            PlaceholderText = "dc1.contoso.local  or  dc1.contoso.local:636",
        };
        _txtDCEntry.TextChanged += (_, _) =>
            _btnDCTest.Enabled = _lstDCs.SelectedIndex >= 0 || !string.IsNullOrWhiteSpace(_txtDCEntry.Text);

        _btnDCAdd = MakeSmallButton("Add");
        _btnDCAdd.Click += (_, _) =>
        {
            var val = _txtDCEntry.Text.Trim();
            if (string.IsNullOrEmpty(val)) return;
            _lstDCs.Items.Add(val);
            _lstDCs.SelectedIndex = _lstDCs.Items.Count - 1;
            _txtDCEntry.Clear();
        };

        _btnDCUpdate = MakeSmallButton("Update");
        _btnDCUpdate.Enabled = false;
        _btnDCUpdate.Click += (_, _) =>
        {
            int sel = _lstDCs.SelectedIndex;
            if (sel < 0) return;
            var val = _txtDCEntry.Text.Trim();
            if (string.IsNullOrEmpty(val)) return;
            _lstDCs.Items[sel] = val;
            _lstDCs.ClearSelected();
            _txtDCEntry.Clear();
        };

        _btnDCRemove = MakeSmallButton("Remove");
        _btnDCRemove.Enabled = false;
        _btnDCRemove.Click += (_, _) =>
        {
            int sel = _lstDCs.SelectedIndex;
            if (sel >= 0) { _lstDCs.Items.RemoveAt(sel); _txtDCEntry.Clear(); }
        };

        dcEntryRow.Controls.Add(_txtDCEntry, 0, 0);
        dcEntryRow.Controls.Add(_btnDCAdd,    1, 0);
        dcEntryRow.Controls.Add(_btnDCUpdate, 2, 0);
        dcEntryRow.Controls.Add(_btnDCRemove, 3, 0);

        // Test Connection row
        _btnDCTest = MakeSmallButton("Test Connection…");
        _btnDCTest.Enabled  = false;
        _btnDCTest.Padding  = new Padding(12, 4, 12, 4);
        _btnDCTest.Anchor   = AnchorStyles.Right | AnchorStyles.Top;
        _btnDCTest.Margin   = new Padding(0, 2, 0, 0);
        _btnDCTest.Click   += async (_, _) => await TestSelectedDc();

        tbl.Controls.Add(domainRow, 0, 0);
        tbl.Controls.Add(lblDCs,    0, 1);
        tbl.Controls.Add(_lstDCs,   0, 2);
        tbl.Controls.Add(dcEntryRow, 0, 3);
        tbl.Controls.Add(_btnDCTest, 0, 4);

        page.Controls.Add(tbl);
        return page;
    }

    // ── Search OUs tab ─────────────────────────────────────────────────────────

    private TabPage BuildOUsTab()
    {
        var page = new TabPage("Search OUs") { Padding = new Padding(10) };

        var tbl = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 5,
            Padding     = new Padding(4),
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // hint
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // list
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // DN entry
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // subtree checkbox
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons

        var lblHint = new Label
        {
            Text        = "Each OU is searched for groups. [S] = subtree (this OU + all child OUs), [T] = this OU only.",
            AutoSize    = true,
            MaximumSize = new Size(580, 0),
            Margin      = new Padding(0, 0, 0, 6),
        };

        _lstOUs = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 9.5f) };
        _lstOUs.SelectedIndexChanged += (_, _) =>
        {
            int sel = _lstOUs.SelectedIndex;
            if (sel >= 0 && sel < _ous.Count)
            {
                _txtOUDn.Text      = _ous[sel].Dn;
                _chkSubtree.Checked = _ous[sel].Subtree;
            }
            _btnOUUpdate.Enabled = sel >= 0;
            _btnOURemove.Enabled = sel >= 0;
        };

        // DN entry row
        var dnRow = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            AutoSize    = true,
            ColumnCount = 2,
            RowCount    = 1,
            Margin      = new Padding(0, 4, 0, 2),
        };
        dnRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        var lblDn = new Label
        {
            Text   = "DN:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(0, 4, 10, 0),
        };
        _txtOUDn = new TextBox
        {
            Dock            = DockStyle.Fill,
            Font            = new Font("Segoe UI", 9.5f),
            PlaceholderText = "OU=Groups,OU=IT,DC=contoso,DC=local",
        };
        dnRow.Controls.Add(lblDn,    0, 0);
        dnRow.Controls.Add(_txtOUDn, 1, 0);

        _chkSubtree = new CheckBox
        {
            Text     = "Include sub-OUs (subtree)",
            AutoSize = true,
            Checked  = true,
            Margin   = new Padding(0, 2, 0, 4),
        };

        // Buttons row
        var ouBtns = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        _btnOUAdd = MakeSmallButton("Add");
        _btnOUAdd.Click += (_, _) =>
        {
            var dn = _txtOUDn.Text.Trim();
            if (string.IsNullOrEmpty(dn)) { MessageBox.Show("Enter a DN.", "Required", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var ou = new SearchOu { Dn = dn, Subtree = _chkSubtree.Checked };
            _ous.Add(ou);
            _lstOUs.Items.Add(OuDisplay(ou));
            _lstOUs.SelectedIndex = _lstOUs.Items.Count - 1;
            _txtOUDn.Clear();
            _lstOUs.ClearSelected();
        };

        _btnOUUpdate = MakeSmallButton("Update");
        _btnOUUpdate.Enabled = false;
        _btnOUUpdate.Click += (_, _) =>
        {
            int sel = _lstOUs.SelectedIndex;
            if (sel < 0) return;
            var dn = _txtOUDn.Text.Trim();
            if (string.IsNullOrEmpty(dn)) { MessageBox.Show("Enter a DN.", "Required", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var ou = new SearchOu { Dn = dn, Subtree = _chkSubtree.Checked };
            _ous[sel] = ou;
            _lstOUs.Items[sel] = OuDisplay(ou);
            _lstOUs.ClearSelected();
            _txtOUDn.Clear();
        };

        _btnOURemove = MakeSmallButton("Remove");
        _btnOURemove.Enabled = false;
        _btnOURemove.Click += (_, _) =>
        {
            int sel = _lstOUs.SelectedIndex;
            if (sel >= 0) { _ous.RemoveAt(sel); _lstOUs.Items.RemoveAt(sel); _txtOUDn.Clear(); }
        };

        ouBtns.Controls.Add(_btnOUAdd);
        ouBtns.Controls.Add(_btnOUUpdate);
        ouBtns.Controls.Add(_btnOURemove);

        tbl.Controls.Add(lblHint,  0, 0);
        tbl.Controls.Add(_lstOUs,  0, 1);
        tbl.Controls.Add(dnRow,    0, 2);
        tbl.Controls.Add(_chkSubtree, 0, 3);
        tbl.Controls.Add(ouBtns,   0, 4);

        page.Controls.Add(tbl);
        return page;
    }

    // ── Access Gate tab ────────────────────────────────────────────────────────

    private TabPage BuildAccessTab()
    {
        var page = new TabPage("Access Gate") { Padding = new Padding(10) };

        var tbl = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 4,
            Padding     = new Padding(4),
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // hint
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // list
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // entry row
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons

        var lblHint = new Label
        {
            Text        = "User must be a (nested) member of at least one group below. Leave empty to allow everyone who can bind.",
            AutoSize    = true,
            MaximumSize = new Size(580, 0),
            ForeColor   = Color.DimGray,
            Margin      = new Padding(0, 0, 0, 6),
        };

        _lstGroups = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 9.5f) };
        _lstGroups.SelectedIndexChanged += (_, _) =>
        {
            bool hasSel = _lstGroups.SelectedIndex >= 0;
            if (hasSel) _txtGroup.Text = _lstGroups.SelectedItem?.ToString() ?? "";
            _btnGroupUpdate.Enabled = hasSel;
            _btnGroupRemove.Enabled = hasSel;
        };

        // Entry row
        var entryRow = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            AutoSize    = true,
            ColumnCount = 2,
            RowCount    = 1,
            Margin      = new Padding(0, 4, 0, 2),
        };
        entryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        var lblEntry = new Label
        {
            Text   = "CN or DN:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(0, 4, 10, 0),
        };
        _txtGroup = new TextBox
        {
            Dock            = DockStyle.Fill,
            Font            = new Font("Segoe UI", 9.5f),
            PlaceholderText = "AD Admins  or  CN=AD Admins,OU=Groups,DC=contoso,DC=local",
        };
        entryRow.Controls.Add(lblEntry,  0, 0);
        entryRow.Controls.Add(_txtGroup, 1, 0);

        // Buttons row
        var groupBtns = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        _btnGroupAdd = MakeSmallButton("Add");
        _btnGroupAdd.Click += (_, _) =>
        {
            var val = _txtGroup.Text.Trim();
            if (string.IsNullOrEmpty(val)) return;
            _lstGroups.Items.Add(val);
            _lstGroups.SelectedIndex = _lstGroups.Items.Count - 1;
            _txtGroup.Clear();
            _lstGroups.ClearSelected();
        };

        _btnGroupUpdate = MakeSmallButton("Update");
        _btnGroupUpdate.Enabled = false;
        _btnGroupUpdate.Click += (_, _) =>
        {
            int sel = _lstGroups.SelectedIndex;
            if (sel < 0) return;
            var val = _txtGroup.Text.Trim();
            if (string.IsNullOrEmpty(val)) return;
            _lstGroups.Items[sel] = val;
            _lstGroups.ClearSelected();
            _txtGroup.Clear();
        };

        _btnGroupRemove = MakeSmallButton("Remove");
        _btnGroupRemove.Enabled = false;
        _btnGroupRemove.Click += (_, _) =>
        {
            int sel = _lstGroups.SelectedIndex;
            if (sel >= 0) { _lstGroups.Items.RemoveAt(sel); _txtGroup.Clear(); }
        };

        groupBtns.Controls.Add(_btnGroupAdd);
        groupBtns.Controls.Add(_btnGroupUpdate);
        groupBtns.Controls.Add(_btnGroupRemove);

        tbl.Controls.Add(lblHint,    0, 0);
        tbl.Controls.Add(_lstGroups, 0, 1);
        tbl.Controls.Add(entryRow,   0, 2);
        tbl.Controls.Add(groupBtns,  0, 3);

        page.Controls.Add(tbl);
        return page;
    }

    // ── Advanced tab ───────────────────────────────────────────────────────────

    private TabPage BuildAdvancedTab()
    {
        var page = new TabPage("Advanced") { Padding = new Padding(10) };

        var tbl = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            AutoSize    = true,
            ColumnCount = 3,
            Padding     = new Padding(4),
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        int row = 0;

        _chkSsl = new CheckBox
        {
            Text     = "Use SSL / LDAPS (strongly recommended — required for LDAP port 636)",
            AutoSize = true,
            Checked  = true,
            Margin   = new Padding(0, 8, 0, 10),
        };
        tbl.Controls.Add(_chkSsl, 0, row);
        tbl.SetColumnSpan(_chkSsl, 3);
        row++;

        _numPort           = AddAdvancedRow(tbl, row++, "Default port:",      1,   65535,  636, "TCP port for DCs without an explicit :port suffix.");
        _numTimeout        = AddAdvancedRow(tbl, row++, "LDAP timeout:",       5,   300,    30,  "Seconds — how long to wait for a query result.");
        _numConnectTimeout = AddAdvancedRow(tbl, row++, "Connect timeout:",  100, 30000,  1500, "Milliseconds — TCP probe timeout per DC before binding.");

        // Audit log path — spans all three columns
        var sep = new Label
        {
            Text      = "Audit logging",
            AutoSize  = true,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 73, 125),
            Margin    = new Padding(0, 14, 0, 4),
        };
        tbl.Controls.Add(sep, 0, row);
        tbl.SetColumnSpan(sep, 3);
        row++;

        var auditRow = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            AutoSize    = true,
            ColumnCount = 3,
            RowCount    = 1,
            Margin      = new Padding(0, 0, 0, 4),
        };
        auditRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        auditRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        auditRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblAudit = new Label
        {
            Text   = "Log path:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(0, 4, 10, 0),
        };
        _txtAuditPath = new TextBox
        {
            Dock            = DockStyle.Fill,
            Font            = new Font("Segoe UI", 9.5f),
            PlaceholderText = @"\\server\share\AuditLogs  (leave empty to disable)",
        };
        var btnBrowseAudit = MakeSmallButton("Browse…");
        btnBrowseAudit.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description         = "Select the audit log folder (can be a network share)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (!string.IsNullOrWhiteSpace(_txtAuditPath.Text))
                dlg.InitialDirectory = _txtAuditPath.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtAuditPath.Text = dlg.SelectedPath;
        };

        auditRow.Controls.Add(lblAudit,       0, 0);
        auditRow.Controls.Add(_txtAuditPath,  1, 0);
        auditRow.Controls.Add(btnBrowseAudit, 2, 0);

        var auditHint = new Label
        {
            Text      = "Per-day CSV files (audit_yyyy-MM-dd.csv) are appended here. Errors are silently noted in the diagnostic log.",
            AutoSize  = true,
            MaximumSize = new Size(580, 0),
            ForeColor = Color.DimGray,
            Font      = new Font("Segoe UI", 8.5f),
            Margin    = new Padding(0, 0, 0, 6),
        };

        tbl.Controls.Add(auditRow,  0, row); tbl.SetColumnSpan(auditRow,  3); row++;
        tbl.Controls.Add(auditHint, 0, row); tbl.SetColumnSpan(auditHint, 3); row++;

        _numRetainDays = AddAdvancedRow(tbl, row++, "Retain logs:", 1, 3650, 31, "Days to keep audit CSV files. Older files are deleted at startup.");

        page.Controls.Add(tbl);
        return page;
    }

    private static NumericUpDown AddAdvancedRow(
        TableLayoutPanel tbl, int row, string label, decimal min, decimal max, decimal def, string tooltip)
    {
        var lbl = new Label
        {
            Text   = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(0, 6, 12, 6),
        };
        var num = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value   = def,
            Width   = 90,
            Margin  = new Padding(0, 4, 14, 4),
        };
        var tip = new Label
        {
            Text      = tooltip,
            AutoSize  = true,
            ForeColor = Color.DimGray,
            Font      = new Font("Segoe UI", 8.5f),
            Anchor    = AnchorStyles.Left | AnchorStyles.Top,
            Margin    = new Padding(0, 6, 0, 6),
        };
        tbl.Controls.Add(lbl, 0, row);
        tbl.Controls.Add(num, 1, row);
        tbl.Controls.Add(tip, 2, row);
        return num;
    }

    // ── Load from config ────────────────────────────────────────────────────────

    private void LoadFromConfig(AppConfig config)
    {
        _txtDomain.Text = config.Domain;

        // Load raw DC strings to preserve explicit ":port" suffixes
        var rawDcs = config.DomainControllers.Count > 0
            ? config.DomainControllers
            : (string.IsNullOrWhiteSpace(config.DomainController)
                ? new List<string>()
                : new List<string> { config.DomainController });
        foreach (var dc in rawDcs)
            if (!string.IsNullOrWhiteSpace(dc))
                _lstDCs.Items.Add(dc.Trim());

        foreach (var ou in config.SearchOus)
        {
            _ous.Add(new SearchOu { Dn = ou.Dn, Subtree = ou.Subtree });
            _lstOUs.Items.Add(OuDisplay(ou));
        }

        foreach (var g in config.AllowedGroups)
            _lstGroups.Items.Add(g);

        _chkSsl.Checked          = config.UseSsl;
        _numPort.Value           = Math.Clamp(config.Port,             _numPort.Minimum,           _numPort.Maximum);
        _numTimeout.Value        = Math.Clamp(config.TimeoutSeconds,   _numTimeout.Minimum,        _numTimeout.Maximum);
        _numConnectTimeout.Value = Math.Clamp(config.ConnectTimeoutMs, _numConnectTimeout.Minimum, _numConnectTimeout.Maximum);
        _txtAuditPath.Text       = config.AuditLogPath;
        _numRetainDays.Value     = Math.Clamp(config.AuditLogRetainDays, _numRetainDays.Minimum, _numRetainDays.Maximum);
    }

    // ── Validate / Save ─────────────────────────────────────────────────────────

    private bool ValidateConfig(out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(_txtDomain.Text)) { error = "Domain is required."; return false; }
        if (_lstDCs.Items.Count == 0)  { error = "At least one domain controller is required."; return false; }
        if (_lstOUs.Items.Count == 0)  { error = "At least one search OU is required."; return false; }
        return true;
    }

    private void DoSave(string path)
    {
        if (!ValidateConfig(out var err))
        {
            MessageBox.Show(err, "Validation error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            WriteJson(path);
            Logger.Info($"ConfigEditor: saved config to {path}");
            MessageBox.Show(
                "Configuration saved.\n\nRestart the application for the changes to take effect.",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Exception($"ConfigEditor: save failed to {path}", ex);
            MessageBox.Show($"Could not save:\n\n{ex.Message}", "Save error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DoSaveAs()
    {
        if (!ValidateConfig(out var err))
        {
            MessageBox.Show(err, "Validation error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Title            = "Save configuration as…",
            Filter           = "JSON config (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_configPath) ?? AppContext.BaseDirectory,
            FileName         = Path.GetFileName(_configPath),
            OverwritePrompt  = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _savePath = dlg.FileName;
        DoSave(_savePath);
    }

    private void WriteJson(string path)
    {
        var dcs = new List<string>();
        foreach (var item in _lstDCs.Items)
            if (item?.ToString() is string s && !string.IsNullOrWhiteSpace(s))
                dcs.Add(s);

        var groups = new List<string>();
        foreach (var item in _lstGroups.Items)
            if (item?.ToString() is string s && !string.IsNullOrWhiteSpace(s))
                groups.Add(s);

        var cfg = new ConfigJson
        {
            Domain             = _txtDomain.Text.Trim(),
            DomainControllers  = dcs,
            Port               = (int)_numPort.Value,
            UseSsl             = _chkSsl.Checked,
            TimeoutSeconds     = (int)_numTimeout.Value,
            ConnectTimeoutMs   = (int)_numConnectTimeout.Value,
            SearchOus          = _ous.Select(ou => new SearchOuJson { Dn = ou.Dn, Subtree = ou.Subtree }).ToList(),
            AllowedGroups      = groups,
            AuditLogPath       = _txtAuditPath.Text.Trim(),
            AuditLogRetainDays = (int)_numRetainDays.Value,
        };

        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, opts));
    }

    // ── Test Connection ────────────────────────────────────────────────────────

    private async Task TestSelectedDc()
    {
        var entry = _lstDCs.SelectedItem?.ToString() ?? _txtDCEntry.Text.Trim();
        if (string.IsNullOrEmpty(entry))
        {
            MessageBox.Show("Select or type a DC entry to test.", "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string host = entry.Trim();
        int port    = (int)_numPort.Value;
        int colon   = host.LastIndexOf(':');
        if (colon > 0 && int.TryParse(host.AsSpan(colon + 1), out int p)) { host = host[..colon]; port = p; }

        _btnDCTest.Enabled = false;
        _btnDCTest.Text    = "Testing…";
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource((int)_numConnectTimeout.Value);
            await tcp.ConnectAsync(host, port, cts.Token);
            MessageBox.Show($"{host}:{port} — reachable.", "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                $"{host}:{port} — no response within {(int)_numConnectTimeout.Value} ms.",
                "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{host}:{port} — {ex.Message}", "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnDCTest.Enabled = true;
            _btnDCTest.Text    = "Test Connection…";
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string OuDisplay(SearchOu ou)
        => $"[{(ou.Subtree ? "S" : "T")}]  {ou.Dn}";

    private static Button MakeButton(string text, Color back, Color fore)
    {
        var btn = new Button
        {
            Text      = text,
            AutoSize  = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Cursor    = Cursors.Hand,
            Margin    = new Padding(0, 0, 8, 0),
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(180, 185, 195);
        if (back.R < 128) btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static Button MakeSmallButton(string text)
    {
        var btn = new Button
        {
            Text      = text,
            AutoSize  = true,
            Padding   = new Padding(10, 4, 10, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(240, 242, 245),
            Cursor    = Cursors.Hand,
            Margin    = new Padding(4, 0, 0, 0),
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(190, 195, 205);
        return btn;
    }

    // ── JSON serialization DTOs ─────────────────────────────────────────────────

    private sealed class ConfigJson
    {
        [JsonPropertyName("domain")]              public string Domain            { get; set; } = "";
        [JsonPropertyName("domain_controllers")]  public List<string> DomainControllers { get; set; } = new();
        [JsonPropertyName("port")]                public int Port                 { get; set; } = 636;
        [JsonPropertyName("use_ssl")]             public bool UseSsl              { get; set; } = true;
        [JsonPropertyName("timeout_seconds")]     public int TimeoutSeconds       { get; set; } = 30;
        [JsonPropertyName("connect_timeout_ms")]  public int ConnectTimeoutMs     { get; set; } = 1500;
        [JsonPropertyName("search_ous")]          public List<SearchOuJson> SearchOus { get; set; } = new();
        [JsonPropertyName("allowed_groups")]      public List<string> AllowedGroups   { get; set; } = new();
        [JsonPropertyName("audit_log_path")]          public string AuditLogPath          { get; set; } = "";
        [JsonPropertyName("audit_log_retain_days")]   public int    AuditLogRetainDays    { get; set; } = 31;
    }

    private sealed class SearchOuJson
    {
        [JsonPropertyName("dn")]      public string Dn      { get; set; } = "";
        [JsonPropertyName("subtree")] public bool   Subtree { get; set; } = true;
    }
}
