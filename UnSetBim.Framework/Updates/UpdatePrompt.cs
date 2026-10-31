using Autodesk.Revit.UI;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Dialogs;
using UnSetBim.Framework.Hosting;

namespace UnSetBim.Framework.Updates;

/// <summary>Shows the update window. Must run inside the Revit API context (command or dispatcher).</summary>
public static class UpdatePrompt
{
    /// <summary>
    /// Show the update window.
    /// <paramref name="startup"/>: shown automatically after Revit start (counts as "notified").
    /// </summary>
    public static void Show(UnSetHost host, UIApplication app, bool startup)
    {
        if (host.Updates.Latest is null)
            return;

        var viewModel = new UpdateViewModel(host.Updates);
        new UpdateWindow(viewModel).AttachToRevit().ShowDialog();

        if (startup)
            host.Updates.MarkNotified();

        if (viewModel.ExitRevitRequested)
            RequestRevitExit(app);
    }

    /// <summary>
    /// False (and the update window shown) while a required update is pending.
    /// Called before every UnSet BIM command.
    /// </summary>
    public static bool EnsureNotBlocked(UnSetHost host, UIApplication app)
    {
        if (!host.Updates.IsUpdateRequired)
            return true;

        Show(host, app, startup: false);
        return false;
    }

    private static void RequestRevitExit(UIApplication app)
    {
        try
        {
            // Same as File > Exit: Revit asks to save open documents. The new version loads on the next start.
            var exit = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
            app.PostCommand(exit);
        }
        catch (Exception ex)
        {
            Log.Error("Could not post the Exit Revit command.", ex);
            MessageDialog.Show("Reinicie o Revit", "Feche e abra o Revit para usar a nova versão.", DialogKind.Info);
        }
    }
}
