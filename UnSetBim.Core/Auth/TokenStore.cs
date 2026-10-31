using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnSetBim.Core.Api;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;

namespace UnSetBim.Core.Auth;

/// <summary>Token persisted between Revit sessions.</summary>
public sealed class StoredToken
{
    public string Token { get; set; } = string.Empty;

    /// <summary>Null = never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt.HasValue && ExpiresAt.Value <= now;
}

/// <summary>
/// Stores the API token encrypted with DPAPI (current Windows user scope)
/// in %LocalAppData%\UnSetBIM\session.dat. Shared by every Revit year on the machine,
/// so signing in once signs in all of them.
/// </summary>
public static class TokenStore
{
    // Extra entropy so other apps of the same user cannot trivially reuse the blob.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("UnSetBim.Revit.Session.v1");

    public static void Save(StoredToken token)
    {
        try
        {
            Directory.CreateDirectory(PluginPaths.Root);
            var json = JsonSerializer.SerializeToUtf8Bytes(token, ApiJson.Options);
            var protectedBytes = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(PluginPaths.SessionFile, protectedBytes);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the session token.", ex);
        }
    }

    /// <summary>The stored token, or null if missing, unreadable or expired.</summary>
    public static StoredToken? Load()
    {
        try
        {
            if (!File.Exists(PluginPaths.SessionFile))
                return null;

            var protectedBytes = File.ReadAllBytes(PluginPaths.SessionFile);
            var json = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var token = JsonSerializer.Deserialize<StoredToken>(json, ApiJson.Options);

            if (token is null || string.IsNullOrWhiteSpace(token.Token))
                return null;

            if (token.IsExpired(DateTimeOffset.Now))
            {
                Log.Info("Stored token expired, discarding it.");
                Clear();
                return null;
            }

            return token;
        }
        catch (Exception ex)
        {
            // Corrupted file or a blob from another Windows user: start signed out.
            Log.Warn("Stored session could not be read, discarding it.", ex);
            Clear();
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(PluginPaths.SessionFile))
                File.Delete(PluginPaths.SessionFile);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not delete the session file.", ex);
        }
    }
}
