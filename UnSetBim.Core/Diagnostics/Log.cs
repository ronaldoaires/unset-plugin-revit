using System.Text;
using UnSetBim.Core.Storage;

namespace UnSetBim.Core.Diagnostics;

/// <summary>
/// Minimal file logger: one file per day in %LocalAppData%\UnSetBIM\Logs, last 14 days kept.
/// Never throws: logging must not break Revit.
/// Never log tokens, device codes or passwords.
/// </summary>
public static class Log
{
    private const int RetentionDays = 14;
    private static readonly object Sync = new();
    private static bool _cleaned;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);

            if (ex is not null)
                line.AppendLine().Append(ex);

            System.Diagnostics.Debug.WriteLine("[UnSet BIM] " + line);

            lock (Sync)
            {
                Directory.CreateDirectory(PluginPaths.LogsFolder);
                var file = Path.Combine(PluginPaths.LogsFolder, $"unsetbim-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, line.AppendLine().ToString(), Encoding.UTF8);

                if (!_cleaned)
                {
                    _cleaned = true;
                    CleanOldFiles();
                }
            }
        }
        catch
        {
            // Swallow: logging is best effort.
        }
    }

    private static void CleanOldFiles()
    {
        var limit = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(PluginPaths.LogsFolder, "unsetbim-*.log"))
        {
            if (File.GetLastWriteTime(file) < limit)
                File.Delete(file);
        }
    }
}
