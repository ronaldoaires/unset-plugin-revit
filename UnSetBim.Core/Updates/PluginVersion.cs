namespace UnSetBim.Core.Updates;

/// <summary>
/// Numeric version comparison compatible with the server (PHP version_compare on plain
/// numeric versions, 1 to 4 parts, optional "v" prefix, no pre-release suffixes).
/// </summary>
public static class PluginVersion
{
    public static Version? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim().TrimStart('v', 'V');
        var parts = text.Split('.');
        if (parts.Length is < 1 or > 4)
            return null;

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
                return null;
        }

        return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    /// <summary>True when <paramref name="candidate"/> is greater than <paramref name="current"/>. False if either is invalid.</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        var a = Parse(candidate);
        var b = Parse(current);
        return a is not null && b is not null && a > b;
    }
}
