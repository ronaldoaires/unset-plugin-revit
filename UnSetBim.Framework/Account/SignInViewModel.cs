using System.Windows.Input;
using System.Windows.Threading;
using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Hosting;
using UnSetBim.UI.Mvvm;

namespace UnSetBim.Framework.Account;

public enum SignInStep
{
    Choose,
    Browser,
    Password,
}

/// <summary>
/// Sign-in window logic. Two ways in (docs/api.md 3.1 and 3.4):
///  - browser (device authorization): recommended, works for Google-only accounts, password never typed in Revit;
///  - e-mail and password.
/// Account creation and password reset open the site.
/// </summary>
public sealed class SignInViewModel : ObservableObject, IDisposable
{
    private readonly UnSetHost _host;
    private readonly DispatcherTimer _countdown;
    private CancellationTokenSource? _browserCts;
    private DeviceAuthorization? _authorization;
    private DateTimeOffset _expiresAt;

    private SignInStep _step = SignInStep.Choose;
    private string _email = string.Empty;
    private string _errorMessage = string.Empty;
    private string _infoMessage = string.Empty;
    private string _userCode = string.Empty;
    private string _countdownText = string.Empty;
    private bool _isBusy;

    public SignInViewModel(UnSetHost host)
    {
        _host = host;

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) => UpdateCountdown();

        InfoMessage = host.Session.LastSignOutReason ?? string.Empty;
        host.Session.ClearSignOutReason();

        BrowserSignInCommand = new AsyncRelayCommand(StartBrowserSignInAsync, () => !IsBusy);
        ReopenBrowserCommand = new RelayCommand(() => Browser.Open(_authorization?.VerificationUriComplete), () => _authorization is not null);
        CancelBrowserCommand = new RelayCommand(CancelBrowser);
        ShowPasswordCommand = new RelayCommand(() => GoTo(SignInStep.Password));
        BackCommand = new RelayCommand(() => GoTo(SignInStep.Choose));
        PasswordSignInCommand = new AsyncRelayCommand(SignInWithPasswordAsync, () => !IsBusy);
        RegisterCommand = new RelayCommand(() => Browser.Open(host.Links.Register));
        ForgotPasswordCommand = new RelayCommand(() => Browser.Open(host.Links.ForgotPassword));
    }

    /// <summary>Raised on the UI thread when the session is signed in.</summary>
    public event EventHandler? SignedIn;

    /// <summary>Set by the view from the PasswordBox (it does not support binding).</summary>
    public string Password { private get; set; } = string.Empty;

    public ICommand BrowserSignInCommand { get; }
    public ICommand ReopenBrowserCommand { get; }
    public ICommand CancelBrowserCommand { get; }
    public ICommand ShowPasswordCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand PasswordSignInCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand ForgotPasswordCommand { get; }

    public SignInStep Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, value))
            {
                OnPropertyChanged(nameof(IsChooseStep));
                OnPropertyChanged(nameof(IsBrowserStep));
                OnPropertyChanged(nameof(IsPasswordStep));
            }
        }
    }

    public bool IsChooseStep => Step == SignInStep.Choose;
    public bool IsBrowserStep => Step == SignInStep.Browser;
    public bool IsPasswordStep => Step == SignInStep.Password;

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string InfoMessage
    {
        get => _infoMessage;
        private set => SetProperty(ref _infoMessage, value);
    }

    public string UserCode
    {
        get => _userCode;
        private set => SetProperty(ref _userCode, value);
    }

    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    // ─────────────────────────────────────────────
    // Browser sign-in (device authorization)
    // ─────────────────────────────────────────────

    private async Task StartBrowserSignInAsync()
    {
        ErrorMessage = string.Empty;
        InfoMessage = string.Empty;
        IsBusy = true;

        CancelBrowserPolling();
        _browserCts = new CancellationTokenSource();
        var ct = _browserCts.Token;

        try
        {
            _authorization = await _host.Session.StartBrowserSignInAsync(ct);
            UserCode = _authorization.UserCode;
            _expiresAt = DateTimeOffset.Now.AddSeconds(_authorization.ExpiresIn);
            UpdateCountdown();
            _countdown.Start();

            GoTo(SignInStep.Browser);
            Browser.Open(_authorization.VerificationUriComplete);

            var result = await _host.Session.CompleteBrowserSignInAsync(_authorization, ct);
            HandleBrowserResult(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled by the user or by closing the window.
        }
        catch (ApiException ex)
        {
            ShowError(ex.UserMessage);
        }
        catch (ApiConnectionException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Browser sign-in failed.", ex);
            ShowError("Não foi possível entrar pelo navegador. Tente novamente.");
        }
        finally
        {
            _countdown.Stop();
            IsBusy = false;
        }
    }

    private void HandleBrowserResult(DevicePollResult result)
    {
        switch (result.Status)
        {
            case DevicePollStatus.Approved:
                SignedIn?.Invoke(this, EventArgs.Empty);
                break;

            case DevicePollStatus.Denied:
                ShowError(result.Message ?? "O acesso foi negado no site.");
                break;

            case DevicePollStatus.AccountInactive:
                ShowError(result.Message ?? "Sua conta está desativada. Entre em contato com o suporte.");
                break;

            default:
                ShowError("O código expirou. Clique em \"Entrar pelo navegador\" para gerar um novo.");
                break;
        }
    }

    private void CancelBrowser()
    {
        CancelBrowserPolling();
        GoTo(SignInStep.Choose);
    }

    private void CancelBrowserPolling()
    {
        _browserCts?.Cancel();
        _browserCts?.Dispose();
        _browserCts = null;
        _authorization = null;
        UserCode = string.Empty;
    }

    private void UpdateCountdown()
    {
        var left = _expiresAt - DateTimeOffset.Now;
        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;

        CountdownText = $"O código expira em {left:mm\\:ss}";
    }

    // ─────────────────────────────────────────────
    // E-mail and password
    // ─────────────────────────────────────────────

    private async Task SignInWithPasswordAsync()
    {
        ErrorMessage = string.Empty;
        InfoMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Preencha o e-mail e a senha.";
            return;
        }

        IsBusy = true;
        try
        {
            await _host.Session.SignInWithPasswordAsync(Email, Password);
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException ex)
        {
            // 422 wrong credentials, 403 account_inactive / email_not_verified, 429 too_many_attempts:
            // the server's Portuguese message explains each case.
            ErrorMessage = ex.UserMessage;
        }
        catch (ApiConnectionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error("Password sign-in failed.", ex);
            ErrorMessage = "Não foi possível entrar. Tente novamente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─────────────────────────────────────────────

    private void GoTo(SignInStep step)
    {
        ErrorMessage = string.Empty;
        Step = step;
    }

    private void ShowError(string message)
    {
        CancelBrowserPolling();
        Step = SignInStep.Choose;
        ErrorMessage = message;
    }

    public void Dispose()
    {
        _countdown.Stop();
        CancelBrowserPolling();
    }
}
