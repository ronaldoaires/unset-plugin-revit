using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace UnSetBim.Modules.Materials.RevitApi;

/// <summary>
/// Marker stored on materials created by the plugin (Extensible Storage): which texture,
/// version and resolution they were built from. Lets the plugin find its own materials again
/// (reuse instead of duplicating, update to a new version, relink missing images later).
/// Hidden from the user, so it cannot be erased by accident like a Comments value.
/// </summary>
internal static class TextureMaterialSchema
{
    // Never change: materials already saved in projects carry this schema id.
    private static readonly Guid SchemaGuid = new("7C3A5E91-2B64-4F0D-9E8A-1D5B3C7F2A46");

    private const string SlugField = "Slug";
    private const string VersionField = "Version";
    private const string ResolutionField = "Resolution";

    public sealed record Marker(string Slug, int Version, string Resolution);

    public static Schema GetOrCreate()
    {
        var existing = Schema.Lookup(SchemaGuid);
        if (existing is not null)
            return existing;

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("UnSetTextureMaterial");
        builder.SetDocumentation("UnSet BIM texture material marker.");
        // Public on purpose: other UnSet builds (any Revit year) and future tools must read it.
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.SetVendorId("UNSET");
        builder.AddSimpleField(SlugField, typeof(string));
        builder.AddSimpleField(VersionField, typeof(int));
        builder.AddSimpleField(ResolutionField, typeof(string));
        return builder.Finish();
    }

    public static Marker? Read(Element element)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
            return null;

        var entity = element.GetEntity(schema);
        if (!entity.IsValid())
            return null;

        var slug = entity.Get<string>(SlugField);
        return string.IsNullOrEmpty(slug)
            ? null
            : new Marker(slug, entity.Get<int>(VersionField), entity.Get<string>(ResolutionField) ?? string.Empty);
    }

    /// <summary>Must run inside an open transaction.</summary>
    public static void Write(Element element, Marker marker)
    {
        var entity = new Entity(GetOrCreate());
        entity.Set(SlugField, marker.Slug);
        entity.Set(VersionField, marker.Version);
        entity.Set(ResolutionField, marker.Resolution ?? string.Empty);
        element.SetEntity(entity);
    }

    /// <summary>
    /// The main material of <paramref name="slug"/>: the oldest one (lowest id). Variations made
    /// later carry the same marker but are never picked here, so applying the texture again from
    /// the library never changes them.
    /// </summary>
    public static Material? FindMaterial(Document doc, string slug)
    {
        if (Schema.Lookup(SchemaGuid) is null)
            return null;

        return new FilteredElementCollector(doc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .Where(m => Read(m)?.Slug == slug)
            .OrderBy(m => m.Id.Value)
            .FirstOrDefault();
    }
}
