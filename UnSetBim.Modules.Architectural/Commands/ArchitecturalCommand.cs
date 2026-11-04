using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using UnSetBim.Framework.Commands;
using UnSetBim.Framework.Modules;

namespace UnSetBim.Modules.Architectural.Commands;

/// <summary>
/// Base of every Architectural command: requires the "architectural" plan module
/// and an open project/family document.
/// </summary>
public abstract class ArchitecturalCommand : UnSetCommand
{
    protected override string? RequiredPlanModule => PlanModules.Architectural;

    public override bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) =>
        applicationData.ActiveUIDocument?.Document is not null;
}
