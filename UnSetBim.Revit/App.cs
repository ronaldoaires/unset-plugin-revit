using Autodesk.Revit.UI;
using UnSetBim.Core;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Account;
using UnSetBim.Framework.Hosting;
using UnSetBim.Framework.Modules;
using UnSetBim.Framework.Updates;

namespace UnSetBim.Revit;

/// <summary>
/// Revit entry point (FullClassName in UnSetBim.addin).
/// Boots the host, creates the "UnSet BIM" tab with the account panel, starts the modules,
/// then validates the session and checks for updates in the background.
/// </summary>
public sealed class App : IExternalApplication
{
    private const string IconBase = "pack://application:,,,/UnSetBim.Framework;component/Resources/";
    private UnSetHost? _host;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            if (!int.TryParse(application.ControlledApplication.VersionNumber, out var year) || year < PluginInfo.MinRevitYear)
            {
                TaskDialog.Show(PluginInfo.Name, $"O {PluginInfo.Name} requer o Revit {PluginInfo.MinRevitYear} ou superior.");
                return Result.Failed;
            }

            _host = UnSetHost.Start(application);

            try
            {
                application.CreateRibbonTab(UnSetHost.RibbonTabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Tab already exists (e.g. a debug and a release copy loaded together): reuse it.
                Log.Warn("Ribbon tab already exists.");
            }

            _host.StartModules(application, ModuleCatalog.Create());
            CreateAccountPanel(application);

            _host.RunStartupTasks();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed.", ex);
            TaskDialog.Show(PluginInfo.Name, "O UnSet BIM não pôde ser iniciado. Reinstale o plugin ou contate o suporte.\n\n" + ex.Message);
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            _host?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Shutdown failed.", ex);
        }

        return Result.Succeeded;
    }

    /// <summary>Last panel of the tab: account and updates (shared by every module).</summary>
    private static void CreateAccountPanel(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel(UnSetHost.RibbonTabName, "Conta");

        panel.AddItem(ModuleContext.CreateButton<AccountCommand>(
            name: "UnSetBim.Account",
            text: "Minha\nconta",
            tooltip: "Entre na sua conta UnSet BIM, veja seu plano e o limite de downloads do mês.",
            largeIcon: IconBase + "account_32.png",
            smallIcon: IconBase + "account_16.png"));

        panel.AddItem(ModuleContext.CreateButton<CheckForUpdatesCommand>(
            name: "UnSetBim.Updates",
            text: "Atualizações",
            tooltip: $"Verificar se há uma nova versão do UnSet BIM. Versão instalada: {PluginInfo.Version}.",
            largeIcon: IconBase + "update_32.png",
            smallIcon: IconBase + "update_16.png"));
    }
}
