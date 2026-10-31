using System.Windows;
using System.Windows.Markup;

// Lets XAML in other assemblies use xmlns:ui="https://unset.com.br/ui" instead of a clr-namespace.
[assembly: XmlnsDefinition("https://unset.com.br/ui", "UnSetBim.UI.Controls")]
[assembly: XmlnsDefinition("https://unset.com.br/ui", "UnSetBim.UI.Converters")]
[assembly: XmlnsPrefix("https://unset.com.br/ui", "ui")]
