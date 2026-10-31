using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using UnSetBim.Framework.Commands;
using UnSetBim.Modules.Families.Views;

namespace UnSetBim.Modules.Families.Commands;

/// <summary>
/// Ribbon "Biblioteca": opens the families window.
/// No plan module required: the catalog is open to every verified user and the server
/// decides each download (docs/api.md section 4, "Client guidance").
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class OpenCatalogCommand : UnSetCommand
{
    protected override Result Run(UIApplication app)
    {
        CatalogWindow.ShowOrActivate(Host);
        return Result.Succeeded;
    }
}
