using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Textures;
using UnSetBim.Framework;
using UnSetBim.Framework.Hosting;
using UnSetBim.Modules.Materials.RevitApi;
using UnSetBim.UI.Mvvm;

namespace UnSetBim.Modules.Materials.ViewModels;

/// <summary>
/// Textures window: categories, filters, paginated grid and the action bar of the selected
/// texture ("Pintar faces" / "Só criar material"). docs/api.md section 13.
/// </summary>
public sealed class TexturesViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 24;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly UnSetHost _host;
    private readonly TextureStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _searchDebounce;
    private CancellationTokenSource? _listCts;
    private CancellationTokenSource? _applyCts;

    private TextureNavItem? _selectedNav;
    private string _search = string.Empty;
    private FilterOption<string?> _selectedType;
    private FilterOption<bool?> _selectedAccess;
    private FilterOption<string> _selectedSort;
    private int _currentPage = 1;
    private int _lastPage = 1;
    private bool _isLoading;
    private string _errorMessage = string.Empty;

    private TextureCardViewModel? _selected;
    private bool _isBusy;
    private double _progress;
    private string _statusText = string.Empty;
    private bool _statusIsError;
    private bool _showSubscribe;
    private bool _isProjectTab;
    private long? _lastMaterialId;

    public TexturesViewModel(UnSetHost host)
    {
        _host = host;
        _store = new TextureStore(host.Api);
        _dispatcher = Dispatcher.CurrentDispatcher;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await LoadPageAsync(1);
        };

        TypeOptions = new[]
        {
            new FilterOption<string?>("Todos os tipos", null),
            new FilterOption<string?>("Imagem (PBR)", TextureMaterialType.Pbr),
            new FilterOption<string?>("Vidro", TextureMaterialType.Glass),
            new FilterOption<string?>("Metal", TextureMaterialType.Metal),
            new FilterOption<string?>("Água", TextureMaterialType.Water),
            new FilterOption<string?>("Espelho", TextureMaterialType.Mirror),
            new FilterOption<string?>("Emissivo", TextureMaterialType.Emissive),
        };
        _selectedType = TypeOptions[0];
        _selectedType.IsSelected = true;

        AccessOptions = new[]
        {
            new FilterOption<bool?>("Todas", null),
            new FilterOption<bool?>("Grátis", true),
            new FilterOption<bool?>("Assinantes", false),
        };
        _selectedAccess = AccessOptions[0];
        _selectedAccess.IsSelected = true;

        SortOptions = new[]
        {
            new FilterOption<string>("Mais recentes", TextureSort.Recent),
            new FilterOption<string>("Mais usadas", TextureSort.Downloads),
            new FilterOption<string>("Nome (A–Z)", TextureSort.Name),
        };
        _selectedSort = SortOptions[0];
        _selectedSort.IsSelected = true;

        NextPageCommand = new AsyncRelayCommand(() => LoadPageAsync(CurrentPage + 1), () => !IsLoading && CurrentPage < LastPage);
        PreviousPageCommand = new AsyncRelayCommand(() => LoadPageAsync(CurrentPage - 1), () => !IsLoading && CurrentPage > 1);
        RetryCommand = new AsyncRelayCommand(InitializeAsync);
        SelectCommand = new RelayCommand<TextureCardViewModel>(Select);
        PaintCommand = new AsyncRelayCommand(() => ApplyAsync(paint: true), () => CanApply);
        CreateOnlyCommand = new AsyncRelayCommand(() => ApplyAsync(paint: false), () => CanApply);
        SubscribeCommand = new RelayCommand(() => Browser.Open(host.Links.Plans));
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync);
        CloseSelectionCommand = new RelayCommand(() => Selected = null);

        // Filter menus (icon buttons): the menu item passes the chosen option.
        SelectCategoryCommand = new RelayCommand<TextureNavItem>(item => SelectedNav = item);
        SelectTypeCommand = new RelayCommand<FilterOption<string?>>(option => SelectedType = option);
        SelectAccessCommand = new RelayCommand<FilterOption<bool?>>(option => SelectedAccess = option);
        SelectSortCommand = new RelayCommand<FilterOption<string>>(option => SelectedSort = option);
        AdjustCommand = new AsyncRelayCommand(OpenEditorAsync, () => _lastMaterialId is not null && !IsBusy);

        Project = new MaterialEditorViewModel(host);

        _host.Session.Changed += OnSessionChanged;
    }

    public event EventHandler? SessionEnded;

    /// <summary>Raised after a page is shown (the view scrolls back to the top).</summary>
    public event EventHandler? PageChanged;

    public ObservableCollection<TextureNavItem> NavItems { get; } = new();

    public ObservableCollection<TextureCardViewModel> Textures { get; } = new();

    public IReadOnlyList<FilterOption<string?>> TypeOptions { get; }

    public IReadOnlyList<FilterOption<bool?>> AccessOptions { get; }

    public IReadOnlyList<FilterOption<string>> SortOptions { get; }

    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand SelectCommand { get; }
    public ICommand PaintCommand { get; }
    public ICommand CreateOnlyCommand { get; }
    public ICommand SubscribeCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand CloseSelectionCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand SelectTypeCommand { get; }
    public ICommand SelectAccessCommand { get; }
    public ICommand SelectSortCommand { get; }

    // Tooltips and "active" highlight of the filter icons.
    public string CategoryTip => $"Categoria: {SelectedNav?.Label ?? "Todas"}";
    public string TypeTip => $"Tipo de material: {SelectedType.Label}";
    public string AccessTip => $"Acesso: {SelectedAccess.Label}";
    public string SortTip => $"Ordenar: {SelectedSort.Label}";
    public bool IsCategoryFiltered => SelectedNav?.CategorySlug is not null;
    public bool IsTypeFiltered => SelectedType.Value is not null;
    public bool IsAccessFiltered => SelectedAccess.Value is not null;

    private void NotifyFilters()
    {
        foreach (var item in NavItems)
            item.IsSelected = ReferenceEquals(item, _selectedNav);
        foreach (var option in TypeOptions)
            option.IsSelected = ReferenceEquals(option, _selectedType);
        foreach (var option in AccessOptions)
            option.IsSelected = ReferenceEquals(option, _selectedAccess);
        foreach (var option in SortOptions)
            option.IsSelected = ReferenceEquals(option, _selectedSort);

        OnPropertyChanged(nameof(CategoryTip));
        OnPropertyChanged(nameof(TypeTip));
        OnPropertyChanged(nameof(AccessTip));
        OnPropertyChanged(nameof(SortTip));
        OnPropertyChanged(nameof(IsCategoryFiltered));
        OnPropertyChanged(nameof(IsTypeFiltered));
        OnPropertyChanged(nameof(IsAccessFiltered));
        OnPropertyChanged(nameof(HasActiveFilters));
    }
    public ICommand AdjustCommand { get; }

    /// <summary>"No projeto" tab: materials of the active document and their editor.</summary>
    public MaterialEditorViewModel Project { get; }

    // ── Tabs ─────────────────────────────────────────────

    public bool IsLibraryTab
    {
        get => !_isProjectTab;
        set
        {
            if (value)
                SetTab(project: false);
        }
    }

    public bool IsProjectTab
    {
        get => _isProjectTab;
        set
        {
            if (value)
                SetTab(project: true);
        }
    }

    private void SetTab(bool project, long? selectMaterial = null)
    {
        if (_isProjectTab != project)
        {
            _isProjectTab = project;
            OnPropertyChanged(nameof(IsLibraryTab));
            OnPropertyChanged(nameof(IsProjectTab));
        }

        // The active document may have changed since last time: always reload the list.
        if (project)
            _ = Project.RefreshAsync(selectMaterial);
        else
            Project.Stop();
    }

    private Task OpenEditorAsync()
    {
        SetTab(project: true, _lastMaterialId);
        return Task.CompletedTask;
    }

    /// <summary>The last apply created or used a material: "Ajustar material" is offered.</summary>
    public bool CanAdjust => _lastMaterialId is not null;

    // ── Header ───────────────────────────────────────────

    public string UserName => _host.Session.User?.Name ?? string.Empty;

    public string AccessText => _host.Session.Access.HasAccess
        ? $"{_host.Session.Access.Subscription?.Plan?.Name ?? "Assinatura"} · todas as texturas"
        : "Sem assinatura · texturas grátis";

    // ── Filters ──────────────────────────────────────────

    public TextureNavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is not null && SetProperty(ref _selectedNav, value))
            {
                OnPropertyChanged(nameof(ListTitle));
                _ = LoadPageAsync(1);
            }
        }
    }

    public string ListTitle => SelectedNav?.Label ?? "Todas as texturas";

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

    public FilterOption<string?> SelectedType
    {
        get => _selectedType;
        set
        {
            if (value is not null && SetProperty(ref _selectedType, value))
                _ = LoadPageAsync(1);
        }
    }

    public FilterOption<bool?> SelectedAccess
    {
        get => _selectedAccess;
        set
        {
            if (value is not null && SetProperty(ref _selectedAccess, value))
                _ = LoadPageAsync(1);
        }
    }

    public FilterOption<string> SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (value is not null && SetProperty(ref _selectedSort, value))
                _ = LoadPageAsync(1);
        }
    }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Search)
        || SelectedType.Value is not null
        || SelectedAccess.Value is not null
        || SelectedNav?.CategorySlug is not null;

    private async Task ClearFiltersAsync()
    {
        _search = string.Empty;
        _selectedType = TypeOptions[0];
        _selectedAccess = AccessOptions[0];
        _selectedNav = NavItems.FirstOrDefault();
        OnPropertyChanged(nameof(Search));
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(SelectedAccess));
        OnPropertyChanged(nameof(SelectedNav));
        OnPropertyChanged(nameof(ListTitle));
        await LoadPageAsync(1);
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

    public bool IsEmpty => !IsLoading && string.IsNullOrEmpty(ErrorMessage) && Textures.Count == 0;

    public string EmptyText => HasActiveFilters
        ? "Tente outro termo de busca ou limpe os filtros."
        : "Ainda não há texturas publicadas.";

    // ── Selection and action bar ─────────────────────────

    public TextureCardViewModel? Selected
    {
        get => _selected;
        private set
        {
            if (IsBusy)
                return;

            var old = _selected;
            if (!SetProperty(ref _selected, value))
                return;

            if (old is not null)
                old.IsSelected = false;
            if (value is not null)
                value.IsSelected = true;

            StatusText = string.Empty;
            StatusIsError = false;
            ShowSubscribe = value?.IsLocked == true;
            Progress = 0;
            _lastMaterialId = null;
            OnPropertyChanged(nameof(CanAdjust));

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanApply));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasSelection => Selected is not null;

    public bool CanApply => Selected is { IsLocked: false } && !IsBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanApply));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>Download progress, 0..1.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public bool ShowSubscribe
    {
        get => _showSubscribe;
        private set => SetProperty(ref _showSubscribe, value);
    }

    private void Select(TextureCardViewModel? card)
    {
        if (card is null || IsBusy)
            return;

        Selected = ReferenceEquals(card, Selected) ? null : card;
    }

    // ── Loading ──────────────────────────────────────────

    public async Task InitializeAsync()
    {
        ErrorMessage = string.Empty;
        IsLoading = true;

        try
        {
            var categories = await _host.Api.GetTextureCategoriesAsync();

            NavItems.Clear();
            NavItems.Add(new TextureNavItem { Label = "Todas as categorias" });
            foreach (var category in categories)
                NavItems.Add(new TextureNavItem { Label = category.Name, CategorySlug = category.Slug });

            _selectedNav = NavItems[0];
            OnPropertyChanged(nameof(SelectedNav));
            OnPropertyChanged(nameof(ListTitle));
        }
        catch (Exception ex) when (ex is ApiException or ApiConnectionException)
        {
            IsLoading = false;
            ErrorMessage = ex is ApiException api ? api.UserMessage : ex.Message;
            return;
        }

        await LoadPageAsync(1);
    }

    private async Task LoadPageAsync(int page)
    {
        _listCts?.Cancel();
        _listCts = new CancellationTokenSource();
        var ct = _listCts.Token;

        IsLoading = true;
        ErrorMessage = string.Empty;
        NotifyFilters();

        try
        {
            var result = await _host.Api.GetTexturesAsync(new TextureQuery
            {
                Search = string.IsNullOrWhiteSpace(Search) ? null : Search,
                Category = SelectedNav?.CategorySlug,
                MaterialType = SelectedType.Value,
                Free = SelectedAccess.Value,
                Sort = SelectedSort.Value,
                Page = page,
                PerPage = PageSize,
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            var selectedSlug = Selected?.Slug;
            if (!IsBusy)
                Selected = null;

            Textures.Clear();
            var hasAccess = _host.Session.Access.HasAccess;
            foreach (var texture in result.Data)
                Textures.Add(new TextureCardViewModel(texture, hasAccess));

            // Keep the action bar on the same texture when it is still listed.
            if (selectedSlug is not null && !IsBusy && Textures.FirstOrDefault(t => t.Slug == selectedSlug) is { } again)
                Selected = again;

            CurrentPage = result.Meta.CurrentPage;
            LastPage = Math.Max(result.Meta.LastPage, 1);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (ApiException ex)
        {
            Textures.Clear();
            ErrorMessage = ex.UserMessage;
        }
        catch (ApiConnectionException ex)
        {
            Textures.Clear();
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error("Textures page failed.", ex);
            Textures.Clear();
            ErrorMessage = "Não foi possível carregar as texturas.";
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(PageText));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(EmptyText));
                OnPropertyChanged(nameof(HasActiveFilters));
                PageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ── Apply ────────────────────────────────────────────

    /// <summary>
    /// Fresh detail (server decision) → package on disk (cache or download) → material in the
    /// active document → optionally the face-painting loop.
    /// </summary>
    private async Task ApplyAsync(bool paint)
    {
        if (Selected is not { } card || IsBusy)
            return;

        IsBusy = true;
        StatusIsError = false;
        ShowSubscribe = false;
        Progress = 0;
        _applyCts = new CancellationTokenSource();
        var picking = false;

        try
        {
            StatusText = "Verificando textura…";
            var detail = await _host.Api.GetTextureAsync(card.Slug, _applyCts.Token);

            if (!detail.Download.Allowed)
            {
                Fail(detail.Download.Message ?? "Esta textura é exclusiva para assinantes.");
                ShowSubscribe = detail.Download.Code == "subscription_required";
                return;
            }

            TexturePackage? package = null;
            if (detail.NeedsDownload)
            {
                StatusText = "Baixando textura…";
                var progress = new Progress<double>(p =>
                {
                    Progress = p;
                    StatusText = p < 1 ? $"Baixando textura… {p.ToString("P0", PtBr)}" : "Preparando imagens…";
                });
                package = await _store.EnsureAsync(detail, null, progress, _applyCts.Token);
            }

            var request = new TextureMaterialRequest(detail, package);

            if (paint)
            {
                StatusText = "Clique nas faces no Revit. Esc para terminar.";
                picking = true;
            }
            else
            {
                StatusText = "Criando material…";
            }

            var outcome = await _host.Revit.InvokeAsync(app => Apply(app, request, paint));

            StatusText = outcome.Message;
            StatusIsError = outcome.IsError;
            _lastMaterialId = outcome.MaterialId;
            OnPropertyChanged(nameof(CanAdjust));
        }
        catch (OperationCanceledException) when (_applyCts.IsCancellationRequested)
        {
            StatusText = string.Empty;
        }
        catch (ApiException ex)
        {
            Fail(ex.UserMessage);
            ShowSubscribe = ex.Code == "subscription_required";
        }
        catch (ApiConnectionException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error($"Applying texture {card.Slug} failed.", ex);
            Fail("Não foi possível aplicar a textura. Detalhes foram gravados no log do UnSet BIM.");
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            if (picking)
                Log.Info("Face painting finished.");
        }
    }

    private void Fail(string message)
    {
        StatusText = message;
        StatusIsError = true;
    }

    private sealed record ApplyOutcome(string Message, bool IsError = false, long? MaterialId = null);

    /// <summary>Runs inside the Revit API context (RevitDispatcher).</summary>
    private static ApplyOutcome Apply(UIApplication app, TextureMaterialRequest request, bool paint)
    {
        var uidoc = app.ActiveUIDocument;
        if (uidoc?.Document is not { } doc)
            return new ApplyOutcome("Abra um projeto no Revit para aplicar a textura.", IsError: true);

        if (doc.IsReadOnly || doc.IsLinked)
            return new ApplyOutcome("Este documento não pode ser alterado.", IsError: true);

        var material = TextureMaterialFactory.Ensure(doc, app.Application, request);

        if (!paint)
        {
            var verb = material.Created ? "criado" : material.Updated ? "atualizado" : "já estava";
            return new ApplyOutcome(
                $"Material \"{material.MaterialName}\" {verb} no projeto. Use pelo Navegador de materiais ou nas propriedades do elemento.",
                MaterialId: material.MaterialId.Value);
        }

        var result = FacePainter.PaintFaces(uidoc, material.MaterialId, material.MaterialName);

        if (result.Painted == 0 && result.Skipped == 0)
            return new ApplyOutcome($"Material \"{material.MaterialName}\" pronto. Nenhuma face pintada.", MaterialId: material.MaterialId.Value);

        var text = result.Painted == 1 ? "1 face pintada" : $"{result.Painted} faces pintadas";
        if (result.Skipped > 0)
            text += $" · {result.Skipped} não aceita{(result.Skipped == 1 ? "" : "m")} pintura (famílias e importados)";

        return new ApplyOutcome($"{text} com \"{material.MaterialName}\".", IsError: result.Painted == 0, MaterialId: material.MaterialId.Value);
    }

    // Session events can come from any thread.
    private void OnSessionChanged(object? sender, EventArgs e) => _dispatcher.InvokeAsync(() =>
    {
        if (!_host.Session.HasStoredToken)
        {
            SessionEnded?.Invoke(this, EventArgs.Empty);
            return;
        }

        var hasAccess = _host.Session.Access.HasAccess;
        foreach (var card in Textures)
            card.SetAccess(hasAccess);

        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(AccessText));
        OnPropertyChanged(nameof(CanApply));
        ShowSubscribe = Selected?.IsLocked == true;
        CommandManager.InvalidateRequerySuggested();
    });

    public void Dispose()
    {
        _host.Session.Changed -= OnSessionChanged;
        _searchDebounce.Stop();
        _listCts?.Cancel();
        _applyCts?.Cancel();
        Project.Stop();
    }
}
