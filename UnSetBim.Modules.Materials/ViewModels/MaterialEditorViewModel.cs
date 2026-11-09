using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Hosting;
using UnSetBim.Modules.Materials.RevitApi;
using UnSetBim.UI.Mvvm;

namespace UnSetBim.Modules.Materials.ViewModels;

/// <summary>
/// "No projeto" tab: the UnSet texture materials of the active document and the editor of the
/// selected one (size, rotation, position, relief, brightness, finish, tint). Changes are applied
/// to the model automatically, shortly after the user stops editing.
/// </summary>
public sealed class MaterialEditorViewModel : ObservableObject
{
    /// <summary>Real-world width shown by the preview square.</summary>
    private const double PreviewSpanMm = 2000;
    public const double PreviewSize = 220;

    private readonly UnSetHost _host;
    private readonly DispatcherTimer _applyTimer;

    private ProjectMaterialInfo? _selected;
    private MaterialSettings? _settings;
    private bool _loading;
    private bool _applying;
    private bool _dirtyWhileApplying;
    private bool _keepRatio = true;
    private double _ratio = 1;
    private string _status = string.Empty;
    private bool _statusIsError;
    private string _listMessage = string.Empty;
    private ImageSource? _diffuseImage;
    private bool _isWorking;

    public MaterialEditorViewModel(UnSetHost host)
    {
        _host = host;

        _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _applyTimer.Tick += async (_, _) =>
        {
            _applyTimer.Stop();
            await ApplyAsync();
        };

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(null));
        ResetSizeCommand = new RelayCommand(ResetSize, () => HasSettings);
        PresetCommand = new RelayCommand<string>(ApplyPreset);
        NudgeCommand = new RelayCommand<string>(Nudge);
        PaintCommand = new AsyncRelayCommand(PaintAsync, () => Selected is not null && !IsWorking);
        CreateVariantCommand = new AsyncRelayCommand(CreateVariantAsync, () => Selected is not null && !IsWorking);
        PickFromFaceCommand = new AsyncRelayCommand(PickFromFaceAsync, () => !IsWorking);
        RemovePaintCommand = new AsyncRelayCommand(RemovePaintAsync, () => !IsWorking);
        DeleteMaterialCommand = new AsyncRelayCommand(DeleteMaterialAsync, () => Selected is not null && !IsWorking);
        TintSwatchCommand = new RelayCommand<string>(hex =>
        {
            TintEnabled = true;
            TintColor = hex ?? "#ffffff";
        });
    }

    public ObservableCollection<ProjectMaterialInfo> Materials { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand ResetSizeCommand { get; }
    public ICommand PresetCommand { get; }
    public ICommand NudgeCommand { get; }
    public ICommand TintSwatchCommand { get; }
    public ICommand PaintCommand { get; }
    public ICommand CreateVariantCommand { get; }
    public ICommand PickFromFaceCommand { get; }
    public ICommand RemovePaintCommand { get; }
    public ICommand DeleteMaterialCommand { get; }

    /// <summary>Painting, picking or creating a variation is running in Revit.</summary>
    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (SetProperty(ref _isWorking, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public IReadOnlyList<string> TintSwatches { get; } = new[]
    {
        "#ffffff", "#f5e6c8", "#d9b38c", "#8b5a2b", "#9e9e9e", "#4a4a4a", "#c0392b", "#2e86c1",
    };

    public string ListMessage
    {
        get => _listMessage;
        private set => SetProperty(ref _listMessage, value);
    }

    public ProjectMaterialInfo? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                _ = LoadAsync(value);
        }
    }

    public bool HasSettings => _settings is not null;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    // ── Editable values ──────────────────────────────────

    public double WidthMm
    {
        get => _settings?.WidthMm ?? 0;
        set
        {
            if (_settings is null || value <= 0 || Same(_settings.WidthMm, value))
                return;

            _settings.WidthMm = Math.Round(value, 1);
            if (KeepRatio)
            {
                _settings.HeightMm = Math.Round(value * _ratio, 1);
                OnPropertyChanged(nameof(HeightMm));
            }

            Changed();
        }
    }

    public double HeightMm
    {
        get => _settings?.HeightMm ?? 0;
        set
        {
            if (_settings is null || value <= 0 || Same(_settings.HeightMm, value))
                return;

            _settings.HeightMm = Math.Round(value, 1);
            if (KeepRatio && _ratio > 0)
            {
                _settings.WidthMm = Math.Round(value / _ratio, 1);
                OnPropertyChanged(nameof(WidthMm));
            }

            Changed();
        }
    }

    public bool KeepRatio
    {
        get => _keepRatio;
        set
        {
            if (SetProperty(ref _keepRatio, value) && value && _settings is { WidthMm: > 0 })
                _ratio = _settings.HeightMm / _settings.WidthMm;
        }
    }

    public double RotationDeg
    {
        get => _settings?.RotationDeg ?? 0;
        set => Set(s => s.RotationDeg, (s, v) => s.RotationDeg = v, Math.Round(Math.Clamp(value, -180, 180), 1));
    }

    public double OffsetXMm
    {
        get => _settings?.OffsetXMm ?? 0;
        set => Set(s => s.OffsetXMm, (s, v) => s.OffsetXMm = v, Math.Round(value, 1));
    }

    public double OffsetYMm
    {
        get => _settings?.OffsetYMm ?? 0;
        set => Set(s => s.OffsetYMm, (s, v) => s.OffsetYMm = v, Math.Round(value, 1));
    }

    public bool HasRelief => _settings?.HasRelief ?? false;

    public double ReliefAmount
    {
        get => _settings?.ReliefAmount ?? 1;
        set => Set(s => s.ReliefAmount, (s, v) => s.ReliefAmount = v, Math.Round(Math.Clamp(value, 0, 3), 2));
    }

    public double ImageBrightness
    {
        get => _settings?.ImageBrightness ?? 1;
        set => Set(s => s.ImageBrightness, (s, v) => s.ImageBrightness = v, Math.Round(Math.Clamp(value, 0.2, 2), 2));
    }

    public bool RoughnessAvailable => _settings?.RoughnessAvailable ?? false;

    public bool UseRoughnessMap
    {
        get => _settings?.UseRoughnessMap ?? false;
        set
        {
            if (_settings is null || _settings.UseRoughnessMap == value)
                return;

            _settings.UseRoughnessMap = value;
            OnPropertyChanged(nameof(CanEditGlossiness));
            Changed();
        }
    }

    public bool CanEditGlossiness => !UseRoughnessMap;

    public double Glossiness
    {
        get => _settings?.Glossiness ?? 0.25;
        set => Set(s => s.Glossiness, (s, v) => s.Glossiness = v, Math.Round(Math.Clamp(value, 0, 1), 2));
    }

    public bool TintEnabled
    {
        get => _settings?.TintEnabled ?? false;
        set
        {
            if (_settings is null || _settings.TintEnabled == value)
                return;

            _settings.TintEnabled = value;
            Changed();
        }
    }

    public string TintColor
    {
        get => _settings?.TintColor ?? "#ffffff";
        set
        {
            if (_settings is null || value is null)
                return;

            var hex = value.Trim().ToLowerInvariant();
            if (!hex.StartsWith('#'))
                hex = "#" + hex;

            if (hex == _settings.TintColor || !System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-f]{6}$"))
                return;

            _settings.TintColor = hex;
            Changed();
        }
    }

    public Brush TintBrush =>
        ColorConverter.ConvertFromString(TintColor) is Color c ? new SolidColorBrush(c) : Brushes.White;

    public string OriginalSizeText =>
        _settings is null ? string.Empty : $"Tamanho do site: {_settings.OriginalWidthMm:0.#} × {_settings.OriginalHeightMm:0.#} mm";

    // ── Preview (2 m × 2 m square, tiled like the model) ──

    public double PreviewSquare => PreviewSize;

    public ImageSource? DiffuseImage
    {
        get => _diffuseImage;
        private set => SetProperty(ref _diffuseImage, value);
    }

    public Rect PreviewViewport
    {
        get
        {
            var k = PreviewSize / PreviewSpanMm;
            var w = Math.Max(WidthMm * k, 2);
            var h = Math.Max(HeightMm * k, 2);
            return new Rect(OffsetXMm * k % w, -OffsetYMm * k % h, w, h);
        }
    }

    public Transform PreviewTransform => new RotateTransform(-RotationDeg, PreviewSize / 2, PreviewSize / 2);

    // ── Loading ──────────────────────────────────────────

    /// <summary>Reloads the list of the active document's UnSet materials and selects <paramref name="selectId"/>.</summary>
    public async Task RefreshAsync(long? selectId)
    {
        try
        {
            var result = await _host.Revit.InvokeAsync(app =>
                app.ActiveUIDocument?.Document is { } doc ? TextureMaterialEditor.List(doc) : null);

            var keep = selectId ?? Selected?.Id;
            Materials.Clear();

            if (result is null)
            {
                ListMessage = "Abra um projeto no Revit para ver os materiais.";
                Selected = null;
                return;
            }

            foreach (var item in result)
                Materials.Add(item);

            ListMessage = Materials.Count == 0
                ? "Nenhum material do UnSet BIM neste projeto. Crie um na aba Biblioteca."
                : string.Empty;

            Selected = Materials.FirstOrDefault(m => m.Id == keep) ?? Materials.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error("Listing project materials failed.", ex);
            ListMessage = "Não foi possível ler os materiais do projeto.";
        }
    }

    private async Task LoadAsync(ProjectMaterialInfo? info)
    {
        _applyTimer.Stop();
        _settings = null;
        DiffuseImage = null;
        Status = string.Empty;
        StatusIsError = false;

        if (info is not null)
        {
            try
            {
                _settings = await _host.Revit.InvokeAsync(app =>
                    app.ActiveUIDocument?.Document is { } doc ? TextureMaterialEditor.Read(doc, info.Id) : null);

                if (_settings is null)
                    Fail("Não foi possível ler este material.");
                else
                    DiffuseImage = LoadImage(_settings.DiffuseFile);
            }
            catch (Exception ex)
            {
                Log.Error($"Reading material {info.Id} failed.", ex);
                Fail("Não foi possível ler este material.");
            }
        }

        _ratio = _settings is { WidthMm: > 0 } s ? s.HeightMm / s.WidthMm : 1;

        _loading = true;
        try
        {
            OnPropertyChanged(string.Empty); // refresh every binding
        }
        finally
        {
            _loading = false;
        }
    }

    private static ImageSource? LoadImage(string? file)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 512;
            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Preview image not loaded: {file}", ex);
            return null;
        }
    }

    // ── Changes ──────────────────────────────────────────

    private void Set(Func<MaterialSettings, double> get, Action<MaterialSettings, double> set, double value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (_settings is null || Same(get(_settings), value))
            return;

        set(_settings, value);
        OnPropertyChanged(name);
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(PreviewViewport));
        OnPropertyChanged(nameof(PreviewTransform));
        OnPropertyChanged(nameof(TintBrush));

        if (_loading)
            return;

        Status = "Alterando…";
        StatusIsError = false;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private async Task ApplyAsync()
    {
        if (_settings is null || Selected is not { } target)
            return;

        if (_applying)
        {
            _dirtyWhileApplying = true;
            return;
        }

        _applying = true;
        var snapshot = _settings.Clone();

        try
        {
            await _host.Revit.InvokeAsync(app =>
            {
                if (app.ActiveUIDocument?.Document is not { } doc)
                    throw new InvalidOperationException("Abra o projeto deste material no Revit.");

                TextureMaterialEditor.Apply(doc, target.Id, snapshot);
            });

            Status = "Aplicado no modelo.";
            StatusIsError = false;
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error($"Adjusting material {target.Id} failed.", ex);
            Fail("Não foi possível aplicar o ajuste. Detalhes foram gravados no log do UnSet BIM.");
        }
        finally
        {
            _applying = false;
        }

        if (_dirtyWhileApplying)
        {
            _dirtyWhileApplying = false;
            await ApplyAsync();
        }
    }

    // ── Local actions (no server calls) ─────────────────

    /// <summary>Paints faces with the selected project material (any variation).</summary>
    private async Task PaintAsync()
    {
        if (Selected is not { } target)
            return;

        await FlushPendingAsync();
        IsWorking = true;
        Status = "Clique nas faces no Revit. Esc para terminar.";
        StatusIsError = false;

        try
        {
            var result = await _host.Revit.InvokeAsync(app =>
            {
                if (app.ActiveUIDocument is not { } uidoc)
                    throw new InvalidOperationException("Abra o projeto deste material no Revit.");

                return FacePainter.PaintFaces(uidoc, new Autodesk.Revit.DB.ElementId(target.Id), target.Name);
            });

            Status = result.Painted switch
            {
                0 when result.Skipped == 0 => "Nenhuma face pintada.",
                0 => $"{result.Skipped} face(s) não aceitam pintura (famílias e importados).",
                1 => "1 face pintada.",
                var n => $"{n} faces pintadas.",
            };
            StatusIsError = result.Painted == 0 && result.Skipped > 0;
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error($"Painting with material {target.Id} failed.", ex);
            Fail("Não foi possível pintar. Detalhes foram gravados no log do UnSet BIM.");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>
    /// Copies the selected material with its own appearance (same images on disk, no download),
    /// so the copy can get another angle, scale or finish. The copy is selected right away.
    /// </summary>
    private async Task CreateVariantAsync()
    {
        if (Selected is not { } source)
            return;

        await FlushPendingAsync();
        IsWorking = true;

        try
        {
            var variant = await _host.Revit.InvokeAsync(app =>
            {
                if (app.ActiveUIDocument?.Document is not { } doc)
                    throw new InvalidOperationException("Abra o projeto deste material no Revit.");

                return TextureMaterialTools.CreateVariant(doc, source.Id);
            });

            await RefreshAsync(variant.Id);
            Status = $"Variação \"{variant.Name}\" criada. Ajuste ângulo, escala ou acabamento e use \"Pintar faces\".";
            StatusIsError = false;
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error($"Creating a variation of material {source.Id} failed.", ex);
            Fail("Não foi possível criar a variação. Detalhes foram gravados no log do UnSet BIM.");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>The user clicks a face in the model and its material opens in the editor.</summary>
    private async Task PickFromFaceAsync()
    {
        await FlushPendingAsync();
        IsWorking = true;
        Status = "Clique numa face no Revit. Esc cancela.";
        StatusIsError = false;

        try
        {
            var picked = await _host.Revit.InvokeAsync(app =>
            {
                if (app.ActiveUIDocument is not { } uidoc)
                    throw new InvalidOperationException("Abra um projeto no Revit.");

                return TextureMaterialTools.PickMaterialFromFace(uidoc);
            });

            if (picked.Cancelled)
            {
                Status = string.Empty;
            }
            else if (picked.MaterialId is null || !picked.IsUnSet)
            {
                Fail("Essa face não usa um material do UnSet BIM.");
            }
            else
            {
                await RefreshAsync(picked.MaterialId);
                Status = "Material da face aberto.";
            }
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Picking a material from a face failed.", ex);
            Fail("Não foi possível ler a face. Detalhes foram gravados no log do UnSet BIM.");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>Click painted faces to remove their paint (any material, not only UnSet ones).</summary>
    private async Task RemovePaintAsync()
    {
        await FlushPendingAsync();
        IsWorking = true;
        Status = "Clique nas faces pintadas no Revit. Esc para terminar.";
        StatusIsError = false;

        try
        {
            var result = await _host.Revit.InvokeAsync(app =>
            {
                if (app.ActiveUIDocument is not { } uidoc)
                    throw new InvalidOperationException("Abra um projeto no Revit.");

                return FacePainter.RemovePaint(uidoc);
            });

            Status = result.Painted switch
            {
                0 when result.Skipped == 0 => "Nenhuma pintura removida.",
                0 => "As faces clicadas não estavam pintadas.",
                1 => "Pintura removida de 1 face.",
                var n => $"Pintura removida de {n} faces.",
            };
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Removing paint failed.", ex);
            Fail("Não foi possível remover a pintura. Detalhes foram gravados no log do UnSet BIM.");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>Deletes the selected material from the project, after confirmation.</summary>
    private async Task DeleteMaterialAsync()
    {
        if (Selected is not { } target)
            return;

        var choice = UnSetBim.Framework.Dialogs.MessageDialog.Show(
            "Excluir material",
            $"Excluir \"{target.Name}\" do projeto?\n\nAs faces pintadas com ele voltam ao material original do elemento, "
            + "e o que usava este material passa para \"Por categoria\". Dá para desfazer com Ctrl+Z no Revit.",
            UnSetBim.Framework.Dialogs.DialogKind.Warning,
            "Excluir", "Cancelar");

        if (choice != 0)
            return;

        _applyTimer.Stop();
        IsWorking = true;

        try
        {
            var name = await _host.Revit.InvokeAsync(app =>
            {
                if (app.ActiveUIDocument?.Document is not { } doc)
                    throw new InvalidOperationException("Abra o projeto deste material no Revit.");

                return TextureMaterialTools.DeleteMaterial(doc, target.Id);
            });

            await RefreshAsync(null);
            Status = $"Material \"{name}\" excluído.";
            StatusIsError = false;
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error($"Deleting material {target.Id} failed.", ex);
            Fail("Não foi possível excluir o material. Detalhes foram gravados no log do UnSet BIM.");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>Applies a pending edit before an action that depends on the current settings.</summary>
    private async Task FlushPendingAsync()
    {
        if (!_applyTimer.IsEnabled)
            return;

        _applyTimer.Stop();
        await ApplyAsync();
    }

    private void ResetSize()
    {
        if (_settings is null)
            return;

        _settings.WidthMm = _settings.OriginalWidthMm;
        _settings.HeightMm = _settings.OriginalHeightMm;
        _settings.RotationDeg = 0;
        _settings.OffsetXMm = 0;
        _settings.OffsetYMm = 0;
        _ratio = _settings.WidthMm > 0 ? _settings.HeightMm / _settings.WidthMm : 1;

        OnPropertyChanged(nameof(WidthMm));
        OnPropertyChanged(nameof(HeightMm));
        OnPropertyChanged(nameof(RotationDeg));
        OnPropertyChanged(nameof(OffsetXMm));
        OnPropertyChanged(nameof(OffsetYMm));
        Changed();
    }

    /// <summary>Finish presets: fixed glossiness without the roughness image.</summary>
    private void ApplyPreset(string? preset)
    {
        if (_settings is null)
            return;

        if (preset == "map")
        {
            UseRoughnessMap = true;
            return;
        }

        var gloss = preset switch
        {
            "matte" => 0.05,
            "satin" => 0.35,
            "gloss" => 0.7,
            "polished" => 0.95,
            _ => _settings.Glossiness,
        };

        _settings.UseRoughnessMap = false;
        _settings.Glossiness = gloss;
        OnPropertyChanged(nameof(UseRoughnessMap));
        OnPropertyChanged(nameof(CanEditGlossiness));
        OnPropertyChanged(nameof(Glossiness));
        Changed();
    }

    /// <summary>Moves the texture by a tenth of the tile.</summary>
    private void Nudge(string? direction)
    {
        if (_settings is null)
            return;

        var dx = Math.Round(_settings.WidthMm / 10, 1);
        var dy = Math.Round(_settings.HeightMm / 10, 1);

        switch (direction)
        {
            case "left": OffsetXMm -= dx; break;
            case "right": OffsetXMm += dx; break;
            case "up": OffsetYMm += dy; break;
            case "down": OffsetYMm -= dy; break;
        }
    }

    private void Fail(string message)
    {
        Status = message;
        StatusIsError = true;
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < 0.0001;

    public void Stop() => _applyTimer.Stop();
}
