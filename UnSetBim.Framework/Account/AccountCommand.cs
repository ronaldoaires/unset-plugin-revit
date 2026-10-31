using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using UnSetBim.Framework.Commands;

namespace UnSetBim.Framework.Account;

/// <summary>Ribbon "Minha conta": sign in when signed out, otherwise show the account window.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class AccountCommand : UnSetCommand
{
    protected override Result Run(UIApplication app)
    {
        AccountWindow.ShowFor(Host);
        return Result.Succeeded;
    }
}
