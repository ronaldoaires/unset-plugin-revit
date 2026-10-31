using System.Diagnostics;
using System.IO;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Framework;

/// <summary>Opens a URL in the user's default browser. Only http(s) URLs are accepted.</summary>
public static class Browser
{
    public static bool Open(Uri url) => Open(url.ToString());

    public static bool Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            Log.Warn($"Refused to open URL: {url}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open the browser for {uri.Host}.", ex);
            return false;
        }
    }

    public static void OpenFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the folder.", ex);
        }
    }
}
