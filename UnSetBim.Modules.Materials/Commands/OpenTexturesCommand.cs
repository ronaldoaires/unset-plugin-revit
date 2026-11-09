using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using UnSetBim.Framework.Commands;
using UnSetBim.Modules.Materials.Views;

namespace UnSetBim.Modules.Materials.Commands;

/// <summary>
/// Ribbon "Texturas": shows the textures dockable pane. No plan module: free textures are open
/// to every signed-in user and the server decides the others (docs/api.md section 13).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class OpenTexturesCommand : UnSetCommand
{
    protected override Result Run(UIApplication app)
    {
        app.GetDockablePane(TexturesPane.PaneId).Show();
        TexturesPane.Instance.Activate(Host);
        return Result.Succeeded;
    }
}
