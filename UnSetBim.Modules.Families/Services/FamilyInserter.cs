using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Families;
using UnSetBim.Framework.Hosting;
using UnSetBim.Modules.Families.RevitApi;

namespace UnSetBim.Modules.Families.Services;

/// <summary>
/// Download a family through the API and load it into the active Revit document (docs/api.md 10, steps 4–5).
/// The server decides access; the plugin only reflects download.allowed and handles the 403 codes.
/// </summary>
internal sealed class FamilyInserter
{
    private readonly UnSetHost _host;

    public FamilyInserter(UnSetHost host) => _host = host;

    /// <summary>
    /// Download and start placement. Throws <see cref="ApiException"/> (403 access_required / quota_exceeded,
    /// 404, 429) and <see cref="ApiConnectionException"/>; the caller shows <c>UserMessage</c>.
    /// </summary>
    public async Task<PlacementResult> InsertAsync(FamilyDetail family, IProgress<double>? progress, CancellationToken ct)
    {
        // Check for an open document first: a download counts in the quota and in the history,
        // so never download a family that cannot be loaded right now.
        var hasDocument = await _host.Revit.InvokeAsync(app => app.ActiveUIDocument?.Document is { IsReadOnly: false });
        if (!hasDocument)
            return new PlacementResult(PlacementOutcome.NoDocument);

        var file = FamilyFileStore.GetFilePath(family);

        await _host.Api.DownloadFamilyAsync(family.Download.Url, file, progress, ct);
        FamilyFileStore.CleanupSiblings(file);
        Log.Info($"Family downloaded: {family.Slug}");

        // The quota may have changed: refresh it in the background for the header.
        _ = Task.Run(() => _host.Session.RefreshAccessAsync());

        var familyName = FamilyFileStore.GetRevitFamilyName(family);
        return await _host.Revit.InvokeAsync(app => FamilyPlacement.LoadAndPlace(app, file, familyName));
    }
}
