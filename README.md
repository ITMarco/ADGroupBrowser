# AD Group Browser

Browse Active Directory group memberships from Azure AD-joined (non-domain-joined) PCs — no PowerShell modules, no Graph API, no domain membership required. A single self-contained `.exe` connects directly to your on-premises domain controllers over LDAPS, authenticates with your AD credentials via NTLM/Kerberos, and lets you explore who is in which group.

---

## User Manual

### Prerequisites

- Network access (TCP port 636) to at least one on-premises domain controller
- A valid AD account (the same credentials you use on-site or over VPN)
- The application has been deployed by your IT admin with a pre-configured `config.json`

### Getting started

1. Run `ADGroupBrowser.exe` (or `ADGroupBrowser-standalone.exe` if provided)
2. The login screen appears
3. Sign in (see below) and click **Connect** (or press Enter)
4. The main window opens showing all groups from the configured OUs in a tree on the left

> If access is restricted to a specific AD group and you are not a member, you will see an "Access denied" message and cannot proceed.

### Signing in

The login screen offers two modes:

**Single Sign-On (default)** — the *"Use my current Windows sign-in"* checkbox is ticked by default. Click **Connect** and the app authenticates using your current Windows session — no username or password entry needed. This works on Azure AD-joined machines with cloud Kerberos trust or Hybrid AADJ. If SSO is not available on your machine, the app automatically falls back to manual sign-in.

