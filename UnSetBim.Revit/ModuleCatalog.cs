using UnSetBim.Framework.Modules;
using UnSetBim.Modules.Architectural;
using UnSetBim.Modules.Families;
using UnSetBim.Modules.Materials;

namespace UnSetBim.Revit;

/// <summary>
/// The modules shipped in this build, in ribbon order.
/// To add a module (e.g. UnSetBim.Modules.Architectural): reference its project from
/// UnSetBim.Revit.csproj and add one line here. Nothing else in the host changes.
/// </summary>
internal static class ModuleCatalog
{
    public static IEnumerable<IUnSetModule> Create() => new IUnSetModule[]
    {
        new FamiliesModule(),
        new ArchitecturalModule(),
        new MaterialsModule(),
        // new StructuralModule(),      // RequiredPlanModule = PlanModules.Structural
    };
}
