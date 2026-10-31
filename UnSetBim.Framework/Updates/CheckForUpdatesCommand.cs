using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using UnSetBim.Framework.Commands;
using UnSetBim.Framework.Dialogs;

namespace UnSetBim.Framework.Updates;

/// <summary>Ribbon "Atualizações": check now and offer the update, or confirm the version is current.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CheckForUpdatesCommand : UnSetCommand
{
    protected override bool RequiresSignIn => false;

    protected override Result Run(UIApplication app)
    {
        var updates = Host.Updates;

        var check = Task.Run(() => updates.CheckAsync());
        if (!((IAsyncResult)check).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(20)) || updates.Latest is null)
        {
            MessageDialog.Show("Atualizações", "Não foi possível verificar atualizações agora. Tente novamente mais tarde.", DialogKind.Warning);
            return Result.Cancelled;
        }

        if (updates.IsUpdateAvailable || updates.IsUpdateRequired || updates.IsInstalledPendingRestart)
        {
            UpdatePrompt.Show(Host, app, startup: false);
            return Result.Succeeded;
        }

        MessageDialog.Show("Atualizações", $"Você já está usando a versão mais recente do UnSet BIM ({updates.CurrentVersion}).");
        return Result.Succeeded;
    }
}
