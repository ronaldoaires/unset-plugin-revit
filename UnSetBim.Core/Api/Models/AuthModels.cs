namespace UnSetBim.Core.Api.Models;

// DTOs of the authentication endpoints (docs/api.md, sections 3 and 4).
// JSON names are snake_case and mapped by ApiJson.Options (no attributes needed).

public sealed class ApiUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

/// <summary>The "access" block: what the current subscription grants.</summary>
public sealed class AccessInfo
{
    public bool HasAccess { get; set; }
    public List<string> Modules { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public SubscriptionInfo? Subscription { get; set; }
    public QuotaInfo? Quota { get; set; }

    public static AccessInfo None => new();

    /// <summary>Known keys: "architectural", "structural". Gates module tools, never families.</summary>
    public bool HasModule(string moduleKey) =>
        HasAccess && Modules.Any(m => string.Equals(m, moduleKey, StringComparison.OrdinalIgnoreCase));

    public bool HasPermission(string permission) =>
        HasAccess && Permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
}

public sealed class SubscriptionInfo
{
    /// <summary>active, trial, canceled, past_due, expired, pending.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Portuguese label ready to show.</summary>
    public string StatusLabel { get; set; } = string.Empty;

    /// <summary>monthly or yearly.</summary>
    public string BillingCycle { get; set; } = string.Empty;

    public PlanRef? Plan { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateTimeOffset? RenewsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class PlanRef
{
    public string? Slug { get; set; }
    public string? Name { get; set; }
}

public sealed class QuotaInfo
{
    /// <summary>Paid families per calendar month; null = unlimited.</summary>
    public int? Limit { get; set; }

    public int Used { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }

    public bool IsUnlimited => Limit is null;
}

/// <summary>Body of POST /auth/login and of an approved POST /auth/device/token.</summary>
public sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTimeOffset? ExpiresAt { get; set; }
    public ApiUser User { get; set; } = new();
    public AccessInfo Access { get; set; } = new();
}

/// <summary>"data" of GET /auth/me: user fields and the access block merged.</summary>
public sealed class MeData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public AccessInfo Access { get; set; } = new();

    public ApiUser ToUser() => new() { Id = Id, Name = Name, Email = Email, AvatarUrl = AvatarUrl };
}

/// <summary>Body of POST /auth/device.</summary>
public sealed class DeviceAuthorization
{
    /// <summary>Secret. Keep in memory only; never show or log it.</summary>
    public string DeviceCode { get; set; } = string.Empty;

    public string UserCode { get; set; } = string.Empty;
    public string VerificationUri { get; set; } = string.Empty;
    public string VerificationUriComplete { get; set; } = string.Empty;
    public int ExpiresIn { get; set; } = 600;
    public int Interval { get; set; } = 5;
}

public enum DevicePollStatus
{
    /// <summary>400 authorization_pending: keep polling.</summary>
    Pending,

    /// <summary>429 slow_down: wait <see cref="DevicePollResult.RetryAfter"/> then keep polling.</summary>
    SlowDown,

    /// <summary>200: token issued.</summary>
    Approved,

    /// <summary>403 access_denied: the user clicked "Negar".</summary>
    Denied,

    /// <summary>403 account_inactive.</summary>
    AccountInactive,

    /// <summary>410 expired_token: expired, already used or unknown.</summary>
    Expired,
}

public sealed class DevicePollResult
{
    public DevicePollStatus Status { get; init; }
    public LoginResponse? Login { get; init; }
    public string? Message { get; init; }
    public TimeSpan? RetryAfter { get; init; }
}
