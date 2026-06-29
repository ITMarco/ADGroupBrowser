using System.DirectoryServices.Protocols;
using System.Reflection;

namespace ADGroupBrowser;

/// <summary>
/// Bulletproof file logger. Never throws from a logging call. Writes a fresh
/// log next to the exe each run (falls back to %TEMP% if that dir is read-only).
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static string _logPath = "";

    public static string LogPath => _logPath;

    public static void Init()
    {
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(exeDir, "ADGroupBrowser.log");

            // Probe writability of the exe directory
            try
            {
                File.WriteAllText(candidate, "");
                _logPath = candidate;
            }
            catch
            {
                _logPath = Path.Combine(Path.GetTempPath(), "ADGroupBrowser.log");
                try { File.WriteAllText(_logPath, ""); } catch { }
            }

            var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
            Raw("==========================================================");
            Info($"ADGroupBrowser log started  (v{ver})");
            Info($"Log file   : {_logPath}");
            Info($"OS         : {Environment.OSVersion}");
            Info($"64-bit proc: {Environment.Is64BitProcess}");
            Info($"CLR        : {Environment.Version}");
            Info($"User       : {Environment.UserDomainName}\\{Environment.UserName}");
            Info($"Machine    : {Environment.MachineName}");
            Raw("==========================================================");
        }
        catch { /* logging must never crash the app */ }
    }

    public static void Info(string m)  => Write("INFO", m);
    public static void Debug(string m) => Write("DEBUG", m);
    public static void Warn(string m)  => Write("WARN", m);
    public static void Error(string m) => Write("ERROR", m);

    public static void Exception(string context, Exception ex)
    {
        Write("ERROR", $"{context}: {ex.GetType().FullName}: {ex.Message}");

        if (ex is LdapException ldap)
        {
            Write("ERROR", $"  LDAP ErrorCode = {ldap.ErrorCode}");
            if (!string.IsNullOrEmpty(ldap.ServerErrorMessage))
                Write("ERROR", $"  LDAP ServerErrorMessage = {ldap.ServerErrorMessage}");
        }
        if (ex is DirectoryOperationException dox)
        {
            try
            {
                Write("ERROR", $"  DirectoryOperation ResultCode = {dox.Response?.ResultCode}");
                if (!string.IsNullOrEmpty(dox.Response?.ErrorMessage))
                    Write("ERROR", $"  DirectoryOperation ErrorMessage = {dox.Response?.ErrorMessage}");
            }
            catch { }
        }

        Write("ERROR", "  StackTrace:\n" + Indent(ex.StackTrace));

        var inner = ex.InnerException;
        int depth = 0;
        while (inner is not null && depth < 6)
        {
            Write("ERROR", $"  --- Inner[{depth}] {inner.GetType().FullName}: {inner.Message}");
            Write("ERROR", "      StackTrace:\n" + Indent(inner.StackTrace));
            inner = inner.InnerException;
            depth++;
        }
    }

    private static string Indent(string? s) =>
        string.IsNullOrEmpty(s) ? "    (none)"
            : string.Join(Environment.NewLine, s.Split('\n').Select(l => "    " + l.TrimEnd()));

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] {message}";
        Raw(line);
    }

    private static void Raw(string line)
    {
        try
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            lock (_lock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch { /* swallow */ }
    }
}
