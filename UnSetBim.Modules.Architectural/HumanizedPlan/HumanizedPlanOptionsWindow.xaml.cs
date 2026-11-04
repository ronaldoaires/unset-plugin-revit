using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UnSetBim.Framework.Dialogs;

namespace UnSetBim.Modules.Architectural.HumanizedPlan;

/// <summary>Modal window: style, prompt language, image width, furnish and free notes.</summary>
public partial class HumanizedPlanOptionsWindow : Window
{
    private HumanizedPlanOptions? _result;

    private HumanizedPlanOptionsWindow(string viewName, int roomCount, HumanizedPlanOptions initial)
    {
        InitializeComponent();
        Style = (Style)FindResource("UnSetWindow");

        ViewInfoText.Text = roomCount > 0
            ? $"Vista: {viewName}  ·  {roomCount} ambiente(s) encontrado(s)"
            : $"Vista: {viewName}";
        NoRoomsBanner.Visibility = roomCount > 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (var style in PlanStyles.All)
            StylesPanel.Children.Add(CreateStyleCard(style, style.Id == PlanStyles.Get(initial.StyleId).Id));

        Select(LanguageCombo, initial.Language.ToString());
        Select(WidthCombo, initial.ImageWidth.ToString());
        FurnishCheck.IsChecked = initial.Furnish;
        NotesBox.Text = initial.Notes;
    }

    /// <summary>Shows the window; null when the user cancels.</summary>
    public static HumanizedPlanOptions? Ask(string viewName, int roomCount, HumanizedPlanOptions initial)
    {
        var window = new HumanizedPlanOptionsWindow(viewName, roomCount, initial).AttachToRevit();
        window.ShowDialog();
        return window._result;
    }

    private RadioButton CreateStyleCard(PlanStyle style, bool isChecked)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = style.Name,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimary"),
        });
        content.Children.Add(new TextBlock
        {
            Text = style.Description,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });

        return new RadioButton
        {
            Style = (Style)FindResource("StyleCard"),
            GroupName = "PlanStyle",
            Tag = style.Id,
            Content = content,
            IsChecked = isChecked,
        };
    }

    private static void Select(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag)
                             ?? combo.Items[0];
    }

    private static string SelectedTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var styleId = StylesPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string
                      ?? PlanStyles.DefaultId;

        var language = Enum.TryParse<PromptLanguage>(SelectedTag(LanguageCombo), out var l) ? l : PromptLanguage.English;
        var width = int.TryParse(SelectedTag(WidthCombo), out var w) ? w : 6000;

        _result = new HumanizedPlanOptions(styleId, language, width, FurnishCheck.IsChecked == true, NotesBox.Text ?? string.Empty);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
