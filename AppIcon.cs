namespace ADGroupBrowser;

internal static class AppIcon
{
    public static Icon? Load()
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("ADGroupBrowser.app.ico");
            return s is not null ? new Icon(s) : null;
        }
        catch { return null; }
    }

    public static Image? LoadLogo(int size = 256)
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("ADGroupBrowser.app.ico");
            if (s is null) return null;
            using var icon = new Icon(s, size, size);
            return icon.ToBitmap();
        }
        catch { return null; }
    }
}
