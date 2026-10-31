using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;

namespace UnSetBim.UI.Controls;

/// <summary>
/// Attached property that loads an image from a URL asynchronously, with memory and disk cache:
/// <code>&lt;Image ui:RemoteImage.Url="{Binding ThumbnailUrl}" ui:RemoteImage.DecodeWidth="360"/&gt;</code>
///
/// Downloading and decoding happen off the UI thread, so scrolling the catalog never blocks Revit.
/// Images that cannot be decoded (e.g. WebP on a Windows without the WebP codec) simply stay
/// empty and the placeholder behind them shows.
/// </summary>
public static class RemoteImage
{
    private const int MaxMemoryEntries = 400;
    private static readonly ConcurrentDictionary<string, BitmapSource> Memory = new();

    /// <summary>HTTP fetcher, set once by the host (uses the API client's HttpClient).</summary>
    public static Func<Uri, CancellationToken, Task<byte[]?>>? Fetcher { get; set; }

    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached(
        "Url", typeof(string), typeof(RemoteImage), new PropertyMetadata(null, OnUrlChanged));

    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.RegisterAttached(
        "DecodeWidth", typeof(int), typeof(RemoteImage), new PropertyMetadata(0));

    // Identifies the latest request of an Image, so a slow old response never overwrites a newer one.
    private static readonly DependencyProperty RequestProperty = DependencyProperty.RegisterAttached(
        "Request", typeof(object), typeof(RemoteImage), new PropertyMetadata(null));

    public static string? GetUrl(DependencyObject d) => (string?)d.GetValue(UrlProperty);
    public static void SetUrl(DependencyObject d, string? value) => d.SetValue(UrlProperty, value);
    public static int GetDecodeWidth(DependencyObject d) => (int)d.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(DependencyObject d, int value) => d.SetValue(DecodeWidthProperty, value);

    private static void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image)
            return;

        image.Source = null;
        var url = e.NewValue as string;
        var request = new object();
        image.SetValue(RequestProperty, request);

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        var decodeWidth = GetDecodeWidth(image);
        var key = $"{decodeWidth}|{url}";

        if (Memory.TryGetValue(key, out var cached))
        {
            image.Source = cached;
            return;
        }

        _ = LoadAsync(image, uri, key, decodeWidth, request);
    }

    private static async Task LoadAsync(Image image, Uri uri, string key, int decodeWidth, object request)
    {
        try
        {
            var bitmap = await Task.Run(async () =>
            {
                var bytes = await ReadOrFetchAsync(uri);
                return bytes is null ? null : Decode(bytes, decodeWidth);
            });

            if (bitmap is null)
                return;

            if (Memory.Count > MaxMemoryEntries)
                Memory.Clear();
            Memory[key] = bitmap;

            await image.Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(image.GetValue(RequestProperty), request))
                    image.Source = bitmap;
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Image load failed: {uri}", ex);
        }
    }

    private static async Task<byte[]?> ReadOrFetchAsync(Uri uri)
    {
        var file = Path.Combine(PluginPaths.ImageCacheFolder, HashName(uri.AbsoluteUri));

        if (File.Exists(file))
        {
            try
            {
                return await File.ReadAllBytesAsync(file);
            }
            catch (IOException)
            {
                // Concurrent write of the same image: fetch again.
            }
        }

        var fetcher = Fetcher;
        if (fetcher is null)
            return null;

        var bytes = await fetcher(uri, CancellationToken.None);
        if (bytes is { Length: > 0 })
        {
            try
            {
                Directory.CreateDirectory(PluginPaths.ImageCacheFolder);
                await File.WriteAllBytesAsync(file, bytes);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }

        return bytes;
    }

    private static BitmapSource? Decode(byte[] bytes, int decodeWidth)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0)
                bitmap.DecodePixelWidth = decodeWidth;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            // Typically a WebP image without the Windows WebP codec installed.
            return null;
        }
    }

    private static string HashName(string url)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(hash).ToLowerInvariant() + ".img";
    }

    /// <summary>Remove disk cache files older than <paramref name="days"/>.</summary>
    public static void PruneDiskCache(int days = 14)
    {
        try
        {
            if (!Directory.Exists(PluginPaths.ImageCacheFolder))
                return;

            var limit = DateTime.Now.AddDays(-days);
            foreach (var file in Directory.EnumerateFiles(PluginPaths.ImageCacheFolder, "*.img"))
            {
                if (File.GetLastAccessTime(file) < limit && File.GetLastWriteTime(file) < limit)
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Image cache prune failed.", ex);
        }
    }
}
