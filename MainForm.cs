namespace ADGroupBrowser;

public partial class MainForm : Form
{
    private readonly LdapService _svc;
    private readonly AppConfig _config;
    private readonly string _configPath;
    private List<OuGroupSection> _sections = new();
    private Font? _ouFont;   // bold font for OU header nodes

    // Per-session member cache, keyed by "<R|D>|<groupDN>" (mode + group).
    private readonly Dictionary<string, List<AdMember>> _memberCache = new();
    // Serializes access to the (non-thread-safe) LdapConnection across background loads.
    private readonly SemaphoreSlim _ldapGate = new(1, 1);
    // Monotonic token so a slow load that the user clicked away from is discarded.
    private int _loadToken = 0;

    // Receives the already-connected service from LoginForm — no second bind needed
    public MainForm(AppConfig config, string configPath, string username, LdapService svc)
    {
        _config = config;
        _configPath = configPath;
        _svc = svc;

        Logger.Info("MainForm: InitializeComponent…");
        InitializeComponent();
        Logger.Info("MainForm: InitializeComponent done.");

        var dc = svc.ActiveEndpoint?.Host ?? config.Domain;
        lblConnected.Text = $"Connected as {username}  |  DC: {dc}";
        btnConfig.Visible = IsFileEditable(configPath);

        // Restore saved window position/size (must happen before the form is shown).
        var prefs = WindowPrefs.Load();
        prefs?.ApplyTo(this);
        if (prefs?.Maximized == true) WindowState = FormWindowState.Maximized;
    }

    // OnLoad runs after the form is shown — safe to call Close() here if needed
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        float scale = DeviceDpi / 96f;
        Logger.Info($"MainForm.OnLoad: form size = {Width}x{Height}, split width = {split.Width}, DPI = {DeviceDpi} (x{scale:0.00})");

        // Apply split sizing now that the form is at full size (scaled for DPI).
        try
        {
            split.Panel1MinSize = (int)(180 * scale);
            split.Panel2MinSize = (int)(300 * scale);
            int desired = (int)(280 * scale);
            int max = split.Width - split.Panel2MinSize - split.SplitterWidth;
            if (max >= split.Panel1MinSize)
            {
                split.SplitterDistance = Math.Clamp(desired, split.Panel1MinSize, max);
                Logger.Info($"MainForm.OnLoad: SplitterDistance set to {split.SplitterDistance}.");
            }
            else
            {
                Logger.Warn($"MainForm.OnLoad: window too narrow ({split.Width}) to set splitter; leaving default.");
            }
        }
        catch (Exception ex)
        {
            Logger.Exception("MainForm.OnLoad: splitter sizing", ex);
        }

