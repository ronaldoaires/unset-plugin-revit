using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using UnSetBim.Framework.Commands;
using UnSetBim.Modules.Materials.Views;

namespace UnSetBim.Modules.Materials.Commands;

/// <summary>
/// Ribbon "Texturas": opens the textures window. No plan module: free textures are open to every
/// signed-in user and the server decides the others (docs/api.md section 13).
/// The window opens without a document; applying a texture asks for an open project.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class OpenTexturesCommand : UnSetCommand
{
    protected override Result Run(UIApplication app)
    {
        TexturesWindow.ShowOrActivate(Host);
        return Result.Succeeded;
    }
}
