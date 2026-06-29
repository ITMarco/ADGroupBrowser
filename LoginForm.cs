namespace ADGroupBrowser;

public partial class LoginForm : Form
{
    // Kept alive after successful connect; Program.cs passes it to MainForm
    public LdapService? ConnectedService { get; private set; }

    public string Username => chkIntegrated.Checked
        ? System.Security.Principal.WindowsIdentity.GetCurrent().Name
        : txtUsername.Text.Trim();

    private readonly AppConfig _config;
    private readonly string _configPath;
    private bool _passwordVisible = false;

    public LoginForm(AppConfig config, string configPath)
    {
        _config = config;
        _configPath = configPath;
        InitializeComponent();

        var eps = config.Endpoints;
        var ssl = config.UseSsl ? "  (SSL)" : "";
        lblServerValue.Text = eps.Count == 1
            ? eps[0] + ssl
            : $"{eps.Count} domain controllers{ssl}";
        lblDomainValue.Text = config.Domain;

        txtUsername.Text = config.NetBiosHint + @"\";
        txtUsername.SelectionStart = txtUsername.Text.Length;

        btnConfig.Visible = IsFileEditable(configPath);

        // Apply initial visibility — checkbox starts checked (SSO), so hide manual fields.
        UpdateCredentialFieldVisibility();
    }

    // ── SSO checkbox ─────────────────────────────────────────────────────────

    private void chkIntegrated_CheckedChanged(object sender, EventArgs e)
        => UpdateCredentialFieldVisibility();

    private void UpdateCredentialFieldVisibility()
    {
        bool sso = chkIntegrated.Checked;
        lblUsernameLabel.Visible = !sso;
        txtUsername.Visible      = !sso;
        lblPasswordLabel.Visible = !sso;
        pwRow.Visible            = !sso;
    }

    // ── eye toggle ────────────────────────────────────────────────────────────

    private void lblEye_Click(object sender, EventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        txtPassword.PasswordChar = _passwordVisible ? '\0' : '●';
        lblEye.Text = _passwordVisible ? "🙈" : "👁";
        txtPassword.Focus();
        txtPassword.SelectionStart = txtPassword.Text.Length;
    }

    // ── connect ───────────────────────────────────────────────────────────────

    private async void btnConnect_Click(object sender, EventArgs e)
    {
        bool sso  = chkIntegrated.Checked;
        var  user = sso
            ? System.Security.Principal.WindowsIdentity.GetCurrent().Name
            : txtUsername.Text.Trim();

        if (!sso && (string.IsNullOrWhiteSpace(user) || user == _config.NetBiosHint + @"\"))
        {
            ShowError("Enter your username.");
            txtUsername.Focus();
            return;
        }

        // Init audit logger now that we have a username (path + machine are stable for the session).
        AuditLogger.Init(_config.AuditLogPath, user, Environment.MachineName, _config.AuditLogRetainDays);

        SetFormBusy(true);
        ShowError(sso ? "Signing in with your Windows credentials…"
            : _config.Endpoints.Count > 1 ? "Locating an available domain controller…"
            : "Connecting…", isInfo: true);

        var svc = new LdapService();

        // 1. Bind
        try
        {
            if (sso)
                await Task.Run(() => svc.ConnectIntegrated(_config));
            else
                await Task.Run(() => svc.Connect(_config, user, txtPassword.Text));
        }
        catch (NoReachableDomainControllerException ex)
        {
            svc.Dispose();
            Logger.Exception("LoginForm: no DC reachable", ex);
            ShowError("No domain controller is reachable. Check the configuration (Config) and try again.");
            SetFormBusy(false);
            return;
        }
        catch (Exception ex) when (sso)
        {
            // SSO failed — uncheck and let the user fall back to manual credentials.
            svc.Dispose();
            Logger.Exception("LoginForm: SSO bind failed — falling back to manual login", ex);
            chkIntegrated.Checked = false;   // triggers UpdateCredentialFieldVisibility
            ShowError("Single Sign-On failed. Enter your credentials to sign in manually.");
            SetFormBusy(false);
            txtUsername.Focus();
            return;
        }
        catch (Exception ex)
        {
            svc.Dispose();
            Logger.Exception("LoginForm: connect failed", ex);
            ShowError($"Connection failed: {ex.Message}");
            SetFormBusy(false);
            txtPassword.Focus();
            txtPassword.SelectAll();
            return;
        }

        // 2. Access gate — must be a (nested) member of an allowed group. Fail-closed.
        try
        {
            ShowError("Checking permissions…", isInfo: true);
            var (granted, detail) = await Task.Run(() => svc.CheckAccess(_config.AllowedGroups, user));
            if (!granted)
            {
                svc.Dispose();
                Logger.Warn($"LoginForm: access DENIED for '{user}': {detail}");
                AuditLogger.LogLoginDenied(detail);
                ShowError("Access denied. " + detail);
                SetFormBusy(false);
                if (!sso) { txtPassword.Focus(); txtPassword.SelectAll(); }
                return;
            }
        }
        catch (Exception ex)
        {
            // Fail-closed: any error during the check denies access.
            svc.Dispose();
            Logger.Exception("LoginForm: access check failed (fail-closed → denied)", ex);
            ShowError("Could not verify permissions, so access was denied. See the log for details.");
            SetFormBusy(false);
            return;
        }

        // 3. Granted
        AuditLogger.LogLoginGranted(svc.ActiveEndpoint?.Host ?? _config.Domain);
        ConnectedService = svc;
        Logger.Info($"LoginForm: access GRANTED for '{user}' on {svc.ActiveEndpoint}; closing with OK.");
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetFormBusy(bool busy)
    {
        btnConnect.Enabled      = !busy;
        chkIntegrated.Enabled   = !busy;
        txtUsername.Enabled     = !busy;
        txtPassword.Enabled     = !busy;
        UseWaitCursor           = busy;
    }

    // ── config editor ─────────────────────────────────────────────────────────

    private void btnConfig_Click(object sender, EventArgs e)
    {
        using var editor = new ConfigEditorForm(_config, _configPath);
        editor.ShowDialog(this);
    }

    // ── exit ──────────────────────────────────────────────────────────────────

    // Application.Exit() does not break a ShowDialog() modal pump — must set
    // DialogResult to make the modal loop return, then let Program.cs exit.
    private void btnExit_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // ── keyboard / helpers ────────────────────────────────────────────────────

    private void txtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) btnConnect_Click(sender, e);
    }

    private void txtUsername_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { txtPassword.Focus(); e.Handled = true; }
    }

    private void ShowError(string msg, bool isInfo = false)
    {
        lblError.Text = msg;
        lblError.ForeColor = isInfo ? Color.DimGray : Color.Firebrick;
    }

    private static bool IsFileEditable(string path)
    {
        try
        {
            using var fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch { return false; }
    }
}
