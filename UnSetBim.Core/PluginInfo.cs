using System.Reflection;

namespace UnSetBim.Core;

/// <summary>
/// Static identity of the plugin. Values that change per environment
/// (API URL, site URL) live in <see cref="Configuration.PluginSettings"/>.
/// </summary>
public static class PluginInfo
{
    public const string Name = "UnSet BIM";

    /// <summary>Must match the AddInId in the .addin manifest. Never change it: Revit uses it to identify the add-in.</summary>
    public const string AddInId = "BC663243-3565-44DC-9920-DC47898C57F6";

    public const string VendorId = "UNSET";

    /// <summary>Oldest Revit year supported by this code base.</summary>
    public const int MinRevitYear = 2025;

    /// <summary>Folder name used under %LocalAppData% for user data.</summary>
    public const string DataFolderName = "UnSetBIM";

    private static string? _version;

    /// <summary>
    /// Plugin version in "major.minor.patch" form, read from the assembly
    /// (set by &lt;Version&gt; in Directory.Build.props).
    /// </summary>
    public static string Version => _version ??= ReadVersion();

    private static string ReadVersion()
    {
        var version = typeof(PluginInfo).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    /// <summary>Informational version, may include a commit suffix. Used only in logs.</summary>
    public static string InformationalVersion =>
        typeof(PluginInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Version;
}
