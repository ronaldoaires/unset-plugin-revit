using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Configuration;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Core.Api;

/// <summary>
/// Typed client of the UnSet BIM API v1 (docs/api.md in ronaldoaires/unset).
/// One instance per Revit session. Thread-safe.
///
/// - Sends Accept: application/json and the plugin User-Agent on every call.
/// - Sends the bearer token only to the API host.
/// - Raises <see cref="SessionEnded"/> when an authenticated call answers 401 or 403 account_inactive,
///   so the session layer clears the stored token.
/// - Throws <see cref="ApiException"/> for API errors and <see cref="ApiConnectionException"/> when the
///   server cannot be reached.
/// </summary>
public sealed class UnSetApiClient : IDisposable
{
    private static readonly TimeSpan JsonTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly Uri _baseUrl;
    private readonly Func<string?> _tokenProvider;
    private readonly string _userAgent;

    /// <summary>The server rejected the stored token (401) or the account was deactivated (403 account_inactive).</summary>
    public event EventHandler<ApiException>? SessionEnded;

    public UnSetApiClient(PluginSettings settings, Func<string?> tokenProvider, int revitYear)
    {
        _baseUrl = settings.ApiBaseUrl;
        _tokenProvider = tokenProvider;
        _userAgent = $"UnSetBIM-Revit/{PluginInfo.Version} (Revit {revitYear})";

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true,
        };

        // Timeouts are applied per call (JSON calls are short, downloads are not).
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public Uri BaseUrl => _baseUrl;

    // ─────────────────────────────────────────────
    // Plugin updates (public)
    // ─────────────────────────────────────────────

