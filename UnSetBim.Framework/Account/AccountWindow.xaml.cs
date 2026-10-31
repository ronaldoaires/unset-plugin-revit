using System.Windows;
using UnSetBim.Framework.Dialogs;
using UnSetBim.Framework.Hosting;

namespace UnSetBim.Framework.Account;

public partial class AccountWindow : Window
{
    private AccountWindow(AccountViewModel viewModel)
    {
        InitializeComponent();
        Style = (Style)FindResource("UnSetWindow");
        DataContext = viewModel;

        viewModel.SignedOut += (_, _) => Close();
        Closed += (_, _) => viewModel.Dispose();
    }

    public static void ShowFor(UnSetHost host)
    {
        var window = new AccountWindow(new AccountViewModel(host)).AttachToRevit();
        window.ShowDialog();
    }
}
