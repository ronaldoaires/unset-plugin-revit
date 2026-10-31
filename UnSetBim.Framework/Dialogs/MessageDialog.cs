using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UnSetBim.Core;

namespace UnSetBim.Framework.Dialogs;

public enum DialogKind
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Themed message box for the plugin windows. Works both inside and outside a Revit API
/// context (Revit's TaskDialog should only be used inside commands).
/// </summary>
public sealed class MessageDialog : Window
{
    private int _result = -1;

    private MessageDialog(string title, string message, DialogKind kind, IReadOnlyList<string> buttons)
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/UnSetBim.UI;component/Themes/Theme.xaml", UriKind.Absolute),
        });

        Style = (Style)FindResource("UnSetWindow");
        Title = PluginInfo.Name;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var accent = kind switch
        {
            DialogKind.Error => (Brush)FindResource("Danger"),
            DialogKind.Warning => (Brush)FindResource("Warning"),
            _ => (Brush)FindResource("Accent"),
        };

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };

        root.Children.Add(new Border
        {
            Height = 3,
            Width = 36,
            CornerRadius = new CornerRadius(2),
            Background = accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 14),
        });

        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimary"),
            TextWrapping = TextWrapping.Wrap,
        });

        root.Children.Add(new TextBlock
        {
            Text = message,
            Style = (Style)FindResource("BodyText"),
            Margin = new Thickness(0, 8, 0, 20),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = buttons[i],
                Style = (Style)FindResource(i == 0 ? "PrimaryButton" : "SecondaryButton"),
                IsDefault = i == 0,
                IsCancel = i == buttons.Count - 1,
            };
            button.Click += (_, _) =>
            {
                _result = index;
                Close();
            };

            // Inserting at 0 leaves the first (primary) button on the right.
            row.Children.Insert(0, button);
        }

        // Re-apply margins after reordering.
        foreach (Button b in row.Children)
            b.Margin = new Thickness(row.Children.IndexOf(b) == 0 ? 0 : 8, 0, 0, 0);

        root.Children.Add(row);
        Content = root;
    }

    /// <summary>Show a modal dialog and return the index of the clicked button (-1 if closed).</summary>
    public static int Show(string title, string message, DialogKind kind = DialogKind.Info, params string[] buttons)
    {
        if (buttons.Length == 0)
            buttons = new[] { "OK" };

        var dialog = new MessageDialog(title, message, kind, buttons).AttachToRevit();
        dialog.ShowDialog();
        return dialog._result;
    }

    public static int Show(Window owner, string title, string message, DialogKind kind = DialogKind.Info, params string[] buttons)
    {
        if (buttons.Length == 0)
            buttons = new[] { "OK" };

        var dialog = new MessageDialog(title, message, kind, buttons) { Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.ShowDialog();
        return dialog._result;
    }
}
