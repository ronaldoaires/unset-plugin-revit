using System.Net;
using System.Text.Json;
using UnSetBim.Core.Api.Models;

namespace UnSetBim.Core.Api;

/// <summary>Error codes of docs/api.md 9.4. Branch on HTTP status + code, never on the message text.</summary>
public static class ApiErrorCodes
{
    public const string AccountInactive = "account_inactive";
    public const string EmailNotVerified = "email_not_verified";
    public const string TooManyAttempts = "too_many_attempts";
    public const string AccessRequired = "access_required";
    public const string QuotaExceeded = "quota_exceeded";
    public const string UpdateInfoUnavailable = "update_info_unavailable";
    public const string AuthorizationPending = "authorization_pending";
    public const string SlowDown = "slow_down";
    public const string AccessDenied = "access_denied";
    public const string ExpiredToken = "expired_token";
}

/// <summary>
/// Non-2xx answer from the API. <see cref="UserMessage"/> is safe to show:
/// it is the server's Portuguese message (or the first validation error), or a generic fallback.
/// </summary>
public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? Code { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public TimeSpan? RetryAfter { get; }
    public string UserMessage { get; }

    public ApiException(HttpStatusCode statusCode, ApiErrorBody? body, TimeSpan? retryAfter)
        : base($"HTTP {(int)statusCode} {body?.Code ?? string.Empty}: {body?.Message}")
    {
        StatusCode = statusCode;
        Code = body?.Code;
        Errors = body?.Errors ?? new Dictionary<string, string[]>();
        RetryAfter = retryAfter;
        UserMessage = BuildUserMessage(statusCode, body, retryAfter);
    }

    public int Status => (int)StatusCode;
    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
    public bool IsAccountInactive => StatusCode == HttpStatusCode.Forbidden && Code == ApiErrorCodes.AccountInactive;
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    public bool IsValidation => (int)StatusCode == 422;
    public bool IsRateLimited => (int)StatusCode == 429;

    /// <summary>The session is gone: the client must clear the token and ask for sign-in.</summary>
    public bool EndsSession => IsUnauthorized || IsAccountInactive;

    private static string BuildUserMessage(HttpStatusCode status, ApiErrorBody? body, TimeSpan? retryAfter)
    {
        // 422: first validation message is the most specific one (e.g. wrong credentials).
        if (body?.Errors is { Count: > 0 })
        {
            foreach (var field in body.Errors.Values)
            {
                var first = field?.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
                if (first is not null)
                    return first;
            }
        }

        var code = (int)status;

        // 401's message is "Unauthenticated." in English: replace it.
        if (code == 401)
            return "Sua sessão expirou. Entre novamente na sua conta UnSet BIM.";

        // 404's message is not guaranteed by the contract.
        if (code == 404)
            return "Família indisponível.";

        // Generic 429 ("Too Many Attempts.") has no code and an English message.
        if (code == 429 && body?.Code is null)
        {
            var wait = retryAfter.HasValue ? $" Aguarde {Math.Ceiling(retryAfter.Value.TotalSeconds)} segundos." : string.Empty;
            return "Muitas requisições em pouco tempo." + wait;
        }

        if (!string.IsNullOrWhiteSpace(body?.Message) && code < 500)
            return body!.Message!;

        // No usable JSON body: usually a proxy/firewall answer, not the API. Show the status for support.
        return code >= 500
            ? $"O servidor do UnSet BIM está com problemas no momento (HTTP {code}). Tente novamente em instantes."
            : $"O servidor recusou a requisição (HTTP {code}). Tente novamente; se continuar, contate o suporte.";
    }

    internal static ApiErrorBody? TryParseBody(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ApiErrorBody>(json, ApiJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The server could not be reached (no network, DNS, TLS, timeout).</summary>
public sealed class ApiConnectionException : Exception
{
    public const string DefaultMessage = "Não foi possível conectar ao UnSet BIM. Verifique sua conexão com a internet e tente novamente.";

    public ApiConnectionException(Exception inner) : base(DefaultMessage, inner) { }
}
