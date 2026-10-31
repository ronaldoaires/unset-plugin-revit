using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using UnSetBim.Core;
using UnSetBim.Core.Api.Models;
using UnSetBim.Framework.Hosting;
using UnSetBim.Framework.Modules;
using UnSetBim.UI.Mvvm;

namespace UnSetBim.Framework.Account;

/// <summary>"Minha conta": who is signed in, the plan, the monthly quota and sign-out.</summary>
public sealed class AccountViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly UnSetHost _host;
    private readonly Dispatcher _dispatcher;
    private bool _isBusy;

    public AccountViewModel(UnSetHost host)
    {
        _host = host;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _host.Session.Changed += OnSessionChanged;

        ManageSubscriptionCommand = new RelayCommand(() => Browser.Open(host.Links.Subscription));
        PlansCommand = new RelayCommand(() => Browser.Open(host.Links.Plans));
        OpenAccountCommand = new RelayCommand(() => Browser.Open(host.Links.Account));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync, () => !IsBusy);
    }

    /// <summary>Raised after sign-out: the window closes.</summary>
    public event EventHandler? SignedOut;

    public ICommand ManageSubscriptionCommand { get; }
    public ICommand PlansCommand { get; }
    public ICommand OpenAccountCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SignOutCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private AccessInfo Access => _host.Session.Access;

    public string UserName => _host.Session.User?.Name ?? string.Empty;

    public string UserEmail => _host.Session.User?.Email ?? string.Empty;

    public bool HasAccess => Access.HasAccess;

    public bool HasNoAccess => !Access.HasAccess;

    public string PlanName => Access.Subscription?.Plan?.Name ?? (HasAccess ? "Plano ativo" : "Sem assinatura");

    public string StatusLabel => Access.Subscription?.StatusLabel ?? "Sem assinatura";

    /// <summary>Explains the subscription state in one line.</summary>
    public string SubscriptionDetail
    {
        get
        {
            var s = Access.Subscription;
            if (s is null)
                return "Famílias gratuitas e compradas continuam disponíveis. Assine para baixar as famílias exclusivas.";

            return s.Status switch
            {
                "trial" when s.TrialEndsAt is { } t => $"Período de teste até {Date(t)}.",
                "canceled" when s.ExpiresAt is { } e => $"Cancelada. Acesso até {Date(e)}.",
                "active" when s.RenewsAt is { } r => $"Renova em {Date(r)} ({Cycle(s.BillingCycle)}).",
                "past_due" => "Pagamento pendente. Regularize no site para manter o acesso.",
                "pending" => "Aguardando a confirmação do pagamento.",
                "expired" => "Assinatura expirada. Renove no site para voltar a baixar as famílias exclusivas.",
                _ => string.Empty,
            };
        }
    }

    public bool HasQuota => Access.Quota is not null;

    public string QuotaText
    {
        get
        {
            var q = Access.Quota;
            if (q is null)
                return string.Empty;

            return q.IsUnlimited
                ? $"Downloads ilimitados ({q.Used} famílias pagas neste mês)"
                : $"{q.Used} de {q.Limit} famílias pagas neste mês";
        }
    }

    public double QuotaPercent
    {
        get
        {
            var q = Access.Quota;
            if (q is null || q.IsUnlimited || q.Limit is not > 0)
                return 0;
            return Math.Min(100, 100.0 * q.Used / q.Limit.Value);
        }
    }

    public bool HasQuotaLimit => Access.Quota is { IsUnlimited: false };

    public string QuotaResetText => Access.Quota?.ResetsAt is { } r ? $"Renova em {Date(r)}" : string.Empty;

    public string ModulesText
    {
        get
        {
            if (!HasAccess || Access.Modules.Count == 0)
                return "Nenhum módulo liberado";

            return string.Join(", ", Access.Modules.Select(PlanModules.DisplayName));
        }
    }

    public string VersionText
    {
        get
        {
            var text = $"{PluginInfo.Name} {PluginInfo.Version} · Revit {_host.RevitYear}";
            if (_host.Settings.IsCustomEnvironment)
                text += $" · API: {_host.Settings.ApiBaseUrl}";
            return text;
        }
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            await _host.Session.RefreshAccessAsync();
            NotifyAll();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SignOutAsync()
    {
        IsBusy = true;
        try
        {
            await _host.Session.SignOutAsync();
            SignedOut?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Session events can come from any thread.
    private void OnSessionChanged(object? sender, EventArgs e) => _dispatcher.InvokeAsync(NotifyAll);

    private void NotifyAll() => OnPropertyChanged(string.Empty);

    private static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("dd/MM/yyyy", PtBr);

    private static string Cycle(string cycle) => cycle == "yearly" ? "anual" : "mensal";

    public void Dispose() => _host.Session.Changed -= OnSessionChanged;
}
