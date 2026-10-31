using System.Diagnostics;
using System.IO;
using UnSetBim.Core;
using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;
using UnSetBim.Core.Updates;

namespace UnSetBim.Framework.Updates;

/// <summary>
/// Plugin update state (docs/api.md section 8 and the installer brief).
///
/// - Automatic check at Revit start at most once a day (plus the "Atualizações" button).
///   While an update is known to exist, it is checked on every start.
/// - update_required: every UnSet BIM command is blocked until the user updates.
/// - update_available: offered once per version.
/// - The installer is downloaded, verified against the published SHA-256 and run silently.
///   It installs into a new versioned folder, so it works with Revit open; the new version
///   loads on the next Revit start.
/// </summary>
public sealed class UpdateManager
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(5);

    private readonly UnSetApiClient _api;

    public UpdateManager(UnSetApiClient api) => _api = api;

    public PluginRelease? Latest { get; private set; }

    public bool IsUpdateAvailable { get; private set; }

    public bool IsUpdateRequired { get; private set; }

    /// <summary>The new version was installed during this session; Revit must restart to load it.</summary>
    public bool IsInstalledPendingRestart { get; private set; }

    public string CurrentVersion => PluginInfo.Version;

    /// <summary>Automatic (startup) check: once a day, or every start while an update is pending.</summary>
    public static bool ShouldRunAutomaticCheck()
    {
        var state = PluginState.Load();
        return state.LastUpdateCheckAt is not { } last
               || state.LastUpdateCheckFoundUpdate
               || DateTimeOffset.Now - last >= AutomaticCheckInterval;
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var release = await _api.GetLatestReleaseAsync(CurrentVersion, ct);
            if (release is null)
                return;

            Latest = release;

            // Trust the server's answer; fall back to a local comparison if it did not compute it.
            IsUpdateAvailable = release.UpdateAvailable ?? PluginVersion.IsNewer(release.Version, CurrentVersion);
            IsUpdateRequired = release.UpdateRequired ?? PluginVersion.IsNewer(release.MinVersion, CurrentVersion);

            var found = IsUpdateAvailable || IsUpdateRequired;
            PluginState.Update(s =>
            {
                s.LastUpdateCheckAt = DateTimeOffset.Now;
                s.LastUpdateCheckFoundUpdate = found;
            });

            Log.Info($"Update check: current={CurrentVersion} latest={release.Version} available={IsUpdateAvailable} required={IsUpdateRequired}");
        }
        catch (Exception ex) when (ex is ApiException or ApiConnectionException)
        {
            // 503 update_info_unavailable and network errors are ignored silently.
            Log.Info($"Update check skipped: {ex.Message}");
        }
    }

    /// <summary>Required updates are always shown; optional ones once per version.</summary>
    public bool ShouldNotify()
    {
        if (Latest is null || IsInstalledPendingRestart)
            return false;

        if (IsUpdateRequired)
            return true;

        if (!IsUpdateAvailable)
            return false;

        var notified = PluginState.Load().NotifiedUpdateVersion;
        return !string.Equals(notified, Latest.Version, StringComparison.OrdinalIgnoreCase);
    }

    public void MarkNotified()
    {
        if (Latest is not null)
            PluginState.Update(s => s.NotifiedUpdateVersion = Latest.Version);
    }

    public Task<string> DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (Latest is null)
            throw new InvalidOperationException("No release known.");

        return new UpdateDownloader(_api).DownloadAsync(Latest, progress, ct);
    }

    /// <summary>
    /// Run the verified installer silently (Inno Setup flags) and wait for it.
    /// Throws <see cref="UpdateInstallException"/> when it fails.
    /// </summary>
    public async Task InstallAsync(string installerPath, CancellationToken ct)
    {
        var logFile = Path.Combine(PluginPaths.LogsFolder, $"installer-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var start = new ProcessStartInfo(installerPath)
        {
            Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=\"{logFile}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Log.Info($"Running installer {Path.GetFileName(installerPath)} silently.");

        using var process = Process.Start(start) ?? throw new UpdateInstallException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(InstallerTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Error("Installer did not finish in time.");
            throw new UpdateInstallException();
        }

        if (process.ExitCode != 0)
        {
            Log.Error($"Installer exit code {process.ExitCode}. See {logFile}.");
            throw new UpdateInstallException();
        }

        IsInstalledPendingRestart = true;
        TryDelete(installerPath);
        Log.Info($"Version {Latest?.Version} installed; it loads on the next Revit start.");
    }

    public void OpenReleasePage()
    {
        if (Latest?.ReleaseUrl is { Length: > 0 } url)
            Browser.Open(url);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temp file: Windows cleans it eventually.
        }
    }
}

public sealed class UpdateInstallException : Exception
{
    public UpdateInstallException()
        : base("Não foi possível instalar a atualização automaticamente. Baixe o instalador pela página da versão.") { }
}
