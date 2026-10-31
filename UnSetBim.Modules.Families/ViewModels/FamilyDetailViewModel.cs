using System.Globalization;
using System.Windows.Input;
using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework;
using UnSetBim.Framework.Hosting;
using UnSetBim.Modules.Families.RevitApi;
using UnSetBim.Modules.Families.Services;
using UnSetBim.UI.Mvvm;

namespace UnSetBim.Modules.Families.ViewModels;

/// <summary>
/// Right-hand panel: GET /families/{slug} and the download decision.
/// "Inserir no projeto" is enabled only when download.allowed is true (server decision);
/// otherwise the panel explains why and offers the purchase page or the plans.
/// </summary>
public sealed class FamilyDetailViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly UnSetHost _host;
    private readonly FamilyInserter _inserter;
    private readonly Func<FamilyCardViewModel, Task> _toggleFavorite;
    private readonly CancellationTokenSource _cts = new();

    private FamilyDetail? _detail;
    private bool _isLoading;
    private bool _isInserting;
    private double _progress;
    private string _loadError = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;

    internal FamilyDetailViewModel(UnSetHost host, FamilyCardViewModel card, Func<FamilyCardViewModel, Task> toggleFavorite)
    {
        _host = host;
        _inserter = new FamilyInserter(host);
        _toggleFavorite = toggleFavorite;
        Card = card;

        InsertCommand = new AsyncRelayCommand(InsertAsync, () => CanInsert);
        PurchaseCommand = new RelayCommand(() => Browser.Open(_detail?.Download.PurchaseUrl), () => HasPurchaseUrl);
        PlansCommand = new RelayCommand(() => Browser.Open(_host.Links.Plans));
        OpenPageCommand = new RelayCommand(() => Browser.Open(_detail?.PageUrl), () => _detail?.PageUrl is not null);
        ToggleFavoriteCommand = new AsyncRelayCommand(() => _toggleFavorite(Card));
        RetryCommand = new AsyncRelayCommand(LoadAsync);
    }

    /// <summary>The user can now click in the view to place the family.</summary>
    public event EventHandler? PlacementStarted;

    public FamilyCardViewModel Card { get; }

    public ICommand InsertCommand { get; }
    public ICommand PurchaseCommand { get; }
    public ICommand PlansCommand { get; }
    public ICommand OpenPageCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand RetryCommand { get; }

    // ── Loading ──────────────────────────────────────────

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string LoadError
    {
        get => _loadError;
        private set => SetProperty(ref _loadError, value);
    }

    public bool IsLoaded => _detail is not null;

    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = string.Empty;

        try
        {
            _detail = await _host.Api.GetFamilyAsync(Card.Slug, _cts.Token);
            Card.IsFavorite = _detail.IsFavorite;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ApiException ex)
        {
            LoadError = ex.IsNotFound ? "Esta família não está mais disponível." : ex.UserMessage;
        }
        catch (ApiConnectionException ex)
        {
            LoadError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error($"Family detail failed: {Card.Slug}", ex);
            LoadError = "Não foi possível carregar os detalhes da família.";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(string.Empty);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // ── Display ──────────────────────────────────────────

    public string Name => _detail?.Name ?? Card.Name;

    public string? ImageUrl => _detail?.ImageUrl ?? Card.ThumbnailUrl;

    public string CategoryText => Card.CategoryText;

    public string? Description => _detail?.Description;

    public string TagsText => _detail?.Tags is { Count: > 0 } tags ? string.Join("  ·  ", tags) : string.Empty;

    public string BrandText => _detail?.Brand?.Name ?? "—";

    public string RevitVersionText => Card.RevitVersionText;

    public string ParametricText => Card.IsParametric ? "Sim" : "Não";

    public string SizeText => _detail?.File?.FormattedSize ?? "—";

    public string DownloadCountText => _detail is null ? "—" : _detail.DownloadCount.ToString("N0", PtBr);


    public string PriceText => Card.PriceText;

    public bool IsFavorite => Card.IsFavorite;

    // ── Download decision ────────────────────────────────

    private DownloadDecision? Decision => _detail?.Download;

    public bool HasFile => _detail?.File is not null;

    public bool CanInsert => IsLoaded && !IsInserting && Card.IsCompatible && HasFile && Decision?.Allowed == true;

    /// <summary>Why "Inserir" is disabled, in Portuguese. Empty when it is enabled.</summary>
    public string BlockedReason
    {
        get
        {
            if (!IsLoaded)
                return string.Empty;

            if (!Card.IsCompatible)
                return $"Esta família requer o Revit {_detail!.RevitVersion} ou superior. Você está usando o Revit {_host.RevitYear}.";

            if (!HasFile)
                return "Esta família está sem arquivo para download no momento.";

            if (Decision is { Allowed: false } d)
            {
                var message = d.Message ?? "Download não disponível para a sua conta.";
                if (d.Code == ApiErrorCodes.QuotaExceeded && _host.Session.Access.Quota?.ResetsAt is { } reset)
                    message += $" O limite renova em {reset.ToLocalTime().ToString("dd/MM/yyyy", PtBr)}.";
                return message;
            }

            return string.Empty;
        }
    }

    public bool HasPurchaseUrl => Decision is { Allowed: false, PurchaseUrl: { Length: > 0 } };

    /// <summary>"Comprar por R$ 19,90".</summary>
    public string PurchaseText => _detail?.Price is > 0 ? $"Comprar por {Card.PriceText}" : "Comprar no site";

    /// <summary>The insert button only shows when the server allows the download (or while it runs).</summary>
    public bool ShowInsert => IsInserting || (IsLoaded && Decision?.Allowed == true && Card.IsCompatible && HasFile);

    /// <summary>Show "Ver planos" when the family requires a subscription.</summary>
    public bool ShowPlans => Decision is { Allowed: false, Code: ApiErrorCodes.AccessRequired };

    /// <summary>Subscribing is the main action when the family cannot be bought separately.</summary>
    public bool ShowPlansPrimary => ShowPlans && !HasPurchaseUrl;

    /// <summary>Subscribing as an alternative below the purchase button.</summary>
    public bool ShowPlansSecondary => ShowPlans && HasPurchaseUrl;

    // ── Insert ───────────────────────────────────────────

    public bool IsInserting
    {
        get => _isInserting;
        private set
        {
            if (SetProperty(ref _isInserting, value))
            {
                OnPropertyChanged(nameof(CanInsert));
                OnPropertyChanged(nameof(ShowInsert));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private async Task InsertAsync()
    {
        if (_detail is null || !CanInsert)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        Progress = 0;
        IsInserting = true;

        try
        {
            var progress = new Progress<double>(p => Progress = p * 100);
            var result = await _inserter.InsertAsync(_detail, progress, _cts.Token);

            switch (result.Outcome)
            {
                case PlacementOutcome.PlacementStarted:
                    StatusMessage = "Família carregada. Clique na vista para posicioná-la.";
                    PlacementStarted?.Invoke(this, EventArgs.Empty);
                    break;

                case PlacementOutcome.LoadedOnly:
                    StatusMessage = result.Message ?? "Família carregada no projeto.";
                    break;

                case PlacementOutcome.NoDocument:
                    ErrorMessage = "Abra um projeto ou uma família no Revit para inserir esta família.";
                    break;

                case PlacementOutcome.Cancelled:
                    break;

                default:
                    ErrorMessage = result.Message ?? "Não foi possível carregar a família no Revit.";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Panel closed.
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.UserMessage;

            // Quota may have run out between the detail and the download: refresh the decision.
            if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden && !ex.EndsSession)
                await LoadAsync();
        }
        catch (ApiConnectionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error($"Insert failed: {Card.Slug}", ex);
            ErrorMessage = "Não foi possível inserir a família. Tente novamente.";
        }
        finally
        {
            IsInserting = false;
        }
    }

    /// <summary>Called by the catalog after the favorite changed.</summary>
    internal void NotifyFavoriteChanged() => OnPropertyChanged(nameof(IsFavorite));

    /// <summary>Called by the catalog when the access block changed (quota text).</summary>
    internal void NotifyAccessChanged() => OnPropertyChanged(nameof(BlockedReason));

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
