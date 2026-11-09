using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Autodesk.Revit.UI;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Account;
using UnSetBim.Framework.Hosting;
using UnSetBim.Modules.Materials.ViewModels;

namespace UnSetBim.Modules.Materials.Views;

/// <summary>
/// Content of the "UnSet BIM — Texturas" dockable pane. Revit creates the pane once at startup
/// (and may show it right away if it was open last session), so the view model starts only when
/// the pane is visible and the user is signed in.
/// </summary>
public partial class TexturesPane : UserControl
{
    /// <summary>Never change: Revit remembers the pane's position by this id.</summary>
    public static readonly DockablePaneId PaneId = new(new Guid("3E1B6C2A-8D47-4F5E-A1C9-6B2D7E40F315"));

    private static TexturesPane? _instance;
    private UnSetHost? _host;
    private TexturesViewModel? _viewModel;
    private bool _sessionHooked;

    private TexturesPane()
    {
        InitializeComponent();

        // Numbers typed and shown the Brazilian way (0,5 / 1.200).
        Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                EnsureStarted();
        };
    }

    /// <summary>The single pane content, created on the UI thread at startup.</summary>
    public static TexturesPane Instance => _instance ??= new TexturesPane();

    /// <summary>Called by the ribbon command after the sign-in check.</summary>
    public void Activate(UnSetHost host)
    {
        _host = host;
        EnsureStarted();
    }

    public static void Shutdown() => _instance?.Reset();

    private void EnsureStarted()
    {
        _host ??= UnSetHost.IsStarted ? UnSetHost.Current : null;
        if (_host is null)
            return;

        if (!_sessionHooked)
        {
            _sessionHooked = true;
            _host.Session.Changed += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                if (_host.Session.IsSignedIn && _viewModel is null && IsVisible)
                    EnsureStarted();
            });
        }

        if (!_host.Session.IsSignedIn)
        {
            ShowSignedOut();
            return;
        }

        if (_viewModel is not null)
            return;

        try
        {
            _viewModel = new TexturesViewModel(_host);
            _viewModel.PageChanged += (_, _) => ResultsScroll.ScrollToTop();
            _viewModel.SessionEnded += (_, _) => Reset();

            MainContent.DataContext = _viewModel;
            MainContent.Visibility = Visibility.Visible;
            SignedOutPanel.Visibility = Visibility.Collapsed;

            _ = _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Textures pane failed to start.", ex);
            ShowSignedOut();
        }
    }

    private void Reset()
    {
        _viewModel?.Dispose();
        _viewModel = null;
        MainContent.DataContext = null;
        ShowSignedOut();
    }

    private void ShowSignedOut()
    {
        MainContent.Visibility = Visibility.Collapsed;
        SignedOutPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Filter icons: a click opens the icon's menu right below it.</summary>
    private void OnFilterButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;

        menu.DataContext = button.DataContext;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnSignInClick(object sender, RoutedEventArgs e)
    {
        if (_host is not null && AccountGate.EnsureSignedIn(_host))
            EnsureStarted();
    }
}

/// <summary>Registers the pane with Revit: tabbed with the Properties palette, hidden until opened.</summary>
internal sealed class TexturesPaneProvider : IDockablePaneProvider
{
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = TexturesPane.Instance;
        data.VisibleByDefault = false;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Tabbed,
            TabBehind = DockablePanes.BuiltInDockablePanes.PropertiesPalette,
        };
    }
}
