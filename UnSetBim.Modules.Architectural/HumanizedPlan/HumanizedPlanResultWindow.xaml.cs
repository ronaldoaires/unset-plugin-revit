using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Dialogs;

namespace UnSetBim.Modules.Architectural.HumanizedPlan;

/// <summary>Shows the exported image and the three prompts with copy buttons.</summary>
public partial class HumanizedPlanResultWindow : Window
{
    private readonly string _imagePath;

    private HumanizedPlanResultWindow(string imagePath, HumanizedPlanPrompts prompts)
    {
        InitializeComponent();
        Style = (Style)FindResource("UnSetWindow");

        _imagePath = imagePath;
        PathText.Text = imagePath;
        PathText.ToolTip = imagePath;

        FullBox.Text = prompts.Full;
        ShortBox.Text = prompts.Short;
        NegativeBox.Text = prompts.Negative;

        Preview.Source = LoadBitmap(imagePath, decodeWidth: 1200);
    }

    public static void ShowFor(string imagePath, HumanizedPlanPrompts prompts)
    {
        var window = new HumanizedPlanResultWindow(imagePath, prompts).AttachToRevit();
        window.ShowDialog();
    }

    /// <summary>Loads without locking the file. decodeWidth 0 = full size.</summary>
    private static BitmapImage? LoadBitmap(string path, int decodeWidth)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0)
                image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn("Humanized plan: could not load the preview.", ex);
            return null;
        }
    }

    private void OnCopyFull(object sender, RoutedEventArgs e) => CopyText(FullBox.Text, CopyFullButton);

    private void OnCopyShort(object sender, RoutedEventArgs e) => CopyText(ShortBox.Text, CopyShortButton);

    private void OnCopyNegative(object sender, RoutedEventArgs e) => CopyText(NegativeBox.Text, CopyNegativeButton);

    private void OnCopyImage(object sender, RoutedEventArgs e)
    {
        var image = LoadBitmap(_imagePath, decodeWidth: 0);
        if (image is null)
            return;

        TryClipboard(() => Clipboard.SetImage(image), CopyImageButton);
    }

    private void CopyText(string text, Button button) => TryClipboard(() => Clipboard.SetText(text), button);

    /// <summary>The clipboard can be locked by another app for a moment: report instead of crashing.</summary>
    private void TryClipboard(Action action, Button button)
    {
        try
        {
            action();
            Flash(button, "Copiado ✓");
        }
        catch (Exception ex)
        {
            Log.Warn("Humanized plan: clipboard failed.", ex);
            MessageDialog.Show(this, "Não foi possível copiar", "A área de transferência está em uso por outro programa. Tente de novo.", DialogKind.Warning);
        }
    }

    private static void Flash(Button button, string text)
    {
        // Tag keeps the real caption, so a double click does not make "Copiado" permanent.
        button.Tag ??= button.Content;
        var original = button.Tag;
        button.Content = text;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = original;
        };
        timer.Start();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_imagePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Humanized plan: could not open the folder.", ex);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
