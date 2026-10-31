using System.Windows;
using System.Windows.Input;
using UnSetBim.Framework.Dialogs;
using UnSetBim.Framework.Hosting;
using UnSetBim.Modules.Families.ViewModels;

namespace UnSetBim.Modules.Families.Views;

/// <summary>Modeless families window. One instance per Revit session (see <see cref="ShowOrActivate"/>).</summary>
public partial class CatalogWindow : Window
{
    private static CatalogWindow? _instance;
    private readonly CatalogViewModel _viewModel;

    private CatalogWindow(CatalogViewModel viewModel)
    {
        InitializeComponent();
        Style = (Style)FindResource("UnSetWindow");

        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.PageChanged += (_, _) => ResultsScroll.ScrollToTop();
        viewModel.PlacementStarted += (_, _) => WindowState = WindowState.Minimized;
        viewModel.SessionEnded += (_, _) => Close();

        // Ctrl+F focuses the search box.
        InputBindings.Add(new KeyBinding(new FocusSearchCommand(this), Key.F, ModifierKeys.Control));

        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) =>
        {
            _viewModel.Dispose();
            _instance = null;
        };
    }

    public static void ShowOrActivate(UnSetHost host)
    {
        if (_instance is not null)
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;

            _instance.Activate();
            return;
        }

        _instance = new CatalogWindow(new CatalogViewModel(host)).AttachToRevit();
        _instance.Show();
    }

    public static void CloseIfOpen() => _instance?.Close();

    private sealed class FocusSearchCommand : ICommand
    {
        private readonly CatalogWindow _window;

        public FocusSearchCommand(CatalogWindow window) => _window = window;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _window.SearchBox.Focus();
            _window.SearchBox.SelectAll();
        }
    }
}
