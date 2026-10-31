using System.Text.Json;
using UnSetBim.Core.Api;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Core.Storage;

/// <summary>Small persisted state shared by every Revit year (state.json).</summary>
public sealed class PluginState
{
    private static readonly object Sync = new();

    /// <summary>Last optional update the user was told about. Avoids asking again on every start.</summary>
    public string? NotifiedUpdateVersion { get; set; }

    /// <summary>Last successful update check (automatic checks run at most once a day).</summary>
    public DateTimeOffset? LastUpdateCheckAt { get; set; }

    /// <summary>The last check found an update: keep checking on every start until it is installed.</summary>
    public bool LastUpdateCheckFoundUpdate { get; set; }

    /// <summary>Last sort chosen in the families catalog.</summary>
    public string? FamiliesSort { get; set; }

    public static PluginState Load()
    {
        lock (Sync)
        {
            try
            {
                if (File.Exists(PluginPaths.StateFile))
                    return JsonSerializer.Deserialize<PluginState>(File.ReadAllText(PluginPaths.StateFile), ApiJson.Options) ?? new();
            }
            catch (Exception ex)
            {
                Log.Warn("state.json could not be read.", ex);
            }

            return new PluginState();
        }
    }

    /// <summary>Load, change and save in one step.</summary>
    public static void Update(Action<PluginState> change)
    {
        lock (Sync)
        {
            try
            {
                var state = Load();
                change(state);
                Directory.CreateDirectory(PluginPaths.Root);
                File.WriteAllText(PluginPaths.StateFile, JsonSerializer.Serialize(state, ApiJson.Options));
            }
            catch (Exception ex)
            {
                Log.Warn("state.json could not be written.", ex);
            }
        }
    }
}
