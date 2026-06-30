namespace ADGroupBrowser;

/// <summary>
/// Shown when there isn't exactly one local config: lists the config*.json files
/// found next to the exe (with their domain), or — if none — offers Browse / Exit.
/// </summary>
public sealed class ConfigChooserForm : Form
{
    public string? SelectedPath { get; private set; }

    private readonly ListBox _list = new();
    private readonly Button _btnLoad = new();
    private readonly Button _btnBrowse = new();
    private readonly Button _btnNew = new();
    private readonly Button _btnExit = new();

    private sealed class Item
    {
        public string Path { get; }
        public string Display { get; }
        public Item(string path, string display) { Path = path; Display = display; }
        public override string ToString() => Display;
    }

    public ConfigChooserForm(string[] paths)
    {
        var brandBlue = Color.FromArgb(31, 73, 125);

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;

        Text            = "AD Group Browser — Choose configuration";
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        Font            = new Font("Segoe UI", 9.5f);
        BackColor       = Color.White;
        ClientSize      = new Size(520, 360);

        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            Padding     = new Padding(20, 18, 20, 14),
            BackColor   = Color.White,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // title
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // hint
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // list
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons

        var title = new Label
        {
            Text     = "Choose a configuration",
            Font     = new Font("Segoe UI", 13.5f),
            ForeColor = brandBlue,
            AutoSize = true,
            Margin   = new Padding(0, 0, 0, 8),
        };

        bool any = paths.Length > 0;
        var hint = new Label
        {
            Text        = any
                ? "More than one configuration was found next to the application. Pick one, browse for another file, or create a new one."
                : "No configuration file was found next to the application. Create a new one, browse for an existing file, or exit.",
            AutoSize    = true,
            MaximumSize = new Size(470, 0),
            ForeColor   = Color.DimGray,
            Margin      = new Padding(0, 0, 0, 10),
        };

        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        _list.Font = new Font("Segoe UI", 10f);
        _list.Margin = new Padding(0, 0, 0, 10);
        foreach (var p in paths)
            _list.Items.Add(new Item(p, Describe(p)));
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.DoubleClick += (_, _) => LoadSelected();
        _list.Enabled = any;

        // Buttons row
        var btnRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, AutoSize = true,
            Margin = new Padding(0, 4, 0, 0),
        };
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _btnBrowse.Text = "Browse…";
        _btnBrowse.AutoSize = true;
        _btnBrowse.Padding = new Padding(14, 6, 14, 6);
        _btnBrowse.FlatStyle = FlatStyle.Flat;
        _btnBrowse.BackColor = Color.FromArgb(240, 242, 245);
        _btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(190, 195, 205);
        _btnBrowse.Margin = new Padding(0, 0, 8, 0);
        _btnBrowse.Click += (_, _) => Browse();

        _btnNew.Text = "New Config…";
        _btnNew.AutoSize = true;
        _btnNew.Padding = new Padding(14, 6, 14, 6);
        _btnNew.FlatStyle = FlatStyle.Flat;
        _btnNew.BackColor = Color.FromArgb(240, 242, 245);
        _btnNew.FlatAppearance.BorderColor = Color.FromArgb(190, 195, 205);
        _btnNew.Click += (_, _) => CreateNew();

        var leftButtons = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0),
        };
        leftButtons.Controls.Add(_btnBrowse);
        leftButtons.Controls.Add(_btnNew);

        var rightButtons = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0),
        };
        _btnExit.Text = "Exit";
        _btnExit.AutoSize = true;
        _btnExit.Padding = new Padding(16, 6, 16, 6);
        _btnExit.FlatStyle = FlatStyle.Flat;
        _btnExit.BackColor = Color.FromArgb(240, 242, 245);
        _btnExit.FlatAppearance.BorderColor = Color.FromArgb(190, 195, 205);
        _btnExit.Margin = new Padding(0, 0, 8, 0);
        _btnExit.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnLoad.Text = "Load";
        _btnLoad.AutoSize = true;
        _btnLoad.Padding = new Padding(22, 6, 22, 6);
        _btnLoad.FlatStyle = FlatStyle.Flat;
        _btnLoad.ForeColor = Color.White;
        _btnLoad.BackColor = brandBlue;
        _btnLoad.Font = new Font("Segoe UI", 10f);
        _btnLoad.FlatAppearance.BorderSize = 0;
        _btnLoad.Enabled = any;
        _btnLoad.Click += (_, _) => LoadSelected();

        rightButtons.Controls.Add(_btnExit);
        rightButtons.Controls.Add(_btnLoad);

        btnRow.Controls.Add(leftButtons, 0, 0);
        btnRow.Controls.Add(rightButtons, 2, 0);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(hint, 0, 1);
        root.Controls.Add(_list, 0, 2);
        root.Controls.Add(btnRow, 0, 3);

        Controls.Add(root);
        AcceptButton = _btnLoad;
        CancelButton = _btnExit;
    }

    private static string Describe(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            var cfg = AppConfig.Load(path);
            return $"{name}   —   {cfg.Domain}  ({cfg.Endpoints.Count} DC, {cfg.SearchOus.Count} OU)";
        }
        catch
        {
            return $"{name}   —   (invalid / unreadable)";
        }
    }

    private void LoadSelected()
    {
        if (_list.SelectedItem is Item it)
        {
            SelectedPath = it.Path;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void Browse()
    {
        using var dlg = new OpenFileDialog
        {
            Title            = "Select a configuration file",
            Filter           = "JSON config (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = AppContext.BaseDirectory,
            CheckFileExists  = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            SelectedPath = dlg.FileName;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void CreateNew()
    {
        var path = SuggestNewConfigPath();

        // A blank AppConfig() already carries the right defaults (port 636, SSL on,
        // 31-day audit retention, etc). The editor's own validation (domain, ≥1 DC,
        // ≥1 OU) keeps an incomplete config from being saved.
        using var editor = new ConfigEditorForm(new AppConfig(), path, "Configuration created.");
        if (editor.ShowDialog(this) == DialogResult.OK && editor.SavedPath is not null)
        {
            SelectedPath = editor.SavedPath;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // First free "config.json", "config2.json", "config3.json", … next to the exe.
    private static string SuggestNewConfigPath()
    {
        var dir = AppContext.BaseDirectory;
        var candidate = Path.Combine(dir, "config.json");
        if (!File.Exists(candidate)) return candidate;

        for (int i = 2; i < 100; i++)
        {
            candidate = Path.Combine(dir, $"config{i}.json");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, $"config-{Guid.NewGuid():N}.json");
    }
}
