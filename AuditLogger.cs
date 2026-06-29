namespace ADGroupBrowser;

/// <summary>
/// Append-only audit log. Records login grants/denials and groups viewed.
/// Writes per-day CSV files to a configurable directory (audit_log_path in config).
/// Per-day files reduce write contention when multiple users run concurrently.
/// All errors are silently absorbed and forwarded to the diagnostic log;
/// audit failures never crash or stall the application.
/// </summary>
internal static class AuditLogger
{
    private static string? _dir;
    private static string _username = "";
    private static string _machine  = "";
    private static readonly object _lock = new();

    private static readonly string[] CsvHeaders =
        ["Timestamp", "Event", "Username", "Machine", "GroupName", "GroupDN", "Mode", "MemberCount", "DC", "Detail"];

    /// <summary>
    /// Configure the logger. Call once the login username is known.
    /// Passing an empty/null auditLogPath silently disables all audit writes.
    /// </summary>
    public static void Init(string? auditLogPath, string username, string machine, int retainDays = 31)
    {
        _username = username;
        _machine  = machine;

        if (string.IsNullOrWhiteSpace(auditLogPath))
        {
            _dir = null;
            return;
        }

        _dir = auditLogPath.Trim();
        Logger.Info($"AuditLogger: audit log dir = {_dir}  (retain {retainDays} days)");
        PruneOldLogs(retainDays);
    }

    /// <summary>Delete audit CSV files older than <paramref name="retainDays"/> days.</summary>
    private static void PruneOldLogs(int retainDays)
    {
        if (_dir is null || retainDays <= 0) return;
        try
        {
            var cutoff = DateTime.Today.AddDays(-retainDays);
            foreach (var file in Directory.GetFiles(_dir, "audit_????-??-??.csv"))
            {
                var name = Path.GetFileNameWithoutExtension(file); // "audit_2026-01-15"
                if (DateTime.TryParse(name["audit_".Length..], out var fileDate) && fileDate < cutoff)
                {
                    File.Delete(file);
                    Logger.Info($"AuditLogger: pruned old log {Path.GetFileName(file)}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"AuditLogger: prune failed: {ex.Message}");
        }
    }

    // ── public events ──────────────────────────────────────────────────────────

    public static void LogLoginGranted(string dc)
        => Append("LoginGranted", dc: dc);

    public static void LogLoginDenied(string reason)
        => Append("LoginDenied", detail: reason);

    public static void LogGroupViewed(string groupName, string groupDn, string mode, int memberCount)
        => Append("GroupViewed", groupName: groupName, groupDn: groupDn, mode: mode, memberCount: memberCount.ToString());

    // ── internals ──────────────────────────────────────────────────────────────

    private static void Append(
        string eventName,
        string groupName   = "",
        string groupDn     = "",
        string mode        = "",
        string memberCount = "",
        string dc          = "",
        string detail      = "")
    {
        if (_dir is null) return;

        var fields = new[]
        {
            DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            eventName,
            _username,
            _machine,
            groupName,
            groupDn,
            mode,
            memberCount,
            dc,
            detail,
        };

        try
        {
            lock (_lock)
            {
                var filePath  = Path.Combine(_dir, $"audit_{DateTime.Today:yyyy-MM-dd}.csv");
                bool newFile  = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;

                using var sw  = new StreamWriter(filePath, append: true, System.Text.Encoding.UTF8);
                if (newFile) sw.WriteLine(ToCsvRow(CsvHeaders));
                sw.WriteLine(ToCsvRow(fields));
            }
        }
        catch (Exception ex)
        {
            // Never crash; the diagnostic log is the fallback record.
            Logger.Warn($"AuditLogger: could not write to \"{_dir}\": {ex.Message}");
        }
    }

    // RFC 4180 CSV: quote any field containing comma, double-quote, or newline.
    private static string ToCsvRow(string[] fields)
        => string.Join(",", fields.Select(CsvEscape));

    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }
}
