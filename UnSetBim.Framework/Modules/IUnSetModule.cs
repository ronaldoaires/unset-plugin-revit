namespace UnSetBim.Framework.Modules;

/// <summary>
/// A feature package of the plugin (Families, Architectural, Structural...).
///
/// To add a module:
///  1. Create a project UnSetBim.Modules.&lt;Name&gt; referencing UnSetBim.Framework.
///  2. Implement this interface (one public class with a parameterless constructor).
///  3. Register the instance in UnSetBim.Revit.ModuleCatalog and reference the project from UnSetBim.Revit.
///
/// Commands of a module derive from <see cref="Commands.UnSetCommand"/>, which already enforces
/// the update check, sign-in and the plan module (<see cref="RequiredPlanModule"/>).
/// </summary>
public interface IUnSetModule
{
    /// <summary>Stable technical id, e.g. "families". Used in logs.</summary>
    string Id { get; }

    /// <summary>Name shown to the user, in Portuguese.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Key of the "modules" array of the access block that unlocks this module's tools
    /// ("architectural", "structural"), or null when any signed-in user can use it.
    /// The families catalog is always null: families are never gated by modules.
    /// </summary>
    string? RequiredPlanModule { get; }

    /// <summary>Create ribbon panels and buttons. Called once in IExternalApplication.OnStartup.</summary>
    void OnStartup(ModuleContext context);

    /// <summary>Release resources. Called in IExternalApplication.OnShutdown.</summary>
    void OnShutdown();
}

/// <summary>Plan module keys known by the API (docs/api.md section 4).</summary>
public static class PlanModules
{
    public const string Architectural = "architectural";
    public const string Structural = "structural";

    public static string DisplayName(string key) => key switch
    {
        Architectural => "Arquitetônico",
        Structural => "Estrutural",
        _ => key,
    };
}
