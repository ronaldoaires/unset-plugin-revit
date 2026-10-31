using System.Security.Cryptography;
using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;

namespace UnSetBim.Core.Updates;

/// <summary>
/// Downloads the installer of a release and verifies its SHA-256 against the value published
/// by GitHub (via GET /plugin/latest). A file that does not match is deleted and never run.
/// </summary>
public sealed class UpdateDownloader
{
    private readonly UnSetApiClient _api;

    public UpdateDownloader(UnSetApiClient api) => _api = api;

    /// <summary>%TEMP%\UnSetBIM-Update: installers are temporary and deleted after a successful install.</summary>
    private static string UpdatesTempFolder => Path.Combine(Path.GetTempPath(), "UnSetBIM-Update");

    /// <summary>A release can be installed from inside the plugin only when it has an asset and a hash.</summary>
    public static bool CanDownload(PluginRelease release) =>
        !string.IsNullOrWhiteSpace(release.DownloadUrl) && !string.IsNullOrWhiteSpace(release.Sha256);

    /// <summary>Returns the path of the verified installer.</summary>
    public async Task<string> DownloadAsync(PluginRelease release, IProgress<double>? progress, CancellationToken ct)
    {
        if (!CanDownload(release))
            throw new InvalidOperationException("Release has no installer or no SHA-256.");

        var fileName = SafeFileName(release.FileName) ?? $"UnSetBIM-Setup-{release.Version}.exe";
        var destination = Path.Combine(UpdatesTempFolder, release.Version, fileName);

        // Already downloaded and still valid: reuse it.
        if (File.Exists(destination) && await HashMatchesAsync(destination, release.Sha256!, ct))
            return destination;

        await _api.DownloadPublicFileAsync(new Uri(release.DownloadUrl!), destination, progress, ct);

        if (!await HashMatchesAsync(destination, release.Sha256!, ct))
        {
            File.Delete(destination);
            Log.Error($"Installer {fileName} failed the SHA-256 check and was deleted.");
            throw new UpdateIntegrityException();
        }

        Log.Info($"Installer {fileName} downloaded and verified.");
        CleanOldVersions(release.Version);
        return destination;
    }

    private static async Task<bool> HashMatchesAsync(string file, string expectedHex, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return string.Equals(Convert.ToHexString(hash), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var clean = Path.GetFileName(name.Trim());
        var ext = Path.GetExtension(clean).ToLowerInvariant();
        return ext is ".exe" or ".msi" ? clean : null;
    }

    private static void CleanOldVersions(string keepVersion)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(UpdatesTempFolder))
            {
                if (!string.Equals(Path.GetFileName(dir), keepVersion, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not clean old installers.", ex);
        }
    }
}

public sealed class UpdateIntegrityException : Exception
{
    public UpdateIntegrityException()
        : base("O instalador baixado não passou na verificação de integridade e foi descartado. Tente novamente mais tarde.") { }
}
