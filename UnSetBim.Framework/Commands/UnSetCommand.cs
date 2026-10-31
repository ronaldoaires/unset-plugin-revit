using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Account;
using UnSetBim.Framework.Hosting;
using UnSetBim.Framework.Updates;

namespace UnSetBim.Framework.Commands;

/// <summary>
/// Base class of every ribbon command. In order, before <see cref="Run"/>:
///  1. blocks the command while a required plugin update is pending;
///  2. makes sure the user is signed in (unless <see cref="RequiresSignIn"/> is false);
///  3. checks the plan module (<see cref="RequiredPlanModule"/>), refreshing access once before denying.
/// Any unhandled exception is logged and shown, never propagated to Revit.
///
/// The class is also its own availability class: by default the button works with no document open.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public abstract class UnSetCommand : IExternalCommand, IExternalCommandAvailability
{
    /// <summary>Plan module key ("architectural", "structural") or null for any signed-in user.</summary>
    protected virtual string? RequiredPlanModule => null;

    /// <summary>Name of the feature shown in the "not in your plan" message.</summary>
    protected virtual string FeatureName => "Este recurso";

    protected virtual bool RequiresSignIn => true;

    protected UnSetHost Host => UnSetHost.Current;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            if (!UnSetHost.IsStarted)
            {
                TaskDialog.Show("UnSet BIM", "O UnSet BIM não foi inicializado corretamente. Reinicie o Revit.");
                return Result.Failed;
            }

            if (!UpdatePrompt.EnsureNotBlocked(Host, commandData.Application))
                return Result.Cancelled;

            if (RequiresSignIn && !AccountGate.EnsureSignedIn(Host))
                return Result.Cancelled;

            if (RequiredPlanModule is { } module && !AccountGate.EnsurePlanModule(Host, module, FeatureName))
                return Result.Cancelled;

            return Run(commandData.Application);
        }
        catch (Exception ex)
        {
            Log.Error($"Command {GetType().Name} failed.", ex);
            TaskDialog.Show("UnSet BIM", "Ocorreu um erro inesperado. Tente novamente.\n\nDetalhes foram gravados no log do UnSet BIM.");
            return Result.Failed;
        }
    }

    /// <summary>The command itself. Runs inside the Revit API context.</summary>
    protected abstract Result Run(UIApplication app);

    public virtual bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
