using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;
using UnSetBim.Framework;
using UnSetBim.Framework.Hosting;
using UnSetBim.UI.Mvvm;

namespace UnSetBim.Modules.Families.ViewModels;

public enum CatalogTab
{
    Catalog,
    Favorites,
    Downloads,
}

/// <summary>
/// Families window: sidebar with categories, filters, paginated grid, detail modal,
/// and the "Favoritos" / "Meus downloads" lists (docs/api.md 5 and 7).
/// Every catalog request sends revit_version = running Revit year.
/// </summary>
public sealed class CatalogViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 24;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly UnSetHost _host;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _searchDebounce;
    private readonly List<Category> _categories = new();
    private CancellationTokenSource? _listCts;
    private bool _rebuildingNav;

    private CatalogTab _tab = CatalogTab.Catalog;
    private NavItem? _selectedNav;
    private string _search = string.Empty;
    private FilterOption<string?>? _selectedBrand;
    private FilterOption<bool?> _selectedPrice;
    private FilterOption<string> _selectedSort;
    private int _currentPage = 1;
    private int _lastPage = 1;
    private int _total;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private FamilyDetailViewModel? _detail;

    public CatalogViewModel(UnSetHost host)
    {
        _host = host;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ReloadFromFirstPageAsync();
        };

        PriceOptions = new[]
        {
            new FilterOption<bool?>("Todas", null),
            new FilterOption<bool?>("Gratuitas", true),
            new FilterOption<bool?>("Pagas", false),
        };
        _selectedPrice = PriceOptions[0];

        SortOptions = new[]
        {
            new FilterOption<string>("Mais recentes", FamilySort.Recent),
            new FilterOption<string>("Mais baixadas", FamilySort.Downloads),
            new FilterOption<string>("Mais vistas", FamilySort.Views),
            new FilterOption<string>("Nome (A–Z)", FamilySort.Name),
        };
        var savedSort = PluginState.Load().FamiliesSort;
        _selectedSort = SortOptions.FirstOrDefault(o => o.Value == savedSort) ?? SortOptions[0];

        NextPageCommand = new AsyncRelayCommand(() => LoadPageAsync(CurrentPage + 1), () => !IsLoading && CurrentPage < LastPage);
        PreviousPageCommand = new AsyncRelayCommand(() => LoadPageAsync(CurrentPage - 1), () => !IsLoading && CurrentPage > 1);
        RetryCommand = new AsyncRelayCommand(InitializeAsync);
        SelectFamilyCommand = new AsyncRelayCommand<FamilyCardViewModel>(SelectFamilyAsync);
        CardActionCommand = new AsyncRelayCommand<FamilyCardViewModel>(CardActionAsync);
        ToggleFavoriteCommand = new AsyncRelayCommand<FamilyCardViewModel>(ToggleFavoriteAsync);
        CloseDetailCommand = new RelayCommand(() => Detail = null);
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync);
        OpenSiteCommand = new RelayCommand(() => Browser.Open(host.Links.Families));

        _host.Session.Changed += OnSessionChanged;
    }

    /// <summary>The session ended (sign-out or revoked token): the window closes.</summary>
    public event EventHandler? SessionEnded;

    /// <summary>Placement started: the window minimizes so the user can click in the view.</summary>
    public event EventHandler? PlacementStarted;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    public ObservableCollection<FilterOption<string?>> BrandOptions { get; } = new();

    public IReadOnlyList<FilterOption<bool?>> PriceOptions { get; }

    public IReadOnlyList<FilterOption<string>> SortOptions { get; }

    public ObservableCollection<FamilyCardViewModel> Families { get; } = new();

    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand SelectFamilyCommand { get; }
    public ICommand CardActionCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand CloseDetailCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand OpenSiteCommand { get; }

    // ── Header ───────────────────────────────────────────

    public string UserName => _host.Session.User?.Name ?? string.Empty;

    public string QuotaText
    {
        get
        {
            var access = _host.Session.Access;
            if (!access.HasAccess)
                return "Sem assinatura · famílias gratuitas e compradas";

            var plan = access.Subscription?.Plan?.Name ?? "Assinatura";
            return access.Quota switch
            {
                null => plan,
                { IsUnlimited: true } => $"{plan} · downloads ilimitados",
                var q => $"{plan} · {q.Used}/{q.Limit} famílias pagas no mês",
            };
        }
    }

    public string RevitText => $"Mostrando famílias compatíveis com o Revit {_host.RevitYear}";

    // ── Tabs ─────────────────────────────────────────────

    public CatalogTab Tab
    {
        get => _tab;
        private set
        {
            if (SetProperty(ref _tab, value))
            {
                OnPropertyChanged(nameof(IsCatalogTab));
                OnPropertyChanged(nameof(IsFavoritesTab));
                OnPropertyChanged(nameof(IsDownloadsTab));
                OnPropertyChanged(nameof(ListTitle));
            }
        }
    }

    // Two-way bound to the tab radio buttons.
    public bool IsCatalogTab
    {
        get => Tab == CatalogTab.Catalog;
        set
        {
            if (value)
                _ = SetTabAsync(CatalogTab.Catalog);
        }
    }

    public bool IsFavoritesTab
    {
        get => Tab == CatalogTab.Favorites;
        set
        {
            if (value)
                _ = SetTabAsync(CatalogTab.Favorites);
        }
    }

    public bool IsDownloadsTab
    {
        get => Tab == CatalogTab.Downloads;
        set
        {
            if (value)
                _ = SetTabAsync(CatalogTab.Downloads);
        }
    }

    public string ListTitle => Tab switch
    {
        CatalogTab.Favorites => "Favoritos",
        CatalogTab.Downloads => "Meus downloads",
        _ => SelectedNav?.Label ?? "Todas as famílias",
    };

    private async Task SetTabAsync(CatalogTab tab)
    {
        if (Tab == tab)
            return;

        Tab = tab;
        Detail = null;
        await ReloadFromFirstPageAsync();
    }

    // ── Filters ──────────────────────────────────────────

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (_rebuildingNav || value is null || ReferenceEquals(value, _selectedNav))
                return;

            _selectedNav = value;
            OnPropertyChanged(nameof(ListTitle));

            // Rebuild the list (to show/hide subcategories) after the ListBox finished its selection change.
            _dispatcher.InvokeAsync(async () =>
            {
                RebuildNav();
                await ReloadFromFirstPageAsync();
            });
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
            {
                _searchDebounce.Stop();
                _searchDebounce.Start();
            }
        }
    }

    public FilterOption<string?>? SelectedBrand
    {
        get => _selectedBrand;
        set
        {
            if (value is not null && SetProperty(ref _selectedBrand, value))
                _ = ReloadFromFirstPageAsync();
        }
    }

    public FilterOption<bool?> SelectedPrice
    {
        get => _selectedPrice;
        set
        {
            if (value is not null && SetProperty(ref _selectedPrice, value))
                _ = ReloadFromFirstPageAsync();
        }
    }

    public FilterOption<string> SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (value is not null && SetProperty(ref _selectedSort, value))
            {
                PluginState.Update(s => s.FamiliesSort = value.Value);
                _ = ReloadFromFirstPageAsync();
            }
        }
    }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Search)
        || SelectedBrand?.Value is not null
        || SelectedPrice.Value is not null
        || SelectedNav?.CategorySlug is not null;

    private async Task ClearFiltersAsync()
    {
        _search = string.Empty;
        _selectedBrand = BrandOptions.FirstOrDefault();
        _selectedPrice = PriceOptions[0];
        _selectedNav = NavItems.FirstOrDefault();
        RebuildNav();
        OnPropertyChanged(nameof(Search));
        OnPropertyChanged(nameof(SelectedBrand));
        OnPropertyChanged(nameof(SelectedPrice));
        OnPropertyChanged(nameof(SelectedNav));
        OnPropertyChanged(nameof(ListTitle));
        await ReloadFromFirstPageAsync();
    }

    // ── List state ───────────────────────────────────────

    public int CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public int LastPage
    {
        get => _lastPage;
        private set => SetProperty(ref _lastPage, value);
    }

    public int Total
    {
        get => _total;
        private set
        {
            if (SetProperty(ref _total, value))
                OnPropertyChanged(nameof(TotalText));
        }
    }

    public string TotalText => Total == 1 ? "1 família" : $"{Total.ToString("N0", PtBr)} famílias";

    public string PageText => $"Página {CurrentPage} de {Math.Max(LastPage, 1)}";

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => !IsLoading && string.IsNullOrEmpty(ErrorMessage) && Families.Count == 0;

    public string EmptyTitle => Tab switch
    {
        CatalogTab.Favorites => "Nenhuma família favorita",
        CatalogTab.Downloads => "Você ainda não baixou famílias",
        _ => "Nenhuma família encontrada",
    };

    public string EmptyText => Tab switch
    {
        CatalogTab.Favorites => "Use o ♡ nos cards para guardar famílias aqui. A lista é a mesma do site.",
        CatalogTab.Downloads => "As famílias que você inserir aparecem aqui.",
        _ => HasActiveFilters ? "Tente outro termo de busca ou limpe os filtros." : "Ainda não há famílias para o seu Revit.",
    };

    public FamilyDetailViewModel? Detail
    {
        get => _detail;
        private set
        {
            var old = _detail;
            if (!SetProperty(ref _detail, value))
                return;

            if (old is not null)
            {
                old.PlacementStarted -= OnPlacementStarted;
                old.Card.IsSelected = false;
                old.Dispose();
            }

            if (value is not null)
            {
                value.PlacementStarted += OnPlacementStarted;
                value.Card.IsSelected = true;
            }

            OnPropertyChanged(nameof(HasDetail));
        }
    }

    public bool HasDetail => Detail is not null;

    // ── Loading ──────────────────────────────────────────

    public async Task InitializeAsync()
    {
        ErrorMessage = string.Empty;
        IsLoading = true;

        try
        {
            // Categories and brands once per window (docs/api.md 10, step 3).
            var categoriesTask = _host.Api.GetCategoriesAsync();
            var brandsTask = _host.Api.GetBrandsAsync();
            await Task.WhenAll(categoriesTask, brandsTask);

            _categories.Clear();
            _categories.AddRange(categoriesTask.Result);

            BrandOptions.Clear();
            BrandOptions.Add(new FilterOption<string?>("Todas as marcas", null));
            foreach (var brand in brandsTask.Result)
                BrandOptions.Add(new FilterOption<string?>(brand.Name, brand.Slug));
            _selectedBrand = BrandOptions[0];
            OnPropertyChanged(nameof(SelectedBrand));

            _selectedNav = null;
            RebuildNav();
        }
        catch (Exception ex) when (ex is ApiException or ApiConnectionException)
        {
            IsLoading = false;
            ErrorMessage = ex is ApiException api ? api.UserMessage : ex.Message;
            return;
        }

        await LoadPageAsync(1);
    }

    private Task ReloadFromFirstPageAsync() => LoadPageAsync(1);

    private async Task LoadPageAsync(int page)
    {
        _listCts?.Cancel();
        _listCts = new CancellationTokenSource();
        var ct = _listCts.Token;

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = Tab switch
            {
                CatalogTab.Favorites => await _host.Api.GetFavoritesAsync(page, PageSize, ct),
                CatalogTab.Downloads => await _host.Api.GetDownloadHistoryAsync(page, PageSize, ct),
                _ => await _host.Api.GetFamiliesAsync(BuildQuery(page), ct),
            };

            if (ct.IsCancellationRequested)
                return;

            Families.Clear();
            var hasAccess = _host.Session.Access.HasAccess;
            foreach (var family in result.Data)
                Families.Add(new FamilyCardViewModel(family, _host.RevitYear, hasAccess));

            CurrentPage = result.Meta.CurrentPage;
            LastPage = Math.Max(result.Meta.LastPage, 1);
            Total = result.Meta.Total;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (ApiException ex)
        {
            Families.Clear();
            ErrorMessage = ex.UserMessage;
        }
        catch (ApiConnectionException ex)
        {
            Families.Clear();
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error("Catalog page failed.", ex);
            Families.Clear();
            ErrorMessage = "Não foi possível carregar as famílias.";
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(PageText));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(EmptyTitle));
                OnPropertyChanged(nameof(EmptyText));
                OnPropertyChanged(nameof(HasActiveFilters));
                PageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Raised after a page is shown (the view scrolls back to the top).</summary>
    public event EventHandler? PageChanged;

    private FamilyQuery BuildQuery(int page) => new()
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search,
        Category = SelectedNav?.CategorySlug,
        Subcategory = SelectedNav?.SubcategorySlug,
        Brand = SelectedBrand?.Value,
        Free = SelectedPrice.Value,
        RevitVersion = _host.RevitYear,
        Sort = SelectedSort.Value,
        Page = page,
        PerPage = PageSize,
    };

    /// <summary>
    /// Sidebar = "Todas" + categories; the subcategories of the selected category are listed right below it.
    /// </summary>
    private void RebuildNav()
    {
        _rebuildingNav = true;
        try
        {
            var selectedCategory = _selectedNav?.CategorySlug;
            var selectedSub = _selectedNav?.SubcategorySlug;

            NavItems.Clear();
            var all = NavItem.All(_categories.Sum(c => c.FamiliesCount));
            NavItems.Add(all);
            NavItem? toSelect = selectedCategory is null ? all : null;

            foreach (var category in _categories)
            {
                var item = new NavItem { Label = category.Name, CategorySlug = category.Slug, Count = category.FamiliesCount };
                NavItems.Add(item);

                if (category.Slug != selectedCategory)
                    continue;

                if (selectedSub is null)
                    toSelect = item;

                foreach (var sub in category.Subcategories)
                {
                    var subItem = new NavItem { Label = sub.Name, CategorySlug = category.Slug, SubcategorySlug = sub.Slug };
                    NavItems.Add(subItem);
                    if (sub.Slug == selectedSub)
                        toSelect = subItem;
                }
            }

            _selectedNav = toSelect ?? all;
        }
        finally
        {
            _rebuildingNav = false;
        }

        OnPropertyChanged(nameof(SelectedNav));
    }

    // ── Detail and favorites ─────────────────────────────

    private async Task SelectFamilyAsync(FamilyCardViewModel card)
    {
        if (Detail?.Card == card)
            return;

        var detail = new FamilyDetailViewModel(_host, card, ToggleFavoriteAsync);
        Detail = detail;
        await detail.LoadAsync();
    }

    /// <summary>
    /// Card button. Loads the detail (server decision) and then: inserts when allowed, opens the purchase page
    /// when the family can be bought, otherwise opens the detail modal with the reason and the plans button.
    /// The modal also opens when the insert needs the user's attention (error, no open project, loaded only).
    /// </summary>
    private async Task CardActionAsync(FamilyCardViewModel card)
    {
        if (card is null || card.IsBusy || !card.IsCompatible)
            return;

        card.IsBusy = true;
        var detail = new FamilyDetailViewModel(_host, card, ToggleFavoriteAsync);
        var showModal = false;

        try
        {
            await detail.LoadAsync();

            if (detail.CanInsert)
            {
                card.IsUnlocked = true;
                detail.PlacementStarted += OnPlacementStarted;
                try
                {
                    await detail.InsertAsync();
                }
                finally
                {
                    detail.PlacementStarted -= OnPlacementStarted;
                }

                showModal = detail.LastOutcome is not (RevitApi.PlacementOutcome.PlacementStarted or RevitApi.PlacementOutcome.Cancelled);
            }
            else if (detail.PurchaseUrl is { } url)
            {
                Browser.Open(url);
            }
            else
            {
                showModal = true;
            }
        }
        finally
        {
            card.IsBusy = false;

            if (showModal)
                Detail = detail;
            else
                detail.Dispose();
        }
    }

    private async Task ToggleFavoriteAsync(FamilyCardViewModel card)
    {
        var target = !card.IsFavorite;
        card.IsFavorite = target;
        Detail?.NotifyFavoriteChanged();

        try
        {
            card.IsFavorite = await _host.Api.SetFavoriteAsync(card.Slug, target);

            if (Tab == CatalogTab.Favorites && !card.IsFavorite)
            {
                Families.Remove(card);
                Total = Math.Max(0, Total - 1);
                OnPropertyChanged(nameof(IsEmpty));
                if (Detail?.Card == card)
                    Detail = null;
            }
        }
        catch (Exception ex) when (ex is ApiException or ApiConnectionException)
        {
            card.IsFavorite = !target;
            Log.Info($"Favorite toggle failed: {ex.Message}");
        }
        finally
        {
            Detail?.NotifyFavoriteChanged();
        }
    }

    private void OnPlacementStarted(object? sender, EventArgs e) => PlacementStarted?.Invoke(this, EventArgs.Empty);

    // Session events can come from any thread.
    private void OnSessionChanged(object? sender, EventArgs e) => _dispatcher.InvokeAsync(() =>
    {
        if (!_host.Session.HasStoredToken)
        {
            SessionEnded?.Invoke(this, EventArgs.Empty);
            return;
        }

        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(QuotaText));
        Detail?.NotifyAccessChanged();
    });

    public void Dispose()
    {
        _host.Session.Changed -= OnSessionChanged;
        _searchDebounce.Stop();
        _listCts?.Cancel();
        Detail = null;
    }
}