    /// <summary>GET /plugin/latest?current=. Returns null on 503 update_info_unavailable.</summary>
    public async Task<PluginRelease?> GetLatestReleaseAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            var envelope = await GetJsonAsync<DataEnvelope<PluginRelease>>(
                $"plugin/latest?current={Uri.EscapeDataString(currentVersion)}", authenticated: false, ct);
            return envelope.Data;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 503)
        {
            Log.Info("Update info unavailable (503).");
            return null;
        }
    }

    // ─────────────────────────────────────────────
    // Authentication
    // ─────────────────────────────────────────────

    public Task<LoginResponse> LoginAsync(string email, string password, string deviceName, CancellationToken ct = default) =>
        PostJsonAsync<LoginResponse>("auth/login", new { email, password, device_name = deviceName }, authenticated: false, ct);

    public Task<DeviceAuthorization> StartDeviceAuthorizationAsync(string deviceName, CancellationToken ct = default) =>
        PostJsonAsync<DeviceAuthorization>("auth/device", new { device_name = deviceName }, authenticated: false, ct);

    /// <summary>One poll of POST /auth/device/token. Maps every documented answer to a status; never throws for them.</summary>
    public async Task<DevicePollResult> PollDeviceTokenAsync(string deviceCode, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "auth/device/token", authenticated: false);
        request.Content = CreateJsonContent(new { device_code = deviceCode });

        using var response = await SendAsync(request, ct, JsonTimeout);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            return new DevicePollResult
            {
                Status = DevicePollStatus.Approved,
                Login = Deserialize<LoginResponse>(json),
            };
        }

        var body = ApiException.TryParseBody(json);
        var retryAfter = ReadRetryAfter(response);
        var status = (int)response.StatusCode;

        return (status, body?.Code) switch
        {
            (400, ApiErrorCodes.AuthorizationPending) => new DevicePollResult { Status = DevicePollStatus.Pending },
            (429, _) => new DevicePollResult { Status = DevicePollStatus.SlowDown, RetryAfter = retryAfter },
            (403, ApiErrorCodes.AccessDenied) => new DevicePollResult { Status = DevicePollStatus.Denied, Message = body?.Message },
            (403, ApiErrorCodes.AccountInactive) => new DevicePollResult { Status = DevicePollStatus.AccountInactive, Message = body?.Message },
            (410, _) => new DevicePollResult { Status = DevicePollStatus.Expired, Message = body?.Message },
            _ => throw new ApiException(response.StatusCode, body, retryAfter),
        };
    }

    public async Task<MeData> GetMeAsync(CancellationToken ct = default)
    {
        var envelope = await GetJsonAsync<DataEnvelope<MeData>>("auth/me", authenticated: true, ct);
        return envelope.Data ?? throw new ApiException(HttpStatusCode.InternalServerError, null, null);
    }

    /// <summary>POST /auth/logout. Errors are ignored: the caller deletes the local token either way.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Post, "auth/logout", authenticated: true);
            using var response = await SendAsync(request, ct, JsonTimeout);
        }
        catch (Exception ex)
        {
            Log.Info($"Logout request failed (ignored): {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // Catalog
    // ─────────────────────────────────────────────

    public async Task<List<Category>> GetCategoriesAsync(CancellationToken ct = default) =>
        (await GetJsonAsync<DataEnvelope<List<Category>>>("categories", authenticated: true, ct)).Data ?? new();

    public async Task<List<Brand>> GetBrandsAsync(CancellationToken ct = default) =>
        (await GetJsonAsync<DataEnvelope<List<Brand>>>("brands", authenticated: true, ct)).Data ?? new();

    public Task<PagedResponse<FamilySummary>> GetFamiliesAsync(FamilyQuery query, CancellationToken ct = default) =>
        GetJsonAsync<PagedResponse<FamilySummary>>("families" + query.ToQueryString(), authenticated: true, ct);

    public async Task<FamilyDetail> GetFamilyAsync(string slug, CancellationToken ct = default)
    {
        var envelope = await GetJsonAsync<DataEnvelope<FamilyDetail>>($"families/{Uri.EscapeDataString(slug)}", authenticated: true, ct);
        return envelope.Data ?? throw new ApiException(HttpStatusCode.NotFound, null, null);
    }

    public async Task<bool> SetFavoriteAsync(string slug, bool favorite, CancellationToken ct = default)
    {
        using var request = CreateRequest(favorite ? HttpMethod.Post : HttpMethod.Delete,
            $"families/{Uri.EscapeDataString(slug)}/favorite", authenticated: true);
        using var response = await SendAndEnsureAsync(request, ct, JsonTimeout);
        var envelope = Deserialize<DataEnvelope<FavoriteState>>(await response.Content.ReadAsStringAsync(ct));
        return envelope.Data?.IsFavorite ?? favorite;
    }

    public Task<PagedResponse<FamilySummary>> GetFavoritesAsync(int page, int perPage, CancellationToken ct = default) =>
        GetJsonAsync<PagedResponse<FamilySummary>>($"me/favorites?page={Math.Max(page, 1)}&per_page={Math.Clamp(perPage, 1, 50)}", authenticated: true, ct);

    public Task<PagedResponse<FamilySummary>> GetDownloadHistoryAsync(int page, int perPage, CancellationToken ct = default) =>
        GetJsonAsync<PagedResponse<FamilySummary>>($"me/downloads?page={Math.Max(page, 1)}&per_page={Math.Clamp(perPage, 1, 50)}", authenticated: true, ct);

    // ─────────────────────────────────────────────
    // Downloads
    // ─────────────────────────────────────────────

    /// <summary>
    /// GET download.url and save the .rfa to <paramref name="destinationFile"/>.
    /// Written to a ".part" file first and moved into place only after the transfer completes.
    /// Throws <see cref="ApiException"/> with 403 access_required/quota_exceeded, 404, 429.
    /// </summary>
    public async Task DownloadFamilyAsync(string downloadUrl, string destinationFile, IProgress<double>? progress, CancellationToken ct)
    {
        var uri = new Uri(downloadUrl, UriKind.Absolute);

        // The token is only ever sent to the API host.
        if (!string.Equals(uri.Host, _baseUrl.Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unexpected download host: {uri.Host}");

        using var request = CreateRequest(HttpMethod.Get, uri.ToString(), authenticated: true);
        using var response = await SendAndEnsureAsync(request, ct, timeout: null);
        await SaveToFileAsync(response, destinationFile, progress, ct);
    }

    /// <summary>Download a public file (plugin installer). No token, no Accept: application/json.</summary>
    public async Task DownloadPublicFileAsync(Uri url, string destinationFile, IProgress<double>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(_userAgent);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Headers.Accept.ParseAdd("*/*");

        using var response = await SendAsync(request, ct, timeout: null);
        if (!response.IsSuccessStatusCode)
            throw new ApiException(response.StatusCode, null, null);

        await SaveToFileAsync(response, destinationFile, progress, ct);
    }

    /// <summary>Fetch a public image (thumbnail). Returns null on any failure.</summary>
    public async Task<byte[]?> TryGetImageBytesAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(_userAgent);
            request.Headers.Accept.ParseAdd("image/*");
            using var response = await SendAsync(request, ct, JsonTimeout);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    // ─────────────────────────────────────────────
    // Plumbing
    // ─────────────────────────────────────────────

    private async Task<T> GetJsonAsync<T>(string path, bool authenticated, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, path, authenticated);
        using var response = await SendAndEnsureAsync(request, ct, JsonTimeout);
        return Deserialize<T>(await response.Content.ReadAsStringAsync(ct));
    }

    private async Task<T> PostJsonAsync<T>(string path, object body, bool authenticated, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, path, authenticated);
        request.Content = CreateJsonContent(body);
        using var response = await SendAndEnsureAsync(request, ct, JsonTimeout);
        return Deserialize<T>(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// JSON body with a known Content-Length and a plain "application/json" content type.
    /// JsonContent.Create streams the body with chunked transfer encoding, which some hosting
    /// firewalls (ModSecurity/LiteSpeed) reject with 406 Not Acceptable.
    /// </summary>
    private static ByteArrayContent CreateJsonContent(object body)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string pathOrUrl, bool authenticated)
    {
        var uri = Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute) && absolute.Scheme.StartsWith("http")
            ? absolute
            : new Uri(_baseUrl, pathOrUrl);

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(_userAgent);

        if (authenticated)
        {
            var token = _tokenProvider();
            if (string.IsNullOrEmpty(token))
                throw new ApiException(HttpStatusCode.Unauthorized, null, null);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct, TimeSpan? timeout)
    {
        using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        using var linked = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            // Headers only: bodies are read by the caller (allows streaming downloads).
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new ApiConnectionException(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiConnectionException(ex);
        }
    }

    private async Task<HttpResponseMessage> SendAndEnsureAsync(HttpRequestMessage request, CancellationToken ct, TimeSpan? timeout)
    {
        var sentToken = request.Headers.Authorization is not null;
        var response = await SendAsync(request, ct, timeout);

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            var json = await SafeReadAsync(response, ct);
            var error = new ApiException(response.StatusCode, ApiException.TryParseBody(json), ReadRetryAfter(response));

            Log.Warn($"{request.Method} {request.RequestUri?.AbsolutePath} -> {error.Status} {error.Code}"
                     + (error.Code is null ? $" | {response.Content.Headers.ContentType} | {Snippet(json)}" : string.Empty));

            if (sentToken && error.EndsSession)
                SessionEnded?.Invoke(this, error);

            throw error;
        }
    }

    private static async Task SaveToFileAsync(HttpResponseMessage response, string destinationFile, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        var partFile = destinationFile + ".part";
        var total = response.Content.Headers.ContentLength;

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    received += read;

                    if (total is > 0)
                        progress?.Report((double)received / total.Value);
                }

                if (total.HasValue && received != total.Value)
                    throw new IOException($"Incomplete transfer: {received} of {total} bytes.");
            }

            File.Move(partFile, destinationFile, overwrite: true);
            progress?.Report(1d);
        }
        catch (HttpRequestException ex)
        {
            TryDelete(partFile);
            throw new ApiConnectionException(ex);
        }
        catch
        {
            TryDelete(partFile);
            throw;
        }
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, ApiJson.Options)
                   ?? throw new JsonException("Empty body.");
        }
        catch (JsonException ex)
        {
            Log.Error($"Invalid JSON for {typeof(T).Name}.", ex);
            throw new ApiException(HttpStatusCode.InternalServerError, null, null);
        }
    }

    private static async Task<string?> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    // First characters of an error body, for the log (error bodies never carry tokens).
    private static string Snippet(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(empty body)";

        var flat = body.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 300 ? flat : flat[..300] + "...";
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta;
        if (header?.Date is { } date)
            return date - DateTimeOffset.UtcNow;
        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose() => _http.Dispose();
}