**Manual sign-in** — untick the checkbox to reveal the username and password fields. Your domain prefix is pre-filled (e.g. `AD\`). Enter your credentials and click **Connect**.

### Browsing groups

- The left panel shows groups organised by OU. Click the **▶ / ▼** triangle to expand or collapse an OU section
- Groups marked `[S]` in the OU header include sub-OUs; groups marked `[T]` come from that OU only
- Use the **filter box** at the top to search groups by name or description — the tree updates as you type

### Viewing members

- Click a group name to load its members on the right
- By default **direct members** are shown (fast). Tick **Recursive (incl. nested)** to include members inherited through nested groups (slower, queries the DC)
- Double-click a member of type **Group** to jump directly to that group in the tree
- Select one or more rows and press **Ctrl+C** to copy members as tab-separated text

### Refreshing

Click **⟳ Refresh** to reload all groups from the domain controllers and clear the member cache (useful if group memberships have changed during your session).

### Config button (admin only)

A **⚙ Config** button appears in the top bar and on the login screen **only when the `config.json` file is writable for you**. Regular users whose config is set to read-only will not see it. Clicking it opens the graphical configuration editor — see the *Technical Manual* below.

---

## Technical Manual — Admin Setup

### Deployment overview

1. Place `ADGroupBrowser.exe` (or the standalone variant) in a shared folder, Intune package, or local directory
2. Copy `config.sample.json` to `config.json` in the same folder and edit it
3. Use the graphical **⚙ Config** editor (visible when the file is writable) or edit `config.json` with any text editor
4. Set NTFS permissions on `config.json`: **Administrators = Full Control**, **Domain Users = Read** — this automatically hides the Config button for regular users, preventing them from changing the configuration
5. Optionally configure a UNC path for the audit log and grant the application's users Write access to it

### Downloads

| File | Size | Requirement |
|---|---|---|
| `ADGroupBrowser.exe` | ~0.4 MB | [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) |
| `ADGroupBrowser-standalone.exe` | ~50 MB | None — fully self-contained |

The framework-dependent build is recommended for managed environments where the .NET runtime is deployed via Intune/SCCM. Use the standalone build for ad-hoc or isolated machines.

### Command-line and multiple configs

If more than one `config*.json` file is placed next to the exe, a chooser dialog appears at startup. You can also specify a config path on the command line:

```
ADGroupBrowser.exe C:\path\to\my-config.json
ADGroupBrowser.exe --config \\server\share\config-production.json
ADGroupBrowser.exe -c config-test.json
ADGroupBrowser.exe --config=config-test.json
```

This lets you deploy a single exe alongside multiple named configs (e.g. `config-prod.json`, `config-test.json`) and let users or scripts select the right environment.

### Making config read-only (recommended)

Setting `config.json` to read-only for regular users has two effects:

1. The **⚙ Config** button is hidden — users cannot open the configuration editor
2. Users cannot accidentally overwrite settings

**NTFS permissions (local deployment)**

Right-click `config.json` → Properties → Security → Edit:
- Administrators: Full Control
- Domain Users (or the specific user group): Read

**Network share deployment**

Deploy the exe and `config.json` to a read-only share. Users can run the exe from the share directly, or you can copy the exe locally and point it to the config via `--config \\server\share\config.json`.

### Graphical config editor

Open the editor via the **⚙ Config** button (visible only when `config.json` is writable). It has four tabs:

| Tab | Contents |
|---|---|
| **Connection** | Domain name, domain controller list (Add / Update / Remove), Test Connection button per DC |
| **Search OUs** | OU list with per-OU subtree flag (`[S]` = include child OUs, `[T]` = this OU only), Add / Update / Remove |
| **Access Gate** | Allowed groups (CN or full DN), Add / Update / Remove |
| **Advanced** | SSL toggle, default port, LDAP timeout, connect timeout, audit log path and retention |

Click **Save** to write the current config file, or **Save As…** to save a copy under a new name.

> Changes take effect the next time the application is started — a running session is not affected.

### config.json reference

```jsonc
{
  // FQDN of your AD domain
  "domain": "contoso.local",

  // List of domain controllers (hostname or hostname:port).
  // The app probes each one at startup and round-robins across reachable DCs.
  // If a DC becomes unreachable mid-session it falls back to the next one.
  "domain_controllers": [
    "dc1.contoso.local",
    "dc2.contoso.local:636"
  ],

  // Default TCP port for DCs listed without an explicit :port suffix.
  "port": 636,

  // Use LDAPS (recommended; required for port 636).
  "use_ssl": true,

  // Seconds to wait for an LDAP query to complete.
  "timeout_seconds": 30,

  // Milliseconds for the TCP health-probe before attempting to bind.
  // Increase if your DCs are across a slow WAN link.
  "connect_timeout_ms": 1500,

  // OUs to search for groups.
  // Plain string = include all child OUs (subtree).
  // Object form lets you restrict to only the named OU (subtree: false).
  "search_ous": [
    "OU=SecurityGroups,OU=IT,DC=contoso,DC=local",
    { "dn": "OU=DistributionLists,DC=contoso,DC=local", "subtree": false }
  ],

  // Access gate: user must be a (nested) member of at least one group.
  // Accepts a friendly CN ("Helpdesk") or a full DN.
  // Leave empty ([]) to allow any user who can successfully bind.
  "allowed_groups": [
    "AD Group Browser Users"
  ],

  // Audit log directory. Per-day CSV files are written here.
  // Leave empty ("") to disable audit logging.
  "audit_log_path": "\\\\fileserver\\AuditLogs\\ADGroupBrowser",

  // How many days of audit CSV files to keep. Files older than this are
  // deleted at startup. Default: 31.
  "audit_log_retain_days": 31
}
```

### Domain controller requirements

- LDAPS (TLS on port 636) must be enabled on your DCs — the default for modern AD
- The application uses `AuthType.Negotiate` (NTLM/Kerberos) over LDAPS; no additional configuration is needed on the DC side
- Server certificates are not validated (self-signed certificates are accepted) — appropriate for internal corporate DCs

### Single Sign-On requirements

For SSO to work on Azure AD-joined machines the following must be in place:

- **Cloud Kerberos trust** (recommended): configured in Azure AD Connect → Hybrid Identity → User sign-in → SSO. Allows Azure AD-joined machines to obtain Kerberos tickets for on-premises resources without a VPN.
- **Hybrid AADJ**: machine joined to both on-premises AD and Azure AD. Standard Kerberos works over line-of-sight to a DC.

If neither is configured, SSO will fail and the app automatically falls back to username/password login.

### Access gate

Set `allowed_groups` to restrict who can use the tool. The check is server-side (uses the `LDAP_MATCHING_RULE_IN_CHAIN` OID for nested group membership) and is always performed on the live DC — results are never cached. An error during the check counts as a denial (fail-closed).

### Audit logging

When `audit_log_path` is configured, the app writes one line per event to a per-day CSV file (`audit_yyyy-MM-dd.csv`) in the specified directory. Events logged:

| Event | When |
|---|---|
| `LoginGranted` | Successful authentication and access check |
| `LoginDenied` | Access check denied (wrong credentials result in no audit event) |
| `GroupViewed` | A user opens a group's member list |

Each row contains: `Timestamp, Event, Username, Machine, GroupName, GroupDN, Mode, MemberCount, DC, Detail`

- Grant the application's users **Modify** (or **Write**) permission to the audit log folder
- If the folder is unreachable, the app logs a warning in its diagnostic log (`ADGroupBrowser.log`) and continues normally — audit failures never block the user
- Files older than `audit_log_retain_days` days are automatically deleted at startup

### Diagnostic log

`ADGroupBrowser.log` is written next to the exe (falls back to `%TEMP%` if the directory is read-only). It contains:
- Version, OS, CLR, machine, user
- All LDAP operations and their outcomes
- Any exception with a full stack trace

This log is for IT troubleshooting only and is not intended to be distributed to end-users.

### Building from source

```
dotnet publish ADGroupBrowser.csproj -c Release -r win-x64 --self-contained true  -p:PublishSingleFile=true -o dist/standalone
dotnet publish ADGroupBrowser.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist/framework
```

To cut a full release (bump version, build both variants, create a GitHub release with assets attached), run `./release.ps1` from the project root. It reads a GitHub token from the Windows credential store — no additional CLI tools required.