        try
        {
            LoadGroups();
        }
        catch (Exception ex)
        {
            Logger.Exception("MainForm.OnLoad: LoadGroups", ex);
            MessageBox.Show($"Failed to load groups:\n\n{ex.Message}\n\nLog: {Logger.LogPath}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    // ── group loading ─────────────────────────────────────────────────────────

    private void LoadGroups()
    {
        SetStatus("Loading groups…");
        btnRefresh.Enabled = false;
        _memberCache.Clear();   // refresh invalidates cached member lists
        treeGroups.Nodes.Clear();
        dgvMembers.Rows.Clear();
        lblGroupHeader.Text = "Groups";
        lblMembersHeader.Text = "Select a group to view members";

        try
        {
            _sections = _svc.GetGroupSections();
            BuildTree(txtSearch.Text.Trim());
            int total = _sections.Sum(s => s.Groups.Count);
            SetStatus($"Loaded {total} groups from {_sections.Count} OU(s).");
            Logger.Info($"MainForm: loaded {total} groups across {_sections.Count} OU(s) into the tree.");
        }
        catch (Exception ex)
        {
            Logger.Exception("MainForm.LoadGroups", ex);
            SetStatus($"Error loading groups: {ex.Message}");
        }
        finally
        {
            btnRefresh.Enabled = true;
        }
    }

    // Rebuild the OU tree, optionally filtered by group name/description.
    private void BuildTree(string filter)
    {
        _ouFont ??= new Font(treeGroups.Font, FontStyle.Bold);
        bool filtering = !string.IsNullOrEmpty(filter);

        treeGroups.BeginUpdate();
        treeGroups.Nodes.Clear();

        int shown = 0, total = 0;
        foreach (var sec in _sections)
        {
            total += sec.Groups.Count;
            var matches = filtering
                ? sec.Groups.Where(g =>
                    g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    g.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList()
                : sec.Groups;

            if (filtering && matches.Count == 0) continue;   // hide OUs with no match

            var scopeTag = sec.Subtree ? "" : "  (this OU only)";
            var ouNode = new TreeNode($"{sec.OuName}  ({matches.Count}){scopeTag}")
            {
                Tag = sec,
                NodeFont = _ouFont,
            };
            foreach (var g in matches)
            {
                var gTag = string.IsNullOrEmpty(g.Scope) ? "" : $"  [{g.Scope[0]}]";
                ouNode.Nodes.Add(new TreeNode(g.Name + gTag) { Tag = g });
                shown++;
            }
            treeGroups.Nodes.Add(ouNode);
            ouNode.Expand();   // sections start expanded (and stay expanded while filtering)
        }

        treeGroups.EndUpdate();

        lblGroupHeader.Text = filtering
            ? $"Groups ({shown} of {total})"
            : $"Groups ({total})";
    }

    // ── member loading (async + cached) ─────────────────────────────────────────

    private void treeGroups_AfterSelect(object sender, TreeViewEventArgs e)
        => _ = LoadSelectedGroupMembersAsync();

    private void chkRecursive_CheckedChanged(object sender, EventArgs e)
        => _ = LoadSelectedGroupMembersAsync();

    private async Task LoadSelectedGroupMembersAsync()
    {
        // OU header nodes (Tag is OuGroupSection) and empty selection are ignored.
        if (treeGroups.SelectedNode?.Tag is not AdGroup group) return;

        bool recursive   = chkRecursive.Checked;
        string cacheKey  = (recursive ? "R|" : "D|") + group.DistinguishedName;
        int myToken      = ++_loadToken;   // mark this as the latest request

        dgvMembers.Rows.Clear();

        // Cache hit → instant, no DC round-trip
        if (_memberCache.TryGetValue(cacheKey, out var cached))
        {
            PopulateMembers(group, cached, recursive, fromCache: true);
            SetBusy(false);
            return;
        }

        lblMembersHeader.Text = $"Loading members of: {group.Name}…";
        SetStatus(recursive ? "Loading members (recursive)…" : "Loading direct members…");
        SetBusy(true);

        try
        {
            List<AdMember> members;
            await _ldapGate.WaitAsync();    // one query at a time on the shared connection
            try
            {
                members = await Task.Run(() => recursive
                    ? _svc.GetMembersRecursive(group.DistinguishedName)
                    : _svc.GetMembersDirect(group.DistinguishedName));
            }
            finally { _ldapGate.Release(); }

            if (myToken != _loadToken) return;   // user moved on; discard stale result

            _memberCache[cacheKey] = members;
            PopulateMembers(group, members, recursive, fromCache: false);
        }
        catch (Exception ex)
        {
            if (myToken != _loadToken) return;
            Logger.Exception($"MainForm: loading members of '{group.Name}' (recursive={recursive})", ex);
            lblMembersHeader.Text = $"Error loading {group.Name}";
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            if (myToken == _loadToken) SetBusy(false);
        }
    }

    private void PopulateMembers(AdGroup group, List<AdMember> members, bool recursive, bool fromCache)
    {
        dgvMembers.Rows.Clear();
        foreach (var m in members)
        {
            var row = dgvMembers.Rows[dgvMembers.Rows.Add()];
            row.Cells["colType"].Value        = TypeIcon(m.Type);
            row.Cells["colDisplayName"].Value = m.DisplayName;
            row.Cells["colSam"].Value         = m.SamAccountName;
            row.Cells["colMail"].Value        = m.Mail;
            row.Tag = m;
        }

        var desc     = string.IsNullOrWhiteSpace(group.Description) ? "" : $"  —  {group.Description}";
        var modeWord = recursive ? "effective" : "direct";
        var note     = fromCache ? "  (cached)" : "";
        lblMembersHeader.Text = $"{group.Name}{desc}  ({members.Count} {modeWord} members)";
        SetStatus($"{members.Count} {modeWord} members in {group.Name}.{note}");

        AuditLogger.LogGroupViewed(group.Name, group.DistinguishedName, recursive ? "Recursive" : "Direct", members.Count);
    }

    private void SetBusy(bool busy)
    {
        progressBar.Visible = busy;
        progressBar.MarqueeAnimationSpeed = busy ? 30 : 0;
        UseWaitCursor = busy;
    }

    // ── toolbar handlers ──────────────────────────────────────────────────────

    private void txtSearch_TextChanged(object sender, EventArgs e) => BuildTree(txtSearch.Text.Trim());

    private void btnRefresh_Click(object sender, EventArgs e) => LoadGroups();

    private void btnConfig_Click(object sender, EventArgs e)
    {
        using var editor = new ConfigEditorForm(_config, _configPath);
        editor.ShowDialog(this);
    }

    private void btnExit_Click(object sender, EventArgs e) => Application.Exit();

    // ── grid interaction ──────────────────────────────────────────────────────

    private void dgvMembers_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (dgvMembers.Rows[e.RowIndex].Tag is not AdMember m) return;

        if (m.Type == "Group")
        {
            // Jump to the group in the tree (clear any filter first so it's present).
            if (txtSearch.Text.Length > 0) txtSearch.Clear();   // triggers BuildTree("")
            var node = FindGroupNode(m.DistinguishedName);
            if (node is not null)
            {
                treeGroups.SelectedNode = node;
                node.EnsureVisible();
                treeGroups.Focus();
                return;
            }
        }

        var info = $"Display name:  {m.DisplayName}\n" +
                   $"SAM account:   {m.SamAccountName}\n" +
                   $"Type:          {m.Type}\n" +
                   $"E-mail:        {m.Mail}\n\n" +
                   $"DN: {m.DistinguishedName}";
        MessageBox.Show(info, m.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Ctrl+C copies selected rows as TSV
    private void dgvMembers_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C)
        {
            var sb = new System.Text.StringBuilder();
            foreach (DataGridViewRow row in dgvMembers.SelectedRows)
                if (row.Tag is AdMember m)
                    sb.AppendLine($"{m.DisplayName}\t{m.SamAccountName}\t{m.Type}\t{m.Mail}");
            if (sb.Length > 0) Clipboard.SetText(sb.ToString());
            e.Handled = true;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void SetStatus(string msg) => lblStatus.Text = msg;

    // Locate the tree node for a group DN across all OU sections (null if not shown).
    private TreeNode? FindGroupNode(string dn)
    {
        foreach (TreeNode ou in treeGroups.Nodes)
            foreach (TreeNode g in ou.Nodes)
                if (g.Tag is AdGroup ag &&
                    ag.DistinguishedName.Equals(dn, StringComparison.OrdinalIgnoreCase))
                    return g;
        return null;
    }

    private static string TypeIcon(string type) => type switch
    {
        "User"     => "👤 User",
        "Group"    => "👥 Group",
        "Computer" => "🖥 Computer",
        "Contact"  => "✉ Contact",
        _          => type,
    };

    private static bool IsFileEditable(string path)
    {
        try
        {
            using var fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch { return false; }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Save window position/size before disposing anything.
        var prefs = new WindowPrefs { Maximized = WindowState == FormWindowState.Maximized };
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        prefs.Left = bounds.Left; prefs.Top = bounds.Top;
        prefs.Width = bounds.Width; prefs.Height = bounds.Height;
        prefs.Save();

        _svc.Dispose();
        _ldapGate.Dispose();
        _ouFont?.Dispose();
        base.OnFormClosed(e);
    }
}
