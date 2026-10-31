using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Auth;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Framework.Session;

/// <summary>
/// The signed-in user for this Revit session.
///
/// Holds the token (persisted with DPAPI by <see cref="TokenStore"/>), the user and the access block,
/// and is the only place that signs in, signs out or reacts to a revoked token.
///
/// <see cref="Changed"/> can be raised on any thread: UI subscribers must marshal to their dispatcher.
/// </summary>
public sealed class SessionManager
{
    private readonly object _sync = new();
    private UnSetApiClient? _api;
    private StoredToken? _token;
    private ApiUser? _user;
    private AccessInfo _access = AccessInfo.None;

    public SessionManager()
    {
        _token = TokenStore.Load();
    }

    /// <summary>Raised after sign-in, sign-out, access refresh or when the server ends the session.</summary>
    public event EventHandler? Changed;

    /// <summary>Called once by the host after the API client exists.</summary>
    internal void Attach(UnSetApiClient api)
    {
        _api = api;
        _api.SessionEnded += OnSessionEnded;
    }

    private UnSetApiClient Api => _api ?? throw new InvalidOperationException("SessionManager not attached.");

    public string? Token
    {
        get
        {
            lock (_sync)
                return _token?.Token;
        }
    }

    /// <summary>A token exists locally (it may still be rejected by the server).</summary>
    public bool HasStoredToken
    {
        get
        {
            lock (_sync)
                return _token is not null;
        }
    }

    /// <summary>The token was confirmed by the server during this Revit session.</summary>
    public bool IsSignedIn
    {
        get
        {
            lock (_sync)
                return _token is not null && _user is not null;
        }
    }

    public ApiUser? User
    {
        get
        {
            lock (_sync)
                return _user;
        }
    }

    public AccessInfo Access
    {
        get
        {
            lock (_sync)
                return _access;
        }
    }

    public DateTimeOffset? TokenExpiresAt
    {
        get
        {
            lock (_sync)
                return _token?.ExpiresAt;
        }
    }

    /// <summary>Server message of the last forced sign-out (e.g. account deactivated). Shown once on the sign-in window.</summary>
    public string? LastSignOutReason { get; private set; }

    /// <summary>
    /// Validate the stored token with GET /auth/me.
    /// Returns false when there is no token or the server rejected it.
    /// Throws <see cref="ApiConnectionException"/> when the server cannot be reached (token kept).
    /// </summary>
    public async Task<bool> RestoreAsync(CancellationToken ct = default)
    {
        if (!HasStoredToken)
            return false;

        try
        {
            var me = await Api.GetMeAsync(ct);
            lock (_sync)
            {
                _user = me.ToUser();
                _access = me.Access ?? AccessInfo.None;
            }

            Log.Info($"Session restored (user {me.Id}).");
            RaiseChanged();
            return true;
        }
        catch (ApiException ex) when (ex.EndsSession)
        {
            // OnSessionEnded already cleared the token.
            return false;
        }
    }

    public async Task SignInWithPasswordAsync(string email, string password, CancellationToken ct = default)
    {
        var login = await Api.LoginAsync(email.Trim(), password, DeviceIdentity.DeviceName, ct);
        Apply(login);
    }

    public Task<DeviceAuthorization> StartBrowserSignInAsync(CancellationToken ct = default) =>
        Api.StartDeviceAuthorizationAsync(DeviceIdentity.DeviceName, ct);

    /// <summary>
    /// Poll POST /auth/device/token every interval until a final answer.
    /// Returns the final result; on <see cref="DevicePollStatus.Approved"/> the session is already signed in.
    /// </summary>
    public async Task<DevicePollResult> CompleteBrowserSignInAsync(DeviceAuthorization authorization, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(authorization.Interval, 1));
        var deadline = DateTimeOffset.Now.AddSeconds(Math.Max(authorization.ExpiresIn, 30));

        while (DateTimeOffset.Now < deadline)
        {
            await Task.Delay(interval, ct);

            DevicePollResult result;
            try
            {
                result = await Api.PollDeviceTokenAsync(authorization.DeviceCode, ct);
            }
            catch (ApiConnectionException)
            {
                // Temporary network failure: keep trying until the code expires.
                continue;
            }

            switch (result.Status)
            {
                case DevicePollStatus.Pending:
                    continue;

                case DevicePollStatus.SlowDown:
                    // RFC 8628: increase the interval by 5 seconds (or what the server asks).
                    interval = result.RetryAfter is { TotalSeconds: > 0 } retry ? retry : interval + TimeSpan.FromSeconds(5);
                    continue;

                case DevicePollStatus.Approved when result.Login is not null:
                    Apply(result.Login);
                    return result;

                default:
                    return result;
            }
        }

        return new DevicePollResult { Status = DevicePollStatus.Expired };
    }

    /// <summary>Refresh the access block (quota, plan). Failures are ignored.</summary>
    public async Task RefreshAccessAsync(CancellationToken ct = default)
    {
        if (!HasStoredToken)
            return;

        try
        {
            await RestoreAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Info($"Access refresh failed (ignored): {ex.Message}");
        }
    }

    /// <summary>POST /auth/logout, then delete the token locally regardless of the answer.</summary>
    public async Task SignOutAsync()
    {
        if (HasStoredToken)
            await Api.LogoutAsync();

        Clear(reason: null);
        Log.Info("Signed out.");
    }

    private void Apply(LoginResponse login)
    {
        var token = new StoredToken { Token = login.Token, ExpiresAt = login.ExpiresAt };
        TokenStore.Save(token);

        lock (_sync)
        {
            _token = token;
            _user = login.User;
            _access = login.Access ?? AccessInfo.None;
        }

        LastSignOutReason = null;
        Log.Info($"Signed in (user {login.User.Id}).");
        RaiseChanged();
    }

    private void OnSessionEnded(object? sender, ApiException error)
    {
        Log.Info($"Session ended by the server ({error.Status} {error.Code}).");
        Clear(error.IsAccountInactive ? error.UserMessage : "Sua sessão expirou. Entre novamente para continuar.");
    }

    private void Clear(string? reason)
    {
        TokenStore.Clear();

        bool hadSession;
        lock (_sync)
        {
            hadSession = _token is not null;
            _token = null;
            _user = null;
            _access = AccessInfo.None;
        }

        LastSignOutReason = reason;

        if (hadSession)
            RaiseChanged();
    }

    /// <summary>Called by the sign-in window after showing <see cref="LastSignOutReason"/>.</summary>
    public void ClearSignOutReason() => LastSignOutReason = null;

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error("Session Changed handler failed.", ex);
        }
    }
}
