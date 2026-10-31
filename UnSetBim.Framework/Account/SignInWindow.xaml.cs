using System.Windows;
using UnSetBim.Framework.Dialogs;
using UnSetBim.Framework.Hosting;

namespace UnSetBim.Framework.Account;

public partial class SignInWindow : Window
{
    private readonly SignInViewModel _viewModel;

    private SignInWindow(SignInViewModel viewModel)
    {
        InitializeComponent();
        Style = (Style)FindResource("UnSetWindow");

        _viewModel = viewModel;
        DataContext = viewModel;

        // PasswordBox does not support binding by design.
        PasswordBox.PasswordChanged += (_, _) => _viewModel.Password = PasswordBox.Password;

        viewModel.SignedIn += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SignInViewModel.IsPasswordStep) && viewModel.IsPasswordStep)
                Dispatcher.InvokeAsync(() => EmailBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        };

        Closed += (_, _) => _viewModel.Dispose();
    }

    /// <summary>Show the modal sign-in window. True when the user signed in.</summary>
    public static bool ShowSignIn(UnSetHost host)
    {
        var window = new SignInWindow(new SignInViewModel(host)).AttachToRevit();
        return window.ShowDialog() == true && host.Session.IsSignedIn;
    }
}
