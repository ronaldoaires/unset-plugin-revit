using Autodesk.Revit.UI;
using UnSetBim.Core;
using UnSetBim.Core.Api;
using UnSetBim.Core.Configuration;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;
using UnSetBim.Framework.Modules;
using UnSetBim.Framework.Threading;
using UnSetBim.Framework.Session;
using UnSetBim.Framework.Updates;
using UnSetBim.UI.Controls;

namespace UnSetBim.Framework.Hosting;

/// <summary>
/// Services shared by every module during one Revit session.
/// Created by the add-in in OnStartup; modules reach it through <see cref="Current"/>
/// (Revit instantiates commands itself, so constructor injection is not available).
/// </summary>
public sealed class UnSetHost : IDisposable
{
    public const string RibbonTabName = "UnSet BIM";

    private static UnSetHost? _current;
    private readonly List<IUnSetModule> _modules = new();

    private UnSetHost(int revitYear, IntPtr mainWindowHandle)
    {
        RevitYear = revitYear;
        MainWindowHandle = mainWindowHandle;

        Settings = PluginSettings.Load();
        Links = new SiteLinks(Settings);
        Session = new SessionManager();
        Api = new UnSetApiClient(Settings, () => Session.Token, revitYear);
        Session.Attach(Api);
        Updates = new UpdateManager(Api);
        Revit = RevitDispatcher.Create();

        RemoteImage.Fetcher = Api.TryGetImageBytesAsync;
    }

    public static UnSetHost Current =>
        _current ?? throw new InvalidOperationException("UnSet BIM não foi inicializado. Reinicie o Revit.");

    public static bool IsStarted => _current is not null;

    /// <summary>Running Revit year (e.g. 2026). Always sent as revit_version to the catalog.</summary>
    public int RevitYear { get; }

    /// <summary>Revit main window, used as owner of every plugin window.</summary>
    public IntPtr MainWindowHandle { get; }

    public PluginSettings Settings { get; }

    public SiteLinks Links { get; }

    public UnSetApiClient Api { get; }

    public SessionManager Session { get; }

    public UpdateManager Updates { get; }

    /// <summary>Runs work inside the Revit API context.</summary>
    public RevitDispatcher Revit { get; }

    public IReadOnlyList<IUnSetModule> Modules => _modules;

    /// <summary>Boot the host. Call once from IExternalApplication.OnStartup.</summary>
    public static UnSetHost Start(UIControlledApplication application)
    {
        if (_current is not null)
            return _current;

        PluginPaths.EnsureFolders();

        var year = int.TryParse(application.ControlledApplication.VersionNumber, out var y) ? y : PluginInfo.MinRevitYear;
        Log.Info($"Starting {PluginInfo.Name} {PluginInfo.InformationalVersion} on Revit {year} ({application.ControlledApplication.VersionBuild}).");

        _current = new UnSetHost(year, application.MainWindowHandle);
        return _current;
    }

    /// <summary>Start every module. A failing module is logged and skipped; the others keep working.</summary>
    public void StartModules(UIControlledApplication application, IEnumerable<IUnSetModule> modules)
    {
        var context = new ModuleContext(application, RibbonTabName, this);

        foreach (var module in modules)
        {
            try
            {
                module.OnStartup(context);
                _modules.Add(module);
                Log.Info($"Module started: {module.Id}");
            }
            catch (Exception ex)
            {
                Log.Error($"Module {module.Id} failed to start.", ex);
            }
        }
    }

    /// <summary>Background work after the ribbon exists: validate the session and check for updates.</summary>
    public void RunStartupTasks()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Session.RestoreAsync();
            }
            catch (Exception ex)
            {
                // Offline: the token is kept and validated again by the first command.
                Log.Info($"Session restore deferred: {ex.Message}");
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                if (!UpdateManager.ShouldRunAutomaticCheck())
                    return;

                await Updates.CheckAsync();

                if (Updates.ShouldNotify())
                    await Revit.InvokeAsync(app => UpdatePrompt.Show(this, app, startup: true));
            }
            catch (Exception ex)
            {
                Log.Warn("Startup update prompt failed.", ex);
            }
        });

        _ = Task.Run(() => RemoteImage.PruneDiskCache());
    }

    public void Dispose()
    {
        foreach (var module in _modules)
        {
            try
            {
                module.OnShutdown();
            }
            catch (Exception ex)
            {
                Log.Error($"Module {module.Id} failed to stop.", ex);
            }
        }

        Revit.Dispose();
        Api.Dispose();
        _current = null;
        Log.Info("UnSet BIM stopped.");
    }
}
