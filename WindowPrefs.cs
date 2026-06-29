using System.Text.Json;

namespace ADGroupBrowser;

/// <summary>
/// Persists the main window's position and size between sessions.
/// Stored in %LOCALAPPDATA%\ADGroupBrowser\window.json (per-user, always writable).
/// All errors are silently swallowed — a missing or corrupt file just means the
/// window opens at its default size.
/// </summary>
internal sealed class WindowPrefs
{
    public int  Left      { get; set; }
    public int  Top       { get; set; }
    public int  Width     { get; set; }
    public int  Height    { get; set; }
    public bool Maximized { get; set; }

    private static string PrefsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ADGroupBrowser", "window.json");

    public static WindowPrefs? Load()
    {
        try
        {
            var path = PrefsPath;
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<WindowPrefs>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    public void Save()
    {
        try
        {
            var path = PrefsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>
    /// Apply saved bounds to a form before it is shown. If the saved position is
    /// entirely off all current screens (e.g. a monitor was disconnected) the
    /// bounds are ignored and the form keeps its default centred position.
    /// </summary>
    public void ApplyTo(Form form)
    {
        if (Width < 200 || Height < 100) return;

        var rect    = new Rectangle(Left, Top, Width, Height);
        bool onScreen = Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect));
        if (!onScreen) return;

        form.StartPosition = FormStartPosition.Manual;
        form.Location      = new Point(Left, Top);
        form.Size          = new Size(Width, Height);
    }
}
