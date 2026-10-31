namespace UnSetBim.Core.Storage;

/// <summary>
/// Every folder and file the plugin writes, all under %LocalAppData%\UnSetBIM.
/// Per-user on purpose: the token and the downloaded families belong to the Windows user,
/// and no admin rights are needed to write there.
/// </summary>
public static class PluginPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        PluginInfo.DataFolderName);

    /// <summary>Downloaded .rfa files, one sub-folder per family slug.</summary>
    public static string FamiliesFolder => Path.Combine(Root, "Families");

    /// <summary>Thumbnails and images shown in the catalog.</summary>
    public static string ImageCacheFolder => Path.Combine(Root, "Cache", "Images");

    public static string LogsFolder => Path.Combine(Root, "Logs");

    /// <summary>Encrypted (DPAPI) API token.</summary>
    public static string SessionFile => Path.Combine(Root, "session.dat");

    /// <summary>Small plugin state (last notified update, etc.).</summary>
    public static string StateFile => Path.Combine(Root, "state.json");

    /// <summary>Optional developer overrides (API URL). Not created by the plugin.</summary>
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(FamiliesFolder);
        Directory.CreateDirectory(ImageCacheFolder);
        Directory.CreateDirectory(LogsFolder);
    }
}
