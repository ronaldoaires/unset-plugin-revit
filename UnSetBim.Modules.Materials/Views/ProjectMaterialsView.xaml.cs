using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace UnSetBim.Modules.Materials.Views;

/// <summary>"No projeto" tab of the textures window: project materials list and the editor.</summary>
public partial class ProjectMaterialsView : UserControl
{
    public ProjectMaterialsView()
    {
        InitializeComponent();

        // Numbers typed and shown the Brazilian way (0,5 / 1.200).
        Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        // Enter confirms a number box right away (bindings update on focus loss otherwise).
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.OriginalSource is TextBox box)
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        };
    }
}
