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
2. The login screen appears. Your domain prefix is pre-filled in the username field (e.g. `AD\`)
3. Enter your username and password, then click **Connect** (or press Enter)
4. The main window opens, showing all groups from the configured OUs in a tree on the left

> If access is restricted to a specific AD group and you are not a member, you will see an "Access denied" message and cannot proceed.

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

If the `config.json` file is writable for you, a **⚙ Config** button appears in the top bar and on the login screen. This opens the graphical configuration editor — see the *Technical Manual* below.

---

## Technical Manual — Admin Setup

### Deployment overview

1. Place `ADGroupBrowser.exe` (or the standalone variant) in a shared folder, Intune package, or local directory
2. Copy `config.sample.json` to `config.json` in the same folder and edit it
3. Set NTFS permissions on `config.json`: Administrators = Full Control, Domain Users = Read (this hides the Config button for regular users)
4. Optionally create a UNC path for the audit log and grant the application's users Write access to it

### Downloads

| File | Size | Requirement |
|---|---|---|
| `ADGroupBrowser.exe` | ~0.4 MB | [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) |
| `ADGroupBrowser-standalone.exe` | ~50 MB | None — fully self-contained |

The framework-dependent build is recommended for managed environments where the .NET runtime is deployed via Intune/SCCM. Use the standalone build for ad-hoc or isolated machines.

### Building from source

```
dotnet publish ADGroupBrowser.csproj -c Release -r win-x64 --self-contained true  -p:PublishSingleFile=true -o dist/standalone
dotnet publish ADGroupBrowser.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist/framework
```

Or run `./release.ps1` (requires `gh` CLI and a GitHub credential) to bump the version, build both variants, and publish a GitHub release.

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

  // Number of days to retain audit CSV files. Older files are deleted at startup.
  "audit_log_retain_days": 31
}
```

### Multiple configs / command-line usage

If more than one `config*.json` file is placed next to the exe, a chooser dialog appears at startup. You can also specify a config path explicitly:

```
ADGroupBrowser.exe C:\path\to\my-config.json
ADGroupBrowser.exe --config \\server\share\config-production.json
```

This lets you deploy a single exe with multiple named configs (e.g. `config-prod.json`, `config-test.json`).

### Domain controller requirements

- LDAPS (TLS on port 636) must be enabled on your DCs — the default for modern AD
- The application uses `AuthType.Negotiate` (NTLM/Kerberos) over LDAPS; no additional configuration is needed on the DC side
- Server certificates are not validated (self-signed certificates are accepted) — appropriate for internal corporate DCs

### Access gate

Set `allowed_groups` to restrict who can use the tool. The check is server-side (uses the `LDAP_MATCHING_RULE_IN_CHAIN` OID for nested group membership) and is always performed on the live DC — results are never cached. An error during the check counts as a denial (fail-closed).

### Audit logging

When `audit_log_path` is configured, the app writes one line per event to a per-day CSV file (`audit_yyyy-MM-dd.csv`) in the specified directory. Events logged:

| Event | When |
|---|---|
| `LoginGranted` | Successful authentication and access check |
| `LoginDenied` | Access check denied (wrong credentials also result in no event) |
| `GroupViewed` | A user opens a group's member list |

Each row contains: `Timestamp, Event, Username, Machine, GroupName, GroupDN, Mode, MemberCount, DC, Detail`

- Grant the application's users **Modify** (or **Write**) permission to the audit log folder
- If the folder is unreachable, the app logs a warning in its diagnostic log (`ADGroupBrowser.log`) and continues normally — audit failures never block the user

### Diagnostic log

`ADGroupBrowser.log` is written next to the exe (falls back to `%TEMP%` if the directory is read-only). It contains:
- Version, OS, CLR, machine, user
- All LDAP operations and their outcomes
- Any exception with a full stack trace

This log is for IT troubleshooting only and is not intended to be distributed to end-users.
