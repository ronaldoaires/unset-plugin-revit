using System.IO;
using System.Windows.Input;
using UnSetBim.Core.Api;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Updates;
using UnSetBim.UI.Mvvm;

namespace UnSetBim.Framework.Updates;

public enum UpdateStep
{
    Offer,
    Downloading,
    Ready,
}

/// <summary>Update window: release notes, download with progress, SHA-256 check, silent install (applies on the next Revit start).</summary>
public sealed class UpdateViewModel : ObservableObject, IDisposable
{
    private readonly UpdateManager _updates;
    private readonly CancellationTokenSource _cts = new();
    private UpdateStep _step = UpdateStep.Offer;
    private double _progress;
    private string _errorMessage = string.Empty;

    public UpdateViewModel(UpdateManager updates)
    {
        _updates = updates;

        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => Step == UpdateStep.Offer && CanDownload);
        ExitRevitCommand = new RelayCommand(() =>
        {
            ExitRevitRequested = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
        OpenReleaseCommand = new RelayCommand(_updates.OpenReleasePage);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        if (updates.IsInstalledPendingRestart)
            _step = UpdateStep.Ready;
    }

    public event EventHandler? CloseRequested;

    /// <summary>Read by <see cref="UpdatePrompt"/> after the window closes.</summary>
    public bool ExitRevitRequested { get; private set; }

    public ICommand DownloadCommand { get; }
    public ICommand ExitRevitCommand { get; }
    public ICommand OpenReleaseCommand { get; }
    public ICommand CloseCommand { get; }

    public bool IsRequired => _updates.IsUpdateRequired;

    public string Title => IsRequired ? "Atualização obrigatória" : "Nova versão disponível";

    public string Summary => IsRequired
        ? $"Esta versão do UnSet BIM ({_updates.CurrentVersion}) não é mais suportada. Atualize para a versão {_updates.Latest?.Version} para continuar usando o plugin."
        : $"A versão {_updates.Latest?.Version} do UnSet BIM está disponível. Você está usando a {_updates.CurrentVersion}.";

    public string Notes => string.IsNullOrWhiteSpace(_updates.Latest?.Notes) ? "Sem notas para esta versão." : _updates.Latest!.Notes!;

    public bool CanDownload => _updates.Latest is { } r && UpdateDownloader.CanDownload(r);

    /// <summary>No installer asset (or no hash): only the release page can be offered.</summary>
    public bool CannotDownload => !CanDownload;

    public string SizeText => _updates.Latest?.SizeBytes is long size and > 0
        ? $"{size / 1024d / 1024d:0.0} MB"
        : string.Empty;

    public string CloseText => IsRequired ? "Fechar" : "Agora não";

    public UpdateStep Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, value))
            {
                OnPropertyChanged(nameof(IsOfferStep));
                OnPropertyChanged(nameof(IsDownloadingStep));
                OnPropertyChanged(nameof(IsReadyStep));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsOfferStep => Step == UpdateStep.Offer;
    public bool IsDownloadingStep => Step == UpdateStep.Downloading;
    public bool IsReadyStep => Step == UpdateStep.Ready;

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    private string _statusText = string.Empty;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string InstalledText =>
        $"A versão {_updates.Latest?.Version} foi instalada. Reinicie o Revit para começar a usar.";

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    private async Task DownloadAsync()
    {
        ErrorMessage = string.Empty;
        Progress = 0;
        StatusText = "Baixando e verificando o instalador…";
        Step = UpdateStep.Downloading;

        try
        {
            // Progress<T> reports on the UI thread (captured context).
            var progress = new Progress<double>(p => Progress = p * 100);
            var installer = await _updates.DownloadAsync(progress, _cts.Token);

            StatusText = "Instalando…";
            await _updates.InstallAsync(installer, _cts.Token);
            Step = UpdateStep.Ready;
        }
        catch (OperationCanceledException)
        {
            Step = UpdateStep.Offer;
        }
        catch (Exception ex) when (ex is ApiException or ApiConnectionException or UpdateIntegrityException or UpdateInstallException or IOException)
        {
            Log.Warn("Update download failed.", ex);
            ErrorMessage = ex is ApiConnectionException or UpdateIntegrityException or UpdateInstallException
                ? ex.Message
                : "Não foi possível baixar a atualização. Tente novamente ou baixe pela página da versão.";
            Step = UpdateStep.Offer;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
