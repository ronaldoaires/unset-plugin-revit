using System.Windows;
using System.Windows.Input;
using UnSetBim.Framework.Dialogs;
using UnSetBim.Framework.Hosting;
using UnSetBim.Modules.Materials.ViewModels;

namespace UnSetBim.Modules.Materials.Views;

/// <summary>Modeless textures window. One instance per Revit session (see <see cref="ShowOrActivate"/>).</summary>
public partial class TexturesWindow : Window
{
    private static TexturesWindow? _instance;
    private readonly TexturesViewModel _viewModel;

    private TexturesWindow(TexturesViewModel viewModel)
    {
        InitializeComponent();
        Style = (Style)FindResource("UnSetWindow");

        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.PageChanged += (_, _) => ResultsScroll.ScrollToTop();
        // Picking faces happens in the Revit view: get out of the way, come back when done.
        viewModel.PickStarted += (_, _) => WindowState = WindowState.Minimized;
        viewModel.PickEnded += (_, _) =>
        {
            WindowState = WindowState.Normal;
            Activate();
        };
        viewModel.SessionEnded += (_, _) => Close();

        InputBindings.Add(new KeyBinding(new FocusSearchCommand(this), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(viewModel.CloseSelectionCommand, Key.Escape, ModifierKeys.None));

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

        _instance = new TexturesWindow(new TexturesViewModel(host)).AttachToRevit();
        _instance.Show();
    }

    public static void CloseIfOpen() => _instance?.Close();

    private sealed class FocusSearchCommand : ICommand
    {
        private readonly TexturesWindow _window;

        public FocusSearchCommand(TexturesWindow window) => _window = window;

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
