namespace UnSetBim.Core.Configuration;

/// <summary>
/// Pages of the site the plugin opens in the default browser.
/// Sign-up, password reset, purchases and subscription management only happen on the site.
/// </summary>
public sealed class SiteLinks
{
    private readonly Uri _site;

    public SiteLinks(PluginSettings settings) => _site = settings.SiteUrl;

    public Uri Home => _site;

    public Uri Register => new(_site, "cadastro");

    public Uri ForgotPassword => new(_site, "esqueci-minha-senha");

    public Uri Plans => new(_site, "#planos");

    public Uri Account => new(_site, "conta");

    public Uri Subscription => new(_site, "conta/assinatura");

    public Uri Families => new(_site, "revit/familias");
}
