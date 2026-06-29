namespace ADGroupBrowser;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Logger.Init();

        // Catch everything — UI-thread and background — and route it to the log.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Logger.Exception("Application.ThreadException (UI thread)", e.Exception);
            ShowCrash(e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Logger.Exception("AppDomain.UnhandledException", ex);
            else
                Logger.Error("AppDomain.UnhandledException: " + e.ExceptionObject);
        };

        try
        {
            // DPI mode comes from <ApplicationHighDpiMode>PerMonitorV2</…> in the csproj,
            // applied here. (Calling SetHighDpiMode again afterwards is a no-op.)
            ApplicationConfiguration.Initialize();
            Logger.Info("WinForms application initialized.");
            Logger.Info($"Command line args: {(args.Length == 0 ? "(none)" : string.Join(" ", args))}");

            // Resolve which config file to use, then load it (re-prompting on failure).
            if (!TryResolveConfig(args, out var config, out var configPath))
            {
                Logger.Info("No config selected; exiting.");
                return;
            }

            Logger.Info($"Config in use: {configPath}");
            Logger.Info($"  Domain={config!.Domain}  DCs={config.Endpoints.Count}  SSL={config.UseSsl}  " +
                        $"OUs={config.SearchOus.Count}  AllowedGroups={config.AllowedGroups.Count}");
            foreach (var ou in config.SearchOus)
                Logger.Info($"  search OU: {ou.Dn}  (subtree={ou.Subtree})");

            using var loginForm = new LoginForm(config, configPath!);
            Logger.Info("Showing login form.");
            var result = loginForm.ShowDialog();
            Logger.Info($"Login dialog returned: {result}");

            if (result != DialogResult.OK || loginForm.ConnectedService is null)
            {
                Logger.Info("No successful login; exiting.");
                return;
            }

            Logger.Info("Constructing main form.");
            using var main = new MainForm(config, configPath!, loginForm.Username, loginForm.ConnectedService);
            Logger.Info("Running main form message loop.");
            Application.Run(main);
            Logger.Info("Main form closed; application exiting normally.");
        }
        catch (Exception ex)
        {
            Logger.Exception("FATAL error in Main", ex);
            ShowCrash(ex);
        }
    }

    // ── config resolution (M4) ──────────────────────────────────────────────────

    /// <summary>
    /// Determines and loads the config:
    ///  1. command-line path (positional or --config/-c),
    ///  2. else the single config*.json next to the exe,
    ///  3. else a chooser dialog (many found) / browse-or-exit (none found).
    /// Re-prompts on load failure. Returns false if the user chose to exit.
    /// </summary>
    private static bool TryResolveConfig(string[] args, out AppConfig? config, out string? path)
    {
        config = null;
        path = null;

        string? candidate = ParseConfigArg(args);
        if (candidate is not null)
        {
            candidate = Path.GetFullPath(candidate);
            if (!File.Exists(candidate))
            {
                Logger.Warn($"Config path from command line not found: {candidate}");
                MessageBox.Show(
                    $"The configuration file specified on the command line was not found:\n\n{candidate}",
                    "Configuration not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                candidate = null;   // fall through to chooser
            }
        }
        else
        {
            // No explicit path: auto-load only when exactly one local config exists.
            var local = FindLocalConfigs();
            if (local.Length == 1)
            {
                candidate = local[0];
                Logger.Info($"Auto-selected the only local config: {candidate}");
            }
            else
            {
                Logger.Info($"Found {local.Length} local config(s); showing chooser.");
            }
        }

        while (true)
        {
            if (candidate is null)
            {
                using var chooser = new ConfigChooserForm(FindLocalConfigs());
                if (chooser.ShowDialog() != DialogResult.OK || chooser.SelectedPath is null)
                    return false;   // user exited
                candidate = chooser.SelectedPath;
            }

            try
            {
                config = AppConfig.Load(candidate);
                path = candidate;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Exception($"Config load failed for {candidate}", ex);
                MessageBox.Show(
                    $"Could not load this configuration:\n\n{candidate}\n\n{ex.Message}\n\n" +
                    $"Tip: copy config.sample.json to config.json and edit it.",
                    "Configuration error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                candidate = null;   // back to the chooser
            }
        }
    }

    // Accepts:  ADGroupBrowser.exe <path>   or   --config <path>   or   --config=<path>   (also -c)
    private static string? ParseConfigArg(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Equals("--config", StringComparison.OrdinalIgnoreCase) || a.Equals("-c", StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Length ? args[i + 1] : null;
            if (a.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
                return a["--config=".Length..];
            if (!a.StartsWith('-'))
                return a;   // positional path
        }
        return null;
    }

    // config*.json next to the exe, excluding the shipped *.sample.json template.
    private static string[] FindLocalConfigs()
    {
        try
        {
            return Directory.GetFiles(AppContext.BaseDirectory, "config*.json")
                .Where(f => !Path.GetFileName(f).EndsWith(".sample.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            Logger.Exception("FindLocalConfigs failed", ex);
            return Array.Empty<string>();
        }
    }

    private static void ShowCrash(Exception ex)
    {
        MessageBox.Show(
            $"An error occurred:\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
            $"Full details were written to:\n{Logger.LogPath}",
            "ADGroupBrowser — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
