using System.Windows;

namespace UnSetBim.Framework.Updates;

public partial class UpdateWindow : Window
{
    internal UpdateWindow(UpdateViewModel viewModel)
    {
        InitializeComponent();
        Style = (Style)FindResource("UnSetWindow");
        DataContext = viewModel;

        viewModel.CloseRequested += (_, _) => Close();
        Closed += (_, _) => viewModel.Dispose();
    }
}
